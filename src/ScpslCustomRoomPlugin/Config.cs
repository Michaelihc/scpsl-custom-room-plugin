using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Interfaces;
using PlayerRoles;

namespace ScpslCustomRoomPlugin
{
    public sealed class Config : IConfig
    {
        [Description("Whether this plugin is enabled.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Whether debug logging is enabled.")]
        public bool Debug { get; set; }

        [Description("Lock the lobby during warmup and use the plugin countdown instead of the vanilla start flow.")]
        public bool LockLobbyDuringWarmup { get; set; } = true;

        [Description("Manual warmup duration in seconds.")]
        public int WarmupSeconds { get; set; } = 90;

        [Description("Minimum verified players required before the warmup countdown decreases.")]
        public int MinimumPlayersToCountdown { get; set; } = 2;

        [Description("Show plugin countdown hints. The vanilla lobby timer remains visible, so this is disabled by default to avoid duplicate countdown UI.")]
        public bool ShowCountdownHints { get; set; }

        [Description("Seconds after vanilla round start before selected SCPs are swapped in.")]
        public float RoleSwapDelaySeconds { get; set; } = 1.5f;

        [Description("World-space origin of the custom class selection room.")]
        public string RoomOrigin { get; set; } = "0, 1030, 0";

        [Description("Tutorial spawn offset from RoomOrigin.")]
        public string TutorialSpawnOffset { get; set; } = "0, 1.2, -7";

        [Description("Coin item type used as an interaction trigger. The pickup is cancelled so it stays in the room.")]
        public ItemType SelectorItemType { get; set; } = ItemType.Coin;

        [Description("Display size for floating SCP class labels.")]
        public string FloatingTextDisplaySize { get; set; } = "2.5, 1";

        [Description("SCP classes exposed in the warmup selector room.")]
        public List<ScpClassOption> ScpClassOptions { get; set; } = new List<ScpClassOption>
        {
            new ScpClassOption(RoleTypeId.Scp049, "SCP-049", "-9, 2.4, 2", "-9, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp079, "SCP-079", "-6, 2.4, 2", "-6, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp096, "SCP-096", "-3, 2.4, 2", "-3, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp106, "SCP-106", "0, 2.4, 2", "0, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp173, "SCP-173", "3, 2.4, 2", "3, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp939, "SCP-939", "6, 2.4, 2", "6, 1.05, 0.6"),
            new ScpClassOption(RoleTypeId.Scp3114, "SCP-3114", "9, 2.4, 2", "9, 1.05, 0.6"),
        };
    }
}
