using System;
using System.Collections.Generic;
using System.Linq;
using CentralAuth;
using InventorySystem.Items;
using InventorySystem.Items.Firearms;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>Aggregate owned-bot phase for the range HUD eyebrow (read-only projection of slot lifecycle state).</summary>
    internal enum RangeBotHudPhase
    {
        None,
        Passive,
        Retaliating,
        Respawning,
    }

    /// <summary>
    /// Owns exactly the native dummies created for the two authored right-bay slots. Identity is live hub/network/player
    /// identity plus a spawn generation; shared dummy UserIds are never consulted.
    /// </summary>
    internal sealed class RangeBotController
    {
        private enum InitializeStage
        {
            AwaitWrapper,
            AwaitRole,
            AwaitFirearmActions,
        }

        private enum ReloadTacticalStage
        {
            None,
            MovingToCover,
            Reloading,
        }

        private sealed class SlotRuntime
        {
            public SlotRuntime(AimBotPath path)
            {
                Path = path;
                Lifecycle = new RangeBotLifecycle(path.SlotId);
            }

            public AimBotPath Path { get; }
            public RangeBotLifecycle Lifecycle { get; }
            public ReferenceHub? Hub { get; set; }
            public Player? Player { get; set; }
            public FirearmItem? Firearm { get; set; }
            public AimWeaponPresetConfig? Preset { get; set; }
            public ushort FirearmSerial { get; set; }
            public InitializeStage InitializeStage { get; set; }
            public double StageReadyAt { get; set; }
            public double FirearmActionDeadline { get; set; }
            public int PathSegmentIndex { get; set; }
            public double PathSegmentStartedAt { get; set; }
            public Vector3 LastProgressPosition { get; set; }
            public double NextProgressCheckAt { get; set; }
            public bool ReportedMovementStall { get; set; }
            public double NextShotAt { get; set; }
            public bool AwaitingShotVerification { get; set; }
            public bool ObservedShotEvent { get; set; }
            public ReferenceHub? AggressorHub { get; set; }
            public int AmmoBeforeShot { get; set; }
            public double ShotVerificationAt { get; set; }
            public bool ShootHeld { get; set; }
            public bool HasFiredThisEngagement { get; set; }
            public bool ZoomHeld { get; set; }
            public Vector3 CombatAnchor { get; set; }
            public double EngagementStartedAt { get; set; }
            public float TacticalPhase { get; set; }
            public int TacticalSide { get; set; }
            public int JumpOrdinal { get; set; }
            public double NextJumpAt { get; set; }
            public ReloadTacticalStage ReloadStage { get; set; }
            public AimBotCover? ReloadCover { get; set; }
            public double CoverReachedAt { get; set; }
            public bool ReloadInvoked { get; set; }
            public bool ReloadStarted { get; set; }
            public int ReloadAttempts { get; set; }
            public double NextReloadAttemptAt { get; set; }
            public bool PermanentlyDisabled { get; set; }
        }

        private const float NativeWalkStepMeters = 0.45f;
        private const float CombatWalkStepMeters = 0.08f;
        private const float PathReachedRadius = 0.35f;
        private const float CoverReachedRadius = 0.5f;
        private const float CombatStrafeRadius = 1.15f;
        private const float ReloadStrafeRadius = 0.3f;
        private const float ZoomDistanceMeters = 10f;
        private const float CloseJumpDistanceMeters = 6f;
        private const double MovementProgressCheckSeconds = 2d;
        private const float MovementProgressMinimumMeters = 0.05f;
        private const double FirearmDrawSeconds = 1d;
        private const double FirearmActionPollSeconds = 3d;
        private const double NormalJumpCooldownSeconds = 2.5d;
        private const double CloseJumpCooldownSeconds = 0.8d;
        private const double CoverLosGraceSeconds = 0.35d;
        private const double ReloadRetrySeconds = 0.45d;
        private static readonly Vector3 DummyLocalPosition = new Vector3(0f, 1f, 0f);

        private readonly WarmupScpSelectorPlugin _plugin;
        private readonly AimRangeSessions _sessions;
        private readonly ActivityManager _activities;
        private readonly Func<Player, bool> _isEligibleHuman;
        private readonly Func<Player, bool> _isCanonicalHuman;
        private readonly Func<Player, string> _key;
        private readonly RangeBotRegistry _registry = new RangeBotRegistry();
        private readonly Dictionary<int, SlotRuntime> _slots = new Dictionary<int, SlotRuntime>();
        private readonly Dictionary<int, ReferenceHub> _ownedHubs = new Dictionary<int, ReferenceHub>();
        private readonly HashSet<int> _retiredHubIds = new HashSet<int>();
        private readonly Dictionary<int, ushort> _retiredSerials = new Dictionary<int, ushort>();
        private readonly List<AimWeaponPresetConfig> _presets = new List<AimWeaponPresetConfig>();

        private AimRangeLayout? _layout;
        private int _rangeGeneration;
        private int _mapSeed;
        private int _seedSalt;
        private int _requestedBotCount;
        private RoleTypeId _botRole;
        private float _botHealth;
        private double _initializeSeconds;
        private double _acquireDelay;
        private double _shotCadence;
        private double _shotVerificationSeconds;
        private double _maxDistance;
        private float _aimTolerance;
        private double _respawnDelay;
        private double _aggroLease;
        private bool _ownsSoloLobbyLock;
        private bool _soloLobbyPriorLock;
        private bool _running;

        public RangeBotController(
            WarmupScpSelectorPlugin plugin,
            AimRangeSessions sessions,
            ActivityManager activities,
            Func<Player, bool> isEligibleHuman,
            Func<Player, bool> isCanonicalHuman,
            Func<Player, string> key)
        {
            _plugin = plugin;
            _sessions = sessions;
            _activities = activities;
            _isEligibleHuman = isEligibleHuman;
            _isCanonicalHuman = isCanonicalHuman;
            _key = key;
        }

        public bool Enabled => _running && _slots.Values.Any(slot => slot.Lifecycle.State != RangeBotState.Disabled);

        public bool NativeRetaliationAvailable => _slots.Values.Any(slot =>
            slot.Lifecycle.State == RangeBotState.PassivePatrol ||
            slot.Lifecycle.State == RangeBotState.Alerted ||
            slot.Lifecycle.State == RangeBotState.ReturningFire);

        public int OwnedBotCount => _registry.Count;

        /// <summary>
        /// Read-only aggregate phase for the range HUD eyebrow: Retaliating if any bot is Alerted/ReturningFire,
        /// else Respawning if any is (re)spawning/dead, else Passive when at least one bot is enabled. Never mutates.
        /// </summary>
        public RangeBotHudPhase HudPhase
        {
            get
            {
                if (!_running)
                {
                    return RangeBotHudPhase.None;
                }

                bool any = false, retaliating = false, respawning = false;
                foreach (SlotRuntime slot in _slots.Values)
                {
                    RangeBotState state = slot.Lifecycle.State;
                    if (state == RangeBotState.Disabled)
                    {
                        continue;
                    }

                    any = true;
                    if (state == RangeBotState.Alerted || state == RangeBotState.ReturningFire)
                    {
                        retaliating = true;
                    }
                    else if (state == RangeBotState.Spawning || state == RangeBotState.Initializing ||
                             state == RangeBotState.Dead || state == RangeBotState.RespawnWait)
                    {
                        respawning = true;
                    }
                }

                if (!any)
                {
                    return RangeBotHudPhase.None;
                }

                return retaliating
                    ? RangeBotHudPhase.Retaliating
                    : respawning ? RangeBotHudPhase.Respawning : RangeBotHudPhase.Passive;
            }
        }

        /// <summary>
        /// Sum of live per-slot spawn generations. Dead slots contribute 0 and a respawn advances a slot's
        /// generation, so a rise after startup means a bot came back — the lane HUD watches this to fire the
        /// BOT BACK flash without any gameplay hook. Read-only.
        /// </summary>
        public int SpawnSignal
        {
            get
            {
                int sum = 0;
                foreach (SlotRuntime slot in _slots.Values)
                {
                    sum += slot.Lifecycle.SpawnGeneration;
                }

                return sum;
            }
        }

        public void Start(int rangeGeneration, AimRangeLayout layout, AimRangeActivityConfig config, int mapSeed, double now)
        {
            Stop();
            if (rangeGeneration <= 0 || layout == null || config == null)
            {
                return;
            }

            _layout = layout;
            _rangeGeneration = rangeGeneration;
            _mapSeed = mapSeed;
            _seedSalt = config.SeedSalt;
            RangeBotValidatedSettings settings = RangeBotValidatedSettings.From(config);
            _requestedBotCount = settings.Count;
            if (_requestedBotCount > 0 && !RangeBotNative.IsSupportedBotRole(config.BotRole))
            {
                Logger.Warn($"[WarmupScpSelector] Aim bots disabled because configured role {config.BotRole} is not an FPC enemy of Tutorial participants.");
                return;
            }

            _botRole = config.BotRole;
            _botHealth = settings.Health;
            _initializeSeconds = settings.InitializeSeconds;
            _acquireDelay = settings.AcquireDelaySeconds;
            _shotCadence = settings.ShotCadenceSeconds;
            _shotVerificationSeconds = settings.ShotVerificationSeconds;
            // A valid aggressor can occupy either half of the continuous widened hall. Older configs still carry
            // the original 24 m cap, which is shorter than the diagonal from the new wall-side armoury to lane 1
            // and silently prevented the native trigger path after a successful provocation. Always cover the
            // authored activity bounds; a larger configured value remains respected.
            _maxDistance = Math.Max(settings.MaxRetaliationDistance, layout.RequiredRetaliationDistance);
            _aimTolerance = settings.AimToleranceDegrees;
            _respawnDelay = settings.RespawnSeconds;
            _aggroLease = settings.AggroLeaseSeconds;
            _presets.AddRange(settings.Presets);

            if (_requestedBotCount == 0)
            {
                return;
            }

            if (_presets.Count == 0)
            {
                Logger.Warn("[WarmupScpSelector] Aim bots disabled because no valid conventional bot weapon preset remains.");
                return;
            }

            foreach (AimBotPath path in layout.BotPaths.OrderBy(path => path.SlotId).Take(_requestedBotCount))
            {
                if (path.Segments == null || path.Segments.Count == 0)
                {
                    continue;
                }

                _slots[path.SlotId] = new SlotRuntime(path);
            }

            _running = _slots.Count > 0;
            if (!_running)
            {
                Logger.Warn("[WarmupScpSelector] Aim bots disabled because no authored bot paths were available.");
                return;
            }

            EnforceLobbySafety(now);
        }

        public void OnCanonicalHumanLeaving(Player departingPlayer, bool wasCanonicalHuman)
        {
            if (!_running || !wasCanonicalHuman || departingPlayer == null || _ownedHubs.Count == 0)
            {
                return;
            }

            try
            {
                int currentHumans = Player.ReadyList.Count(player => _isCanonicalHuman(player));
                bool departingStillCounted = Player.ReadyList.Any(player =>
                    player?.ReferenceHub == departingPlayer.ReferenceHub && _isCanonicalHuman(player));
                int remainingHumans = Math.Max(0, currentHumans - (departingStillCounted ? 1 : 0));
                if (remainingHumans > 1)
                {
                    return;
                }

                TryAcquireSoloLobbyLock();
                if (remainingHumans == 0)
                {
                    foreach (SlotRuntime slot in _slots.Values.Where(slot => slot.Lifecycle.State != RangeBotState.Disabled).ToList())
                    {
                        DisableSlot(slot, "last canonical human disconnected", permanent: false);
                    }

                    ReleaseSoloLobbyLock();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim bot disconnect safety failed: {ex.Message}");
            }
        }

        public void Tick(double now)
        {
            if (!_running)
            {
                return;
            }

            EnforceLobbySafety(now);

            foreach (SlotRuntime slot in _slots.Values.ToList())
            {
                try
                {
                    bool hadAggressor = slot.Lifecycle.HasAggressor;
                    slot.Lifecycle.Advance(now);
                    if (hadAggressor && !slot.Lifecycle.HasAggressor)
                    {
                        ResetTacticalState(slot);
                    }
                    else if (!slot.Lifecycle.HasAggressor)
                    {
                        slot.AggressorHub = null;
                        ReleaseHeldActions(slot);
                    }

                    if (slot.Lifecycle.State == RangeBotState.Spawning)
                    {
                        BeginSpawn(slot, now);
                    }
                    else if (slot.Lifecycle.State == RangeBotState.Initializing)
                    {
                        TickInitializing(slot, now);
                    }
                    else if (slot.Lifecycle.State == RangeBotState.Dead)
                    {
                        slot.Lifecycle.Advance(now);
                    }
                    else if (slot.Lifecycle.RespawnDue(now))
                    {
                        BeginSpawn(slot, now);
                    }
                    else if (slot.Lifecycle.State == RangeBotState.PassivePatrol ||
                             slot.Lifecycle.State == RangeBotState.RetaliationUnavailable)
                    {
                        TickMovement(slot, now);
                    }
                    else if (slot.Lifecycle.State == RangeBotState.Alerted ||
                             slot.Lifecycle.State == RangeBotState.ReturningFire)
                    {
                        TickRetaliation(slot, now);
                    }
                }
                catch (Exception ex)
                {
                    DisableSlot(slot, $"runtime failure: {ex.GetBaseException().Message}");
                }
            }
        }

        public bool IsOwnedBot(Player? player)
        {
            if (player?.ReferenceHub == null)
            {
                return false;
            }

            return _registry.TryByHub(player.ReferenceHub.GetInstanceID(), out RangeBotIdentity identity) &&
                _registry.IsCurrent(identity.SlotId, identity.SpawnGeneration);
        }

        public bool TryProvoke(Player bot, Player attacker, int attackerSessionToken, ushort attackerWeaponSerial, double now)
        {
            if (!_running || bot?.ReferenceHub == null || attacker == null || attackerWeaponSerial == 0 ||
                !_registry.TryByHub(bot.ReferenceHub.GetInstanceID(), out RangeBotIdentity identity) ||
                !_registry.IsCurrent(identity.SlotId, identity.SpawnGeneration) ||
                !_slots.TryGetValue(identity.SlotId, out SlotRuntime slot) ||
                slot.FirearmSerial == 0 || !_isEligibleHuman(attacker))
            {
                return false;
            }

            string userKey = _key(attacker);
            if (!_sessions.TryGet(userKey, out AimRangeSessions.Session session) ||
                session.Token != attackerSessionToken || session.OwnedItemSerial != attackerWeaponSerial ||
                !_activities.IsCurrent(userKey, LaneHintIds.Aim, attackerSessionToken))
            {
                return false;
            }

            // Every valid activity-owned hit is allowed. The lifecycle independently locks the first attacker;
            // a later valid attacker may still damage the bot but cannot steal the fixed active lease.
            bool sameLease = slot.Lifecycle.HasAggressor && now < slot.Lifecycle.AggroExpiresAt &&
                string.Equals(slot.Lifecycle.AggressorKey, userKey, StringComparison.Ordinal) &&
                slot.Lifecycle.AggressorLifeId == attacker.LifeId &&
                slot.Lifecycle.AggressorSessionToken == attackerSessionToken;
            bool ownsLease = slot.Lifecycle.TryAggro(
                userKey,
                attacker.LifeId,
                attackerSessionToken,
                now,
                _acquireDelay,
                _aggroLease);
            if (ownsLease && string.Equals(slot.Lifecycle.AggressorKey, userKey, StringComparison.Ordinal) &&
                slot.Lifecycle.AggressorLifeId == attacker.LifeId &&
                slot.Lifecycle.AggressorSessionToken == attackerSessionToken)
            {
                slot.AggressorHub = attacker.ReferenceHub;
                if (!sameLease)
                {
                    BeginEngagement(slot, now);
                }
            }

            return true;
        }

        public bool HandleDying(Player player, double now)
        {
            if (player?.ReferenceHub == null ||
                !_registry.TryByHub(player.ReferenceHub.GetInstanceID(), out RangeBotIdentity identity) ||
                !_registry.IsCurrent(identity.SlotId, identity.SpawnGeneration) ||
                !_slots.TryGetValue(identity.SlotId, out SlotRuntime slot))
            {
                return false;
            }

            ReferenceHub? oldHub = slot.Hub;
            ushort oldSerial = slot.FirearmSerial;
            int oldHubId = oldHub?.GetInstanceID() ?? 0;

            _registry.InvalidateSlot(identity.SlotId);
            ResetTacticalState(slot);
            RangeBotNative.ClearReserveAmmo(oldHub);
            slot.Lifecycle.MarkDead(now, _respawnDelay);
            slot.Hub = null;
            slot.Player = null;
            slot.Firearm = null;
            slot.FirearmSerial = 0;
            slot.Preset = null;
            slot.ShootHeld = false;
            slot.ZoomHeld = false;

            if (oldHubId != 0)
            {
                _retiredHubIds.Add(oldHubId);
                if (oldSerial != 0) _retiredSerials[oldHubId] = oldSerial;
            }

            Timing.CallDelayed(0.01f, () =>
            {
                RangeBotNative.DestroySerial(oldSerial);
                RangeBotNative.DestroyOwnedHub(oldHub);
                if (oldHubId != 0)
                {
                    _retiredHubIds.Remove(oldHubId);
                    _retiredSerials.Remove(oldHubId);
                    _ownedHubs.Remove(oldHubId);
                }
            });

            return true;
        }

        public bool ShouldCancelRagdoll(Player? player)
        {
            if (player?.ReferenceHub == null)
            {
                return false;
            }

            int hubId = player.ReferenceHub.GetInstanceID();
            return _retiredHubIds.Contains(hubId) || _ownedHubs.ContainsKey(hubId) || IsOwnedBot(player);
        }

        public void ObserveShot(PlayerShotWeaponEventArgs ev)
        {
            if (!_running || ev?.Player?.ReferenceHub == null || ev.FirearmItem == null ||
                !_registry.TryByHub(ev.Player.ReferenceHub.GetInstanceID(), out RangeBotIdentity identity) ||
                !_registry.IsCurrent(identity.SlotId, identity.SpawnGeneration) ||
                !_slots.TryGetValue(identity.SlotId, out SlotRuntime slot) ||
                !slot.AwaitingShotVerification || ev.FirearmItem.Serial != slot.FirearmSerial)
            {
                return;
            }

            slot.ObservedShotEvent = true;
        }

        public void ClearAggroFor(string userKey)
        {
            if (string.IsNullOrEmpty(userKey))
            {
                return;
            }

            foreach (SlotRuntime slot in _slots.Values)
            {
                if (slot.Lifecycle.ClearAggroFor(userKey))
                {
                    ResetTacticalState(slot);
                }
            }
        }

        public void ClearAllAggro()
        {
            foreach (SlotRuntime slot in _slots.Values)
            {
                slot.Lifecycle.ClearAggro();
                ResetTacticalState(slot);
                if (slot.Lifecycle.State == RangeBotState.Alerted || slot.Lifecycle.State == RangeBotState.ReturningFire)
                {
                    slot.Lifecycle.MarkPassive();
                }
            }
        }

        public void Stop()
        {
            _running = false;
            // Snapshot ownership before touching native state. Round reset can destroy a hub or its action table
            // between any two calls, so teardown must detach every slot even when releasing one stale handle fails.
            List<ReferenceHub> hubs = _ownedHubs.Values.ToList();
            HashSet<ushort> serials = new HashSet<ushort>(_retiredSerials.Values.Where(serial => serial != 0));
            foreach (SlotRuntime slot in _slots.Values)
            {
                if (slot.FirearmSerial != 0)
                {
                    serials.Add(slot.FirearmSerial);
                }
            }

            _registry.InvalidateAll();
            foreach (SlotRuntime slot in _slots.Values)
            {
                try { ResetTacticalState(slot); } catch { }
                try { slot.Lifecycle.Disable(); } catch { }
                slot.Hub = null;
                slot.Player = null;
                slot.Firearm = null;
                slot.FirearmSerial = 0;
                slot.Preset = null;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                foreach (ushort serial in serials) RangeBotNative.DestroySerial(serial);
                foreach (ReferenceHub hub in hubs) RangeBotNative.DestroyOwnedHub(hub);
            }

            _slots.Clear();
            _ownedHubs.Clear();
            _retiredHubIds.Clear();
            _retiredSerials.Clear();
            _presets.Clear();
            _layout = null;
            _rangeGeneration = 0;
            _requestedBotCount = 0;
            // Counted dummy hubs must be gone before the solo lock opens. This preserves the invariant even if
            // teardown later gains a yielding step or a re-entrant callback observes the native connection count.
            ReleaseSoloLobbyLock();
        }

        private void BeginSpawn(SlotRuntime slot, double now)
        {
            if (!_running || _layout == null || slot.Lifecycle.State == RangeBotState.Disabled)
            {
                return;
            }

            CleanupSlotEntities(slot);
            int spawnGeneration = _registry.BeginSpawn(slot.Path.SlotId);
            slot.Lifecycle.BeginSpawn(spawnGeneration);

            int presetIndex = AimRangeDeterminism.SelectBotPresetIndex(
                _presets.Count,
                _mapSeed,
                _rangeGeneration,
                slot.Path.SlotId,
                slot.Lifecycle.SpawnOrdinal,
                _seedSalt);
            if (presetIndex < 0)
            {
                DisableSlot(slot, "no deterministic weapon preset");
                return;
            }

            slot.Preset = _presets[presetIndex];
            try
            {
                RangePoint start = slot.Path.Segments[0].From;
                Vector3 startPosition = WorldPathPoint(start);
                slot.Hub = DummyUtils.SpawnDummy($"AIM-RANGE-{slot.Path.SlotId + 1}") ??
                    throw new InvalidOperationException("DummyUtils.SpawnDummy returned null");
                int hubId = slot.Hub.GetInstanceID();
                _ownedHubs[hubId] = slot.Hub;
                slot.Hub.transform.position = startPosition + DummyLocalPosition + Vector3.down * 300f;
                slot.InitializeStage = InitializeStage.AwaitWrapper;
                slot.StageReadyAt = now + _initializeSeconds;
                slot.PathSegmentIndex = 0;
                slot.PathSegmentStartedAt = 0d;
                slot.LastProgressPosition = startPosition;
                slot.NextProgressCheckAt = 0d;
                slot.ReportedMovementStall = false;
                slot.NextShotAt = 0d;
                InitializeTacticalVariation(slot, now);
                ResetTacticalState(slot);
                slot.Lifecycle.MarkInitializing();
            }
            catch (Exception ex)
            {
                DisableSlot(slot, $"spawn failed: {ex.GetBaseException().Message}");
            }
        }

        private void TickInitializing(SlotRuntime slot, double now)
        {
            if (slot.Hub == null || slot.Hub.gameObject == null)
            {
                DisableSlot(slot, "owned hub disappeared during initialization");
                return;
            }

            if (slot.InitializeStage == InitializeStage.AwaitWrapper)
            {
                if (now < slot.StageReadyAt)
                {
                    return;
                }

                slot.Player = Player.Get(slot.Hub);
                if (slot.Player == null || slot.Player.IsDestroyed)
                {
                    DisableSlot(slot, "LabAPI dummy wrapper was unavailable");
                    return;
                }

                slot.Player.SetRole(_botRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                // Native role initialization grants this independently of RoleSpawnFlags.
                slot.Player.DisableEffect<CustomPlayerEffects.SpawnProtected>();
                slot.InitializeStage = InitializeStage.AwaitRole;
                slot.StageReadyAt = now + 0.2d;
                return;
            }

            if (slot.InitializeStage == InitializeStage.AwaitRole)
            {
                if (now < slot.StageReadyAt)
                {
                    return;
                }

                if (slot.Player == null || slot.Player.Role != _botRole || slot.Player.RoleBase is not IFpcRole)
                {
                    DisableSlot(slot, $"configured role {_botRole} did not initialize as FPC");
                    return;
                }

                slot.Player.ClearInventory();
                slot.Player.DisableEffect<CustomPlayerEffects.SpawnProtected>();
                slot.Player.MaxHealth = _botHealth;
                slot.Player.Health = _botHealth;
                Vector3 startPosition = WorldPathPoint(slot.Path.Segments[0].From);
                slot.Player.Position = startPosition + DummyLocalPosition;
                slot.Player.Rotation = InitialPathFacing(slot);
                if (!ConfigureFirearm(slot))
                {
                    DisableSlot(slot, "native firearm setup failed");
                    return;
                }

                RangeBotIdentity identity = new RangeBotIdentity(
                    slot.Path.SlotId,
                    slot.Lifecycle.SpawnGeneration,
                    slot.Hub.GetInstanceID(),
                    slot.Player.NetworkId,
                    slot.Player.PlayerId);
                if (!_registry.Register(identity))
                {
                    DisableSlot(slot, "spawn generation became stale before registration");
                    return;
                }

                slot.InitializeStage = InitializeStage.AwaitFirearmActions;
                slot.StageReadyAt = now + FirearmDrawSeconds;
                slot.FirearmActionDeadline = slot.StageReadyAt + FirearmActionPollSeconds;
                return;
            }

            if (slot.InitializeStage == InitializeStage.AwaitFirearmActions)
            {
                if (slot.Hub == null || slot.Firearm == null || slot.FirearmSerial == 0)
                {
                    DisableSlot(slot, "equipped firearm disappeared during native action initialization");
                    return;
                }

                if (slot.Hub.inventory.CurInstance?.ItemSerial != slot.FirearmSerial)
                {
                    slot.Hub.inventory.ServerSelectItem(slot.FirearmSerial);
                    if (now >= slot.FirearmActionDeadline)
                    {
                        DisableSlot(slot, "native firearm never became the equipped item");
                    }

                    return;
                }

                if (now >= slot.StageReadyAt && HasInitialNativeActions(slot.Hub))
                {
                    slot.PathSegmentIndex = 0;
                    slot.PathSegmentStartedAt = Math.Max(0.001d, now);
                    slot.LastProgressPosition = slot.Hub.transform.position;
                    slot.NextProgressCheckAt = now + MovementProgressCheckSeconds;
                    slot.NextShotAt = now;
                    slot.Lifecycle.MarkPassive();
                    _plugin.LogDebug($"Aim bot slot {slot.Path.SlotId} initialized with {slot.Preset?.Id} generation {slot.Lifecycle.SpawnGeneration}.");
                    return;
                }

                if (now >= slot.FirearmActionDeadline)
                {
                    DisableSlot(slot, "equipped firearm never exposed Shoot->Hold, Reload->Click, Zoom->Hold, and Jump");
                }
            }
        }

        private bool ConfigureFirearm(SlotRuntime slot)
        {
            if (slot.Player == null || !AimWeaponPresetRules.IsBotAutomatic(slot.Preset))
            {
                return false;
            }

            Item? added = slot.Player.AddItem(slot.Preset!.Firearm);
            if (added is not FirearmItem firearm || firearm.AmmoType != slot.Preset.Ammo)
            {
                return false;
            }

            if (slot.Preset.AttachmentsCode != 0)
            {
                if (!firearm.CheckAttachmentsCode(slot.Preset.AttachmentsCode))
                {
                    return false;
                }

                firearm.AttachmentsCode = slot.Preset.AttachmentsCode;
                if (firearm.AttachmentsCode != slot.Preset.AttachmentsCode)
                {
                    return false;
                }
            }

            slot.Firearm = firearm;
            slot.FirearmSerial = firearm.Serial;
            slot.Player.SetAmmo(slot.Preset.Ammo, (ushort)Math.Max(0, Math.Min(ushort.MaxValue, slot.Preset.ReserveAmmo)));
            slot.Hub!.inventory.ServerSelectItem(slot.FirearmSerial);
            return true;
        }

        private Vector3 WorldPathPoint(RangePoint point)
        {
            if (_layout == null)
            {
                throw new InvalidOperationException("Aim range layout is unavailable");
            }

            return _layout.MovingTargetOrigin + new Vector3(point.X, point.Y, point.Z);
        }

        private Quaternion InitialPathFacing(SlotRuntime slot)
        {
            if (_layout == null)
            {
                return Quaternion.identity;
            }

            Vector3 start = WorldPathPoint(slot.Path.Segments[0].From);
            Vector3 direction = PassiveFacingPoint() - start;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.identity;
        }

        private void TickMovement(SlotRuntime slot, double now)
        {
            if (_layout == null || slot.Hub == null || slot.Hub.gameObject == null ||
                slot.Path.Segments.Count == 0 || slot.PathSegmentStartedAt <= 0d)
            {
                return;
            }

            int index = slot.PathSegmentIndex % slot.Path.Segments.Count;
            RangePathSegment segment = slot.Path.Segments[index];
            Vector3 destination = WorldPathPoint(segment.To);
            float remaining = RangeBotNative.FlatDistance(slot.Hub, destination);
            if (remaining <= PathReachedRadius)
            {
                AdvancePathSegment(slot, now);
                destination = WorldPathPoint(slot.Path.Segments[slot.PathSegmentIndex].To);
                remaining = RangeBotNative.FlatDistance(slot.Hub, destination);
            }

            bool moving = RangeBotNative.WalkTowards(slot.Hub, destination, NativeWalkStepMeters);
            RangeBotNative.ApplyLook(slot.Hub, PassiveFacingPoint());
            if (moving)
            {
                TryJumpWhileMoving(slot, now, float.PositiveInfinity);
                CheckMovementProgress(slot, destination, remaining, now);
            }
        }

        private void AdvancePathSegment(SlotRuntime slot, double now)
        {
            slot.PathSegmentIndex = (slot.PathSegmentIndex + 1) % slot.Path.Segments.Count;
            slot.PathSegmentStartedAt = now;
            slot.LastProgressPosition = slot.Hub != null ? slot.Hub.transform.position : Vector3.zero;
            slot.NextProgressCheckAt = now + MovementProgressCheckSeconds;
            slot.ReportedMovementStall = false;
        }

        private static void CheckMovementProgress(SlotRuntime slot, Vector3 destination, float remaining, double now)
        {
            if (slot.Hub == null || now < slot.NextProgressCheckAt)
            {
                return;
            }

            Vector3 movement = slot.Hub.transform.position - slot.LastProgressPosition;
            movement.y = 0f;
            float moved = movement.magnitude;
            if (remaining > PathReachedRadius && moved < MovementProgressMinimumMeters)
            {
                if (!slot.ReportedMovementStall)
                {
                    slot.ReportedMovementStall = true;
                    Logger.Warn(
                        $"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} native walk stalled " +
                        $"({moved:0.###} m in {MovementProgressCheckSeconds:0.#} s, {remaining:0.##} m remaining). " +
                        "ReceivedPosition targets are stepped to avoid the native distant-target freeze trap.");
                }
            }
            else if (moved >= MovementProgressMinimumMeters)
            {
                slot.ReportedMovementStall = false;
            }

            slot.LastProgressPosition = slot.Hub.transform.position;
            slot.NextProgressCheckAt = now + MovementProgressCheckSeconds;
        }

        private void TickRetaliation(SlotRuntime slot, double now)
        {
            if ((slot.Lifecycle.State != RangeBotState.Alerted && slot.Lifecycle.State != RangeBotState.ReturningFire) ||
                slot.Hub == null || slot.Player == null || slot.Firearm == null || slot.FirearmSerial == 0)
            {
                ResetTacticalState(slot);
                return;
            }

            Player? target = ResolveCurrentAggressor(slot);
            if (target == null)
            {
                slot.Lifecycle.MarkPassive();
                ResetTacticalState(slot);
                return;
            }

            // Preserve a short readable reaction beat before snapping to the torso. Alert-phase strafing remains
            // paused so the bot cannot lose the provoking sightline before native automatic fire is held.
            if (slot.Lifecycle.State == RangeBotState.Alerted)
            {
                ReleaseHeldActions(slot);
                return;
            }

            Vector3 aimPoint = RangeBotNative.AimPoint(target.ReferenceHub);
            float distance = Vector3.Distance(slot.Hub.PlayerCameraReference.position, aimPoint);
            RangeBotNative.ApplyLook(slot.Hub, aimPoint);

            if (slot.Hub.inventory.CurInstance?.ItemSerial != slot.FirearmSerial)
            {
                ReleaseHeldActions(slot);
                slot.Hub.inventory.ServerSelectItem(slot.FirearmSerial);
                slot.NextShotAt = now + FirearmDrawSeconds;
                return;
            }

            if (!TryCompleteShotVerification(slot, now))
            {
                return;
            }

            int loaded = RangeBotNative.FirearmAmmoUnits(slot.Firearm);
            if (slot.ReloadStage != ReloadTacticalStage.None || slot.Firearm.IsReloadingOrUnloading || loaded <= 3 ||
                (!slot.Firearm.OpenBolt && (!slot.Firearm.Cocked || slot.Firearm.BoltLocked)))
            {
                TickReloadTactics(slot, target, aimPoint, distance, loaded, now);
                return;
            }

            // Native movement and automatic-fire dispersion are substantial even at short range. Hold ADS
            // throughout retaliation so a stationary participant is punished consistently, while the authored
            // upper-chest aim point still keeps every shot away from the head collider.
            SetZoomHeld(slot, true);
            bool canFire = slot.Lifecycle.State == RangeBotState.ReturningFire && distance <= _maxDistance &&
                RangeBotNative.HasLineOfSight(slot.Hub, target.ReferenceHub, aimPoint);
            if (!canFire)
            {
                ReleaseShoot(slot, cancelVerification: true);
                slot.NextShotAt = now + Math.Min(0.25d, _shotCadence);
                if (slot.HasFiredThisEngagement)
                {
                    TickEngagedMovement(slot, aimPoint, distance, now);
                }
                return;
            }

            float aimError = RangeBotNative.AimErrorDegrees(slot.Hub, aimPoint);
            if (slot.ShootHeld)
            {
                if (aimError > _aimTolerance)
                {
                    ReleaseShoot(slot, cancelVerification: true);
                }

                if (slot.HasFiredThisEngagement)
                {
                    TickEngagedMovement(slot, aimPoint, distance, now);
                }

                return;
            }

            if (now < slot.NextShotAt || aimError > _aimTolerance)
            {
                if (slot.HasFiredThisEngagement)
                {
                    TickEngagedMovement(slot, aimPoint, distance, now);
                }
                return;
            }

            slot.AmmoBeforeShot = loaded;
            slot.ObservedShotEvent = false;
            slot.AwaitingShotVerification = true;
            slot.ShotVerificationAt = now + _shotVerificationSeconds;
            if (!TryHoldShoot(slot))
            {
                slot.AwaitingShotVerification = false;
                slot.Lifecycle.MarkRetaliationUnavailable();
                ResetTacticalState(slot);
                Logger.Warn($"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} retaliation disabled: native Shoot->Hold unavailable.");
            }
        }

        private void TickEngagedMovement(SlotRuntime slot, Vector3 aimPoint, float distance, double now)
        {
            if (slot.Hub == null)
            {
                return;
            }

            if (slot.EngagementStartedAt <= 0d)
            {
                BeginEngagement(slot, now);
            }

            Vector3 forward = aimPoint - slot.Hub.transform.position;
            forward.y = 0f;
            Vector3 lateral = forward.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, forward.normalized) * slot.TacticalSide
                : Vector3.right * slot.TacticalSide;
            float wave = Mathf.Sin((float)((now - slot.EngagementStartedAt) * 2.6d) + slot.TacticalPhase);
            Vector3 destination = slot.CombatAnchor + lateral * (CombatStrafeRadius * wave);
            bool moving = RangeBotNative.WalkTowards(slot.Hub, destination, CombatWalkStepMeters);
            if (moving)
            {
                TryJumpWhileMoving(slot, now, distance);
            }
        }

        private void TickReloadTactics(
            SlotRuntime slot,
            Player target,
            Vector3 aimPoint,
            float attackerDistance,
            int loaded,
            double now)
        {
            if (slot.Hub == null || slot.Firearm == null)
            {
                ResetTacticalState(slot);
                return;
            }

            ReleaseHeldActions(slot);
            if (slot.ReloadStage == ReloadTacticalStage.None)
            {
                slot.ReloadStage = ReloadTacticalStage.MovingToCover;
                slot.ReloadCover = FindNearestReloadCover(slot);
                slot.CoverReachedAt = 0d;
                slot.ReloadInvoked = false;
                slot.ReloadStarted = false;
                slot.ReloadAttempts = 0;
                slot.NextReloadAttemptAt = now;
            }

            bool atCover = false;
            bool moving = false;
            if (slot.ReloadCover is AimBotCover cover)
            {
                float remaining = RangeBotNative.FlatDistance(slot.Hub, cover.ReloadPoint);
                atCover = remaining <= CoverReachedRadius;
                if (!atCover)
                {
                    moving = RangeBotNative.WalkTowards(slot.Hub, cover.ReloadPoint, NativeWalkStepMeters);
                }
                else
                {
                    if (slot.CoverReachedAt <= 0d)
                    {
                        slot.CoverReachedAt = now;
                    }

                    float amplitude = Mathf.Min(ReloadStrafeRadius, Mathf.Max(0.12f, cover.Size.x * 0.2f));
                    float wave = Mathf.Sin((float)(now * 3.1d) + slot.TacticalPhase);
                    Vector3 protectedStep = cover.ReloadPoint + Vector3.right * (amplitude * wave * slot.TacticalSide);
                    moving = RangeBotNative.WalkTowards(slot.Hub, protectedStep, NativeWalkStepMeters * 0.5f);
                }
            }

            if (moving)
            {
                TryJumpWhileMoving(slot, now, attackerDistance);
            }

            RangeBotNative.ApplyLook(slot.Hub, aimPoint);
            if (slot.Firearm.IsReloadingOrUnloading)
            {
                slot.ReloadStarted = true;
                slot.ReloadStage = ReloadTacticalStage.Reloading;
                return;
            }

            if (slot.ReloadStarted)
            {
                ResetReload(slot);
                slot.NextShotAt = now + 0.1d;
                return;
            }

            bool coverBlocksLos = atCover &&
                !RangeBotNative.HasLineOfSight(target.ReferenceHub, slot.Hub, RangeBotNative.AimPoint(slot.Hub));
            bool coverGraceElapsed = atCover && slot.CoverReachedAt > 0d && now - slot.CoverReachedAt >= CoverLosGraceSeconds;
            bool reloadNow = slot.ReloadCover == null || (atCover && (coverBlocksLos || coverGraceElapsed));
            if (!reloadNow || (slot.ReloadInvoked && now < slot.NextReloadAttemptAt))
            {
                return;
            }

            if (!RangeBotNative.TryInvokeDummyAction(slot.Hub, "Reload->Click"))
            {
                slot.Lifecycle.MarkRetaliationUnavailable();
                ResetTacticalState(slot);
                Logger.Warn($"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} retaliation disabled: native Reload->Click unavailable.");
                return;
            }

            slot.ReloadInvoked = true;
            slot.ReloadAttempts++;
            slot.NextReloadAttemptAt = now + ReloadRetrySeconds;
            if (slot.ReloadAttempts >= 4)
            {
                slot.Lifecycle.MarkRetaliationUnavailable();
                ResetTacticalState(slot);
                Logger.Warn($"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} retaliation disabled: native reload never entered a reloading state.");
            }
        }

        private AimBotCover? FindNearestReloadCover(SlotRuntime slot)
        {
            if (_layout == null || slot.Hub == null)
            {
                return null;
            }

            AimBotCover? nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (AimBotCover cover in _layout.BotCovers)
            {
                if (cover.SlotId != slot.Path.SlotId || !cover.FullHeight)
                {
                    continue;
                }

                float distance = (cover.ReloadPoint - slot.Hub.transform.position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = cover;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private bool TryCompleteShotVerification(SlotRuntime slot, double now)
        {
            if (!slot.AwaitingShotVerification || slot.Firearm == null)
            {
                return true;
            }

            int ammoAfter = RangeBotNative.FirearmAmmoUnits(slot.Firearm);
            if (slot.ObservedShotEvent && slot.AmmoBeforeShot >= 0 && ammoAfter < slot.AmmoBeforeShot)
            {
                slot.AwaitingShotVerification = false;
                slot.ObservedShotEvent = false;
                slot.HasFiredThisEngagement = true;
                // Verification proves the native automatic input is live. Keep Shoot->Hold latched; LOS, aim,
                // reload, lease loss, death, and teardown remain the only release paths.
                return true;
            }

            if (now < slot.ShotVerificationAt)
            {
                return true;
            }

            ReleaseShoot(slot, cancelVerification: true);
            slot.Lifecycle.MarkRetaliationUnavailable();
            ResetTacticalState(slot);
            Logger.Warn($"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} retaliation disabled: native ShotWeapon/ammo verification failed ({slot.AmmoBeforeShot}->{ammoAfter}).");
            return false;
        }

        private void BeginEngagement(SlotRuntime slot, double now)
        {
            ResetTacticalState(slot, clearAggressorHub: false);
            slot.CombatAnchor = slot.Hub != null ? slot.Hub.transform.position : Vector3.zero;
            slot.EngagementStartedAt = Math.Max(0.001d, now);
            slot.NextShotAt = now;
        }

        private void InitializeTacticalVariation(SlotRuntime slot, double now)
        {
            float unit = DeterministicUnit(slot, 0);
            slot.TacticalPhase = unit * Mathf.PI * 2f;
            slot.TacticalSide = DeterministicUnit(slot, 1) < 0.5f ? -1 : 1;
            slot.JumpOrdinal = 0;
            slot.NextJumpAt = now + 0.35d + DeterministicUnit(slot, 2) * 1.4d;
        }

        private static float DeterministicUnit(SlotRuntime slot, int salt)
        {
            unchecked
            {
                uint value = (uint)(slot.Path.SlotId + 1) * 2654435761u;
                value ^= (uint)(slot.Lifecycle.SpawnGeneration + 17) * 2246822519u;
                value ^= (uint)(slot.Lifecycle.SpawnOrdinal + 31) * 3266489917u;
                value ^= (uint)(salt + 47) * 668265263u;
                value ^= value >> 15;
                value *= 2246822519u;
                value ^= value >> 13;
                return (value & 0x00ffffffu) / 16777215f;
            }
        }

        private void TryJumpWhileMoving(SlotRuntime slot, double now, float attackerDistance)
        {
            if (slot.Hub == null || now < slot.NextJumpAt)
            {
                return;
            }

            if (!RangeBotNative.TryInvokeDummyAction(slot.Hub, "Jump"))
            {
                slot.NextJumpAt = now + NormalJumpCooldownSeconds;
                return;
            }

            slot.JumpOrdinal++;
            double cooldown = attackerDistance < CloseJumpDistanceMeters
                ? CloseJumpCooldownSeconds
                : NormalJumpCooldownSeconds;
            slot.NextJumpAt = now + cooldown + DeterministicUnit(slot, slot.JumpOrdinal + 10) * cooldown * 0.2d;
        }

        private Vector3 PassiveFacingPoint()
        {
            if (_layout == null)
            {
                return Vector3.forward;
            }

            return _layout.EntranceSpawn + Vector3.up * 1.35f;
        }

        private static bool HasInitialNativeActions(ReferenceHub hub) =>
            RangeBotNative.HasDummyAction(hub, "Shoot->Hold") &&
            RangeBotNative.HasDummyAction(hub, "Reload->Click") &&
            RangeBotNative.HasDummyAction(hub, "Zoom->Hold") &&
            RangeBotNative.HasDummyAction(hub, "Jump");

        private bool TryHoldShoot(SlotRuntime slot)
        {
            if (slot.Hub == null)
            {
                return false;
            }

            if (slot.ShootHeld || RangeBotNative.HasDummyAction(slot.Hub, "Shoot->Release"))
            {
                slot.ShootHeld = true;
                return true;
            }

            if (!RangeBotNative.TryInvokeDummyAction(slot.Hub, "Shoot->Hold"))
            {
                return false;
            }

            slot.ShootHeld = true;
            return true;
        }

        private bool TryHoldZoom(SlotRuntime slot)
        {
            if (slot.Hub == null)
            {
                return false;
            }

            if (slot.ZoomHeld || RangeBotNative.HasDummyAction(slot.Hub, "Zoom->Release"))
            {
                slot.ZoomHeld = true;
                return true;
            }

            if (!RangeBotNative.TryInvokeDummyAction(slot.Hub, "Zoom->Hold"))
            {
                return false;
            }

            slot.ZoomHeld = true;
            return true;
        }

        private void SetZoomHeld(SlotRuntime slot, bool shouldHold)
        {
            if (shouldHold)
            {
                if (!TryHoldZoom(slot))
                {
                    slot.Lifecycle.MarkRetaliationUnavailable();
                    ResetTacticalState(slot);
                    Logger.Warn($"[WarmupScpSelector] Aim bot slot {slot.Path.SlotId} retaliation disabled: native Zoom->Hold unavailable.");
                }

                return;
            }

            ReleaseZoom(slot);
        }

        private void ReleaseHeldActions(SlotRuntime slot)
        {
            ReleaseShoot(slot, cancelVerification: true);
            ReleaseZoom(slot);
        }

        private void ReleaseShoot(SlotRuntime slot, bool cancelVerification)
        {
            if (slot.Hub == null || slot.Hub.gameObject == null)
            {
                slot.ShootHeld = false;
            }
            else if (slot.ShootHeld || RangeBotNative.HasDummyAction(slot.Hub, "Shoot->Release"))
            {
                if (RangeBotNative.TryInvokeDummyAction(slot.Hub, "Shoot->Release"))
                {
                    slot.ShootHeld = false;
                }
            }

            if (cancelVerification)
            {
                slot.AwaitingShotVerification = false;
                slot.ObservedShotEvent = false;
            }
        }

        private void ReleaseZoom(SlotRuntime slot)
        {
            if (slot.Hub == null || slot.Hub.gameObject == null)
            {
                slot.ZoomHeld = false;
            }
            else if (slot.ZoomHeld || RangeBotNative.HasDummyAction(slot.Hub, "Zoom->Release"))
            {
                if (RangeBotNative.TryInvokeDummyAction(slot.Hub, "Zoom->Release"))
                {
                    slot.ZoomHeld = false;
                }
            }
        }

        private static void ResetReload(SlotRuntime slot)
        {
            slot.ReloadStage = ReloadTacticalStage.None;
            slot.ReloadCover = null;
            slot.CoverReachedAt = 0d;
            slot.ReloadInvoked = false;
            slot.ReloadStarted = false;
            slot.ReloadAttempts = 0;
            slot.NextReloadAttemptAt = 0d;
        }

        private void ResetTacticalState(SlotRuntime slot, bool clearAggressorHub = true)
        {
            ReleaseHeldActions(slot);
            slot.AwaitingShotVerification = false;
            slot.ObservedShotEvent = false;
            slot.AmmoBeforeShot = -1;
            slot.ShotVerificationAt = 0d;
            slot.HasFiredThisEngagement = false;
            slot.CombatAnchor = Vector3.zero;
            slot.EngagementStartedAt = 0d;
            ResetReload(slot);
            if (clearAggressorHub)
            {
                slot.AggressorHub = null;
            }
        }

        private Player? ResolveCurrentAggressor(SlotRuntime slot)
        {
            if (!slot.Lifecycle.HasAggressor ||
                !_sessions.TryGet(slot.Lifecycle.AggressorKey, out AimRangeSessions.Session session) ||
                session.Token != slot.Lifecycle.AggressorSessionToken || session.IsLeaving ||
                !_activities.IsCurrent(session.UserKey, LaneHintIds.Aim, session.Token))
            {
                return null;
            }

            ReferenceHub? hub = slot.AggressorHub;
            Player? player = hub != null && hub.gameObject != null ? Player.Get(hub) : null;
            if (player == null || !_isEligibleHuman(player) ||
                !string.Equals(_key(player), session.UserKey, StringComparison.Ordinal) ||
                player.LifeId != slot.Lifecycle.AggressorLifeId)
            {
                return null;
            }

            return player;
        }

        private int CurrentAllowedBotCount()
        {
            try
            {
                if (!_slots.Values.Any(slot => !slot.PermanentlyDisabled))
                {
                    ReleaseSoloLobbyLock();
                    return 0;
                }

                int canonicalHumans = Player.ReadyList.Count(player => _isCanonicalHuman(player));
                int countedConnections = ReferenceHub.GetPlayerCount(
                    ClientInstanceMode.ReadyClient,
                    ClientInstanceMode.Host,
                    ClientInstanceMode.Dummy);
                int countedWithoutOwnedBots = Math.Max(0, countedConnections - _ownedHubs.Count);
                if (Server.MaxPlayers - countedWithoutOwnedBots - 1 <= 0)
                {
                    ReleaseSoloLobbyLock();
                    return 0;
                }

                bool soloLobbyProtected = UpdateSoloLobbyProtection(canonicalHumans);
                return RangeBotLobbyPolicy.AllowedBotCount(
                    _requestedBotCount,
                    canonicalHumans,
                    countedWithoutOwnedBots,
                    Server.MaxPlayers,
                    soloLobbyProtected);
            }
            catch (Exception ex)
            {
                ReleaseSoloLobbyLock();
                Logger.Warn($"[WarmupScpSelector] Aim bot lobby safety check failed closed: {ex.Message}");
                return 0;
            }
        }

        private void EnforceLobbySafety(double now)
        {
            int allowed = CurrentAllowedBotCount();
            List<SlotRuntime> ordered = _slots.Values.OrderBy(slot => slot.Path.SlotId).ToList();
            int remaining = allowed;
            foreach (SlotRuntime slot in ordered)
            {
                bool shouldRun = remaining > 0 && !slot.PermanentlyDisabled;
                if (shouldRun)
                {
                    remaining--;
                }

                if (shouldRun && slot.Lifecycle.State == RangeBotState.Disabled)
                {
                    slot.Lifecycle.Enable();
                    BeginSpawn(slot, now);
                }
                else if (!shouldRun && slot.Lifecycle.State != RangeBotState.Disabled)
                {
                    DisableSlot(slot, "canonical-human or public-slot lobby safety threshold was lost", permanent: false);
                }
            }
        }

        private bool UpdateSoloLobbyProtection(int canonicalHumans)
        {
            if (_requestedBotCount <= 0 || canonicalHumans != 1)
            {
                ReleaseSoloLobbyLock();
                return canonicalHumans > 1;
            }

            return TryAcquireSoloLobbyLock();
        }

        private bool TryAcquireSoloLobbyLock()
        {
            if (Round.IsLobbyLocked)
            {
                return true;
            }

            _soloLobbyPriorLock = false;
            Round.IsLobbyLocked = true;
            _ownsSoloLobbyLock = Round.IsLobbyLocked;
            if (_ownsSoloLobbyLock)
            {
                Logger.Info("[WarmupScpSelector] Aim bots temporarily locked the solo lobby; the lock releases when another human joins or Aim stops.");
            }

            return _ownsSoloLobbyLock;
        }

        private void ReleaseSoloLobbyLock()
        {
            if (!_ownsSoloLobbyLock)
            {
                return;
            }

            try
            {
                Round.IsLobbyLocked = _soloLobbyPriorLock;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Aim bot solo-lobby lock restore failed: {ex.Message}");
            }
            finally
            {
                _ownsSoloLobbyLock = false;
                _soloLobbyPriorLock = false;
            }
        }

        private void DisableSlot(SlotRuntime slot, string reason, bool permanent = true)
        {
            int slotId = slot.Path.SlotId;
            _registry.InvalidateSlot(slotId);
            CleanupSlotEntities(slot);
            slot.Lifecycle.Disable();
            slot.PermanentlyDisabled |= permanent;
            if (permanent)
            {
                Logger.Warn($"[WarmupScpSelector] Aim bot slot {slotId} disabled for this warmup: {reason}.");
            }
        }

        private void CleanupSlotEntities(SlotRuntime slot)
        {
            ResetTacticalState(slot);
            ReferenceHub? hub = slot.Hub;
            int hubId = hub?.GetInstanceID() ?? 0;
            RangeBotNative.DestroySerial(slot.FirearmSerial);
            RangeBotNative.DestroyOwnedHub(hub);
            if (hubId != 0)
            {
                _ownedHubs.Remove(hubId);
                _retiredHubIds.Remove(hubId);
            }

            slot.Hub = null;
            slot.Player = null;
            slot.Firearm = null;
            slot.FirearmSerial = 0;
            slot.Preset = null;
            slot.ShootHeld = false;
            slot.ZoomHeld = false;
            slot.PathSegmentIndex = 0;
            slot.PathSegmentStartedAt = 0d;
            slot.LastProgressPosition = Vector3.zero;
            slot.NextProgressCheckAt = 0d;
            slot.ReportedMovementStall = false;
            slot.AwaitingShotVerification = false;
            slot.ObservedShotEvent = false;
            slot.AggressorHub = null;
        }

    }
}
