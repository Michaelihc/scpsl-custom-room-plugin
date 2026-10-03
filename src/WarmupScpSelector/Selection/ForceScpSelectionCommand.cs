using System;
using System.Linq;
using CommandSystem;
using LabApi.Features.Wrappers;
using PlayerRoles;
using WarmupScpSelector.Replacement;

namespace WarmupScpSelector.Selection;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ForceScpSelectionCommand : ICommand
{
    public string Command => "warmupforce";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Force a player's next-round SCP: warmupforce <playerId> <scp> | list | clear <playerId|all>.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.ForceclassWithoutRestrictions, out response))
            return false;

        var plugin = WarmupScpSelectorPlugin.Instance;
        var controller = plugin?.Controller;
        if (plugin?.Config.AdminForceSelectionEnabled != true || controller == null)
        {
            response = "管理员强制选择已关闭（admin_force_selection_enabled）。";
            return false;
        }

        if (arguments.Count == 1 && arguments.At(0).Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            response = controller.ListForcedSelections();
            return true;
        }
        if (arguments.Count == 2 && arguments.At(0).Equals("clear", StringComparison.OrdinalIgnoreCase))
            return controller.ClearForcedSelection(arguments.At(1), out response);

        if (arguments.Count != 2 || !int.TryParse(arguments.At(0), out int playerId))
        {
            response = "用法：warmupforce <playerId> <scp> | list | clear <playerId|all>";
            return false;
        }

        RoleTypeId role = Enum.GetValues(typeof(RoleTypeId)).Cast<RoleTypeId>()
            .Where(candidate => ScpReplacementPolicy.IsMainScp(candidate) &&
                ScpReplacementPolicy.MatchesScpArgument(candidate, arguments.At(1)))
            .DefaultIfEmpty(RoleTypeId.None).First();
        if (!ScpReplacementPolicy.IsMainScp(role))
        {
            response = "请选择 SCP：049、079、096、106、173、939 或 3114。";
            return false;
        }

        Player? player = Player.ReadyList.FirstOrDefault(candidate => candidate.PlayerId == playerId);
        return controller.TryForceSelection(player, role, out response);
    }
}
