using System;
using System.Collections.Generic;
using System.IO;
using CommandSystem;
using LabApi.Loader;
using LabApi.Loader.Features.Paths;
using WarmupScpSelector.Activities.Parkour;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.Export;

/// <summary>
/// Remote Admin / server console: <c>warmupexport [name]</c>.
///
/// Dumps the warmup station that is standing right now as a ProjectMER schematic, so it can be handed to
/// someone to rearrange in the in-game map editor and given back. It writes into ProjectMER's own
/// Schematics folder when that plugin is installed, so the file is immediately loadable with
/// <c>mp spawn &lt;name&gt;</c>; otherwise it falls back to this plugin's config folder.
///
/// Read-only with respect to the running station: it inspects and writes a file, and never moves,
/// destroys, or respawns anything.
/// </summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ExportStationCommand : ICommand
{
    /// <summary>
    /// Underscored on purpose: ProjectMER's own schematic lister filters out any name containing '-',
    /// so a hyphenated default would export a file the editor then refuses to list.
    /// </summary>
    private const string DefaultName = "warmup_station";

    public string Command => "warmupexport";

    public string[] Aliases => new[] { "wsexport" };

    public string Description =>
        "Export the standing warmup station as a ProjectMER schematic. Usage: warmupexport [name]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.ServerConsoleCommands, out response))
        {
            return false;
        }

        WarmupScpSelectorPlugin? plugin = WarmupScpSelectorPlugin.Instance;
        SelectorRoom? room = plugin?.WarmupRoom;
        if (plugin == null || room?.Hall == null || !room.IsSpawned)
        {
            response = "The warmup station is not standing. Export during waiting-for-players.";
            return false;
        }

        string name = arguments.Count >= 1 ? Sanitize(arguments.At(0)) : DefaultName;
        if (name.Length == 0)
        {
            response = "That schematic name has no usable characters. Usage: warmupexport [name]";
            return false;
        }

        ParkourLayout? parkour = plugin.Controller?.ParkourLayout;
        AimRangeLayout? aim = plugin.Controller?.AimLayout;

        if (!StationSchematicExporter.TryExport(room.Hall, parkour, aim, room.NewsBoard.Owns, name, ResolveSchematicsDirectory(plugin), out StationExportResult result, out string error))
        {
            response = $"Export failed: {error}";
            return false;
        }

        string missing = parkour == null || aim == null
            ? $" NOTE: {(parkour == null ? "parkour" : string.Empty)}{(parkour == null && aim == null ? " and " : string.Empty)}{(aim == null ? "aim range" : string.Empty)} " +
              "was not running, so its geometry and anchors are absent from this export."
            : string.Empty;

        response = $"Exported {result} to {result.Path}.{missing}";
        return true;
    }

    /// <summary>
    /// Prefer ProjectMER's Schematics folder so the file is directly loadable by whoever edits it; fall
    /// back to this plugin's own config folder when ProjectMER is not installed on the port.
    /// </summary>
    /// <summary>Every folder a schematic may live in, plugin-local first so a local copy wins.</summary>
    internal static IEnumerable<string> SchematicSearchRoots(WarmupScpSelectorPlugin plugin)
    {
        yield return Path.Combine(plugin.GetConfigDirectory().FullName, "Schematics");
        string projectMer;
        try
        {
            projectMer = Path.Combine(PathManager.Configs.FullName, "ProjectMER", "Schematics");
        }
        catch
        {
            yield break;
        }

        yield return projectMer;
    }

    internal static string ResolveSchematicsDirectory(WarmupScpSelectorPlugin plugin)
    {
        try
        {
            string projectMer = Path.Combine(PathManager.Configs.FullName, "ProjectMER", "Schematics");
            if (Directory.Exists(projectMer))
            {
                return projectMer;
            }
        }
        catch
        {
            // Fall through to the plugin-local folder.
        }

        return Path.Combine(plugin.GetConfigDirectory().FullName, "Schematics");
    }

    /// <summary>
    /// ProjectMER discovers schematics by filename and excludes any name containing '-' from its own
    /// listing helper, so keep names to safe characters and translate separators to underscores.
    /// </summary>
    private static string Sanitize(string value)
    {
        char[] buffer = new char[value.Length];
        int length = 0;
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                buffer[length++] = c;
            }
            else if (c == '-' || c == ' ' || c == '.')
            {
                buffer[length++] = '_';
            }
        }

        return new string(buffer, 0, length);
    }
}
