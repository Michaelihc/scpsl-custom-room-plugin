using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MapGeneration;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;
using WarmupScpSelector.Models;
using WarmupScpSelector.Services;
using WarmupScpSelector.Text;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>
    /// Runtime orchestrator for range occupancy, main-gallery armoury pickups, native targets, owned deterministic dummies,
    /// damage isolation, lethal human reset, the shared scheduler, and synchronous round-handoff teardown.
    /// </summary>
    internal sealed class AimRangeActivityLane : IActivityLane
    {
        private readonly WarmupScpSelectorPlugin _plugin;
        private readonly HsmHintDisplayProvider _hints;
        private readonly ActivityManager _activities;
        private readonly Func<Player, bool> _isOwnedWarmupHuman;
        private readonly AimRangeSessions _sessions = new AimRangeSessions();
        private readonly AimRangeWorld _world = new AimRangeWorld();
        private readonly AimRangeScheduler _scheduler = new AimRangeScheduler();
        private readonly SlidingTargetController _slidingTargets = new SlidingTargetController();
        private readonly RangeBotController _bots;
        private readonly WeaponShelfController _shelves;
        private readonly SphereTargetController _sphereTargets;

        private SelectorRoom? _room;
        private int _rangeGeneration;
        private double _now;
        private bool _running;
        private bool _subscribed;
        private bool _parkourCarveoutEnabled;

        // HUD change-detection: last per-player counter snapshot (to fire hit/incoming flashes on a rise) and the
        // last aggregate bot spawn-signal (a rise after startup = a bot respawned -> BOT BACK flash). Pure state;
        // the provider owns the actual change-skip cache for the persistent hero/footer hints.
        private readonly Dictionary<string, HudCounters> _hudCounters = new Dictionary<string, HudCounters>(StringComparer.Ordinal);
        private int _lastSpawnSignal = -1;

        private readonly struct HudCounters
        {
            public HudCounters(int shots, int targetHits, int botHits, int incomingHits)
            {
                Shots = shots;
                TargetHits = targetHits;
                BotHits = botHits;
                IncomingHits = incomingHits;
            }

            public int Shots { get; }
            public int TargetHits { get; }
            public int BotHits { get; }
            public int IncomingHits { get; }
        }

        public AimRangeActivityLane(
            WarmupScpSelectorPlugin plugin,
            HsmHintDisplayProvider hints,
            ActivityManager activities,
            Func<Player, bool> isOwnedWarmupHuman)
        {
            _plugin = plugin;
            _hints = hints;
            _activities = activities;
            _isOwnedWarmupHuman = isOwnedWarmupHuman;
            _shelves = new WeaponShelfController(_sessions, IsEligibleHuman, SelectorController.Key);
            _bots = new RangeBotController(
                plugin,
                _sessions,
                activities,
                IsEligibleHuman,
                SelectorController.IsCanonicalHuman,
                SelectorController.Key);
            _sphereTargets = new SphereTargetController(
                IsActiveParticipant,
                player => TryGetCurrentOwnedSession(player, out _),
                OnSphereTargetHit,
                message => Logger.Warn("[WarmupScpSelector] " + message));
        }

        public string LaneId => LaneHintIds.Aim;

        public bool Enabled => _plugin.Config.ActivitiesEnabled && _plugin.Config.Activities?.Aim?.Enabled == true;

        internal AimRangeLayout? Layout => _running ? _world.Layout : null;

        internal void SetParkourCarveout(bool enabled)
        {
            _parkourCarveoutEnabled = enabled;
        }

        private AimRangeActivityConfig AimConfig => _plugin.Config.Activities?.Aim ?? new AimRangeActivityConfig();

        private bool UseChinese => string.Equals(_plugin.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

        private bool UseAscii => _plugin.Config.Activities?.UseAsciiGlyphFallback == true;

        // Configurable HSM center-X for the whole in-range HUD lane. The default lands the lane in the narrow
        // left corridor (~px216..496 at 1920x1080) between the native inventory list and wheel, so the HUD never
        // sits on either while TAB is held. Clamped to the on-screen center-X range (edge-to-edge ~±1745).
        private float HudX => Sanitize(AimConfig.HudX, -1745f, 1745f, -1077f);

        // Configurable, non-overlapping HSM bands (defaults flash 592 / hero 700 / footer 805). The collapsed
        // status strip at 900 is owned by SelectorController. All clamped into the on-screen 0..1080 range.
        private float FlashY => SanitizeY(AimConfig.FlashY, 592f);

        private float HeroY => SanitizeY(AimConfig.HeroY, 700f);

        private float FooterY => SanitizeY(AimConfig.FooterY, 805f);

        private float FlashDuration => Sanitize(_plugin.Config.Activities?.FlashDurationSeconds ?? 0.7f, 0.05f, 10f, 0.7f);

        public void Start(SelectorRoom room)
        {
            StopInternal();
            if (!Enabled || room == null || !room.AimRangeDoorPrepared)
            {
                return;
            }

            try
            {
                _room = room;
                _rangeGeneration = Next(_rangeGeneration);

                float doorOffset = room.AimRangeDoorPlaneZ - room.Origin.z;
                if (!_world.Build(room.Origin, doorOffset, room.Width, room.Depth) || _world.Layout == null)
                {
                    StopInternal();
                    return;
                }

                _now = 0d;
                if (!_shelves.Start(_world.ShelfAnchors, AimConfig))
                {
                    Logger.Warn("[WarmupScpSelector] Aim Range stayed closed because no valid gallery armoury pickup could start.");
                    StopInternal();
                    return;
                }

                SlidingTargetCallbacks slidingCallbacks = new SlidingTargetCallbacks(
                    ResolveSlidingParticipant,
                    IsCurrentSlidingParticipant,
                    HasCurrentSlidingWeapon,
                    OnSlidingTargetHit,
                    message => Logger.Warn("[WarmupScpSelector] " + message));
                if (!_slidingTargets.Start(
                        _rangeGeneration,
                        SeedSynchronizer.Seed,
                        AimConfig.SeedSalt,
                        _world.Layout.SlidingTargetTracks,
                        slidingCallbacks))
                {
                    Logger.Warn("[WarmupScpSelector] Aim Range lane 2 started without sliding targets because no authored track could spawn.");
                }

                try
                {
                    SphereTargetLayout sphereLayout = SphereTargetLayout.CreateWidenedThirdLane(
                        _world.Layout.SphereLaneOrigin,
                        _world.Layout.SphereLaneRotation,
                        _world.Layout.SphereBayWidth);
                    SphereTargetSettings sphereSettings = new SphereTargetSettings
                    {
                        ActiveCount = AimConfig.SphereActiveCount,
                        Diameter = AimConfig.SphereDiameter,
                    };
                    _sphereTargets.Start(
                        _rangeGeneration,
                        sphereLayout,
                        sphereSettings,
                        0d);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Aim Range lane 3 sphere drill disabled: {ex.GetBaseException().Message}");
                    _sphereTargets.Stop();
                }

                _bots.Start(_rangeGeneration, _world.Layout, AimConfig, SeedSynchronizer.Seed, 0d);
                Subscribe();
                _running = true;

                if (!room.OpenAimRangeDoor())
                {
                    Logger.Warn("[WarmupScpSelector] Aim Range stayed closed because its gallery door could not be opened.");
                    StopInternal();
                    return;
                }

                _scheduler.Start(AimConfig.SchedulerRateHz, AimConfig.OccupancyRateHz, Tick);
                _plugin.LogDebug($"Aim Range generation {_rangeGeneration} started with {_slidingTargets.Count} sliding targets and {_sphereTargets.ActiveCount} sphere targets.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range startup failed: {ex}");
                StopInternal();
            }
        }

        public void StopForRoundStart() => StopInternal();

        public void StopAll() => StopInternal();

        public void OnPlayerLeft(string userKey)
        {
            try
            {
                ReleaseSession(userKey);
                _shelves.DestroyForPlayer(userKey);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range player cleanup failed: {ex.Message}");
            }
        }

        public void OnCanonicalHumanLeaving(Player player, bool wasCanonicalHuman)
        {
            try
            {
                _bots.OnCanonicalHumanLeaving(player, wasCanonicalHuman);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range disconnect lobby safety failed: {ex.Message}");
            }
        }

        /// <summary>UI-only range-side check; combat and issued-weapon ownership remain full-room.</summary>
        public bool IsInAimUiArea(Player player) =>
            _running && player != null && _world.Layout?.ContainsAimUi(player.Position) == true;

        /// <summary>
        /// Reserved routing boundary for a later lethal reset implementation. It returns true only for a reset
        /// already marked by that future implementation; the current lane never marks one.
        /// </summary>
        public bool TryRoutePendingRangeReset(Player player, out Vector3 spawnPosition)
        {
            spawnPosition = default;
            if (!_running || player == null || _world.Layout == null || !IsEligibleHuman(player))
            {
                return false;
            }

            string userKey = SelectorController.Key(player);
            if (!_sessions.TryGet(userKey, out AimRangeSessions.Session session) ||
                !_activities.IsCurrent(userKey, LaneId, session.Token) ||
                !_sessions.ConsumePendingReset(userKey))
            {
                return false;
            }

            spawnPosition = _world.Layout.EntranceSpawn;
            return true;
        }

        private void Tick(double elapsedSeconds, bool checkOccupancy)
        {
            if (!_running)
            {
                return;
            }

            _now = elapsedSeconds;
            try
            {
                if (checkOccupancy)
                {
                    UpdateOccupancy();
                }

                _slidingTargets.Tick(elapsedSeconds);
                _sphereTargets.Tick(elapsedSeconds);
                _bots.Tick(elapsedSeconds);

                // Lower-frequency HUD refresh piggybacks on the occupancy tick (OccupancyRateHz). The provider's
                // change-skip cache keeps unchanged hero/footer submissions off the wire, so this stays cheap.
                if (checkOccupancy)
                {
                    RenderHud();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range scheduler tick failed: {ex.Message}");
            }
        }

        // ---- HUD (collision-free bilingual range card) ------------------------------------------------------
        // Per active session: the persistent hero card + footer, plus force-shown flashes when a session counter
        // rises (target hit / bot engaged / incoming) or a bot respawns. Bounded to the three lane IDs
        // (aim.hero / aim.footer / aim.flash); the collapsed warmupscp.status strip is owned by SelectorController.

        private void RenderHud()
        {
            if (!_running)
            {
                return;
            }

            bool cn = UseChinese;
            bool ascii = UseAscii;
            float heroY = HeroY;
            float footerY = FooterY;
            float flashY = FlashY;
            float hudX = HudX;

            HashSet<string> live = new HashSet<string>(StringComparer.Ordinal);
            foreach (AimRangeSessions.Session session in _sessions.All.ToList())
            {
                if (session.IsLeaving || !_activities.IsCurrent(session.UserKey, LaneId, session.Token))
                {
                    continue;
                }

                Player? player = ResolveHuman(session.UserKey);
                if (player == null)
                {
                    continue;
                }

                if (!IsInAimUiArea(player))
                {
                    try { _hints.RemoveLane(player, LaneId); }
                    catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range out-of-area HUD cleanup failed: {ex.Message}"); }
                    continue;
                }

                live.Add(session.UserKey);
                AimRangeViewState view = BuildViewState(session);
                try { _hints.ShowLaneHero(player, LaneId, heroY, AimRangeText.BuildHero(view, cn, ascii), hudX); }
                catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range hero render failed: {ex.Message}"); }

                try { _hints.ShowLaneFooter(player, LaneId, footerY, AimRangeText.BuildFooter(view, cn, ascii), hudX); }
                catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range footer render failed: {ex.Message}"); }

                EmitCounterFlashes(player, session, flashY, cn, ascii);
            }

            // Drop counter snapshots for sessions that are no longer live so a re-entry starts clean.
            if (_hudCounters.Count > 0)
            {
                foreach (string key in _hudCounters.Keys.Where(key => !live.Contains(key)).ToList())
                {
                    _hudCounters.Remove(key);
                }
            }

            // A rise in the aggregate spawn signal after the first observation means a bot came back.
            int spawnSignal = _bots.SpawnSignal;
            if (_lastSpawnSignal >= 0 && spawnSignal > _lastSpawnSignal)
            {
                FlashAllCurrent(AimFlashKind.BotRespawn);
            }

            _lastSpawnSignal = spawnSignal;
        }

        private AimRangeViewState BuildViewState(AimRangeSessions.Session session)
        {
            bool hasWeapon = session.OwnedItemSerial != 0;
            AimTargetKind target = _slidingTargets.IsRunning || _sphereTargets.IsRunning
                ? AimTargetKind.Moving
                : AimTargetKind.None;
            AimBotPhase bots = MapBotPhase(_bots.HudPhase);
            return new AimRangeViewState(
                hasWeapon,
                session.PresetId,
                target,
                bots,
                session.Shots,
                session.TargetHits,
                session.BotHits,
                session.IncomingHits);
        }

        private static AimBotPhase MapBotPhase(RangeBotHudPhase phase)
        {
            switch (phase)
            {
                case RangeBotHudPhase.Passive: return AimBotPhase.Passive;
                case RangeBotHudPhase.Retaliating: return AimBotPhase.Retaliating;
                case RangeBotHudPhase.Respawning: return AimBotPhase.Respawning;
                default: return AimBotPhase.None;
            }
        }

        private void EmitCounterFlashes(Player player, AimRangeSessions.Session session, float flashY, bool cn, bool ascii)
        {
            AimFlashKind kind = AimFlashKind.None;
            if (_hudCounters.TryGetValue(session.UserKey, out HudCounters previous))
            {
                // Priority when several rise in one tick: being shot is the most urgent, then a target hit, then
                // engaging a bot. Only the top verdict occupies the single flash slot.
                if (session.IncomingHits > previous.IncomingHits)
                {
                    kind = AimFlashKind.IncomingHit;
                }
                else if (session.TargetHits > previous.TargetHits)
                {
                    kind = AimFlashKind.TargetHit;
                }
                else if (session.BotHits > previous.BotHits)
                {
                    kind = AimFlashKind.BotProvoked;
                }
            }

            _hudCounters[session.UserKey] = new HudCounters(session.Shots, session.TargetHits, session.BotHits, session.IncomingHits);
            if (kind != AimFlashKind.None)
            {
                Flash(player, kind, flashY, cn, ascii);
            }
        }

        private void FlashAllCurrent(AimFlashKind kind)
        {
            if (!_running)
            {
                return;
            }

            bool cn = UseChinese;
            bool ascii = UseAscii;
            float flashY = FlashY;
            foreach (AimRangeSessions.Session session in _sessions.All.ToList())
            {
                if (session.IsLeaving || !_activities.IsCurrent(session.UserKey, LaneId, session.Token))
                {
                    continue;
                }

                Player? player = ResolveHuman(session.UserKey);
                if (player != null)
                {
                    Flash(player, kind, flashY, cn, ascii);
                }
            }
        }

        private void Flash(Player player, AimFlashKind kind, float flashY, bool cn, bool ascii)
        {
            try
            {
                if (!IsInAimUiArea(player))
                {
                    return;
                }

                string text = AimRangeText.BuildFlash(kind, cn, ascii);
                if (text.Length == 0)
                {
                    return;
                }

                _hints.ShowFlash(player, LaneId, flashY, text, FlashDuration, HudX);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range flash failed: {ex.Message}");
            }
        }

        private static float SanitizeY(float value, float fallback)
        {
            return Sanitize(value, 0f, 1080f, fallback);
        }

        private void UpdateOccupancy()
        {
            if (_world.Layout == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Player player in Player.ReadyList.ToList())
            {
                if (!IsEligibleHuman(player))
                {
                    continue;
                }

                string userKey = SelectorController.Key(player);
                seen.Add(userKey);
                bool inside = _world.Layout.ContainsVerified(player.Position) &&
                    !(_parkourCarveoutEnabled && _world.Layout.ContainsParkourBay(player.Position));
                AimRangeOccupancyTransition transition = _sessions.UpdateOccupancy(
                    userKey,
                    inside,
                    () => _activities.BeginSession(userKey, LaneId));

                if (transition == AimRangeOccupancyTransition.Entered)
                {
                    if (_sessions.TryGet(userKey, out AimRangeSessions.Session session))
                    {
                        _shelves.AttachToSession(player, session);
                    }
                    _plugin.LogDebug($"Aim Range occupancy entered: {userKey}.");
                }
                else if (transition == AimRangeOccupancyTransition.Left)
                {
                    ReleaseSession(userKey);
                }
            }

            foreach (AimRangeSessions.Session session in _sessions.All.ToList())
            {
                if (!seen.Contains(session.UserKey))
                {
                    ReleaseSession(session.UserKey);
                }
            }
        }

        private void ReleaseSession(string userKey)
        {
            if (string.IsNullOrEmpty(userKey))
            {
                return;
            }

            AimRangeSessions.Session? session = _sessions.Remove(userKey);
            if (session == null)
            {
                _activities.EndSession(userKey);
                return;
            }

            session.IsLeaving = true;
            session.Token = 0;
            session.PendingRangeResetSpawn = false;
            session.PendingResetOriginalLifeId = 0;
            _hudCounters.Remove(userKey);
            _hints.ForgetLane(userKey, LaneId);
            Player? player = ResolveHuman(userKey);
            try
            {
                _shelves.DetachSession(session);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range weapon cleanup failed for {userKey}: {ex.Message}");
            }

            try
            {
                _bots.ClearAggroFor(userKey);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range bot-aggro cleanup failed for {userKey}: {ex.Message}");
            }

            try
            {
                if (player != null)
                {
                    _hints.RemoveLane(player, LaneId);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range hint cleanup failed for {userKey}: {ex.Message}");
            }

            _activities.EndSession(userKey);
        }

        private void OnDamagingTarget(PlayerDamagingShootingTargetEventArgs ev)
        {
            try
            {
                if (!_running || ev == null)
                {
                    return;
                }

                _slidingTargets.HandleDamaging(ev, _rangeGeneration);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range sliding-target validation failed: {ex.Message}");
                if (ev?.ShootingTarget != null && _slidingTargets.Owns(ev.ShootingTarget))
                {
                    ev.IsAllowed = false;
                }
            }
        }

        private SlidingTargetParticipantToken? ResolveSlidingParticipant(Player player)
        {
            if (!TryGetCurrentSession(player, out AimRangeSessions.Session session) || session.OwnedItemSerial == 0)
            {
                return null;
            }

            return new SlidingTargetParticipantToken(session.UserKey, session.Token, session.OwnedItemSerial);
        }

        private bool IsCurrentSlidingParticipant(Player player, SlidingTargetParticipantToken participant)
        {
            return TryGetCurrentSession(player, out AimRangeSessions.Session session) &&
                string.Equals(session.UserKey, participant.UserKey, StringComparison.Ordinal) &&
                session.Token == participant.SessionToken;
        }

        private bool HasCurrentSlidingWeapon(Player player, SlidingTargetParticipantToken participant)
        {
            return IsCurrentSlidingParticipant(player, participant) && player.CurrentItem != null &&
                player.CurrentItem.Serial == participant.OwnedWeaponSerial;
        }

        private void OnSlidingTargetHit(SlidingTargetHit hit)
        {
            if (!_running || !TryGetCurrentSession(hit.Player, out AimRangeSessions.Session session) ||
                !string.Equals(session.UserKey, hit.Participant.UserKey, StringComparison.Ordinal) ||
                session.Token != hit.Participant.SessionToken ||
                session.OwnedItemSerial != hit.Participant.OwnedWeaponSerial)
            {
                return;
            }

            session.TargetHits++;
            try { hit.Player.SendHitMarker(); }
            catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range sliding-target hitmarker failed: {ex.Message}"); }
        }

        private void OnSphereTargetHit(SphereTargetHit hit)
        {
            if (_running && TryGetCurrentOwnedSession(hit.Player, out AimRangeSessions.Session session))
            {
                session.TargetHits++;
                try { hit.Player.SendHitMarker(); }
                catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range sphere-target hitmarker failed: {ex.Message}"); }
            }
        }

        private void OnShotWeapon(PlayerShotWeaponEventArgs ev)
        {
            try
            {
                _bots.ObserveShot(ev);
                if (_running && ev?.Player != null && ev.FirearmItem != null &&
                    TryGetCurrentSession(ev.Player, out AimRangeSessions.Session session) &&
                    session.OwnedItemSerial == ev.FirearmItem.Serial)
                {
                    session.Shots++;
                    session.TrackedReserveAmmo = ev.Player.GetAmmo(ev.FirearmItem.AmmoType);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range shot accounting failed: {ex.Message}");
            }
        }

        private void OnHurting(PlayerHurtingEventArgs ev)
        {
            try
            {
                if (!_running || ev?.Player == null)
                {
                    return;
                }

                bool victimParticipant = TryGetCurrentSession(ev.Player, out AimRangeSessions.Session victimSession);
                AimRangeSessions.Session attackerSession = null!;
                bool attackerParticipant = ev.Attacker != null && TryGetCurrentSession(ev.Attacker, out attackerSession);
                ushort damageWeaponSerial = ev.DamageHandler is FirearmDamageHandler firearmDamageHandler &&
                    firearmDamageHandler.Firearm != null ? firearmDamageHandler.Firearm.ItemSerial : (ushort)0;
                bool attackerHasIssuedWeapon = ev.Attacker != null &&
                    damageWeaponSerial != 0 && _shelves.IsIssuedWeapon(ev.Attacker, damageWeaponSerial);
                bool attackerOwnsWeapon = attackerParticipant && attackerHasIssuedWeapon &&
                    _shelves.SynchronizeIssuedWeapon(ev.Attacker, attackerSession, damageWeaponSerial);
                bool attackerBot = _bots.IsOwnedBot(ev.Attacker);
                bool victimBot = _bots.IsOwnedBot(ev.Player);

                // Gallery armoury guns can be carried outside the range, but can never damage anything there.
                if (attackerHasIssuedWeapon && !attackerParticipant)
                {
                    ev.IsAllowed = false;
                    return;
                }

                RangeDamageDisposition disposition = RangeDamagePolicy.Decide(
                    attackerParticipant,
                    attackerOwnsWeapon,
                    attackerBot,
                    victimParticipant,
                    victimBot);

                if (disposition == RangeDamageDisposition.Cancel)
                {
                    ev.IsAllowed = false;
                    return;
                }

                if (disposition == RangeDamageDisposition.AllowHumanToOwnedBot && ev.Attacker != null)
                {
                    // Damage authorization and retaliation setup are deliberately independent. A valid issued-gun
                    // hit always hurts an owned dummy; a transient bot registry/input failure must not make it immune.
                    bool provoked = _bots.TryProvoke(
                        ev.Player,
                        ev.Attacker,
                        attackerSession.Token,
                        damageWeaponSerial,
                        Now);
                    _plugin.LogDebug(
                        $"Aim bot hit routed: attacker={attackerSession.UserKey} serial={damageWeaponSerial} " +
                        $"bot={ev.Player.PlayerId} provoked={provoked} current={ev.Attacker.CurrentItem?.Serial ?? 0}.");
                    attackerSession.BotHits++;
                    return;
                }

                if (disposition == RangeDamageDisposition.AllowOwnedBotToParticipant)
                {
                    victimSession.IncomingHits++;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range damage policy failed: {ex.Message}");
                if (ev != null && (_bots.IsOwnedBot(ev.Player) || _bots.IsOwnedBot(ev.Attacker) ||
                    IsActiveParticipant(ev.Player) || (ev.Attacker != null && IsActiveParticipant(ev.Attacker))))
                {
                    ev.IsAllowed = false;
                }
            }
        }

        private void OnDying(PlayerDyingEventArgs ev)
        {
            try
            {
                if (!_running || ev?.Player == null)
                {
                    return;
                }

                if (TryGetCurrentSession(ev.Player, out AimRangeSessions.Session session))
                {
                    // Authoritative lethal boundary: cancellation is the first mutation so native KillPlayer,
                    // Spectator, ragdoll, drops, death text, and Death never run for a current range human.
                    ev.IsAllowed = false;
                    ResetHumanAfterLethal(ev.Player, session);
                    return;
                }

                // Owned bots are intentionally allowed to complete native death. The controller invalidates the
                // generation and clears target/fire state now, then destroys only this owned hub after the callback.
                if (_bots.HandleDying(ev.Player, Now))
                {
                    FlashAllCurrent(AimFlashKind.BotDown);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range lethal handling failed: {ex.Message}");
                if (ev?.Player != null && IsActiveParticipant(ev.Player))
                {
                    ev.IsAllowed = false;
                    EmergencyRestoreHuman(ev.Player, ResolveSession(ev.Player));
                }
            }
        }

        private void OnChangingRole(PlayerChangingRoleEventArgs ev)
        {
            try
            {
                if (_running && ev?.Player != null && ev.ChangeReason == RoleChangeReason.Died &&
                    ev.NewRole == RoleTypeId.Spectator && IsActiveParticipant(ev.Player))
                {
                    ev.IsAllowed = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range Spectator guard failed: {ex.Message}");
            }
        }

        private void OnSpawningRagdoll(PlayerSpawningRagdollEventArgs ev)
        {
            try
            {
                if (_running && ev?.Player != null &&
                    (_bots.ShouldCancelRagdoll(ev.Player) || IsActiveParticipant(ev.Player)))
                {
                    ev.IsAllowed = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range ragdoll guard failed: {ex.Message}");
            }
        }

        private void ResetHumanAfterLethal(Player player, AimRangeSessions.Session session)
        {
            if (player == null || session == null || _world.Layout == null)
            {
                return;
            }

            string userKey = session.UserKey;
            int oldLifeId = player.LifeId;
            if (session.OwnedItemSerial != 0 &&
                Item.TryGet(session.OwnedItemSerial, out Item? ownedItem) &&
                ownedItem is FirearmItem ownedFirearm &&
                ownedFirearm.CurrentOwner == player)
            {
                session.TrackedReserveAmmo = player.GetAmmo(ownedFirearm.AmmoType);
            }
            _bots.ClearAggroFor(userKey);
            if (!_sessions.BeginLethalReset(userKey, oldLifeId))
            {
                EmergencyRestoreHuman(player, session);
                return;
            }

            try
            {
                player.SetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.UseSpawnpoint);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range Tutorial reinitialization threw: {ex.Message}");
            }

            player.Position = _world.Layout.EntranceSpawn;
            AimRangeLethalResetResult result = _sessions.CompleteLethalReset(
                userKey,
                player.Role == RoleTypeId.Tutorial,
                player.LifeId);
            if (result != AimRangeLethalResetResult.Reinitialized)
            {
                EmergencyRestoreHuman(player, session);
                return;
            }

            RestoreHumanState(player, session);
        }

        private void EmergencyRestoreHuman(Player player, AimRangeSessions.Session? session)
        {
            if (player == null)
            {
                return;
            }

            if (session != null)
            {
                _sessions.CompleteLethalReset(session.UserKey, player.Role == RoleTypeId.Tutorial, player.LifeId);
                _bots.ClearAggroFor(session.UserKey);
            }

            RestoreHumanState(player, session);
        }

        private void RestoreHumanState(Player player, AimRangeSessions.Session? session)
        {
            float fullHealth = Sanitize(AimConfig.HumanResetHealth, 1f, 10000f, 100f);
            try { player.DisableAllEffects(); } catch { }
            try { player.ArtificialHealth = 0f; } catch { }
            try { player.MaxHealth = fullHealth; } catch { }
            try { player.Health = fullHealth; } catch { }
            try { if (_world.Layout != null) player.Position = _world.Layout.EntranceSpawn; } catch { }
            if (session != null)
            {
                try
                {
                    if (!_shelves.EnsureOwnedWeapon(player, session))
                    {
                        Logger.Warn($"[WarmupScpSelector] Aim Range could not preserve/reselect the tracked gun for {session.UserKey} after lethal reset.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Aim Range gun restore failed for {session.UserKey}: {ex.Message}");
                }

                // Re-baseline the HUD counters so the reset does not immediately re-fire the incoming-hit flash
                // for the lethal shot that was already counted; the range-reset flash below is shown instead.
                _hudCounters[session.UserKey] = new HudCounters(
                    session.Shots, session.TargetHits, session.BotHits, session.IncomingHits);
            }

            Flash(player, AimFlashKind.Reset, FlashY, UseChinese, UseAscii);
        }

        private AimRangeSessions.Session? ResolveSession(Player player)
        {
            if (player == null)
            {
                return null;
            }

            return _sessions.TryGet(SelectorController.Key(player), out AimRangeSessions.Session session) ? session : null;
        }

        private void OnPickingUp(PlayerPickingUpItemEventArgs ev)
        {
            try
            {
                _shelves.OnPickingUp(ev);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range armoury grant failed: {ex.Message}");
            }
        }

        private void OnDropped(PlayerDroppedItemEventArgs ev)
        {
            try
            {
                _shelves.OnDropped(ev);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim Range dropped-weapon cleanup failed: {ex.Message}");
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            PlayerEvents.PickingUpItem += OnPickingUp;
            PlayerEvents.DroppedItem += OnDropped;
            PlayerEvents.ShotWeapon += OnShotWeapon;
            PlayerEvents.DamagingShootingTarget += OnDamagingTarget;
            PlayerEvents.Hurting += OnHurting;
            PlayerEvents.Dying += OnDying;
            PlayerEvents.ChangingRole += OnChangingRole;
            PlayerEvents.SpawningRagdoll += OnSpawningRagdoll;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            PlayerEvents.PickingUpItem -= OnPickingUp;
            PlayerEvents.DroppedItem -= OnDropped;
            PlayerEvents.ShotWeapon -= OnShotWeapon;
            PlayerEvents.DamagingShootingTarget -= OnDamagingTarget;
            PlayerEvents.Hurting -= OnHurting;
            PlayerEvents.Dying -= OnDying;
            PlayerEvents.ChangingRole -= OnChangingRole;
            PlayerEvents.SpawningRagdoll -= OnSpawningRagdoll;
            _subscribed = false;
        }

        private void StopInternal()
        {
            _running = false;

            try { _scheduler.Stop(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range scheduler cleanup failed: {ex.Message}"); }
            try { Unsubscribe(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range event cleanup failed: {ex.Message}"); }
            try { _slidingTargets.Stop(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range sliding-target cleanup failed: {ex.Message}"); }
            try { _sphereTargets.Stop(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range sphere-target cleanup failed: {ex.Message}"); }
            try { _bots.Stop(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range bot cleanup failed: {ex.Message}"); }

            foreach (AimRangeSessions.Session session in _sessions.InvalidateAndRemoveAll())
            {
                Player? player = ResolveHuman(session.UserKey);
                try { if (player != null) _hints.RemoveLane(player, LaneId); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range hint teardown failed: {ex.Message}"); }
                try { _activities.EndSession(session.UserKey); } catch { }
            }

            try { _shelves.Stop(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range armoury teardown failed: {ex.Message}"); }
            try { _world.Despawn(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range world teardown failed: {ex.Message}"); }
            try { _room?.CloseAimRangeDoor(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Aim Range door cleanup failed: {ex.Message}"); }

            _now = 0d;
            _parkourCarveoutEnabled = false;
            _hudCounters.Clear();
            _lastSpawnSignal = -1;
            _room = null;
        }

        private bool TryGetCurrentOwnedSession(Player player, out AimRangeSessions.Session session)
        {
            return TryGetCurrentSession(player, out session) && IsCurrentOwnedWeapon(player, session);
        }

        private bool TryGetCurrentSession(Player player, out AimRangeSessions.Session session)
        {
            session = null!;
            if (!IsEligibleHuman(player))
            {
                return false;
            }

            string userKey = SelectorController.Key(player);
            return _sessions.TryGet(userKey, out session) &&
                !session.IsLeaving &&
                _activities.IsCurrent(userKey, LaneId, session.Token);
        }

        private static bool IsCurrentOwnedWeapon(Player player, AimRangeSessions.Session session)
        {
            return player != null && session != null && session.OwnedItemSerial != 0 &&
                player.CurrentItem != null && player.CurrentItem.Serial == session.OwnedItemSerial;
        }

        private bool IsActiveParticipant(Player player) => TryGetCurrentSession(player, out _);

        private bool IsEligibleHuman(Player player) => player != null && _isOwnedWarmupHuman(player);

        private Player? ResolveHuman(string userKey)
        {
            if (string.IsNullOrEmpty(userKey))
            {
                return null;
            }

            foreach (Player player in Player.ReadyList)
            {
                if (IsEligibleHuman(player) && string.Equals(SelectorController.Key(player), userKey, StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return null;
        }

        private double Now => _now;

        private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;

        private static float Sanitize(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
