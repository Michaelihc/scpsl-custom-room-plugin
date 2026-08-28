using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;

namespace WarmupScpSelector.Activities
{
    public sealed class ActivityConfig
    {
        [Description("Force ASCII fallbacks for signature glyphs if the server font renders Unicode as tofu.")]
        public bool UseAsciiGlyphFallback { get; set; } = false;

        [Description("Seconds a lane flash stays visible.")]
        public float FlashDurationSeconds { get; set; } = 0.7f;

        [Description("Full warmup Aim Range settings.")]
        public AimRangeActivityConfig Aim { get; set; } = new AimRangeActivityConfig();

        [Description("Parkour course settings for the empty far-left Aim-hall subarea.")]
        public ParkourActivityConfig Parkour { get; set; } = new ParkourActivityConfig();
    }

    public sealed class ParkourActivityConfig
    {
        [Description("Offer Pulse Line parkour during warmup. Requires ActivitiesEnabled and the Aim hall. Default OFF.")]
        public bool Enabled { get; set; } = false;

        [Description("Parkour occupancy, gate, recovery, and HUD updates per second.")]
        public float SchedulerRateHz { get; set; } = 20f;

        [Description("Seconds a player must remain on the start plate before the countdown begins.")]
        public float StartHoldSeconds { get; set; } = 0.6f;

        [Description("Quiet countdown duration before the authoritative timer starts.")]
        public float CountdownSeconds { get; set; } = 3f;

        [Description("Seconds after recovery during which gates and another recovery are ignored.")]
        public float RecoveryGraceSeconds { get; set; } = 0.35f;

        [Description("HSM center-X for the parkour flash, hero, and footer.")]
        public float HudX { get; set; } = -1077f;

        [Description("HSM Y of parkour event flashes.")]
        public float FlashY { get; set; } = 592f;

        [Description("HSM Y of the persistent parkour timer and split spine.")]
        public float HeroY { get; set; } = 700f;

        [Description("HSM Y of the parkour instruction footer.")]
        public float FooterY { get; set; } = 805f;
    }

    public sealed class AimRangeActivityConfig
    {
        [Description("Offer the Aim Range during warmup. Requires ActivitiesEnabled. Default OFF.")]
        public bool Enabled { get; set; } = false;

        [Description("Main scheduler updates per second.")]
        public float SchedulerRateHz { get; set; } = 20f;

        [Description("Full-room activity occupancy checks per second.")]
        public float OccupancyRateHz { get; set; } = 5f;

        [Description("Stable salt combined with map/range/card generations.")]
        public int SeedSalt { get; set; } = 17031;

        [Description("Legacy compatibility key; persistent shooting-counter pickups do not replenish.")]
        public float ShelfReplenishSeconds { get; set; } = 1.5f;

        [Description("Persistent shooting-counter weapon presets. Picking one grants an owned inventory copy without removing the displayed pickup.")]
        public List<AimWeaponPresetConfig> WeaponPresets { get; set; } = new List<AimWeaponPresetConfig>
        {
            new AimWeaponPresetConfig("com15", ItemType.GunCOM15, ItemType.Ammo9x19, 60),
            new AimWeaponPresetConfig("com18", ItemType.GunCOM18, ItemType.Ammo9x19, 90),
            new AimWeaponPresetConfig("fsp9", ItemType.GunFSP9, ItemType.Ammo9x19, 120),
            new AimWeaponPresetConfig("crossvec", ItemType.GunCrossvec, ItemType.Ammo9x19, 120),
            new AimWeaponPresetConfig("ak", ItemType.GunAK, ItemType.Ammo762x39, 120),
            new AimWeaponPresetConfig("e11sr", ItemType.GunE11SR, ItemType.Ammo556x45, 120),
        };

        [Description("Seconds each normal target remains live.")]
        public float TargetLiveSeconds { get; set; } = 3f;

        [Description("Seconds between normal target cards.")]
        public float TargetCooldownSeconds { get; set; } = 0.8f;

        [Description("Number of cards in the deterministic target deck before it repeats. Legacy compatibility key; the three persistent sliding targets ignore it.")]
        public int TargetSequenceSize { get; set; } = 24;

        [Description("Number of simultaneous Aim-Lab sphere targets in lane 3. The default 20 uses static toys and the shared hitscan callback; no per-frame raycasts are added.")]
        public int SphereActiveCount { get; set; } = 20;

        [Description("Diameter in metres of each Aim-Lab sphere target.")]
        public float SphereDiameter { get; set; } = 0.72f;

        [Description("Number of authored native dummy bot slots enabled in lane 1 (0-2). Default 0; set explicitly to opt in.")]
        public int BotCount { get; set; } = 0;

        [Description("Human FPC role assigned to range bots.")]
        public RoleTypeId BotRole { get; set; } = RoleTypeId.NtfPrivate;

        [Description("Maximum/current health assigned to each bot spawn.")]
        public float BotHealth { get; set; } = 250f;

        [Description("Seconds allowed for the dummy wrapper and role to initialize.")]
        public float BotInitializeSeconds { get; set; } = 0.35f;

        [Description("Legacy compatibility timeout; direct native FPC movement does not use waypoint association.")]
        public float BotAssociationTimeoutSeconds { get; set; } = 1.5f;

        [Description("Pre-trigger alert stage before the bot snaps to the torso and holds native automatic fire. Clamped to 0.5-0.55 seconds.")]
        public float BotAcquireDelaySeconds { get; set; } = 0.5f;

        [Description("Native Shoot->Hold re-arm cadence. Clamped to 0.5-0.6 seconds so automatic retaliation stays consistent.")]
        public float BotShotCadenceSeconds { get; set; } = 0.55f;

        [Description("Seconds allowed for native ShotWeapon plus ammo-consumption verification.")]
        public float BotShotVerificationSeconds { get; set; } = 0.65f;

        [Description("Configured bot retaliation distance. Values shorter than the continuous hall diagonal are raised at runtime so every valid in-hall aggressor can receive return fire.")]
        public float BotMaxRetaliationDistance { get; set; } = 24f;

        [Description("Maximum camera angular error accepted before native automatic fire is held.")]
        public float BotAimToleranceDegrees { get; set; } = 3f;

        [Description("Seconds before a dead owned bot is respawned with a new identity and gun.")]
        public float BotRespawnSeconds { get; set; } = 4f;

        [Description("Seconds the first valid attacker remains locked as a bot's aggressor.")]
        public float BotAggroLeaseSeconds { get; set; } = 12f;

        [Description("Bot weapon preset. Range bots are forced to the Crossvec automatic preset; legacy non-Crossvec entries are ignored.")]
        public List<AimWeaponPresetConfig> BotWeaponPresets { get; set; } = new List<AimWeaponPresetConfig>
        {
            new AimWeaponPresetConfig("bot-crossvec", ItemType.GunCrossvec, ItemType.Ammo9x19, 240),
        };

        [Description("Full positive health restored by a same-life Aim lethal intercept.")]
        public float HumanResetHealth { get; set; } = 100f;

        [Description("HSM center-X for the whole in-range HUD lane (flash + hero + footer + collapsed status). Rendered center px = 960 + X/2 (in-game measured 2026-08-18), so the default -1077 renders near px421, between the native inventory list and the inventory wheel while TAB is held. CAUTION: multi-char lines starting left of X = -800 word-wrap after their first glyph in game; values below -800 need the ghost-tail placement recipe and an in-game recheck.")]
        public float HudX { get; set; } = -1077f;

        [Description("HSM Y of the force-shown range event flash (HIT / INCOMING / BOT DOWN ...). Keep the flash/hero/footer/collapsed bands non-overlapping. 0 (top) .. 1080 (bottom).")]
        public float FlashY { get; set; } = 592f;

        [Description("HSM Y of the persistent range hero card (eyebrow + hero line + compact data strip). 0..1080.")]
        public float HeroY { get; set; } = 700f;

        [Description("HSM Y of the single active-voice range coaching footer. 0..1080.")]
        public float FooterY { get; set; } = 805f;

        [Description("HSM Y of the one-line collapsed warmup status strip shown while inside the range (replaces the full SCP draft panel). 0..1080.")]
        public float CollapsedStatusY { get; set; } = 900f;

        // Compatibility keys retained so an existing spike-era YAML (including port 7777) still deserializes.
        [Description("Legacy compatibility firearm; ignored when WeaponPresets is populated.")]
        public ItemType Firearm { get; set; } = ItemType.GunCOM15;

        [Description("Legacy compatibility ammo; ignored when WeaponPresets is populated.")]
        public ItemType Ammo { get; set; } = ItemType.Ammo9x19;

        [Description("Legacy compatibility reserve amount; ignored when WeaponPresets is populated.")]
        public int AmmoAmount { get; set; } = 60;
    }

    public sealed class AimWeaponPresetConfig
    {
        public AimWeaponPresetConfig() { }

        public AimWeaponPresetConfig(string id, ItemType firearm, ItemType ammo, int reserveAmmo)
        {
            Id = id;
            Firearm = firearm;
            Ammo = ammo;
            ReserveAmmo = reserveAmmo;
        }

        [Description("Stable preset id.")]
        public string Id { get; set; } = string.Empty;

        [Description("Conventional firearm type.")]
        public ItemType Firearm { get; set; } = ItemType.GunCOM15;

        [Description("Reserve ammo type.")]
        public ItemType Ammo { get; set; } = ItemType.Ammo9x19;

        [Description("Reserve ammo granted while this range gun is owned.")]
        public int ReserveAmmo { get; set; } = 60;

        [Description("Native attachment code. Invalid codes disable only this slot.")]
        public uint AttachmentsCode { get; set; } = 0;
    }
}
