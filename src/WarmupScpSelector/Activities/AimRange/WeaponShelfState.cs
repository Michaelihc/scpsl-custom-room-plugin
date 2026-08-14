using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities.AimRange
{
    public enum ShelfSlotPhase
    {
        Disabled,
        Available,
        PendingClaim,
        Replenishing,
    }

    /// <summary>Pure persistent floor-pickup slot state used by the gallery armoury controller.</summary>
    public sealed class WeaponShelfState
    {
        public sealed class Slot
        {
            internal Slot(int slotId, string presetId)
            {
                SlotId = slotId;
                PresetId = presetId ?? string.Empty;
            }

            public int SlotId { get; }
            public string PresetId { get; }
            public int Generation { get; internal set; }
            public ushort PickupSerial { get; internal set; }
            public string PendingUserKey { get; internal set; } = string.Empty;
            public double ReplenishAt { get; internal set; }
            public ShelfSlotPhase Phase { get; internal set; } = ShelfSlotPhase.Disabled;
        }

        private readonly Dictionary<int, Slot> _slots = new Dictionary<int, Slot>();
        private readonly Dictionary<ushort, int> _bySerial = new Dictionary<ushort, int>();

        public IEnumerable<Slot> Slots => _slots.Values;

        public Slot Define(int slotId, string presetId)
        {
            Slot slot = new Slot(slotId, presetId);
            _slots[slotId] = slot;
            return slot;
        }

        public void Enable(int slotId)
        {
            if (_slots.TryGetValue(slotId, out Slot slot))
            {
                Invalidate(slot);
                slot.Generation = Next(slot.Generation);
                slot.Phase = ShelfSlotPhase.Replenishing;
            }
        }

        public void Disable(int slotId)
        {
            if (_slots.TryGetValue(slotId, out Slot slot))
            {
                Invalidate(slot);
                slot.Generation = Next(slot.Generation);
                slot.Phase = ShelfSlotPhase.Disabled;
            }
        }

        public bool Spawned(int slotId, ushort serial)
        {
            if (serial == 0 || !_slots.TryGetValue(slotId, out Slot slot) || slot.Phase == ShelfSlotPhase.Disabled)
            {
                return false;
            }

            Invalidate(slot);
            slot.Generation = Next(slot.Generation);
            slot.PickupSerial = serial;
            slot.Phase = ShelfSlotPhase.Available;
            _bySerial[serial] = slotId;
            return true;
        }

        public bool BeginClaim(ushort pickupSerial, string userKey, out int slotId, out int generation)
        {
            slotId = -1;
            generation = 0;
            if (pickupSerial == 0 || string.IsNullOrEmpty(userKey) || !_bySerial.TryGetValue(pickupSerial, out int id) ||
                !_slots.TryGetValue(id, out Slot slot) || slot.Phase != ShelfSlotPhase.Available)
            {
                return false;
            }

            slot.Phase = ShelfSlotPhase.PendingClaim;
            slot.PendingUserKey = userKey;
            slotId = id;
            generation = slot.Generation;
            return true;
        }

        /// <summary>Resolves a persistent display pickup without consuming or changing its slot.</summary>
        public bool TryResolveAvailable(ushort pickupSerial, out int slotId)
        {
            slotId = -1;
            return pickupSerial != 0 && _bySerial.TryGetValue(pickupSerial, out int id) &&
                _slots.TryGetValue(id, out Slot slot) && slot.Phase == ShelfSlotPhase.Available &&
                (slotId = id) >= 0;
        }

        public bool ConfirmClaim(int slotId, int generation, string userKey, ushort itemSerial, double now, double replenishDelay)
        {
            if (!_slots.TryGetValue(slotId, out Slot slot) || slot.Generation != generation ||
                slot.Phase != ShelfSlotPhase.PendingClaim || !string.Equals(slot.PendingUserKey, userKey, StringComparison.Ordinal) ||
                itemSerial == 0 || itemSerial != slot.PickupSerial)
            {
                return false;
            }

            _bySerial.Remove(slot.PickupSerial);
            slot.PickupSerial = 0;
            slot.PendingUserKey = string.Empty;
            slot.ReplenishAt = now + Math.Max(0d, replenishDelay);
            slot.Phase = ShelfSlotPhase.Replenishing;
            return true;
        }

        public void CancelClaim(int slotId, int generation)
        {
            if (_slots.TryGetValue(slotId, out Slot slot) && slot.Generation == generation && slot.Phase == ShelfSlotPhase.PendingClaim)
            {
                slot.PendingUserKey = string.Empty;
                slot.Phase = ShelfSlotPhase.Available;
            }
        }

        public List<int> Due(double now)
        {
            List<int> due = new List<int>();
            foreach (Slot slot in _slots.Values)
            {
                if (slot.Phase == ShelfSlotPhase.Replenishing && now >= slot.ReplenishAt)
                {
                    due.Add(slot.SlotId);
                }
            }

            return due;
        }

        public void InvalidateAll()
        {
            foreach (Slot slot in _slots.Values)
            {
                Invalidate(slot);
                slot.Generation = Next(slot.Generation);
                slot.Phase = ShelfSlotPhase.Disabled;
            }

            _bySerial.Clear();
        }

        public bool TryGet(int slotId, out Slot slot) => _slots.TryGetValue(slotId, out slot);

        private void Invalidate(Slot slot)
        {
            if (slot.PickupSerial != 0)
            {
                _bySerial.Remove(slot.PickupSerial);
            }

            slot.PickupSerial = 0;
            slot.PendingUserKey = string.Empty;
            slot.ReplenishAt = 0d;
        }

        private static int Next(int value) => value == int.MaxValue ? 1 : value + 1;
    }
}
