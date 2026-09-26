using System.Collections.Generic;
using System.Linq;
using PlayerRoles;
using WarmupScpSelector.Replacement;

namespace WarmupScpSelector.Text;

/// <summary>Bilingual player-facing copy for the SCP replacement flow.</summary>
internal static class ScpReplacementText
{
    private const string Header = "<color=#d6b35a>[SCP REPLACEMENT]</color>\n";

    public static string Departure(RoleTypeId role, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        return Header + (chinese
            ? $"<color=#ff6b6b>SCP-{number}</color> 已空缺。旁观者和存活的非SCP玩家可打开控制台 <color=#f4cf72>~</color> 输入 <color=#62e6c6>.volunteer {number}</color> 参加抽选。"
            : $"<color=#ff6b6b>SCP-{number}</color> is vacant. Spectators and living non-SCP players can open <color=#f4cf72>~</color> and use <color=#62e6c6>.volunteer {number}</color>. ");
    }

    public static string Available(IReadOnlyList<RoleTypeId> roles, bool chinese)
    {
        string list = string.Join(", ", roles.Select(role => "SCP-" + ScpReplacementPolicy.ScpNumber(role)));
        return chinese ? $"当前可替补：{list}" : $"Available replacements: {list}";
    }

    public static string Entered(RoleTypeId role, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        return chinese
            ? $"你已参加 SCP-{number} 的替补抽选。"
            : $"You entered the lottery for SCP-{number}.";
    }

    public static string EnteredBroadcast(RoleTypeId role, bool chinese) =>
        Header + Entered(role, chinese);

    public static string Winner(RoleTypeId role, bool isWinner, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        if (chinese)
        {
            return Header + (isWinner ? $"你已成为 <color=#ff6b6b>SCP-{number}</color>。" : $"<color=#ff6b6b>SCP-{number}</color> 已由一名志愿者补位。");
        }

        return Header + (isWinner ? $"You replaced <color=#ff6b6b>SCP-{number}</color>." : $"A volunteer replaced <color=#ff6b6b>SCP-{number}</color>.");
    }

    public static string NoWinner(RoleTypeId role, bool roleAlreadyFilled, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        if (chinese)
        {
            return Header + (roleAlreadyFilled
                ? $"SCP-{number} 已不再空缺，本次替补已取消。"
                : $"SCP-{number} 没有仍符合条件的志愿者，本次替补已取消。");
        }

        return Header + (roleAlreadyFilled
            ? $"SCP-{number} is no longer vacant; the replacement was cancelled."
            : $"SCP-{number} had no eligible volunteers; the replacement was cancelled.");
    }

    public static string HumanHint(bool chinese) => Header + (chinese
        ? "若想和人类交换身份，可在回合早期打开控制台 <color=#f4cf72>~</color> 输入 <color=#62e6c6>.human</color>。"
        : "To swap roles with a human early this round, open <color=#f4cf72>~</color> and use <color=#62e6c6>.human</color>.");

    public static string SwapOffered(RoleTypeId role, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        return Header + (chinese
            ? $"<color=#ff6b6b>SCP-{number}</color> 想和人类交换身份（保留血量与等级）。存活的人类可打开控制台 <color=#f4cf72>~</color> 输入 <color=#62e6c6>.volunteer {number}</color>，先到先得。"
            : $"<color=#ff6b6b>SCP-{number}</color> wants to swap roles with a human (health and tiers carry over). Living humans can open <color=#f4cf72>~</color> and use <color=#62e6c6>.volunteer {number}</color>; first come, first served.");
    }

    public static string SwapOfferOpened(RoleTypeId role, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        return chinese
            ? $"已发出交换请求。第一个输入 .volunteer {number} 的人类会和你交换身份。"
            : $"Swap offer sent. The first human to use .volunteer {number} trades roles with you.";
    }

    public static string SwapDone(RoleTypeId role, bool isNewScp, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        if (isNewScp)
        {
            return Header + (chinese ? $"你已和 SCP-{number} 交换身份。" : $"You swapped roles and are now SCP-{number}.");
        }

        return Header + (chinese
            ? $"有人接受了交换，你已接手对方的人类身份与装备。"
            : $"A human accepted the swap; you took over their role and kit.");
    }

    public static string SwapTaken(RoleTypeId role, bool chinese)
    {
        string number = ScpReplacementPolicy.ScpNumber(role);
        return Header + (chinese ? $"SCP-{number} 的交换已被接受。" : $"SCP-{number}'s swap offer was taken.");
    }

    public static string SwapNeedsHuman(bool chinese) =>
        chinese ? "只有存活的人类可以接受交换。" : "Only a living human can accept a swap.";

    public static string SwapUnavailable(bool chinese) =>
        chinese ? "该交换请求已失效。" : "That swap offer is no longer available.";

    public static string SwapAlreadyOffered(bool chinese) =>
        chinese ? "你已经发出了交换请求。" : "You already offered a swap.";

    public static string ClaimedRole(bool chinese) =>
        chinese ? "你当前的特殊身份不能参加替补或交换。" : "Your current special role cannot volunteer or swap.";

    public static string Usage(bool chinese) => chinese
        ? "用法：.volunteer <SCP编号>（例如 .volunteer 079 或 .v 079）"
        : "Usage: .volunteer <SCP number> (for example .volunteer 079 or .v 079)";

    public static string PlayerOnly(bool chinese) => chinese ? "此命令只能由游戏内玩家使用。" : "Only an in-game player can use this command.";
    public static string Disabled(bool chinese) => chinese ? "SCP替补功能当前未启用。" : "SCP replacement is not enabled.";
    public static string NoPending(bool chinese) => chinese ? "当前没有可替补的SCP。" : "No SCP is currently available for replacement.";
    public static string TooLate(bool chinese) => chinese ? "本回合的SCP替补窗口已经结束。" : "The SCP replacement window has closed for this round.";
    public static string Invalid(bool chinese) => chinese ? "该SCP编号当前不可替补。" : "That SCP number is not currently available.";
    public static string AlreadyEntered(bool chinese) => chinese ? "你已经参加了该SCP的抽选。" : "You already entered that SCP lottery.";
    public static string ScpCannotVolunteer(bool chinese) => chinese ? "SCP不能参加替补抽选。" : "SCPs cannot volunteer for a replacement.";
    public static string SpectatorOnly(bool chinese) => chinese ? "服务器当前只允许旁观者参加替补抽选。" : "This server currently allows spectators only.";
    public static string Cooldown(double seconds, bool chinese) => chinese ? $"请等待 {seconds:0.0} 秒后再使用。" : $"Wait {seconds:0.0}s before using this command again.";
    public static string HumanDisabled(bool chinese) => chinese ? ".human 当前已禁用。" : ".human is disabled on this server.";
    public static string HumanNotScp(bool chinese) => chinese ? "只有SCP可以使用 .human。" : "Only an SCP can use .human.";
    public static string HumanIgnored(bool chinese) => chinese ? "你的SCP角色不能使用 .human。" : "Your SCP role cannot use .human.";
    public static string HumanLowHealth(bool chinese) => chinese ? "你的生命值过低，不能放弃SCP身份。" : "Your health is too low to give up the SCP role.";
    public static string CapacityReached(bool chinese) => chinese ? "本回合的替补数量已达上限。" : "This round's replacement limit has been reached.";
    public static string SlotAlreadyOpen(bool chinese) => chinese ? "该SCP已经有一个待处理的替补名额。" : "That SCP already has a pending replacement slot.";
    public static string RoleChangeFailed(bool chinese) => chinese ? "职业切换失败；没有开放替补名额。" : "The role change failed; no replacement slot was opened.";
    public static string SwapFailed(bool chinese) => chinese ? "交换失败，双方身份保持不变。" : "The swap failed; both roles are unchanged.";
}
