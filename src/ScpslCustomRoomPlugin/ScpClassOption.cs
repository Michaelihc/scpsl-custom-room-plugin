using System.ComponentModel;
using PlayerRoles;

namespace ScpslCustomRoomPlugin
{
    public sealed class ScpClassOption
    {
        public ScpClassOption()
        {
        }

        public ScpClassOption(RoleTypeId role, string label, string textOffset, string coinOffset)
        {
            Role = role;
            Label = label;
            TextOffset = textOffset;
            CoinOffset = coinOffset;
        }

        [Description("SCP role selected by this option.")]
        public RoleTypeId Role { get; set; }

        [Description("Floating label shown for this role.")]
        public string Label { get; set; } = "SCP";

        [Description("World-space offset from RoomOrigin for the floating text.")]
        public string TextOffset { get; set; } = "0, 2.4, 2";

        [Description("World-space offset from RoomOrigin for the persistent selector coin.")]
        public string CoinOffset { get; set; } = "0, 1.05, 0.6";
    }
}
