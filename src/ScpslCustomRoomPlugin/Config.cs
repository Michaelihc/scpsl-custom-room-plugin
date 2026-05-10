using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Enums;
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

        [Description("Lock the vanilla lobby countdown while the plugin warmup is active. Keep this false when using the game's normal start timer.")]
        public bool LockLobbyDuringWarmup { get; set; } = false;

        [Description("Force-start a temporary round for warmup. Disabled by default; the Lobby-style existing-room warmup runs during waiting-for-players.")]
        public bool ForceRoundStartForWarmup { get; set; }

        [Description("Prevent round-end checks while the selector warmup is active.")]
        public bool SuppressRoundEndDuringWarmup { get; set; } = true;

        [Description("Legacy manual warmup duration in seconds. The normal selector warmup uses the game's native lobby countdown.")]
        public int WarmupSeconds { get; set; } = 90;

        [Description("Minimum verified players required before the game's lobby countdown can run.")]
        public int MinimumPlayersToCountdown { get; set; } = 2;

        [Description("Use an existing generated map room instead of spawning a floating primitive room.")]
        public bool UseExistingLobbyRoom { get; set; } = true;

        [Description("Existing room used as the warmup selector lobby.")]
        public RoomType ExistingLobbyRoomType { get; set; } = RoomType.Lcz173;

        [Description("Use this door as the selector lobby anchor when available.")]
        public DoorType ExistingLobbyAnchorDoorType { get; set; } = DoorType.Scp173Gate;

        [Description("Local offset from ExistingLobbyAnchorDoorType used as the selector lobby origin.")]
        public string ExistingLobbyAnchorDoorOffset { get; set; } = "0, 0, -2";

        [Description("Open the selector lobby anchor gate while warmup is active.")]
        public bool OpenExistingLobbyAnchorDoor { get; set; } = true;

        [Description("Local position inside ExistingLobbyRoomType for the selector lobby origin. Default matches the Lobby plugin SCP173 location.")]
        public string ExistingLobbyRoomOffset { get; set; } = "17, 13, 8";

        [Description("Local rotation inside ExistingLobbyRoomType for the selector lobby origin.")]
        public string ExistingLobbyRoomRotation { get; set; } = "0, -90, 0";

        [Description("Hide the vanilla waiting-for-players start-round UI object while the selector lobby is active.")]
        public bool HideNativeWaitingUi { get; set; } = true;

        [Description("Lock the SCP-173 connector door while the selector lobby is active.")]
        public bool LockScp173ConnectorDuringWarmup { get; set; } = true;

        [Description("Show plugin status hints while the selector is active.")]
        public bool ShowCountdownHints { get; set; } = true;

        [Description("Seconds after vanilla round start before selected SCPs are swapped in.")]
        public float RoleSwapDelaySeconds { get; set; } = 1.5f;

        [Description("World-space origin of the custom class selection room.")]
        public string RoomOrigin { get; set; } = "0, 1030, 0";

        [Description("Tutorial spawn offset from RoomOrigin.")]
        public string TutorialSpawnOffset { get; set; } = "0, 1.2, -7";

        [Description("Coin item type used as an interaction trigger. The pickup is cancelled so it stays in the room.")]
        public ItemType SelectorItemType { get; set; } = ItemType.Coin;

        [Description("Display size for floating SCP class labels.")]
        public string FloatingTextDisplaySize { get; set; } = "0.9, 0.35";

        [Description("Scale applied to each selector coin pickup.")]
        public string SelectorCoinScale { get; set; } = "7.5, 7.5, 7.5";

        [Description("SCP classes exposed in the warmup selector room.")]
        public List<ScpClassOption> ScpClassOptions { get; set; } = new List<ScpClassOption>
        {
            new ScpClassOption(RoleTypeId.Scp049, "049", "-8, 2.0, 2", "-8, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp079, "079", "-6, 2.0, 2", "-6, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp096, "096", "-4, 2.0, 2", "-4, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp106, "106", "-2, 2.0, 2", "-2, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp173, "173", "0, 2.0, 2", "0, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp939, "939", "2, 2.0, 2", "2, 1.05, 2.0"),
            new ScpClassOption(RoleTypeId.Scp3114, "3114", "4, 2.0, 2", "4, 1.05, 2.0"),
        };
    }
}
