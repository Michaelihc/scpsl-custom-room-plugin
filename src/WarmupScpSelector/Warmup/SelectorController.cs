using System;
using System.Collections.Generic;
using System.Linq;
using GameCore;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Activities.AimRange;
using WarmupScpSelector.Activities.Parkour;
using WarmupScpSelector.Selection;
using WarmupScpSelector.Services;
using WarmupScpSelector.Text;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// Orchestrates the warmup selector lifecycle. Deliberately small and event-driven:
/// build room on waiting-for-players, record coin picks, hand players back for vanilla assignment at
/// round start, then swap the picked SCP slots in. No timer manipulation, watchdog, or lobby locking.
/// </summary>
internal sealed class SelectorController
{
    // Stable HSM hint id for the warmup status panel, so it can be updated in place and removed explicitly
    // (kept per repo policy: provider-backed HSM hints use stable IDs/groups rather than fire-and-forget text).
    private const string StatusTagId = "status";

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly HsmHintDisplayProvider _hints;
    private readonly SelectorRoom _room;
    private readonly WarmupMusicPlayer _music;

    // Shared warmup-activity lifecycle core. The critical StopForRoundStart teardown runs before Tutorial
    // players flip to None so range-owned hazards cannot leak into vanilla assignment.
    private readonly ActivityManager _activities = new();
    private readonly AimRangeActivityLane _aimLane;
    private readonly ParkourActivityLane _parkourLane;
    private readonly SelectorParticipantState _participantState = new();

    // TEST SUPPORT ONLY (dummy harness): display-only picks for players that can't grab a coin (dummies).
    // Folded only into the test display overlay; never admitted to SelectorParticipantState or round-start swaps.
    private readonly Dictionary<string, RoleTypeId> _externalSelections = new();

    private readonly System.Random _random = new();

    // Snapshot of the SCPs actually on offer this warmup, used to draw the live chip row in the status panel.
    // Captured once when the room is built (it never changes mid-warmup) so per-tick rendering stays cheap.
    private IReadOnlyList<WarmupOption> _offeredOptions = Array.Empty<WarmupOption>();

    private CoroutineHandle _hintLoop;
    private CoroutineHandle _maintainLoop;
    private CoroutineHandle _swapDelay;
    private bool _active;
    private bool _handedOff;
    private Vector3? _savedStartRoundScale;

    public SelectorController(WarmupScpSelectorPlugin plugin, HsmHintDisplayProvider hints)
    {
        _plugin = plugin;
        _hints = hints;
        _room = new SelectorRoom(plugin);
        _music = new WarmupMusicPlayer(plugin);
        _aimLane = new AimRangeActivityLane(plugin, hints, _activities, IsOwnedWarmupHuman);
        _parkourLane = new ParkourActivityLane(plugin, hints, _activities, IsOwnedWarmupHuman);
    }

    private Config Config => _plugin.Config;

    // Exposed so activity lanes (Aim first, Task #3+) can register themselves for lifecycle/teardown.
    internal ActivityManager Activities => _activities;

    private bool UseChinese => string.Equals(Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

    // ---- Server lifecycle ------------------------------------------------------------------------

    public void OnWaitingForPlayers()
    {
        if (!Config.IsEnabled)
        {
            return;
        }

        try
        {
            ResetState();
            _room.Build();
            _offeredOptions = _room.OfferedOptions
                .Select(option => new WarmupOption(option.Role, option.Label))
                .ToList();

            if (_room.CoinRoles.Count == 0)
            {
                // No valid SCP options to offer: don't move anyone into an empty selector; let the round run normally.
                Logger.Warn("[WarmupScpSelector] No valid SCP options configured; selector not started this round.");
                _room.Despawn();
                return;
            }

            _active = true;
            PrepareActivities();
            _music.Start(_room.SpawnPosition);

            foreach (Player player in Participants().ToList())
            {
                try
                {
                    MoveIntoSelector(player);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Could not move a player into the selector: {ex.Message}");
                }
            }

            Timing.KillCoroutines(_hintLoop);
            _hintLoop = Timing.RunCoroutine(HintLoop());
            Timing.KillCoroutines(_maintainLoop);
            _maintainLoop = Timing.RunCoroutine(MaintainPlayers());
            HideWaitingUi();
        }
        catch (Exception ex)
        {
            Logger.Error($"[WarmupScpSelector] Failed to start warmup: {ex}");
            Cleanup();
        }
    }

    // RoleAssigner.OnPlayersSpawned: vanilla roles are now assigned, in the SAME round-start call as the
    // handoff (which set _handedOff). Scheduling here, rather than on LabAPI RoundStarted, guarantees this
    // runs after assignment regardless of cross-subscriber event ordering.
    public void OnVanillaRolesAssigned()
    {
        if (!_handedOff)
        {
            return;
        }

        _handedOff = false;

        // Runs inside the round-start path, so it must not throw. Snapshot the PURE vanilla assignment now
        // (before the settle delay and before other plugins can change roles); the swap plan is built from it.
        try
        {
            _participantState.ClearVanillaRoles();
            foreach (Player player in Participants().ToList())
            {
                try
                {
                    if (player != null)
                    {
                        _participantState.SnapshotVanillaRole(Identity(player), player.Role);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Could not snapshot a vanilla role: {ex.Message}");
                }
            }

            float delay = Config.RoleSwapDelaySeconds;
            if (float.IsNaN(delay) || float.IsInfinity(delay))
            {
                delay = 1.5f;
            }

            delay = Math.Max(0f, Math.Min(30f, delay));
            _swapDelay = Timing.CallDelayed(delay, ApplySelectedSwaps);
        }
        catch (Exception ex)
        {
            Logger.Error($"[WarmupScpSelector] Scheduling swaps failed: {ex}");
        }
    }

    public void OnRoundRestarted()
    {
        Cleanup();
    }

    /// <summary>
    /// Runs inside the vanilla round-start, just before <c>RoleAssigner</c> counts eligible hubs.
    /// Tutorial warmup players are "alive", so the assigner would skip them; flip them to None (which is
    /// dead and not a spectator role) so they are counted and given real vanilla roles. Then despawn.
    /// </summary>
    public void OnBeforeVanillaRoleAssignment()
    {
        if (!_active)
        {
            return;
        }

        // This runs INSIDE the vanilla round-start; it must NEVER throw, or it breaks role assignment
        // for everyone. Everything is wrapped, including the participant snapshot, with cleanup in finally.
        try
        {
            // Tear down every warmup activity FIRST — while its players are still Tutorial and controllable —
            // so any granted weapon, dropped pickup, hazard, pooled toy/light, or lane HSM ID is destroyed
            // before the vanilla assigner runs. Doing this before FlipTrackedTutorialPlayersToNone is the one
            // ordering the hazard invariant requires; StopForRoundStart is itself idempotent and non-throwing,
            // and is wrapped again here so an activity fault can never reach the flip or the assigner.
            StopActivitiesForRoundStart();

            // Only hand back the players THIS selector moved in (tracked by key), never unrelated Tutorial players.
            FlipTrackedTutorialPlayersToNone();
        }
        catch (Exception ex)
        {
            Logger.Error($"[WarmupScpSelector] Pre-assignment handoff error: {ex}");
        }
        finally
        {
            // Always end the warmup and clear the room, even if a flip threw, so the live round is never
            // left with the selector room up or the lifecycle flags stuck (which would skip the swap).
            // Every call here is wrapped separately so nothing can escape this core hook, and despawn runs
            // even if coroutine cleanup throws.
            _active = false;
            _handedOff = true;
            try
            {
                Timing.KillCoroutines(_hintLoop);
                Timing.KillCoroutines(_maintainLoop);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Loop cleanup failed: {ex.Message}");
            }

            try
            {
                _music.FadeOutAndStop();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Music fade-out failed: {ex.Message}");
            }

            try
            {
                ClearAllHints();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Hint cleanup failed: {ex.Message}");
            }

            try
            {
                RestoreWaitingUi();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Waiting UI cleanup failed: {ex.Message}");
            }

            try
            {
                _room.Despawn();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Selector room despawn failed: {ex.Message}");
            }
        }
    }

    // ---- Player events ---------------------------------------------------------------------------

    public void OnPlayerJoined(PlayerJoinedEventArgs ev)
    {
        if (!_active || _handedOff)
        {
            return;
        }

        Player player = ev.Player;
        Timing.CallDelayed(0.5f, () =>
        {
            try
            {
                if (_active && !_handedOff && IsCanonicalHuman(player))
                {
                    MoveIntoSelector(player);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Delayed move-in failed: {ex.Message}");
            }
        });
    }

    public void OnPlayerLeft(PlayerLeftEventArgs ev)
    {
        if (ev.Player != null)
        {
            string key = Key(ev.Player);
            bool wasCanonicalHuman = IsCanonicalHuman(ev.Player) || _participantState.IsMovedIn(key);

            // Counted native dummies can cross the lobby-start threshold in the disconnect frame. Engage the Aim
            // controller's ownership-safe lock before ordinary per-player teardown has any chance to yield.
            _aimLane.OnCanonicalHumanLeaving(ev.Player, wasCanonicalHuman);

            // Tear down lane/provider state while the departing player is still canonically owned. The lane also
            // has a key-only provider cleanup fallback for disconnect events where ReadyList already dropped it.
            try
            {
                _activities.OnPlayerLeft(key);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Activity player-left cleanup failed: {ex.Message}");
            }

            try
            {
                _hints.Remove(ev.Player, StatusTagId);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Status player-left cleanup failed: {ex.Message}");
            }

            // Drop canonical ownership only after activity/hint cleanup, so a same-UserId reconnect starts clean.
            _participantState.Remove(key);
            _externalSelections.Remove(key);
            _music.RemovePlayer(ev.Player);
        }
    }

    public void OnPlayerSpawning(PlayerSpawningEventArgs ev)
    {
        Player player = ev.Player;
        if (!_active || _handedOff || !IsCanonicalHuman(player) || ev.Role.RoleTypeId != RoleTypeId.Tutorial)
        {
            return;
        }

        // Reserved routing boundary for the later lethal Tutorial reinitialization. No bot/death implementation
        // marks this state yet, but a nested same-role spawn can already be routed safely when that task lands.
        if (_aimLane.TryRoutePendingRangeReset(player, out Vector3 resetSpawn))
        {
            ev.SetSpawnpoint(resetSpawn, 0f);
            return;
        }

        // Only redirect Tutorial spawns for players WE moved in, never unrelated Tutorial players.
        if (_participantState.IsMovedIn(Key(player)))
        {
            ev.SetSpawnpoint(_room.SpawnPosition, 0f);
        }
    }

    // SearchingPickup fires for any pickup type (coin/ammo/armor/...), so the selector works whatever
    // SelectorItem is configured to. Cancelling it keeps the coin in place as a reusable selector trigger.
    public void OnSearchingPickup(PlayerSearchingPickupEventArgs ev)
    {
        Player player = ev.Player;
        if (!_active || !IsOwnedWarmupHuman(player) || ev.Pickup == null)
        {
            return;
        }

        if (!_room.CoinRoles.TryGetValue(ev.Pickup.Serial, out RoleTypeId role))
        {
            return;
        }

        ev.IsAllowed = false; // keep the coin in place; it is only a selector trigger
        if (!_participantState.RecordSelection(Identity(player), role))
        {
            return;
        }
        try
        {
            // Reflect the new pick immediately through HSM (don't wait up to a full HintLoop tick): the chip
            // row re-highlights and "Selected" updates the instant the coin is grabbed.
            ShowStatus(player);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Selection hint failed: {ex.Message}");
        }

        _plugin.LogDebug($"{player.UserId} selected {role}.");
    }

    // ---- Internals -------------------------------------------------------------------------------

    public void Cleanup()
    {
        Timing.KillCoroutines(_hintLoop);
        Timing.KillCoroutines(_maintainLoop);
        _music.StopImmediate();
        ClearAllHints();
        RestoreWaitingUi();
        // Tear down any activity lanes (hazards/granted items/lane hints) before releasing players — same
        // ordering as the round-start handoff. Idempotent and non-throwing; a no-op while no lanes are wired.
        StopAllActivities();
        // Return players WE moved into the selector (Tutorial) to a vanilla-eligible state, so a mid-lobby
        // disable / failed startup never leaves them stuck as Tutorial when the round assigns roles.
        FlipTrackedTutorialPlayersToNone();
        _room.Despawn();
        ResetState();
    }

    // Register activity lanes for this warmup if the suite is enabled. Each lane remains independently gated.
    private void PrepareActivities()
    {
        try
        {
            // Always start from a clean slate so a prior interrupted teardown can't leave a stale session.
            _activities.StopAll();

            if (!Config.ActivitiesEnabled)
            {
                return;
            }

            if (_aimLane.Enabled)
            {
                _activities.RegisterLane(_aimLane);
                _aimLane.Start(_room);
            }

            if (_parkourLane.Enabled)
            {
                _activities.RegisterLane(_parkourLane);
                bool started = _parkourLane.Start(_aimLane.Layout);
                _aimLane.SetParkourCarveout(started);
                if (!started)
                {
                    Logger.Warn("[WarmupScpSelector] Parkour stayed closed because its Aim-hall subarea was unavailable.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Activity setup failed: {ex.Message}");
        }
    }

    // Round-start hazard teardown. Wrapped so it can never throw out of the core OnBeforeVanillaRoleAssignment
    // hook even though ActivityManager.StopForRoundStart is already non-throwing.
    private void StopActivitiesForRoundStart()
    {
        try
        {
            _activities.StopForRoundStart();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Activity round-start teardown failed: {ex.Message}");
        }
    }

    private void StopAllActivities()
    {
        try
        {
            _activities.StopAll();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Activity teardown failed: {ex.Message}");
        }
    }

    // Flip back to None only the players THIS selector moved into Tutorial (tracked in pure participant state), so cleanup
    // and the round-start handoff never disturb unrelated Tutorial players from admins or other plugins.
    private void FlipTrackedTutorialPlayersToNone()
    {
        try
        {
            foreach (Player player in Participants().ToList())
            {
                try
                {
                    if (player != null && _participantState.IsMovedIn(Key(player)) && player.Role == RoleTypeId.Tutorial)
                    {
                        player.SetRole(RoleTypeId.None, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);

                        // Detect a cancelled/no-op role change (another plugin can block it without throwing).
                        if (player.Role == RoleTypeId.Tutorial)
                        {
                            Logger.Warn("[WarmupScpSelector] A warmup player could not be handed back to vanilla (role change blocked); they may be skipped by role assignment.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Could not release a warmup player: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Releasing warmup players failed: {ex.Message}");
        }
    }

    private void ResetState()
    {
        // Cancel any pending swap so a stale callback from a prior round can never run against a new one.
        Timing.KillCoroutines(_swapDelay);
        Timing.KillCoroutines(_maintainLoop);
        _participantState.Clear();
        _externalSelections.Clear();
        _offeredOptions = Array.Empty<WarmupOption>();
        _active = false;
        _handedOff = false;
    }

    private void MoveIntoSelector(Player player)
    {
        if (!IsCanonicalHuman(player))
        {
            return;
        }

        // Claim ownership BEFORE SetRole so OnPlayerSpawning routes this player's Tutorial spawn into the
        // room. The pure state object rechecks canonical identity at the mutation boundary.
        string key = Key(player);
        _participantState.TrackMovedIn(Identity(player));
        try
        {
            if (player.Role != RoleTypeId.Tutorial)
            {
                player.SetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
            }

            player.ClearInventory();
            player.Position = _room.SpawnPosition;

            // Show the status panel immediately on entry (full countdown + "Selected: None" + how-to), so the
            // player sees it before the first HintLoop tick. Subsequent ticks just update this same HSM hint.
            ShowStatus(player);
            _music.AddPlayer(player);
        }
        catch
        {
            if (player.Role != RoleTypeId.Tutorial)
            {
                _participantState.Remove(key);
            }

            throw;
        }
    }

    // Re-place warmup players who fell through/out of the floating selector floor. The spawned floor's
    // collider can be momentarily absent on the client right after the teleport (client-load race on a
    // far-from-origin room), so a freshly placed player can start falling before it loads. A light 0.5s
    // sweep snaps any fallen/wandered warmup player back onto the floor until their client settles.
    private IEnumerator<float> MaintainPlayers()
    {
        while (_active)
        {
            try
            {
                Vector3 spawn = _room.SpawnPosition;
                foreach (Player player in Participants().ToList())
                {
                    try
                    {
                        if (!IsCanonicalHuman(player) || player.Role != RoleTypeId.Tutorial ||
                            !_participantState.IsMovedIn(Key(player)))
                        {
                            continue;
                        }

                        Vector3 pos = player.Position;
                        if (pos.y < spawn.y - 4f || Vector3.Distance(pos, spawn) > 30f)
                        {
                            player.Position = spawn;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[WarmupScpSelector] Maintain re-place failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Maintain sweep failed: {ex.Message}");
            }

            yield return Timing.WaitForSeconds(0.5f);
        }
    }

    // Hide the vanilla "WAITING FOR PLAYERS / ROUND START IS PAUSED" block while the selector is active by
    // zeroing the StartRound UI object's scale (the approach the original plugin used). Restored on warmup end.
    private void HideWaitingUi()
    {
        if (!Config.HideWaitingUi)
        {
            return;
        }

        try
        {
            GameObject startRound = GameObject.Find("StartRound");
            if (startRound != null)
            {
                _savedStartRoundScale ??= startRound.transform.localScale;
                startRound.transform.localScale = Vector3.zero;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not hide waiting UI: {ex.Message}");
        }
    }

    private void RestoreWaitingUi()
    {
        if (_savedStartRoundScale is not { } scale)
        {
            return;
        }

        try
        {
            GameObject startRound = GameObject.Find("StartRound");
            if (startRound != null)
            {
                startRound.transform.localScale = scale;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Could not restore waiting UI: {ex.Message}");
        }
        finally
        {
            _savedStartRoundScale = null;
        }
    }

    private void ApplySelectedSwaps()
    {
        try
        {
            List<Player> participants = Participants().ToList();

            // Vanilla assignment from the snapshot taken at OnPlayersSpawned, mapped to players still present.
            // (Players who disconnected during the settle delay simply fall out here.)
            Dictionary<Player, RoleTypeId> originalRoles = new();
            foreach (Player player in participants)
            {
                if (player != null &&
                    _participantState.TryGetVanillaRole(Key(player), out RoleTypeId vanilla) &&
                    VanillaRoleAssignmentResolver.TryResolve(vanilla, null, out RoleTypeId resolved))
                {
                    originalRoles[player] = resolved;
                }
            }

            // Pools of pickers, restricted to players WITH a resolved vanilla round role: only those can be
            // swapped, and this keeps an unresolved picker from consuming a slot a valid picker could fill.
            Dictionary<RoleTypeId, List<Player>> pools = new();
            foreach (Player player in participants)
            {
                if (!originalRoles.ContainsKey(player) || !_participantState.TryGetSelection(Key(player), out RoleTypeId selected))
                {
                    continue;
                }

                if (!pools.TryGetValue(selected, out List<Player> list))
                {
                    list = new List<Player>();
                    pools[selected] = list;
                }

                list.Add(player);
            }

            if (pools.Count == 0)
            {
                return;
            }

            // Distinct SCP roles only: a duplicate role in config would otherwise process the same pool twice.
            List<RoleTypeId> roleOrder = (Config.ScpOptions ?? new List<ScpOption>())
                .Where(option => option != null && ScpOption.IsScpRole(option.Role))
                .Select(option => option.Role)
                .Distinct()
                .ToList();
            Dictionary<RoleTypeId, IReadOnlyList<Player>> plannerPools =
                pools.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Player>)pair.Value);

            SelectionSwapPlan<Player> plan = SelectionSwapPlanner.BuildPlan(
                roleOrder,
                originalRoles,
                plannerPools,
                candidates => candidates[_random.Next(candidates.Count)]);

            // Apply pairwise in plan order: promote the picker FIRST, then demote the displaced holder
            // only if that succeeded. This preserves the SCP-slot count even if a SetRole throws, and
            // keeps swap semantics intact (FinalRoles differs from the vanilla roles only via these swaps).
            foreach (SelectionSwap<Player> swap in plan.Swaps)
            {
                Player picker = swap.SelectedPlayer;
                Player holder = swap.Holder;
                bool pickerPromoted = false;
                try
                {
                    if (picker == null || holder == null || !picker.IsReady || !holder.IsReady)
                    {
                        continue;
                    }

                    // Only apply if the live roles still match the plan. A prior failed/rolled-back swap or an
                    // external role change invalidates later chained swaps; skipping keeps the SCP multiset intact.
                    if (holder.Role != swap.TargetRole || picker.Role != swap.ReplacementRole)
                    {
                        continue;
                    }

                    picker.SetRole(swap.TargetRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);

                    // Verify the promotion took: another plugin can cancel a role change WITHOUT throwing.
                    // If it no-op'd, leave the holder untouched so the pair just stays at vanilla (no harm).
                    if (picker.Role != swap.TargetRole)
                    {
                        Logger.Warn($"[WarmupScpSelector] Swap into {swap.TargetRole} was blocked (picker promotion no-op); leaving the vanilla holder in place.");
                        continue;
                    }

                    pickerPromoted = true;
                    holder.SetRole(swap.ReplacementRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);

                    // Verify the demotion took. If the holder is still on the SCP, the picker and holder would
                    // both hold it, so roll the picker back to never create an extra SCP (the core guarantee).
                    if (holder.Role == swap.TargetRole)
                    {
                        Logger.Warn($"[WarmupScpSelector] Swap into {swap.TargetRole} left the holder on the role; rolling the picker back.");
                        picker.SetRole(swap.ReplacementRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Swap into {swap.TargetRole} failed: {ex.Message}");

                    // If the picker was promoted but demoting the holder failed, both would sit on the same
                    // SCP. Roll the picker back so the plugin never creates an extra SCP (its core guarantee).
                    if (pickerPromoted && picker != null)
                    {
                        try
                        {
                            picker.SetRole(swap.ReplacementRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                        }
                        catch (Exception rollbackEx)
                        {
                            Logger.Error($"[WarmupScpSelector] Swap rollback failed: {rollbackEx.Message}");
                        }
                    }
                }

                // Final invariant backstop: if, despite the verified apply + rollback, both players somehow
                // ended on the SCP (e.g. an external plugin cancelled BOTH the demotion and the rollback),
                // detect it, log a hard error, and force the holder off so we never leave an extra SCP.
                try
                {
                    if (picker != null && holder != null && picker.IsReady && holder.IsReady &&
                        picker.Role == swap.TargetRole && holder.Role == swap.TargetRole)
                    {
                        Logger.Error($"[WarmupScpSelector] Extra-{swap.TargetRole} invariant breach after swap; forcing the holder off it.");
                        holder.SetRole(swap.ReplacementRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WarmupScpSelector] Invariant backstop failed: {ex.Message}");
                }
            }

            _plugin.LogDebug($"Applied {plan.Swaps.Count} swap(s), {plan.NaturalSelections.Count} natural, skipped {plan.SkippedUnspawnedRoles.Count} unspawned.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[WarmupScpSelector] Swap application failed: {ex}");
        }
        finally
        {
            ResetState();
        }
    }

    private IEnumerator<float> HintLoop()
    {
        float interval = Config.HintIntervalSeconds;
        if (float.IsNaN(interval) || float.IsInfinity(interval))
        {
            interval = 1f;
        }

        interval = Math.Max(0.5f, Math.Min(10f, interval));
        while (_active)
        {
            // Keep the loop alive across a bad player/send; one disconnecting wrapper must not stop all hints.
            try
            {
                // Sample the live countdown/player count + selection tally once per tick; each player's panel
                // differs only by their own pick (and whether the tally highlights that pick's count).
                CountdownContext context = CountdownNow();
                IReadOnlyDictionary<RoleTypeId, int> counts = SelectionCounts();
                _music.UpdateCountdown(context.Timer);
                foreach (Player player in Participants().ToList())
                {
                    try
                    {
                        if (player != null && player.IsReady)
                        {
                            ShowStatus(player, context, counts);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[WarmupScpSelector] Status hint send failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Status hint loop iteration failed: {ex.Message}");
            }

            yield return Timing.WaitForSeconds(interval);
        }
    }

    // Sample the current state once (used by the immediate-on-pick / on-entry paths) and render.
    private void ShowStatus(Player player)
    {
        ShowStatus(player, CountdownNow(), SelectionCounts());
    }

    // Draw/refresh the original warmup status panel outside the Aim UI area. Crossing the UI-only floor line
    // removes it completely so only the Aim hero/footer can render; mechanics remain active on both sides.
    // The provider owns the change-skip cache, so an unchanged render never reaches HSM or the network.
    private void ShowStatus(Player player, CountdownContext context, IReadOnlyDictionary<RoleTypeId, int> counts)
    {
        if (player == null || !player.IsReady)
        {
            return;
        }

        string key = Key(player);
        RoleTypeId? selection = Selection(player);

        string? currentLane = _activities.CurrentLane(key);
        if ((string.Equals(currentLane, LaneHintIds.Aim, StringComparison.Ordinal) && _aimLane.IsInAimUiArea(player)) ||
            (string.Equals(currentLane, LaneHintIds.Parkour, StringComparison.Ordinal) && _parkourLane.Contains(player.Position)))
        {
            _hints.Remove(player, StatusTagId);
            return;
        }

        string text = WarmupText.BuildWarmupStatusHint(
            context.Timer, context.Players, context.Max, _offeredOptions, selection, counts, UseChinese);
        _hints.ShowPrompt(player, StatusTagId, Config.StatusHintY, text);
    }

    // Tally, per offered SCP, how many current participants have it selected. Bounded by the small warmup
    // lobby, so it's cheap to rebuild each render tick, keeping the "N picks" badge current as coins change.
    private Dictionary<RoleTypeId, int> SelectionCounts()
    {
        Dictionary<RoleTypeId, int> counts = new();
        foreach (RoleTypeId role in _participantState.SelectedRoles)
        {
            counts.TryGetValue(role, out int current);
            counts[role] = current + 1;
        }

        // Test-only dummy picks are display-only; fold them in so the count can be verified with bots.
        foreach (RoleTypeId role in _externalSelections.Values)
        {
            counts.TryGetValue(role, out int current);
            counts[role] = current + 1;
        }

        return counts;
    }

    // TEST SUPPORT ONLY (dummy harness). Replace the display-only external picks (see _externalSelections)
    // with the caller's current set — rebuilt each call so entries for departed dummies drop automatically.
    // Cleared and ignored unless the selector is active; never affects role assignment or the swap.
    public void SetExternalSelections(IEnumerable<KeyValuePair<Player, RoleTypeId>> picks)
    {
        _externalSelections.Clear();
        if (!_active || picks == null)
        {
            return;
        }

        foreach (KeyValuePair<Player, RoleTypeId> pick in picks)
        {
            Player player = pick.Key;
            if (player != null && player.ReferenceHub != null)
            {
                _externalSelections[Key(player)] = pick.Value;
            }
        }
    }

    // Remove the warmup status panel from every current participant. Called when the warmup ends (handoff) and
    // on cleanup so the HSM hint never lingers into the live round.
    private void ClearAllHints()
    {
        try
        {
            foreach (Player player in Participants().ToList())
            {
                try
                {
                    if (player != null && player.ReferenceHub != null)
                    {
                        _hints.Remove(player, StatusTagId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Clearing a status hint failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] Clearing status hints failed: {ex.Message}");
        }
    }

    private CountdownContext CountdownNow()
    {
        short timer = RoundStart.singleton != null ? RoundStart.singleton.NetworkTimer : (short)-2;
        int players = Participants().Count();
        int max = Server.MaxPlayers > 0 ? Server.MaxPlayers : Math.Max(players, 1);
        return new CountdownContext(timer, players, max);
    }

    // Live round-start state sampled once per render: vanilla countdown timer, current player count, and cap.
    private readonly struct CountdownContext
    {
        public CountdownContext(short timer, int players, int max)
        {
            Timer = timer;
            Players = players;
            Max = max;
        }

        public short Timer { get; }

        public int Players { get; }

        public int Max { get; }
    }

    private RoleTypeId? Selection(Player player)
    {
        return _participantState.TryGetSelection(Key(player), out RoleTypeId role) ? role : null;
    }

    private static IEnumerable<Player> Participants() => Player.ReadyList.Where(IsCanonicalHuman);

    internal bool IsOwnedWarmupHuman(Player player) =>
        IsCanonicalHuman(player) && _participantState.IsMovedIn(Key(player));

    internal static bool IsCanonicalHuman(Player player)
    {
        if (player == null || player.ReferenceHub == null)
        {
            return false;
        }

        return Identity(player).IsCanonicalHuman;
    }

    internal static ParticipantIdentity Identity(Player player)
    {
        if (player == null || player.ReferenceHub == null)
        {
            return default;
        }

        string key = Key(player);
        return new ParticipantIdentity(player.IsHost, player.IsDummy, player.IsPlayer, player.IsReady, key);
    }

    // Humans use their authenticated id with network/player fallbacks. Dummies deliberately use live instance
    // identity because native dummies can share ID_Dummy and must never alias one another or a human session.
    internal static string Key(Player player)
    {
        if (player == null || player.ReferenceHub == null)
        {
            return string.Empty;
        }

        return player.IsDummy
            ? ParticipantIdentityRules.DummyInstanceKey(player.NetworkId, player.PlayerId, player.ReferenceHub.GetInstanceID())
            : ParticipantIdentityRules.HumanKey(player.UserId, player.NetworkId, player.PlayerId);
    }
}
