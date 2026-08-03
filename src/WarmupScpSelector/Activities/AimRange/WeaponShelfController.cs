using System;
using System.Collections.Generic;
using System.Linq;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Wrappers;
using UnityEngine;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>Owns physical shelf pickups and all range-gun item/pickup/ammo cleanup.</summary>
    internal sealed class WeaponShelfController
    {
        private readonly AimRangeSessions _sessions;
        private readonly WeaponShelfState _state = new WeaponShelfState();
        private readonly Dictionary<int, AimWeaponPresetConfig> _presets = new Dictionary<int, AimWeaponPresetConfig>();
        private readonly Dictionary<int, AimShelfAnchor> _anchors = new Dictionary<int, AimShelfAnchor>();
        private readonly Dictionary<string, PendingClaim> _pending = new Dictionary<string, PendingClaim>(StringComparer.Ordinal);
        private readonly Func<Player, bool> _isHuman;
        private readonly Func<Player, string> _key;
        private float _replenishSeconds;
        private bool _running;

        private readonly struct PendingClaim
        {
            public PendingClaim(int slotId, int generation, ushort serial, double expiresAt)
            {
                SlotId = slotId;
                Generation = generation;
                Serial = serial;
                ExpiresAt = expiresAt;
            }
            public int SlotId { get; }
            public int Generation { get; }
            public ushort Serial { get; }
            public double ExpiresAt { get; }
        }

        public WeaponShelfController(AimRangeSessions sessions, Func<Player, bool> isHuman, Func<Player, string> key)
        {
            _sessions = sessions;
            _isHuman = isHuman;
            _key = key;
        }

        public bool Start(IReadOnlyList<AimShelfAnchor> anchors, AimRangeActivityConfig config, double now)
        {
            Stop();
            if (anchors == null || anchors.Count == 0 || config == null)
            {
                return false;
            }

            _running = true;
            _replenishSeconds = Sanitize(config.ShelfReplenishSeconds, 0f, 30f, 1.5f);
            List<AimWeaponPresetConfig> configured = config.WeaponPresets ?? new List<AimWeaponPresetConfig>();
            HashSet<string> duplicateIds = AimWeaponPresetRules.FindDuplicateConventionalIds(configured);
            for (int i = 0; i < anchors.Count; i++)
            {
                AimShelfAnchor anchor = anchors[i];
                _anchors[anchor.SlotId] = anchor;
                AimWeaponPresetConfig? preset = i < configured.Count ? configured[i] : null;
                if (preset == null && i == 0)
                {
                    preset = new AimWeaponPresetConfig("legacy", config.Firearm, config.Ammo, config.AmmoAmount);
                }

                _state.Define(anchor.SlotId, preset?.Id ?? string.Empty);
                if (!AimWeaponPresetRules.IsConventional(preset))
                {
                    _state.Disable(anchor.SlotId);
                    Logger.Warn($"[WarmupScpSelector] Aim shelf slot {anchor.SlotId} disabled: invalid firearm/ammo preset.");
                    continue;
                }

                if (duplicateIds.Contains(preset!.Id))
                {
                    _state.Disable(anchor.SlotId);
                    Logger.Warn($"[WarmupScpSelector] Aim shelf slot {anchor.SlotId} disabled: preset id '{preset.Id}' is duplicated.");
                    continue;
                }

                _presets[anchor.SlotId] = preset;
                _state.Enable(anchor.SlotId);
                SpawnSlot(anchor.SlotId);
            }

            return _presets.Count > 0;
        }

        public void OnPickingUp(PlayerPickingUpItemEventArgs ev, double now)
        {
            if (!_running || ev?.Player == null || ev.Pickup == null)
            {
                return;
            }

            if (!_state.BeginClaim(ev.Pickup.Serial, _key(ev.Player), out int slotId, out int generation))
            {
                return;
            }

            if (!_isHuman(ev.Player) || !_sessions.TryGet(_key(ev.Player), out AimRangeSessions.Session session) || session.IsLeaving)
            {
                ev.IsAllowed = false;
                _state.CancelClaim(slotId, generation);
                return;
            }

            _pending[_key(ev.Player)] = new PendingClaim(slotId, generation, ev.Pickup.Serial, now + 1.5d);
        }

        public void OnPickedUp(PlayerPickedUpItemEventArgs ev, double now)
        {
            if (!_running || ev?.Player == null || ev.Item == null)
            {
                return;
            }

            string userKey = _key(ev.Player);
            if (!_pending.TryGetValue(userKey, out PendingClaim claim) || claim.Serial != ev.Item.Serial ||
                !_state.ConfirmClaim(claim.SlotId, claim.Generation, userKey, ev.Item.Serial, now, _replenishSeconds))
            {
                return;
            }

            _pending.Remove(userKey);
            if (!_presets.TryGetValue(claim.SlotId, out AimWeaponPresetConfig preset) || ev.Item is not FirearmItem firearm ||
                firearm.Type != preset.Firearm || (preset.AttachmentsCode != 0 && !firearm.CheckAttachmentsCode(preset.AttachmentsCode)))
            {
                DestroySerial(ev.Item.Serial);
                _state.Disable(claim.SlotId);
                _presets.Remove(claim.SlotId);
                Logger.Warn($"[WarmupScpSelector] Aim shelf slot {claim.SlotId} disabled after native pickup validation failed.");
                return;
            }

            if (_sessions.TryGet(userKey, out AimRangeSessions.Session session) && session.OwnedItemSerial != 0 &&
                session.OwnedItemSerial != ev.Item.Serial)
            {
                DestroyOwnedWeapon(ev.Player, session);
            }

            if (preset.AttachmentsCode != 0)
            {
                firearm.AttachmentsCode = preset.AttachmentsCode;
                if (firearm.AttachmentsCode != preset.AttachmentsCode)
                {
                    DestroySerial(ev.Item.Serial);
                    _state.Disable(claim.SlotId);
                    _presets.Remove(claim.SlotId);
                    return;
                }
            }

            if (firearm.AmmoType != preset.Ammo || !TryPreload(firearm))
            {
                DestroySerial(ev.Item.Serial);
                _state.Disable(claim.SlotId);
                _presets.Remove(claim.SlotId);
                Logger.Warn($"[WarmupScpSelector] Aim shelf slot {claim.SlotId} disabled: firearm could not be preloaded safely.");
                return;
            }

            int reserve = Math.Max(0, Math.Min(ushort.MaxValue, preset.ReserveAmmo));
            ev.Player.SetAmmo(preset.Ammo, (ushort)reserve);
            _sessions.TrackWeapon(userKey, preset.Id, ev.Item.Serial, reserve);
        }

        public bool OnDropped(PlayerDroppedItemEventArgs ev)
        {
            if (!_running || ev?.Pickup == null || !_sessions.TryFindWeaponOwner(ev.Pickup.Serial, out AimRangeSessions.Session session))
            {
                return false;
            }

            try { ev.Pickup.Destroy(); } catch { }
            Player? owner = ResolveHuman(session.UserKey);
            ZeroAmmo(owner, session.PresetId);
            _sessions.ClearWeapon(session.UserKey);
            return true;
        }

        public void Tick(double now)
        {
            if (!_running)
            {
                return;
            }

            foreach (KeyValuePair<string, PendingClaim> pair in _pending.ToList())
            {
                if (now >= pair.Value.ExpiresAt)
                {
                    _state.CancelClaim(pair.Value.SlotId, pair.Value.Generation);
                    _pending.Remove(pair.Key);
                }
            }

            foreach (int slotId in _state.Due(now))
            {
                SpawnSlot(slotId);
            }
        }

        public void DestroyOwnedWeapon(Player? player, AimRangeSessions.Session session)
        {
            if (session == null)
            {
                return;
            }

            ushort serial = session.OwnedItemSerial;
            string presetId = session.PresetId;
            ZeroAmmo(player, presetId);
            _sessions.ClearWeapon(session.UserKey);
            DestroySerial(serial);
        }

        public bool EnsureOwnedWeapon(Player player, AimRangeSessions.Session session)
        {
            if (player == null || session == null || string.IsNullOrEmpty(session.PresetId))
            {
                return false;
            }

            AimWeaponPresetConfig? preset = _presets.Values.FirstOrDefault(value =>
                string.Equals(value.Id, session.PresetId, StringComparison.Ordinal));
            if (!AimWeaponPresetRules.IsConventional(preset))
            {
                return false;
            }

            FirearmItem? firearm = null;
            if (session.OwnedItemSerial != 0 && Item.TryGet(session.OwnedItemSerial, out Item? existing) &&
                existing is FirearmItem existingFirearm && existingFirearm.CurrentOwner == player)
            {
                firearm = existingFirearm;
            }

            if (firearm == null)
            {
                Item? added = player.AddItem(preset!.Firearm);
                firearm = added as FirearmItem;
                if (firearm == null)
                {
                    return false;
                }

                _sessions.TrackWeapon(session.UserKey, preset.Id, firearm.Serial, session.TrackedReserveAmmo);
            }

            if (preset!.AttachmentsCode != 0)
            {
                if (!firearm.CheckAttachmentsCode(preset.AttachmentsCode))
                {
                    return false;
                }

                firearm.AttachmentsCode = preset.AttachmentsCode;
            }

            if (firearm.AmmoType != preset.Ammo || !TryPreload(firearm))
            {
                return false;
            }

            int reserve = Math.Max(0, Math.Min(ushort.MaxValue, session.TrackedReserveAmmo));
            player.SetAmmo(preset.Ammo, (ushort)reserve);
            player.CurrentItem = firearm;
            return player.CurrentItem?.Serial == firearm.Serial;
        }

        public void Stop()
        {
            _running = false;
            List<ushort> shelfSerials = _state.Slots.Select(slot => slot.PickupSerial).Where(serial => serial != 0).ToList();
            _state.InvalidateAll(); // invalidate generations before destroying network objects
            _pending.Clear();

            foreach (ushort serial in shelfSerials)
            {
                DestroySerial(serial);
            }

            // Second owned-entity sweep by configured slot serials is harmless and catches wrapper lag.
            foreach (ushort serial in shelfSerials)
            {
                DestroySerial(serial);
            }

            _presets.Clear();
            _anchors.Clear();
        }

        private void SpawnSlot(int slotId)
        {
            if (!_running || !_presets.TryGetValue(slotId, out AimWeaponPresetConfig preset) || !_anchors.TryGetValue(slotId, out AimShelfAnchor anchor))
            {
                return;
            }

            Pickup? pickup = null;
            try
            {
                pickup = Pickup.Create(preset.Firearm, anchor.LocalPosition, anchor.LocalRotation, Vector3.one, networkSpawn: false);
                if (pickup == null || pickup.Serial == 0 || !_state.Spawned(slotId, pickup.Serial))
                {
                    pickup?.Destroy();
                    _state.Disable(slotId);
                    return;
                }

                pickup.Spawn();
                if (pickup.Rigidbody == null)
                {
                    throw new InvalidOperationException("pickup has no standard rigidbody");
                }

                pickup.Rigidbody.isKinematic = true;
            }
            catch (Exception ex)
            {
                try { pickup?.Destroy(); } catch { }
                _state.Disable(slotId);
                _presets.Remove(slotId);
                Logger.Warn($"[WarmupScpSelector] Aim shelf slot {slotId} spawn failed: {ex.Message}");
            }
        }

        private void ZeroAmmo(Player? player, string presetId)
        {
            if (player == null)
            {
                return;
            }

            AimWeaponPresetConfig? preset = _presets.Values.FirstOrDefault(value => string.Equals(value.Id, presetId, StringComparison.Ordinal));
            if (preset != null)
            {
                try { player.SetAmmo(preset.Ammo, 0); } catch { }
            }
        }

        private static bool TryPreload(FirearmItem firearm)
        {
            try
            {
                if (firearm == null || firearm.MaxAmmo <= 0)
                {
                    return false;
                }

                MagazineModule? magazine = firearm.Modules.OfType<MagazineModule>().FirstOrDefault();
                if (magazine != null)
                {
                    magazine.ServerSetInstanceAmmo(firearm.Serial, magazine.AmmoMax);
                }
                else
                {
                    if (!firearm.MagazineInserted)
                    {
                        firearm.MagazineInserted = true;
                    }

                    firearm.StoredAmmo = firearm.MaxAmmo;
                }

                firearm.BoltLocked = false;
                firearm.Cocked = true;
                if (!firearm.OpenBolt && firearm.ChamberMax > 0)
                {
                    firearm.ChamberedAmmo = Math.Min(1, firearm.ChamberMax);
                }

                return firearm.StoredAmmo > 0 && firearm.Cocked && !firearm.BoltLocked &&
                    (firearm.OpenBolt || firearm.ChamberedAmmo > 0);
            }
            catch
            {
                return false;
            }
        }

        private static void DestroySerial(ushort serial)
        {
            if (serial == 0) return;
            try
            {
                if (Item.TryGet(serial, out Item? item) && item != null && item.CurrentOwner is Player owner)
                {
                    owner.RemoveItem(item);
                }
            }
            catch { }
            try
            {
                if (Pickup.TryGet(serial, out Pickup? pickup) && pickup != null)
                {
                    pickup.Destroy();
                }
            }
            catch { }
        }

        private Player? ResolveHuman(string userKey)
        {
            foreach (Player player in Player.ReadyList)
            {
                if (_isHuman(player) && string.Equals(_key(player), userKey, StringComparison.Ordinal))
                {
                    return player;
                }
            }
            return null;
        }

        private static float Sanitize(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }
}
