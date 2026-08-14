using System;
using System.Collections.Generic;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Firearms.Modules.Misc;
using LabApi.Features.Wrappers;
using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Activities.AimRange
{
    public readonly struct SphereTargetHit
    {
        internal SphereTargetHit(Player player, int slotId, int targetGeneration, int authoredPointIndex, Vector3 position)
        {
            Player = player;
            SlotId = slotId;
            TargetGeneration = targetGeneration;
            AuthoredPointIndex = authoredPointIndex;
            Position = position;
        }

        public Player Player { get; }
        public int SlotId { get; }
        public int TargetGeneration { get; }
        public int AuthoredPointIndex { get; }
        public Vector3 Position { get; }
    }

    /// <summary>
    /// Isolated lane-3 sphere-target runtime. It uses collidable PrimitiveObjectToy spheres and the native
    /// HitscanHitregModuleBase.ServerAnyPlayerFired result because LabAPI's ShotWeapon event has no raycast hit,
    /// while DamagingShootingTarget only applies to the rectangular native ShootingTarget prefab.
    ///
    /// Integration contract: construct with the lane's current-session, owned-firearm, shared-clock, and credit
    /// callbacks; call Start after the widened lane world exists; call Tick from AimRangeScheduler; call Stop before
    /// destroying the lane world or flipping warmup roles. The controller owns its native event subscription.
    /// </summary>
    internal sealed class SphereTargetController
    {
        private const double StartupSpawnRetrySeconds = 0.5d;

        private sealed class RuntimeSlot
        {
            public RuntimeSlot(int slotId)
            {
                SlotId = slotId;
            }

            public int SlotId { get; }
            public int Generation { get; set; }
            public int PointIndex { get; set; } = -1;
            public PrimitiveObjectToy? Toy { get; set; }
        }

        private readonly Func<Player, bool> _isCurrentParticipant;
        private readonly Func<Player, bool> _ownsCurrentRangeFirearm;
        private readonly Action<SphereTargetHit> _credit;
        private readonly Action<string>? _warn;
        private readonly SphereTargetState _state = new SphereTargetState();
        private readonly Dictionary<int, RuntimeSlot> _slots = new Dictionary<int, RuntimeSlot>();

        private SphereTargetLayout? _layout;
        private SphereTargetValidatedSettings _settings;
        private long _nextPointOrdinal;
        private int _rangeGeneration;
        private bool _running;
        private bool _subscribed;

        public SphereTargetController(
            Func<Player, bool> isCurrentParticipant,
            Func<Player, bool> ownsCurrentRangeFirearm,
            Action<SphereTargetHit> credit,
            Action<string>? warn = null)
        {
            _isCurrentParticipant = isCurrentParticipant ?? throw new ArgumentNullException(nameof(isCurrentParticipant));
            _ownsCurrentRangeFirearm = ownsCurrentRangeFirearm ?? throw new ArgumentNullException(nameof(ownsCurrentRangeFirearm));
            _credit = credit ?? throw new ArgumentNullException(nameof(credit));
            _warn = warn;
        }

        public int ActiveCount => _slots.Count;

        public bool IsRunning => _running;

        public void Start(
            int rangeGeneration,
            SphereTargetLayout layout,
            SphereTargetSettings settings,
            double now)
        {
            Stop();
            if (rangeGeneration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rangeGeneration));
            }

            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            if (_layout.Points.Count < 2)
            {
                throw new ArgumentException("Sphere target layout requires at least two points.", nameof(layout));
            }

            _settings = (settings ?? new SphereTargetSettings()).Validate(_layout.Points.Count);
            _nextPointOrdinal = 0L;
            _rangeGeneration = rangeGeneration;
            _state.Start(rangeGeneration, _settings.ActiveCount, now);
            for (int slotId = 0; slotId < _settings.ActiveCount; slotId++)
            {
                _slots.Add(slotId, new RuntimeSlot(slotId));
            }

            Subscribe();
            _running = true;
            Tick(now);
        }

        /// <summary>Called by the existing shared AimRangeScheduler; this class starts no coroutine.</summary>
        public void Tick(double now)
        {
            if (!_running || _layout == null)
            {
                return;
            }

            IReadOnlyList<int> due = _state.GetDueSlots(_rangeGeneration, now);
            HashSet<int> occupied = new HashSet<int>();
            foreach (RuntimeSlot runtime in _slots.Values)
            {
                if (runtime.PointIndex >= 0 && runtime.Toy != null && !runtime.Toy.IsDestroyed)
                {
                    occupied.Add(runtime.PointIndex);
                }
            }

            for (int i = 0; i < due.Count; i++)
            {
                int slotId = due[i];
                if (!_state.TryBeginSpawn(_rangeGeneration, slotId, now, out SphereTargetSpawnTicket ticket))
                {
                    continue;
                }

                int pointIndex = SelectPoint(ticket.PreviousPointIndex, occupied);
                if (pointIndex < 0)
                {
                    _state.FailSpawn(_rangeGeneration, ticket, now, StartupSpawnRetrySeconds);
                    Warn($"Sphere target slot {slotId} had no free authored point.");
                    continue;
                }

                if (TrySpawn(ticket, pointIndex, now))
                {
                    occupied.Add(pointIndex);
                    _nextPointOrdinal++;
                }
            }
        }

        public void Stop()
        {
            _running = false;

            // Invalidate every callback token before unsubscribing and before touching any Unity object.
            _state.Stop();
            _rangeGeneration = 0;

            try
            {
                Unsubscribe();
            }
            catch (Exception ex)
            {
                Warn($"Sphere target event cleanup failed: {ex.GetBaseException().Message}");
            }

            foreach (RuntimeSlot slot in _slots.Values)
            {
                DestroyRuntimeToy(slot);
            }

            _slots.Clear();
            _layout = null;
            _nextPointOrdinal = 0L;
        }

        private void OnAnyPlayerFired(ReferenceHub owner, HitscanResult result)
        {
            try
            {
                if (!_running || owner == null || result == null || result.Obstacles.Count == 0)
                {
                    return;
                }

                Player? player = Player.Get(owner);
                if (player == null || !_isCurrentParticipant(player) || !_ownsCurrentRangeFirearm(player))
                {
                    return;
                }

                // One firearm action may contain several pellet rays. Snapshot each distinct current generation so
                // a sphere can credit at most once for the action even though its collider is never disabled.
                Dictionary<int, int> candidates = new Dictionary<int, int>();
                for (int hitIndex = 0; hitIndex < result.Obstacles.Count; hitIndex++)
                {
                    Collider? collider = result.Obstacles[hitIndex].Hit.collider;
                    if (collider == null)
                    {
                        continue;
                    }

                    foreach (RuntimeSlot slot in _slots.Values)
                    {
                        if (slot.Toy == null || slot.Toy.IsDestroyed || slot.Generation == 0 ||
                            !_state.IsCurrentLive(_rangeGeneration, slot.SlotId, slot.Generation))
                        {
                            continue;
                        }

                        Transform targetTransform = slot.Toy.Transform;
                        Transform hitTransform = collider.transform;
                        if (hitTransform == targetTransform || hitTransform.IsChildOf(targetTransform))
                        {
                            candidates[slot.SlotId] = slot.Generation;
                            break;
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    return;
                }

                foreach (KeyValuePair<int, int> candidate in candidates)
                {
                    if (!_slots.TryGetValue(candidate.Key, out RuntimeSlot runtime) ||
                        runtime.Toy == null || runtime.Toy.IsDestroyed ||
                        runtime.Generation != candidate.Value ||
                        !_state.IsCurrentLive(_rangeGeneration, runtime.SlotId, runtime.Generation))
                    {
                        continue;
                    }

                    int creditedPointIndex = runtime.PointIndex;
                    int creditedGeneration = runtime.Generation;
                    Vector3 creditedPosition = runtime.Toy.Position;

                    HashSet<int> occupied = new HashSet<int>();
                    foreach (RuntimeSlot other in _slots.Values)
                    {
                        if (other.SlotId != runtime.SlotId && other.PointIndex >= 0 &&
                            other.Toy != null && !other.Toy.IsDestroyed)
                        {
                            occupied.Add(other.PointIndex);
                        }
                    }

                    int pointIndex = SelectPoint(creditedPointIndex, occupied);
                    if (pointIndex < 0)
                    {
                        Warn($"Sphere target slot {runtime.SlotId} had no free relocation point; it remains live in place.");
                        continue;
                    }

                    // Score and send the native hitmarker while the sphere is still at the point that was hit.
                    // The callback is synchronous; immediately afterward the same always-collidable toy moves.
                    try
                    {
                        _credit(new SphereTargetHit(
                            player,
                            runtime.SlotId,
                            creditedGeneration,
                            creditedPointIndex,
                            creditedPosition));
                    }
                    catch (Exception ex)
                    {
                        Warn($"Sphere target credit callback failed: {ex.GetBaseException().Message}");
                    }

                    if (!TryRelocate(runtime, pointIndex))
                    {
                        Warn($"Sphere target slot {runtime.SlotId} could not relocate; it remains live in place.");
                        continue;
                    }

                    _nextPointOrdinal++;
                }
            }
            catch (Exception ex)
            {
                Warn($"Sphere target hitscan callback failed: {ex.GetBaseException().Message}");
            }
        }

        private bool TryRelocate(RuntimeSlot runtime, int pointIndex)
        {
            if (_layout == null || runtime.Toy == null || runtime.Toy.IsDestroyed ||
                pointIndex < 0 || pointIndex >= _layout.Points.Count ||
                !_state.IsCurrentLive(_rangeGeneration, runtime.SlotId, runtime.Generation))
            {
                return false;
            }

            PrimitiveObjectToy toy = runtime.Toy;
            Vector3 oldPosition = toy.Position;
            try
            {
                // Do not hide, disable, destroy, respawn, or park the target. The same collider moves directly.
                toy.Position = _layout.Points[pointIndex];
                Physics.SyncTransforms();
                if (!_state.TryCommitRelocation(
                        _rangeGeneration,
                        runtime.SlotId,
                        runtime.Generation,
                        pointIndex,
                        out int relocatedGeneration))
                {
                    toy.Position = oldPosition;
                    Physics.SyncTransforms();
                    return false;
                }

                runtime.Generation = relocatedGeneration;
                runtime.PointIndex = pointIndex;
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    toy.Position = oldPosition;
                    Physics.SyncTransforms();
                }
                catch
                {
                    // The original live generation is still valid; teardown remains the final safety boundary.
                }

                Warn($"Sphere target slot {runtime.SlotId} relocation failed: {ex.GetBaseException().Message}");
                return false;
            }
        }

        private bool TrySpawn(SphereTargetSpawnTicket ticket, int pointIndex, double now)
        {
            if (_layout == null || !_slots.TryGetValue(ticket.SlotId, out RuntimeSlot runtime))
            {
                _state.FailSpawn(_rangeGeneration, ticket, now, StartupSpawnRetrySeconds);
                return false;
            }

            PrimitiveObjectToy? toy = runtime.Toy;
            bool created = toy == null || toy.IsDestroyed;
            try
            {
                Vector3 position = _layout.Points[pointIndex];
                if (created)
                {
                    toy = PrimitiveObjectToy.Create(
                        position,
                        Quaternion.identity,
                        Vector3.one * _settings.Diameter,
                        networkSpawn: false);
                }

                if (toy == null)
                {
                    throw new InvalidOperationException("Sphere target toy creation returned null.");
                }

                toy.Type = PrimitiveType.Sphere;
                toy.Color = _settings.Color;
                toy.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
                // Pooled targets move after their initial network spawn. Static AdminToys deliberately stop
                // synchronizing local transforms, which leaves clients rendering the old position while the
                // authoritative collider has moved. Keep transform sync enabled; unchanged transforms do not
                // dirty Mirror sync vars, so these stationary-between-pop toys remain inexpensive.
                toy.IsStatic = false;
                toy.MovementSmoothing = 0;
                toy.SyncInterval = 0.05f;
                toy.Position = position;
                toy.Scale = Vector3.one * _settings.Diameter;
                if (created)
                {
                    toy.Spawn();
                }

                if (!_state.CommitSpawn(_rangeGeneration, ticket, pointIndex))
                {
                    if (created && !toy.IsDestroyed)
                    {
                        toy.Destroy();
                    }

                    return false;
                }

                runtime.Generation = ticket.Generation;
                runtime.PointIndex = pointIndex;
                runtime.Toy = toy;
                Physics.SyncTransforms();
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (created && toy != null && !toy.IsDestroyed)
                    {
                        toy.Destroy();
                    }
                }
                catch
                {
                    // The state invalidation below is the safety boundary; cleanup remains best effort.
                }

                _state.FailSpawn(_rangeGeneration, ticket, now, StartupSpawnRetrySeconds);
                Warn($"Sphere target slot {ticket.SlotId} spawn failed: {ex.GetBaseException().Message}");
                return false;
            }
        }

        private int SelectPoint(int previousPointIndex, HashSet<int> occupied)
        {
            if (_layout == null)
            {
                return -1;
            }

            int pointCount = _layout.Points.Count;
            int start = SphereTargetLayout.GetSequencePointIndex(_nextPointOrdinal, pointCount);
            if (start < 0)
            {
                return -1;
            }

            for (int offset = 0; offset < pointCount; offset++)
            {
                int pointIndex = (start + offset) % pointCount;
                if (pointIndex != previousPointIndex && !occupied.Contains(pointIndex))
                {
                    return pointIndex;
                }
            }

            return -1;
        }

        private void DestroyRuntimeToy(RuntimeSlot runtime)
        {
            PrimitiveObjectToy? toy = runtime.Toy;
            runtime.Toy = null;
            runtime.Generation = 0;
            runtime.PointIndex = -1;
            if (toy == null)
            {
                return;
            }

            try
            {
                if (!toy.IsDestroyed)
                {
                    toy.Destroy();
                }
            }
            catch (Exception ex)
            {
                Warn($"Sphere target slot {runtime.SlotId} cleanup failed: {ex.GetBaseException().Message}");
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            HitscanHitregModuleBase.ServerAnyPlayerFired += OnAnyPlayerFired;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            HitscanHitregModuleBase.ServerAnyPlayerFired -= OnAnyPlayerFired;
            _subscribed = false;
        }

        private void Warn(string message) => _warn?.Invoke(message);
    }
}
