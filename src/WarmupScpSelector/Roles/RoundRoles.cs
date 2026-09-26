using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Roles;

/// <summary>
/// Round-scoped special-role draft and claim registry shared by every plugin that turns a player into a
/// special role (Facility Manager, GOC spy, SCP-999, ...).
///
/// <para><b>Draft.</b> Once per round, right after the native role assignment and the SCP coin draft have
/// finished, registered <see cref="RoleSlot"/>s are filled in priority order from unclaimed living players.
/// No plugin schedules its own delayed pick, so two special roles can never land on one player.</para>
///
/// <para><b>Claims.</b> A claim says which plugin owns a player's current role. Every SCP is claimed as
/// <see cref="ScpClaim"/> when the draft runs. A claim ends on disconnect and on any role change the owner
/// did not make; owners make their own role changes inside <see cref="RunAsOwner"/>. Mid-round role changers
/// (replacement volunteers, admin commands, reinforcements) ask <see cref="IsClaimed"/> first.</para>
///
/// Slots may be registered before this plugin is enabled; the registry is static and the draft only runs
/// while the host plugin is enabled (<see cref="IsHostActive"/>).
/// </summary>
public static class RoundRoles
{
    /// <summary>Claim held by every SCP-team player from the draft onward.</summary>
    public const string ScpClaim = "warmup.scp";

    private const string LogPrefix = "[WarmupScpSelector:RoundRoles]";

    private static readonly List<RoleSlot> Slots = new();
    private static readonly Dictionary<int, string> Claims = new();
    private static readonly HashSet<int> OwnerScopes = new();
    private static readonly System.Random Random = new();

    private static bool _hostActive;
    private static bool _draftPending;
    private static bool _draftCompleted;
    private static CoroutineHandle _draftHandle;

    /// <summary>Raised after a player gains a claim.</summary>
    public static event Action<Player, string>? Claimed;

    /// <summary>Raised after a player's claim ends.</summary>
    public static event Action<Player, string>? Released;

    /// <summary>Raised once per round after every slot has been drafted.</summary>
    public static event Action? DraftCompleted;

    /// <summary>True while the host plugin is enabled and will run the draft.</summary>
    public static bool IsHostActive => _hostActive;

    /// <summary>True once this round's draft has run.</summary>
    public static bool IsDraftCompleted => _draftCompleted;

    /// <summary>Registers a slot for every future round. Dispose the result on plugin disable.</summary>
    public static IDisposable Register(RoleSlot slot)
    {
        if (slot == null)
        {
            throw new ArgumentNullException(nameof(slot));
        }

        lock (Slots)
        {
            Slots.RemoveAll(existing => existing.Id == slot.Id);
            Slots.Add(slot);
        }

        return new Registration(slot);
    }

    /// <summary>The claim id that owns this player's role, or null.</summary>
    public static string? ClaimOf(Player? player) =>
        player != null && Claims.TryGetValue(player.PlayerId, out string id) ? id : null;

    public static bool IsClaimed(Player? player) => ClaimOf(player) != null;

    /// <summary>Claims an unclaimed player (or re-confirms an existing claim with the same id).</summary>
    public static bool TryClaim(Player? player, string id)
    {
        if (player == null || player.IsDestroyed || string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (Claims.TryGetValue(player.PlayerId, out string existing))
        {
            return existing == id;
        }

        Claims[player.PlayerId] = id;
        Raise(Claimed, player, id);
        return true;
    }

    /// <summary>Ends <paramref name="id"/>'s claim on the player. No-op for another owner's claim.</summary>
    public static void Release(Player? player, string id)
    {
        if (player != null && Claims.TryGetValue(player.PlayerId, out string existing) && existing == id)
        {
            Claims.Remove(player.PlayerId);
            Raise(Released, player, id);
        }
    }

    /// <summary>Moves a claim to an unclaimed player, as in an SCP swap.</summary>
    public static bool TryTransfer(Player? from, Player? to, string id)
    {
        if (from == null || to == null || ClaimOf(from) != id || IsClaimed(to))
        {
            return false;
        }

        Release(from, id);
        return TryClaim(to, id);
    }

    /// <summary>
    /// Runs the owner's own role change for a player without ending their claim. Use it for every role change
    /// a claim owner makes after the draft (conversions, takeovers, re-kits).
    /// </summary>
    public static void RunAsOwner(Player player, Action action)
    {
        if (player == null || action == null)
        {
            return;
        }

        bool added = OwnerScopes.Add(player.PlayerId);
        try
        {
            action();
        }
        finally
        {
            if (added)
            {
                OwnerScopes.Remove(player.PlayerId);
            }
        }
    }

    // ---------------------------------------------------------------- host lifecycle

    internal static void EnableHost()
    {
        _hostActive = true;
        PlayerEvents.ChangedRole += OnChangedRole;
        PlayerEvents.Left += OnLeft;
        LabApi.Events.Handlers.ServerEvents.WaitingForPlayers += ResetRound;
        LabApi.Events.Handlers.ServerEvents.RoundRestarted += ResetRound;
    }

    internal static void DisableHost()
    {
        _hostActive = false;
        PlayerEvents.ChangedRole -= OnChangedRole;
        PlayerEvents.Left -= OnLeft;
        LabApi.Events.Handlers.ServerEvents.WaitingForPlayers -= ResetRound;
        LabApi.Events.Handlers.ServerEvents.RoundRestarted -= ResetRound;
        ResetRound();
    }

    /// <summary>
    /// Called from <c>RoleAssigner.OnPlayersSpawned</c> (inside native round start). The draft itself runs on
    /// the next frame, outside the native callback, unless the SCP coin draft still has a compatibility swap
    /// pending, in which case that swap calls <see cref="RunPendingDraft"/> when it finishes.
    /// </summary>
    internal static void OnNativeRolesAssigned(bool scpSwapPending)
    {
        if (!_hostActive)
        {
            return;
        }

        _draftPending = true;
        _draftCompleted = false;
        if (!scpSwapPending)
        {
            Timing.KillCoroutines(_draftHandle);
            _draftHandle = Timing.CallDelayed(0f, RunPendingDraft);
        }
    }

    internal static void RunPendingDraft()
    {
        if (!_hostActive || !_draftPending)
        {
            return;
        }

        _draftPending = false;
        try
        {
            RunDraft();
        }
        catch (Exception ex)
        {
            Logger.Error($"{LogPrefix} Draft failed: {ex}");
        }
        finally
        {
            _draftCompleted = true;
            try
            {
                DraftCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn($"{LogPrefix} A DraftCompleted handler failed: {ex.Message}");
            }
        }
    }

    private static void RunDraft()
    {
        foreach (Player scp in Player.List.Where(p => !p.IsHost && !p.IsDestroyed && p.IsSCP))
        {
            TryClaim(scp, ScpClaim);
        }

        int connected = Player.List.Count(p => !p.IsHost && !p.IsDestroyed);
        List<RoleSlot> slots;
        lock (Slots)
        {
            slots = Slots.Where(slot => SafeActive(slot, connected)).ToList();
        }

        List<Player> candidates = Player.List
            .Where(p => !p.IsHost && !p.IsDestroyed && p.IsReady && p.IsAlive && !IsClaimed(p))
            .ToList();
        List<DraftSlot<Player>> planned = slots
            .Select(slot => new DraftSlot<Player>(slot.Id, slot.Priority, slot.MaxCount, slot.IsEligible, slot.Weight))
            .ToList();
        List<KeyValuePair<string, Player>> picks = RoleDraftPlanner.Plan(planned, candidates, Random.NextDouble);

        foreach (KeyValuePair<string, Player> pick in picks)
        {
            RoleSlot slot = slots.First(s => s.Id == pick.Key);
            Player player = pick.Value;
            if (player.IsDestroyed || !TryClaim(player, slot.Id))
            {
                continue;
            }

            try
            {
                RunAsOwner(player, () => slot.Apply(player));
                Logger.Info($"{LogPrefix} Drafted {player.Nickname} as {slot.Id}.");
            }
            catch (Exception ex)
            {
                Release(player, slot.Id);
                Logger.Error($"{LogPrefix} {slot.Id} failed to apply to {player.Nickname}: {ex}");
            }
        }

        if (picks.Count == 0 && slots.Count > 0)
        {
            Logger.Info($"{LogPrefix} Draft ran with {slots.Count} active slot(s) and no eligible player.");
        }
    }

    // ---------------------------------------------------------------- claim upkeep

    private static void OnChangedRole(PlayerChangedRoleEventArgs ev)
    {
        Player? player = ev.Player;
        if (player == null || OwnerScopes.Contains(player.PlayerId) || !Claims.TryGetValue(player.PlayerId, out string id))
        {
            return;
        }

        // An SCP claim survives SCP-to-SCP changes made by the native game (such as SCP-3114 disguises).
        if (id == ScpClaim && ev.NewRole.Team == Team.SCPs)
        {
            return;
        }

        Release(player, id);
    }

    private static void OnLeft(PlayerLeftEventArgs ev)
    {
        Player? player = ev.Player;
        if (player != null && Claims.TryGetValue(player.PlayerId, out string id))
        {
            Release(player, id);
        }
    }

    private static void ResetRound()
    {
        Timing.KillCoroutines(_draftHandle);
        _draftPending = false;
        _draftCompleted = false;
        Claims.Clear();
        OwnerScopes.Clear();
    }

    private static bool SafeActive(RoleSlot slot, int connected)
    {
        try
        {
            return slot.IsActive?.Invoke(connected) ?? true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} {slot.Id} IsActive failed: {ex.Message}");
            return false;
        }
    }

    private static void Raise(Action<Player, string>? handler, Player player, string id)
    {
        if (handler == null)
        {
            return;
        }

        foreach (Action<Player, string> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(player, id);
            }
            catch (Exception ex)
            {
                Logger.Warn($"{LogPrefix} A claim listener failed for {id}: {ex.Message}");
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private RoleSlot? _slot;

        public Registration(RoleSlot slot) => _slot = slot;

        public void Dispose()
        {
            RoleSlot? slot = _slot;
            _slot = null;
            if (slot != null)
            {
                lock (Slots)
                {
                    Slots.Remove(slot);
                }
            }
        }
    }
}
