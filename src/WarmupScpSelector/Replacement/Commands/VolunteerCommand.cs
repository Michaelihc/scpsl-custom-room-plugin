using System;
using CommandSystem;
using LabApi.Features.Wrappers;
using WarmupScpSelector.Text;

namespace WarmupScpSelector.Replacement.Commands;

/// <summary>Client-console command: <c>.volunteer [SCP number]</c>, alias <c>.v</c>.</summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class VolunteerCommand : ICommand
{
    public string Command => "volunteer";

    public string[] Aliases => new[] { "v" };

    public string Description => "List vacant SCPs or enter a replacement lottery. Usage: .volunteer <SCP number>";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        WarmupScpSelectorPlugin? plugin = WarmupScpSelectorPlugin.Instance;
        bool chinese = string.Equals(plugin?.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);
        if (plugin?.ReplacementService == null)
        {
            response = ScpReplacementText.Disabled(chinese);
            return false;
        }

        if (arguments.Count > 1)
        {
            response = ScpReplacementText.Usage(chinese);
            return false;
        }

        Player? player = Player.Get(sender);
        string? argument = arguments.Count == 1 ? arguments.At(0) : null;
        return plugin.ReplacementService.ExecuteVolunteer(player, argument, out response);
    }
}
