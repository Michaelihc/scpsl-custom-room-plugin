using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Interfaces;

namespace ScpslCustomRoomPlugin
{
    public sealed class Config : IConfig
    {
        [Description("Whether this plugin is enabled.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Whether debug logging is enabled.")]
        public bool Debug { get; set; }

        [Description("Whether custom room setup should run when a round starts.")]
        public bool SpawnRoomsOnRoundStart { get; set; } = true;

        [Description("Custom room definitions. Coordinates are placeholders for future spawn/build logic.")]
        public List<CustomRoomDefinition> CustomRooms { get; set; } = new List<CustomRoomDefinition>
        {
            new CustomRoomDefinition
            {
                Name = "example_room",
                Enabled = false,
                Position = "0, 1000, 0",
                Rotation = "0, 0, 0",
            },
        };
    }
}
