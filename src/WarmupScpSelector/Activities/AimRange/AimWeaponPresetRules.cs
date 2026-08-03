using System;
using System.Collections.Generic;
using WarmupScpSelector.Activities;

namespace WarmupScpSelector.Activities.AimRange
{
    public static class AimWeaponPresetRules
    {
        public static bool IsConventional(AimWeaponPresetConfig? preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || preset.ReserveAmmo < 0)
            {
                return false;
            }

            switch (preset.Firearm)
            {
                case ItemType.GunCOM15: return preset.Ammo == ItemType.Ammo9x19;
                case ItemType.GunCOM18: return preset.Ammo == ItemType.Ammo9x19;
                case ItemType.GunFSP9: return preset.Ammo == ItemType.Ammo9x19;
                case ItemType.GunCrossvec: return preset.Ammo == ItemType.Ammo9x19;
                case ItemType.GunAK: return preset.Ammo == ItemType.Ammo762x39;
                case ItemType.GunE11SR: return preset.Ammo == ItemType.Ammo556x45;
                default: return false;
            }
        }

        public static bool IsBotAutomatic(AimWeaponPresetConfig? preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || preset.ReserveAmmo < 0)
            {
                return false;
            }

            switch (preset.Firearm)
            {
                case ItemType.GunE11SR: return preset.Ammo == ItemType.Ammo556x45;
                case ItemType.GunLogicer: return preset.Ammo == ItemType.Ammo762x39;
                case ItemType.GunAK: return preset.Ammo == ItemType.Ammo762x39;
                default: return false;
            }
        }

        public static HashSet<string> FindDuplicateConventionalIds(IEnumerable<AimWeaponPresetConfig>? presets)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> duplicates = new HashSet<string>(StringComparer.Ordinal);
            if (presets == null)
            {
                return duplicates;
            }

            foreach (AimWeaponPresetConfig preset in presets)
            {
                if (IsConventional(preset) && !seen.Add(preset.Id))
                {
                    duplicates.Add(preset.Id);
                }
            }

            return duplicates;
        }
    }
}
