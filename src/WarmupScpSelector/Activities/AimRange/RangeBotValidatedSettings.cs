using System;
using System.Collections.Generic;
using WarmupScpSelector.Activities;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>Deterministic fail-closed validation for native range-bot configuration.</summary>
    public sealed class RangeBotValidatedSettings
    {
        private RangeBotValidatedSettings() { }

        public int Count { get; private set; }
        public float Health { get; private set; }
        public double InitializeSeconds { get; private set; }
        public double AssociationTimeoutSeconds { get; private set; }
        public double AcquireDelaySeconds { get; private set; }
        public double ShotCadenceSeconds { get; private set; }
        public double ShotVerificationSeconds { get; private set; }
        public double MaxRetaliationDistance { get; private set; }
        public float AimToleranceDegrees { get; private set; }
        public double RespawnSeconds { get; private set; }
        public double AggroLeaseSeconds { get; private set; }
        public IReadOnlyList<AimWeaponPresetConfig> Presets { get; private set; } = Array.Empty<AimWeaponPresetConfig>();

        public static RangeBotValidatedSettings From(AimRangeActivityConfig? config)
        {
            config ??= new AimRangeActivityConfig();
            List<AimWeaponPresetConfig> presets = new List<AimWeaponPresetConfig>();
            foreach (AimWeaponPresetConfig preset in config.BotWeaponPresets ?? new List<AimWeaponPresetConfig>())
            {
                if (AimWeaponPresetRules.IsBotAutomatic(preset))
                {
                    presets.Add(preset);
                }
            }

            // Existing YAML files may still contain the former E11/Logicer/AK deck. The gameplay contract now
            // requires both authored bot slots to use Crossvec, so migrate such configs safely in memory.
            if (presets.Count == 0)
            {
                presets.Add(new AimWeaponPresetConfig("bot-crossvec", ItemType.GunCrossvec, ItemType.Ammo9x19, 240));
            }

            return new RangeBotValidatedSettings
            {
                Count = Clamp(config.BotCount, 0, 2),
                Health = Sanitize(config.BotHealth, 1f, 10000f, 250f),
                InitializeSeconds = Sanitize(config.BotInitializeSeconds, 0.05f, 5f, 0.35f),
                AssociationTimeoutSeconds = Sanitize(config.BotAssociationTimeoutSeconds, 0.1f, 10f, 1.5f),
                AcquireDelaySeconds = Sanitize(config.BotAcquireDelaySeconds, 0.5f, 0.55f, 0.5f),
                ShotCadenceSeconds = Sanitize(config.BotShotCadenceSeconds, 0.5f, 0.6f, 0.55f),
                ShotVerificationSeconds = Sanitize(config.BotShotVerificationSeconds, 0.1f, 3f, 0.65f),
                MaxRetaliationDistance = Sanitize(config.BotMaxRetaliationDistance, 1f, 100f, 24f),
                AimToleranceDegrees = Sanitize(config.BotAimToleranceDegrees, 0.25f, 30f, 3f),
                RespawnSeconds = Sanitize(config.BotRespawnSeconds, 0f, 60f, 4f),
                AggroLeaseSeconds = Sanitize(config.BotAggroLeaseSeconds, 0.1f, 60f, 12f),
                Presets = presets,
            };
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static float Sanitize(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value < min ? min : value > max ? max : value;
        }
    }
}
