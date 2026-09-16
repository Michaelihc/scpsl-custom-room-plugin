using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Replacement;
using WarmupScpSelector.Services;

namespace WarmupScpSelector;

public sealed class Config
{
    [Description("Whether this plugin is enabled.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Whether debug logging is enabled.")]
    public bool Debug { get; set; } = false;

    [Description("Master switch for the optional warmup activity suite. Default OFF: the SCP draft behaves exactly as before. Turning it on only enables lanes that are also gated on under Activities. Each lane owns one station compartment: Aim fits out the east Aim Bay with persistent counter guns, deterministic native targets, owned native RA bots, lethal human reset, and a bilingual HSM HUD; Parkour generates the Pulse Line in the north shaft. Persistence/scoring remain separate work.")]
    public bool ActivitiesEnabled { get; set; } = false;

    [Description("Shared settings for the warmup activity suite. Only applies when ActivitiesEnabled is true.")]
    public ActivityConfig Activities { get; set; } = new();

    [Description("Early-round SCP disconnect replacement and optional .human opt-out settings. This system is independent of the waiting-for-players draft.")]
    public ScpReplacementConfig ScpReplacement { get; set; } = new();

    [Description("Filename of an authored ProjectMER station schematic to spawn INSTEAD of the generated shell, decor, and gallery exhibits. Empty (default) generates the station in code. Looked up in this plugin's config folder under Schematics/<name>/<name>.json, then ProjectMER's own Schematics folder. Gameplay stays code-driven: selection coins, counter guns, hatch gates, targets, and bots are still spawned at the code anchors, and the Aim Bay is always built by code so its furniture is not spawned twice. An authored parkour shaft IS used, and the Pulse Line reads its gates back off those landings. Keep the exported marker_* blocks in the file - they are the anchor contract.")]
    public string AuthoredStationAsset { get; set; } = string.Empty;

    [Description("Write the standing station to a ProjectMER schematic every time it is built, under that name. Empty (default) disables it. Use this to hand a fresh snapshot to someone editing the room in the in-game map editor; the same export is available on demand via the warmupexport RA command. Written to ProjectMER's Schematics folder when that plugin is installed, otherwise this plugin's own config folder.")]
    public string ExportSchematicName { get; set; } = string.Empty;

    [Description("Player-facing language: \"cn\" for Simplified Chinese (default), \"en\" for English.")]
    public string Language { get; set; } = "cn";

    [Description("Meters to float the warmup station above the surface zone. SCP:SL collision/physics misbehave at extreme coords, so the room is anchored just above the static surface (no map gen there) at sane coordinates where its floor is actually walkable. Enough to clear surface structures.")]
    public float SurfaceClearance { get; set; } = 30f;

    [Description("Fallback world-space origin (station deck centre) used only if the surface zone cannot be found (it normally always can). Avoid extreme coordinates.")]
    public Vector3 RoomOrigin { get; set; } = new(0f, 1015f, 0f);

    [Description("Horizontal spacing between adjacent SCP display stands in the gallery back rank, in meters. Wider spacing fits fewer stands in the back rank and pushes the rest onto the side walls.")]
    public float PedestalSpacing { get; set; } = 3.7f;

    [Description("Uniform scale applied to each spawned SCP model.")]
    public float ModelScale { get; set; } = 1f;

    [Description("Item used as the selection coin in front of each model. The pickup is cancelled so the coin stays put.")]
    public ItemType SelectorItem { get; set; } = ItemType.Coin;

    [Description("Uniform scale of each selection coin (kept large and obvious).")]
    public float SelectorCoinScale { get; set; } = 6f;

    [Description("Seconds between selector status hint refreshes.")]
    public float HintIntervalSeconds { get; set; } = 1f;

    [Description("HSM Y coordinate of the warmup status panel. HSM uses 0 at the top of the screen and 1080 at the bottom, so lower values sit higher up. Default sits near the bottom of the screen.")]
    public float StatusHintY { get; set; } = 840f;

    [Description("HintServiceMeow display settings for the warmup status panel (hint-ID prefix, position, and text size). The countdown/selection text is drawn through HSM so it composes with other HSM/CUIMeow hints instead of being clobbered by the vanilla hint channel.")]
    public HintDisplayConfig HintDisplay { get; set; } = new();

    [Description("Fallback-only delay used if atomic pre-spawn SCP remapping cannot be prepared. Normal round starts apply only each player's final role before it is sent.")]
    public float RoleSwapDelaySeconds { get; set; } = 1.5f;

    [Description("Hide the vanilla 'WAITING FOR PLAYERS / ROUND START IS PAUSED' block while the selector is active.")]
    public bool HideWaitingUi { get; set; } = true;

    [Description("Whether to play lobby music inside the warmup selector.")]
    public bool MusicEnabled { get; set; } = false;

    [Description("Path to preconverted lobby music. Use 48000 Hz mono little-endian float32 raw PCM (.f32le). Relative paths resolve under this plugin's LabAPI config folder.")]
    public string MusicFilePath { get; set; } = "lobby.f32le";

    [Description("Speaker controller id used for lobby music. Change this if another plugin uses the same speaker controller.")]
    public int MusicControllerId { get; set; } = 73;

    [Description("Lobby music volume.")]
    public float MusicVolume { get; set; } = 0.35f;

    [Description("If true, music uses 3D falloff from a speaker in the selector room. If false, it plays as non-spatial lobby music.")]
    public bool MusicSpatial { get; set; } = false;

    [Description("Spatial music distance where falloff starts.")]
    public float MusicMinDistance { get; set; } = 2f;

    [Description("Spatial music distance where falloff reaches zero.")]
    public float MusicMaxDistance { get; set; } = 35f;

    [Description("Seconds used to fade lobby music in for each player when they enter the selector.")]
    public float MusicFadeInSeconds { get; set; } = 2.5f;

    [Description("Seconds used to fade lobby music out before round start.")]
    public float MusicFadeOutSeconds { get; set; } = 5f;

    [Description("Starts the fade-out when the native lobby countdown reaches this many seconds. Use 0 to fade only at final handoff.")]
    public float MusicFadeOutBeforeStartSeconds { get; set; } = 5f;

    [Description("Safety cap for the decoded music length in seconds.")]
    public int MusicMaxSeconds { get; set; } = 240;

    [Description("SCPs offered in the gallery, in stand order (back rank first, then side walls). Each gets one model + one coin. Model is the embedded .mer.json basename; leave it blank to use this plugin's built-in model for known SCP roles.")]
    public List<ScpOption> ScpOptions { get; set; } = new()
    {
        new ScpOption(RoleTypeId.Scp049, "SCP-049", "scp-049"),
        new ScpOption(RoleTypeId.Scp079, "SCP-079", "scp-079"),
        new ScpOption(RoleTypeId.Scp096, "SCP-096", "scp-096"),
        new ScpOption(RoleTypeId.Scp106, "SCP-106", "scp-106"),
        new ScpOption(RoleTypeId.Scp173, "SCP-173", "scp-173"),
        new ScpOption(RoleTypeId.Scp939, "SCP-939", "scp-939"),
        new ScpOption(RoleTypeId.Scp3114, "SCP-3114", "scp-3114"),
    };
}
