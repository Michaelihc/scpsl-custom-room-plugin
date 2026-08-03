using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities.AimRange
{
    public enum SphereTargetPhase
    {
        Inactive,
        Waiting,
        Spawning,
        Live,
    }

    public readonly struct SphereTargetSpawnTicket
    {
        internal SphereTargetSpawnTicket(int slotId, int generation, int spawnOrdinal, int previousPointIndex)
        {
            SlotId = slotId;
            Generation = generation;
            SpawnOrdinal = spawnOrdinal;
            PreviousPointIndex = previousPointIndex;
        }

        public int SlotId { get; }
        public int Generation { get; }
        public int SpawnOrdinal { get; }
        public int PreviousPointIndex { get; }
    }

    public readonly struct SphereTargetPop
    {
        internal SphereTargetPop(int slotId, int generation, int pointIndex)
        {
            SlotId = slotId;
            Generation = generation;
            PointIndex = pointIndex;
        }

        public int SlotId { get; }
        public int Generation { get; }
        public int PointIndex { get; }
    }

    /// <summary>Pure multi-slot deadline and generation gate; it owns no Unity or LabAPI objects.</summary>
    public sealed class SphereTargetState
    {
        private sealed class Slot
        {
            public Slot(int slotId)
            {
                SlotId = slotId;
            }

            public int SlotId { get; }
            public int Generation { get; set; }
            public int SpawnOrdinal { get; set; }
            public int PointIndex { get; set; } = -1;
            public int PreviousPointIndex { get; set; } = -1;
            public double RespawnAt { get; set; }
            public SphereTargetPhase Phase { get; set; }
        }

        private readonly Dictionary<int, Slot> _slots = new Dictionary<int, Slot>();
        private int _rangeGeneration;
        private bool _running;

        public int RangeGeneration => _rangeGeneration;

        public bool IsRunning => _running;

        public int SlotCount => _slots.Count;

        public void Start(int rangeGeneration, int slotCount, double now)
        {
            Stop();
            if (rangeGeneration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rangeGeneration));
            }

            if (slotCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            _rangeGeneration = rangeGeneration;
            _running = true;
            for (int slotId = 0; slotId < slotCount; slotId++)
            {
                Slot slot = new Slot(slotId)
                {
                    Generation = Next(slotId),
                    RespawnAt = Math.Max(0d, now),
                    Phase = SphereTargetPhase.Waiting,
                };
                _slots.Add(slotId, slot);
            }
        }

        public bool TryBeginSpawn(int rangeGeneration, int slotId, double now, out SphereTargetSpawnTicket ticket)
        {
            ticket = default;
            if (!_running || rangeGeneration != _rangeGeneration ||
                !_slots.TryGetValue(slotId, out Slot slot) ||
                slot.Phase != SphereTargetPhase.Waiting || now < slot.RespawnAt)
            {
                return false;
            }

            slot.Generation = Next(slot.Generation);
            slot.SpawnOrdinal = Next(slot.SpawnOrdinal);
            slot.Phase = SphereTargetPhase.Spawning;
            ticket = new SphereTargetSpawnTicket(slotId, slot.Generation, slot.SpawnOrdinal, slot.PreviousPointIndex);
            return true;
        }

        public bool CommitSpawn(int rangeGeneration, SphereTargetSpawnTicket ticket, int pointIndex)
        {
            if (!_running || rangeGeneration != _rangeGeneration || pointIndex < 0 ||
                !_slots.TryGetValue(ticket.SlotId, out Slot slot) ||
                slot.Generation != ticket.Generation || slot.Phase != SphereTargetPhase.Spawning)
            {
                return false;
            }

            slot.PointIndex = pointIndex;
            slot.Phase = SphereTargetPhase.Live;
            return true;
        }

        public void FailSpawn(int rangeGeneration, SphereTargetSpawnTicket ticket, double now, double retrySeconds)
        {
            if (!_running || rangeGeneration != _rangeGeneration ||
                !_slots.TryGetValue(ticket.SlotId, out Slot slot) ||
                slot.Generation != ticket.Generation || slot.Phase != SphereTargetPhase.Spawning)
            {
                return;
            }

            slot.Generation = Next(slot.Generation);
            slot.PointIndex = -1;
            slot.Phase = SphereTargetPhase.Waiting;
            slot.RespawnAt = Math.Max(0d, now) + Math.Max(0.001d, retrySeconds);
        }

        public bool TryPop(
            int rangeGeneration,
            int slotId,
            int generation,
            double now,
            double respawnDelaySeconds,
            out SphereTargetPop pop)
        {
            pop = default;
            if (!_running || rangeGeneration != _rangeGeneration ||
                !_slots.TryGetValue(slotId, out Slot slot) ||
                slot.Generation != generation || slot.Phase != SphereTargetPhase.Live)
            {
                return false;
            }

            int creditedPoint = slot.PointIndex;
            pop = new SphereTargetPop(slotId, generation, creditedPoint);

            // Invalidate before the controller destroys the collider. Re-entrant, duplicate, or stale callbacks
            // cannot credit this generation after this point.
            slot.Generation = Next(slot.Generation);
            slot.PreviousPointIndex = creditedPoint;
            slot.PointIndex = -1;
            slot.Phase = SphereTargetPhase.Waiting;
            slot.RespawnAt = Math.Max(0d, now) + Math.Max(0.001d, respawnDelaySeconds);
            return true;
        }

        public bool IsCurrentLive(int rangeGeneration, int slotId, int generation) =>
            _running && rangeGeneration == _rangeGeneration &&
            _slots.TryGetValue(slotId, out Slot slot) &&
            slot.Generation == generation && slot.Phase == SphereTargetPhase.Live;

        public IReadOnlyList<int> GetDueSlots(int rangeGeneration, double now)
        {
            List<int> due = new List<int>();
            if (!_running || rangeGeneration != _rangeGeneration)
            {
                return due;
            }

            foreach (Slot slot in _slots.Values)
            {
                if (slot.Phase == SphereTargetPhase.Waiting && now >= slot.RespawnAt)
                {
                    due.Add(slot.SlotId);
                }
            }

            due.Sort();
            return due;
        }

        public IReadOnlyCollection<int> GetOccupiedPointIndices(int rangeGeneration)
        {
            HashSet<int> occupied = new HashSet<int>();
            if (!_running || rangeGeneration != _rangeGeneration)
            {
                return occupied;
            }

            foreach (Slot slot in _slots.Values)
            {
                if (slot.PointIndex >= 0 && (slot.Phase == SphereTargetPhase.Spawning || slot.Phase == SphereTargetPhase.Live))
                {
                    occupied.Add(slot.PointIndex);
                }
            }

            return occupied;
        }

        public void Stop()
        {
            _running = false;
            _rangeGeneration = Next(_rangeGeneration);
            foreach (Slot slot in _slots.Values)
            {
                slot.Generation = Next(slot.Generation);
                slot.Phase = SphereTargetPhase.Inactive;
                slot.PointIndex = -1;
                slot.RespawnAt = 0d;
            }

            _slots.Clear();
        }

        private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;
    }
}
