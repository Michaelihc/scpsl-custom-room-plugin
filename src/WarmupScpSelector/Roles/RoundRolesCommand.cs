using System;
using System.Linq;
using System.Text;
using CommandSystem;
using LabApi.Features.Wrappers;

namespace WarmupScpSelector.Roles;

/// <summary>
/// Remote Admin / server console: <c>roundroles</c>. Read-only list of the registered draft slots and every
/// current round role claim, so staff can see who holds which special role.
/// </summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class RoundRolesCommand : ICommand
{
    public string Command => "roundroles";

    public string[] Aliases => new[] { "rroles" };

    public string Description => "List round role draft slots and current claims.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.GameplayData, out response))
        {
            return false;
        }

        StringBuilder text = new();
        text.Append("ROUNDROLES host=").Append(RoundRoles.IsHostActive ? "active" : "inactive")
            .Append(" draft=").Append(RoundRoles.IsDraftCompleted ? "done" : "pending").Append('\n');
        text.Append("slots: ").Append(string.Join(", ", RoundRoles.SlotIds())).Append('\n');
        foreach (var claim in RoundRoles.Snapshot())
        {
            Player? player = Player.Get(claim.Key);
            text.Append("claim ").Append(claim.Key).Append(' ').Append(claim.Value).Append(' ')
                .Append(player?.Nickname ?? "?").Append(' ').Append(player?.Role.ToString() ?? "?").Append('\n');
        }

        response = text.ToString().TrimEnd();
        return true;
    }
}
