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
    /// <summary>Owns persistent shooting-counter pickups and all granted range-gun cleanup.</summary>
    internal sealed class WeaponShelfController
    {
        private readonly AimRangeSessions _sessions;
        private readonly WeaponShelfState _state = new WeaponShelfState();
        private readonly Dictionary<int, AimWeaponPresetConfig> _presets = new Dictionary<int, AimWeaponPresetConfig>();
        private readonly Dictionary<int, AimShelfAnchor> _anchors = new Dictionary<int, AimShelfAnchor>();
        private readonly Dictionary<string, IssuedWeapon> _issued = new Dictionary<string, IssuedWeapon>(StringComparer.Ordinal);
        private readonly Func<Player, bool> _isHuman;
        private readonly Func<Player, string> _key;
        private bool _running;

        private sealed class IssuedWeapon
        {
            public IssuedWeapon(string presetId, ushort serial, int reserveAmmo)
            {
                PresetId = presetId;
                Serial = serial;
                ReserveAmmo = reserveAmmo;
            }
            public string PresetId { get; }
            public ushort Serial { get; }
            public int ReserveAmmo { get; set; }
        }

        public WeaponShelfController(AimRangeSessions sessions, Func<Player, bool> isHuman, Func<Player, string> key)
        {
            _sessions = sessions;
            _isHuman = isHuman;
            _key = key;
        }

        public bool Start(IReadOnlyList<AimShelfAnchor> anchors, AimRangeActivityConfig config)
        {
            Stop();
            if (anchors == null || anchors.Count == 0 || config == null)
            {
                return false;
            }

            _running = true;
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
                    Logger.Warn($"[WarmupScpSelector] Armoury counter slot {anchor.SlotId} disabled: invalid firearm/ammo preset.");
                    continue;
                }

                if (duplicateIds.Contains(preset!.Id))
                {
                    _state.Disable(anchor.SlotId);
                    Logger.Warn($"[WarmupScpSelector] Armoury counter slot {anchor.SlotId} disabled: preset id '{preset.Id}' is duplicated.");
                    continue;
                }

                _presets[anchor.SlotId] = preset;
                _state.Enable(anchor.SlotId);
                SpawnSlot(anchor.SlotId);
            }

            return _presets.Count > 0;
        }

        public void OnPickingUp(PlayerPickingUpItemEventArgs ev)
        {
            if (!_running || ev?.Player == null || ev.Pickup == null)
            {
                return;
            }

            if (!_state.TryResolveAvailable(ev.Pickup.Serial, out int slotId))
            {
                return;
            }

            // The counter gun is a permanent dispenser trigger. Cancel native collection first, then grant a
            // separate owned inventory item; every player sees the original pickup remain in place.
            ev.IsAllowed = false;
            if (!_isHuman(ev.Player) || !_presets.TryGetValue(slotId, out AimWeaponPresetConfig preset))
            {
                return;
            }

            Grant(ev.Player, preset);
        }

        public bool OnDropped(PlayerDroppedItemEventArgs ev)
        {
            if (!_running || ev?.Pickup == null)
            {
                return false;
            }

            KeyValuePair<string, IssuedWeapon> issued = _issued.FirstOrDefault(pair => pair.Value.Serial == ev.Pickup.Serial);
            if (string.IsNullOrEmpty(issued.Key)) return false;

            try { ev.Pickup.Destroy(); } catch { }
            Player? owner = ResolveHuman(issued.Key);
            ZeroAmmo(owner, issued.Value.PresetId);
            _sessions.ClearWeapon(issued.Key);
            _issued.Remove(issued.Key);
            return true;
        }

        public void DetachSession(AimRangeSessions.Session session)
        {
            if (session != null) _sessions.ClearWeapon(session.UserKey);
        }

        public void DestroyForPlayer(string userKey, Player? player = null)
        {
            if (string.IsNullOrEmpty(userKey)) return;
            if (_issued.TryGetValue(userKey, out IssuedWeapon issued))
            {
                ZeroAmmo(player ?? ResolveHuman(userKey), issued.PresetId);
                DestroySerial(issued.Serial);
                _issued.Remove(userKey);
            }
            _sessions.ClearWeapon(userKey);
        }

        public bool AttachToSession(Player player, AimRangeSessions.Session session)
        {
            if (player == null || session == null || !_issued.TryGetValue(session.UserKey, out IssuedWeapon issued) ||
                !Item.TryGet(issued.Serial, out Item? item) || item?.CurrentOwner != player)
            {
                return false;
            }

            _sessions.TrackWeapon(session.UserKey, issued.PresetId, issued.Serial, issued.ReserveAmmo);
            return true;
        }

        public bool IsIssuedWeapon(Player? player, ushort serial)
        {
            if (player == null || serial == 0) return false;
            return _issued.TryGetValue(_key(player), out IssuedWeapon issued) && issued.Serial == serial;
        }

        /// <summary>
        /// Reasserts session ownership from the controller's authoritative issued-item record. Native firearm
        /// damage can be raised while the wrapper's CurrentItem is transiently unavailable, so damage routing
        /// must not depend on that presentation-time property when the handler serial is already definitive.
        /// </summary>
        public bool SynchronizeIssuedWeapon(Player? player, AimRangeSessions.Session? session, ushort serial)
        {
            if (player == null || session == null || serial == 0 ||
                !_issued.TryGetValue(_key(player), out IssuedWeapon issued) || issued.Serial != serial ||
                !string.Equals(session.UserKey, _key(player), StringComparison.Ordinal))
            {
                return false;
            }

            _sessions.TrackWeapon(session.UserKey, issued.PresetId, issued.Serial, issued.ReserveAmmo);
            return true;
        }

        public bool EnsureOwnedWeapon(Player player, AimRangeSessions.Session session)
        {
            if (player == null || session == null || string.IsNullOrEmpty(session.PresetId))
            {
                return false;
            }

            if (!_issued.TryGetValue(session.UserKey, out IssuedWeapon issued)) return false;
            AimWeaponPresetConfig? preset = _presets.Values.FirstOrDefault(value =>
                string.Equals(value.Id, issued.PresetId, StringComparison.Ordinal));
            if (!AimWeaponPresetRules.IsConventional(preset))
            {
                return false;
            }

            FirearmItem? firearm = null;
            if (issued.Serial != 0 && Item.TryGet(issued.Serial, out Item? existing) &&
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

                issued = new IssuedWeapon(preset.Id, firearm.Serial, issued.ReserveAmmo);
                _issued[session.UserKey] = issued;
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

            int reserve = Math.Max(0, Math.Min(ushort.MaxValue, issued.ReserveAmmo));
            player.SetAmmo(preset.Ammo, (ushort)reserve);
            _sessions.TrackWeapon(session.UserKey, preset.Id, firearm.Serial, reserve);
            player.CurrentItem = firearm;
            return player.CurrentItem?.Serial == firearm.Serial;
        }

        public void Stop()
        {
            _running = false;
            List<ushort> shelfSerials = _state.Slots.Select(slot => slot.PickupSerial).Where(serial => serial != 0).ToList();
            _state.InvalidateAll(); // invalidate generations before destroying network objects
            foreach (ushort serial in shelfSerials)
            {
                DestroySerial(serial);
            }

            // Second owned-entity sweep by configured slot serials is harmless and catches wrapper lag.
            foreach (ushort serial in shelfSerials)
            {
                DestroySerial(serial);
            }

            foreach (KeyValuePair<string, IssuedWeapon> pair in _issued.ToList())
            {
                ZeroAmmo(ResolveHuman(pair.Key), pair.Value.PresetId);
                DestroySerial(pair.Value.Serial);
                _sessions.ClearWeapon(pair.Key);
            }

            _issued.Clear();
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
                pickup.IsLocked = false;
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
                Logger.Warn($"[WarmupScpSelector] Armoury counter slot {slotId} spawn failed: {ex.Message}");
            }
        }

        private void Grant(Player player, AimWeaponPresetConfig preset)
        {
            string userKey = _key(player);
            DestroyForPlayer(userKey, player);

            FirearmItem? firearm = player.AddItem(preset.Firearm) as FirearmItem;
            if (firearm == null)
            {
                Logger.Warn($"[WarmupScpSelector] Could not grant armoury weapon '{preset.Id}' to {userKey}.");
                return;
            }

            if (preset.AttachmentsCode != 0)
            {
                firearm.AttachmentsCode = preset.AttachmentsCode;
            }

            if (firearm.Type != preset.Firearm ||
                (preset.AttachmentsCode != 0 && firearm.AttachmentsCode != preset.AttachmentsCode) ||
                firearm.AmmoType != preset.Ammo || !TryPreload(firearm))
            {
                DestroySerial(firearm.Serial);
                Logger.Warn($"[WarmupScpSelector] Armoury weapon '{preset.Id}' failed native inventory validation.");
                return;
            }

            int reserve = Math.Max(0, Math.Min(ushort.MaxValue, preset.ReserveAmmo));
            player.SetAmmo(preset.Ammo, (ushort)reserve);
            _issued[userKey] = new IssuedWeapon(preset.Id, firearm.Serial, reserve);
            if (_sessions.TryGet(userKey, out AimRangeSessions.Session session) && !session.IsLeaving)
            {
                _sessions.TrackWeapon(userKey, preset.Id, firearm.Serial, reserve);
            }
            player.CurrentItem = firearm;
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

    }
}
