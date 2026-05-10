using System.ComponentModel;

namespace ScpslCustomRoomPlugin
{
    public sealed class CustomRoomDefinition
    {
        [Description("Unique identifier for this custom room.")]
        public string Name { get; set; } = "custom_room";

        [Description("Whether this room definition should be processed.")]
        public bool Enabled { get; set; } = true;

        [Description("World position in x, y, z format.")]
        public string Position { get; set; } = "0, 1000, 0";

        [Description("World rotation in x, y, z format.")]
        public string Rotation { get; set; } = "0, 0, 0";
    }
}
