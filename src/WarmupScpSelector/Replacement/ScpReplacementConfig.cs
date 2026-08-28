using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;

namespace WarmupScpSelector.Replacement;

/// <summary>Settings for the early-round SCP disconnect replacement flow.</summary>
public sealed class ScpReplacementConfig
{
    [Description("Enable early-round replacement when a healthy SCP disconnects.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Latest round time, in seconds, at which an SCP departure opens a replacement slot.")]
    public float DepartureCutoffSeconds { get; set; } = 60f;

    [Description("Latest round time, in seconds, at which .volunteer may be used.")]
    public float VolunteerCutoffSeconds { get; set; } = 90f;

    [Description("Minimum percentage of maximum health the departing SCP must still have.")]
    public float RequiredHealthPercentage { get; set; } = 95f;

    [Description("Seconds between the first volunteer entering a slot and its lottery resolving.")]
    public float LotterySeconds { get; set; } = 10f;

    [Description("Shared per-player cooldown for .volunteer and .human. Set to 0 to disable.")]
    public float CommandCooldownSeconds { get; set; } = 3f;

    [Description("Allow living non-SCP players as well as spectators to volunteer. Enabled by default for this port.")]
    public bool AllowAliveVolunteers { get; set; } = true;

    [Description("Allow an early-round SCP to use .human (alias .no), opening their SCP slot and becoming a weighted random human role.")]
    public bool AllowHumanCommand { get; set; } = true;

    [Description("Maximum successful replacements per round. 0 means unlimited. Pending slots also reserve capacity.")]
    public int MaxReplacementsPerRound { get; set; } = 0;

    [Description("SCP roles that never open a replacement slot.")]
    public List<RoleTypeId> IgnoredRoles { get; set; } = new()
    {
        RoleTypeId.Scp0492,
    };

    [Description("Weighted human roles used by .human. Non-human/dead roles and non-positive weights are ignored.")]
    public Dictionary<RoleTypeId, int> HumanCommandRoles { get; set; } = new()
    {
        [RoleTypeId.ClassD] = 45,
        [RoleTypeId.Scientist] = 45,
        [RoleTypeId.FacilityGuard] = 10,
    };

    [Description("Extra items granted if .human selects ClassD.")]
    public List<ItemType> ClassDBonusItems { get; set; } = new()
    {
        ItemType.Flashlight,
        ItemType.Coin,
    };

    [Description("Also write replacement announcements to each eligible player's client console.")]
    public bool AnnounceInConsole { get; set; } = true;

    [Description("Seconds the replacement-available broadcast remains visible.")]
    public ushort AnnounceBroadcastSeconds { get; set; } = 16;

    [Description("Seconds confirmation/result broadcasts remain visible.")]
    public ushort ResultBroadcastSeconds { get; set; } = 5;

    [Description("Seconds to advertise .human to SCPs when the round starts. 0 disables the reminder.")]
    public ushort HumanHintSeconds { get; set; } = 8;
}
