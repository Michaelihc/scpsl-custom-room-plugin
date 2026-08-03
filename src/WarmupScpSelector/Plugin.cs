using System;
using System.Collections.Generic;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using PlayerRoles;
using PlayerRoles.RoleAssign;
using WarmupScpSelector.Services;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector;

/// <summary>
/// Warmup SCP draft. During waiting-for-players the plugin builds one room holding a model of every
/// offered SCP, each with a big coin. Players walk up and grab a coin to pick that SCP. When the round
/// starts, the plugin lets the game assign vanilla roles, then swaps the selected SCP slots over to the
/// players who picked them (the displaced holder inherits the picker's original role). It never creates
/// extra SCPs: a pick is honoured only if vanilla actually spawned that SCP this round.
/// </summary>
public sealed class WarmupScpSelectorPlugin : Plugin<Config>
{
    private SelectorController _controller = null!;
    private HsmHintDisplayProvider _hints = null!;

    public static WarmupScpSelectorPlugin Instance { get; private set; } = null!;

    public override string Name => "WarmupScpSelector";

    public override string Description => "Warmup room where players pick their SCP from a gallery of models; selected SCPs are swapped into the vanilla round assignment at round start.";

    public override string Author => "Michael";

    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        Instance = this;

        // Player-facing text goes through HintServiceMeow so the warmup status panel composes with the
        // server's HSM/CUIMeow HUD instead of being clobbered on the vanilla hint channel.
        _hints = new HsmHintDisplayProvider(Config.HintDisplay);
        _hints.Enable();

        _controller = new SelectorController(this, _hints);

        ServerEvents.WaitingForPlayers += _controller.OnWaitingForPlayers;
        ServerEvents.RoundRestarted += _controller.OnRoundRestarted;
        PlayerEvents.Joined += _controller.OnPlayerJoined;
        PlayerEvents.Left += _controller.OnPlayerLeft;
        PlayerEvents.Spawning += _controller.OnPlayerSpawning;
        // SearchingPickup (not PickingUpItem) so the selector works for ANY configured pickup type:
        // ammo/armor route through their own pickup events, but all of them fire SearchingPickup.
        PlayerEvents.SearchingPickup += _controller.OnSearchingPickup;

        // These two fire INSIDE the vanilla round-start, in a deterministic order within the same call:
        // OnBeforePlayersSpawned (before assignment) hands warmup players back to vanilla; OnPlayersSpawned
        // (after assignment) is where we schedule the SCP swaps. Using OnPlayersSpawned instead of LabAPI's
        // RoundStarted avoids depending on cross-subscriber event ordering (RoundStarted may fire first).
        RoleAssigner.OnBeforePlayersSpawned += _controller.OnBeforeVanillaRoleAssignment;
        RoleAssigner.OnPlayersSpawned += _controller.OnVanillaRolesAssigned;

        Logger.Info($"{Name} {Version} enabled.");
    }

    public override void Disable()
    {
        ServerEvents.WaitingForPlayers -= _controller.OnWaitingForPlayers;
        ServerEvents.RoundRestarted -= _controller.OnRoundRestarted;
        PlayerEvents.Joined -= _controller.OnPlayerJoined;
        PlayerEvents.Left -= _controller.OnPlayerLeft;
        PlayerEvents.Spawning -= _controller.OnPlayerSpawning;
        PlayerEvents.SearchingPickup -= _controller.OnSearchingPickup;
        RoleAssigner.OnBeforePlayersSpawned -= _controller.OnBeforeVanillaRoleAssignment;
        RoleAssigner.OnPlayersSpawned -= _controller.OnVanillaRolesAssigned;

        _controller.Cleanup();
        _hints?.Disable();
        _hints = null!;
        Instance = null!;
        Logger.Info($"{Name} disabled.");
    }

    public void LogDebug(string message)
    {
        if (Config.Debug)
        {
            Logger.Info($"[WarmupScpSelector] {message}");
        }
    }

    // TEST SUPPORT ONLY (dummy harness): inject display-only selections so the live count badge can be
    // eyeballed with bots that can't grab a coin. These never affect role assignment or the round-start
    // swap. See SelectorController.SetExternalSelections. Not for production use.
    public void SetTestSelections(IEnumerable<KeyValuePair<Player, RoleTypeId>> picks)
    {
        _controller?.SetExternalSelections(picks);
    }
}
