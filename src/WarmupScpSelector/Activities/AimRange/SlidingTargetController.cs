using System;
using System.Collections.Generic;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>A current activity-session snapshot supplied by the owning lane.</summary>
    public readonly struct SlidingTargetParticipantToken
    {
        public SlidingTargetParticipantToken(string userKey, int sessionToken, ushort ownedWeaponSerial)
        {
            UserKey = userKey ?? string.Empty;
            SessionToken = sessionToken;
            OwnedWeaponSerial = ownedWeaponSerial;
        }

        public string UserKey { get; }
        public int SessionToken { get; }
        public ushort OwnedWeaponSerial { get; }
        public bool IsValid => UserKey.Length > 0 && SessionToken > 0 && OwnedWeaponSerial != 0;
    }

    /// <summary>Immutable credit/feedback payload for one accepted hit.</summary>
    public readonly struct SlidingTargetHit
    {
        internal SlidingTargetHit(
            Player player,
            SlidingTargetParticipantToken participant,
            int slotId,
            int rangeGeneration,
            int controllerGeneration,
            int targetGeneration,
            double elapsedSeconds,
            Vector3 targetPosition)
        {
            Player = player;
            Participant = participant;
            SlotId = slotId;
            RangeGeneration = rangeGeneration;
            ControllerGeneration = controllerGeneration;
            TargetGeneration = targetGeneration;
            ElapsedSeconds = elapsedSeconds;
            TargetPosition = targetPosition;
        }

        public Player Player { get; }
        public SlidingTargetParticipantToken Participant { get; }
        public int SlotId { get; }
        public int RangeGeneration { get; }
        public int ControllerGeneration { get; }
        public int TargetGeneration { get; }
        public double ElapsedSeconds { get; }
        public Vector3 TargetPosition { get; }
    }

    public enum SlidingTargetHitDisposition
    {
        NotOwned,
        Rejected,
        Credited,
        CallbackFailed,
    }

    /// <summary>Integration delegates kept outside the controller's activity/session implementation.</summary>
    public sealed class SlidingTargetCallbacks
    {
        public SlidingTargetCallbacks(
            Func<Player, SlidingTargetParticipantToken?> resolveParticipant,
            Func<Player, SlidingTargetParticipantToken, bool> isCurrentParticipant,
            Func<Player, SlidingTargetParticipantToken, bool> hasCurrentOwnedRangeGun,
            Action<SlidingTargetHit> onValidHit,
            Action<string>? warn = null)
        {
            ResolveParticipant = resolveParticipant ?? throw new ArgumentNullException(nameof(resolveParticipant));
            IsCurrentParticipant = isCurrentParticipant ?? throw new ArgumentNullException(nameof(isCurrentParticipant));
            HasCurrentOwnedRangeGun = hasCurrentOwnedRangeGun ?? throw new ArgumentNullException(nameof(hasCurrentOwnedRangeGun));
            OnValidHit = onValidHit ?? throw new ArgumentNullException(nameof(onValidHit));
            Warn = warn;
        }

        public Func<Player, SlidingTargetParticipantToken?> ResolveParticipant { get; }
        public Func<Player, SlidingTargetParticipantToken, bool> IsCurrentParticipant { get; }
        public Func<Player, SlidingTargetParticipantToken, bool> HasCurrentOwnedRangeGun { get; }
        public Action<SlidingTargetHit> OnValidHit { get; }
        public Action<string>? Warn { get; }
    }

    /// <summary>
    /// Owns multiple simultaneous native targets. The lane supplies one shared scheduler tick and routes the native
    /// pre-damage event here. Accepted hits are credited synchronously, then native damage is cancelled so targets
    /// never lose client-side HP, lower, recycle, or enter a dead state.
    /// </summary>
    internal sealed class SlidingTargetController
    {
        private const byte RawMovementSmoothing = 60;
        private const float NetworkSyncInterval = 1f / 15f;

        private sealed class SlotRuntime
        {
            public SlotRuntime(SlidingTargetMotion motion, ShootingTargetToy target, int targetGeneration)
            {
                Motion = motion;
                Target = target;
                TargetGeneration = targetGeneration;
            }

            public SlidingTargetMotion Motion { get; }
            public ShootingTargetToy Target { get; }
            public int TargetGeneration { get; }
        }

        private readonly Dictionary<int, SlotRuntime> _slots = new Dictionary<int, SlotRuntime>();
        private readonly Dictionary<AdminToys.ShootingTarget, SlotRuntime> _byTarget =
            new Dictionary<AdminToys.ShootingTarget, SlotRuntime>();

        private SlidingTargetCallbacks? _callbacks;
        private int _rangeGeneration;
        private int _controllerGeneration;
        private int _targetGeneration;
        private double _lastElapsed;
        private bool _running;

        public bool IsRunning => _running;
        public int Count => _slots.Count;
        public int RangeGeneration => _rangeGeneration;
        public int ControllerGeneration => _controllerGeneration;

        public bool Start(
            int rangeGeneration,
            int mapSeed,
            int seedSalt,
            IReadOnlyList<SlidingTargetTrackDefinition> tracks,
            SlidingTargetCallbacks callbacks)
        {
            Stop();
            if (rangeGeneration <= 0 || tracks == null || tracks.Count == 0 || callbacks == null)
            {
                return false;
            }

            _rangeGeneration = rangeGeneration;
            _controllerGeneration = Next(_controllerGeneration);
            _callbacks = callbacks;
            _lastElapsed = 0d;

            List<SlidingTargetTrackDefinition> ordered = new List<SlidingTargetTrackDefinition>(tracks);
            ordered.Sort((left, right) => left.SlotId.CompareTo(right.SlotId));
            for (int ordinal = 0; ordinal < ordered.Count; ordinal++)
            {
                SlidingTargetTrackDefinition track = ordered[ordinal];
                if (_slots.ContainsKey(track.SlotId))
                {
                    Warn($"Sliding target slot {track.SlotId} was duplicated and the later definition was ignored.");
                    continue;
                }

                if (!SlidingTargetLogic.TryBuildMotion(
                        track,
                        mapSeed,
                        rangeGeneration,
                        ordinal,
                        seedSalt,
                        out SlidingTargetMotion motion))
                {
                    Warn($"Sliding target slot {track.SlotId} was disabled because its authored track was invalid.");
                    continue;
                }

                TrySpawn(motion);
            }

            _running = _slots.Count > 0;
            if (!_running)
            {
                _callbacks = null;
                _rangeGeneration = 0;
            }

            return _running;
        }

        /// <summary>Moves every live target from the same absolute scheduler elapsed value.</summary>
        public void Tick(double elapsedSeconds)
        {
            if (!_running)
            {
                return;
            }

            _lastElapsed = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d
                : Math.Max(0d, elapsedSeconds);
            foreach (SlotRuntime slot in new List<SlotRuntime>(_slots.Values))
            {
                if (!IsCurrent(slot))
                {
                    Retire(slot, destroy: false);
                    continue;
                }

                try
                {
                    slot.Target.Position = slot.Motion.Evaluate(_lastElapsed);
                    slot.Target.Rotation = slot.Motion.Track.Rotation;
                }
                catch (Exception ex)
                {
                    Warn($"Sliding target slot {slot.Motion.Track.SlotId} stopped after a movement failure: {ex.GetBaseException().Message}");
                    Retire(slot, destroy: true);
                }
            }

            if (_slots.Count == 0)
            {
                _running = false;
            }
        }

        /// <summary>
        /// Routes the cancellable pre-hit event. For every currently owned target this method sets IsAllowed=false
        /// before validation, preventing the native target RPC from subtracting HP. A valid current participant/gun
        /// invokes exactly one synchronous integration callback per shot.
        /// </summary>
        public SlidingTargetHitDisposition HandleDamaging(
            PlayerDamagingShootingTargetEventArgs ev,
            int expectedRangeGeneration)
        {
            if (ev?.ShootingTarget?.Base == null || !_byTarget.TryGetValue(ev.ShootingTarget.Base, out SlotRuntime slot))
            {
                return SlidingTargetHitDisposition.NotOwned;
            }

            // Keep every target persistent even when the player/session/gun validation below rejects the credit.
            ev.IsAllowed = false;
            int controllerGeneration = _controllerGeneration;
            int targetGeneration = slot.TargetGeneration;
            SlidingTargetCallbacks? callbacks = _callbacks;
            if (!_running || callbacks == null || expectedRangeGeneration != _rangeGeneration ||
                !IsCurrent(slot, controllerGeneration, targetGeneration) || ev.Player == null)
            {
                return SlidingTargetHitDisposition.Rejected;
            }

            try
            {
                SlidingTargetParticipantToken? resolved = callbacks.ResolveParticipant(ev.Player);
                if (!resolved.HasValue || !resolved.Value.IsValid)
                {
                    return SlidingTargetHitDisposition.Rejected;
                }

                SlidingTargetParticipantToken participant = resolved.Value;
                if (!callbacks.IsCurrentParticipant(ev.Player, participant) ||
                    !callbacks.HasCurrentOwnedRangeGun(ev.Player, participant))
                {
                    return SlidingTargetHitDisposition.Rejected;
                }

                // Re-check all generations and the activity token immediately before handing stat/feedback ownership
                // to integration. This also rejects re-entrant Stop/Start changes made by a validator.
                if (!IsCurrent(slot, controllerGeneration, targetGeneration) ||
                    !callbacks.IsCurrentParticipant(ev.Player, participant))
                {
                    return SlidingTargetHitDisposition.Rejected;
                }

                SlidingTargetHit hit = new SlidingTargetHit(
                    ev.Player,
                    participant,
                    slot.Motion.Track.SlotId,
                    _rangeGeneration,
                    controllerGeneration,
                    targetGeneration,
                    _lastElapsed,
                    slot.Target.Position);
                try
                {
                    callbacks.OnValidHit(hit);
                    return SlidingTargetHitDisposition.Credited;
                }
                catch (Exception ex)
                {
                    Warn($"Sliding target slot {slot.Motion.Track.SlotId} hit callback failed: {ex.GetBaseException().Message}");
                    return SlidingTargetHitDisposition.CallbackFailed;
                }
            }
            catch (Exception ex)
            {
                Warn($"Sliding target slot {slot.Motion.Track.SlotId} hit validation failed: {ex.GetBaseException().Message}");
                return SlidingTargetHitDisposition.Rejected;
            }
        }

        public bool Owns(ShootingTargetToy? target) =>
            target?.Base != null && _byTarget.ContainsKey(target.Base);

        /// <summary>Invalidates every generation before destroying toys. Repeated calls never throw.</summary>
        public void Stop()
        {
            _running = false;
            _rangeGeneration = 0;
            _controllerGeneration = Next(_controllerGeneration);
            _targetGeneration = Next(_targetGeneration);
            _callbacks = null;
            _lastElapsed = 0d;

            List<ShootingTargetToy> targets = new List<ShootingTargetToy>(_slots.Count);
            foreach (SlotRuntime slot in _slots.Values)
            {
                targets.Add(slot.Target);
            }

            _slots.Clear();
            _byTarget.Clear();
            foreach (ShootingTargetToy target in targets)
            {
                Destroy(target);
            }
        }

        private void TrySpawn(SlidingTargetMotion motion)
        {
            ShootingTargetToy? target = null;
            try
            {
                target = ShootingTargetToy.Create(
                    motion.Evaluate(0d),
                    motion.Track.Rotation,
                    motion.Track.Scale,
                    parent: null,
                    networkSpawn: false);
                target.IsGlobal = true;
                target.IsStatic = false;
                // The LabAPI wrapper stores 256-value; write the small raw value directly so clients
                // interpolate sparse server keyframes instead of snapping between scheduler updates.
                target.Base.NetworkMovementSmoothing = RawMovementSmoothing;
                target.SyncInterval = NetworkSyncInterval;
                target.Spawn();

                int generation = Next(_targetGeneration);
                _targetGeneration = generation;
                SlotRuntime slot = new SlotRuntime(motion, target, generation);
                _slots.Add(motion.Track.SlotId, slot);
                _byTarget.Add(target.Base, slot);
            }
            catch (Exception ex)
            {
                Warn($"Sliding target slot {motion.Track.SlotId} failed to spawn: {ex.GetBaseException().Message}");
                Destroy(target);
            }
        }

        private bool IsCurrent(SlotRuntime slot) =>
            IsCurrent(slot, _controllerGeneration, slot.TargetGeneration);

        private bool IsCurrent(SlotRuntime slot, int controllerGeneration, int targetGeneration)
        {
            return _running && controllerGeneration == _controllerGeneration && targetGeneration == slot.TargetGeneration &&
                _slots.TryGetValue(slot.Motion.Track.SlotId, out SlotRuntime current) && ReferenceEquals(current, slot) &&
                slot.Target != null && !slot.Target.IsDestroyed &&
                _byTarget.TryGetValue(slot.Target.Base, out SlotRuntime byTarget) && ReferenceEquals(byTarget, slot);
        }

        private void Retire(SlotRuntime slot, bool destroy)
        {
            if (_slots.TryGetValue(slot.Motion.Track.SlotId, out SlotRuntime current) && ReferenceEquals(current, slot))
            {
                _slots.Remove(slot.Motion.Track.SlotId);
            }

            if (slot.Target?.Base != null && _byTarget.TryGetValue(slot.Target.Base, out SlotRuntime byTarget) &&
                ReferenceEquals(byTarget, slot))
            {
                _byTarget.Remove(slot.Target.Base);
            }

            if (destroy)
            {
                Destroy(slot.Target);
            }
        }

        private static void Destroy(ShootingTargetToy? target)
        {
            try
            {
                if (target != null && !target.IsDestroyed)
                {
                    target.Destroy();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Sliding target cleanup failed: {ex.GetBaseException().Message}");
            }
        }

        private void Warn(string message)
        {
            try
            {
                if (_callbacks?.Warn != null)
                {
                    _callbacks.Warn(message);
                }
                else
                {
                    Logger.Warn("[WarmupScpSelector] " + message);
                }
            }
            catch
            {
                // Diagnostics must never destabilize start, tick, hit routing, or teardown.
            }
        }

        private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;
    }
}
