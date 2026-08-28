using System;
using CommandSystem;
using LabApi.Features.Wrappers;
using WarmupScpSelector.Text;

namespace WarmupScpSelector.Replacement.Commands;

/// <summary>Client-console SCP opt-out command: <c>.human</c>, alias <c>.no</c>.</summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class HumanCommand : ICommand
{
    public string Command => "human";

    public string[] Aliases => new[] { "no" };

    public string Description => "Give up an early-round SCP role and open it for replacement. Usage: .human";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        WarmupScpSelectorPlugin? plugin = WarmupScpSelectorPlugin.Instance;
        bool chinese = string.Equals(plugin?.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);
        if (plugin?.ReplacementService == null)
        {
            response = ScpReplacementText.Disabled(chinese);
            return false;
        }

        if (arguments.Count != 0)
        {
            response = chinese ? "用法：.human" : "Usage: .human";
            return false;
        }

        return plugin.ReplacementService.ExecuteHuman(Player.Get(sender), out response);
    }
}
