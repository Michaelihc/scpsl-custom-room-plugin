using PlayerRoles;

namespace ScpslCustomRoomPlugin
{
    public static class WarmupText
    {
        public static string InitialSelectionHint(bool useChineseLocalization)
        {
            return useChineseLocalization
                ? "与硬币互动来选择SCP职业。"
                : "Choose an SCP class by interacting with its coin.";
        }

        public static string SelectionName(RoleTypeId? selectedRole, bool useChineseLocalization)
        {
            if (!selectedRole.HasValue)
            {
                return useChineseLocalization ? "无" : "None";
            }

            return FormatRoleName(selectedRole.Value);
        }

        public static string BuildWarmupStatusHint(string countdownLine, RoleTypeId? selectedRole, bool useChineseLocalization)
        {
            string selection = SelectionName(selectedRole, useChineseLocalization);

            return useChineseLocalization
                ? $"{countdownLine}\n已选择SCP：{selection}\n与硬币互动可更改选择。"
                : $"{countdownLine}\nSelected SCP: {selection}\nInteract with a coin to change selection.";
        }

        public static string BuildCountdownLine(short nativeTimer, int playerCount, int maxPlayers, bool useChineseLocalization)
        {
            if (nativeTimer == -2)
            {
                return useChineseLocalization
                    ? $"倒计时：等待玩家\n玩家：{playerCount}/{maxPlayers}"
                    : $"Countdown: Waiting for players\nPlayers: {playerCount}/{maxPlayers}";
            }

            if (useChineseLocalization)
            {
                string timerLine = nativeTimer <= 0
                    ? "倒计时：回合即将开始"
                    : $"倒计时：{nativeTimer}秒";

                return $"{timerLine}\n玩家：{playerCount}/{maxPlayers}";
            }

            string englishTimerLine = nativeTimer <= 0
                ? "Countdown: Round starting"
                : $"Countdown: {nativeTimer}s";

            return $"{englishTimerLine}\nPlayers: {playerCount}/{maxPlayers}";
        }

        private static string FormatRoleName(RoleTypeId role)
        {
            string roleName = role.ToString();
            return roleName.StartsWith("Scp", System.StringComparison.Ordinal)
                ? "SCP-" + roleName.Substring(3)
                : roleName;
        }
    }
}
