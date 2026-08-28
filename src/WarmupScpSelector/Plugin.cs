using System;
using System.Collections.Generic;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using PlayerRoles;
using PlayerRoles.RoleAssign;
using WarmupScpSelector.Replacement;
using WarmupScpSelector.Services;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector;

/// <summary>
/// Warmup SCP draft. During waiting-for-players the plugin builds one room holding a model of every
/// offered SCP, each with a big coin. Players walk up and grab a coin to pick that SCP. When the round
/// starts, vanilla still chooses the SCP role multiset; the plugin buffers those pending assignments and
/// remaps their recipients before any SCP role is initialized or sent. Displaced holders remain eligible for
/// vanilla human assignment. It never creates extra SCPs: a pick is honoured only if vanilla spawned it.
/// </summary>
public sealed class WarmupScpSelectorPlugin : Plugin<Config>
{
    private SelectorController _controller = null!;
    private HsmHintDisplayProvider _hints = null!;
    private ScpReplacementService _replacement = null!;

    public static WarmupScpSelectorPlugin Instance { get; private set; } = null!;

    internal ScpReplacementService? ReplacementService => _replacement;

    public override string Name => "WarmupScpSelector";

    public override string Description => "Warmup SCP draft plus early-round replacement for healthy SCP disconnects.";

    public override string Author => "Michael";

    public override Version Version => new(1, 1, 0);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        Instance = this;

        // Player-facing text goes through HintServiceMeow so the warmup status panel composes with the
        // server's HSM/CUIMeow HUD instead of being clobbered on the vanilla hint channel.
        _hints = new HsmHintDisplayProvider(Config.HintDisplay);
        _hints.Enable();

        _controller = new SelectorController(this, _hints);
        _replacement = new ScpReplacementService(this);

        // Register replacement first so its Left handler snapshots an SCP's role/health before any other
        // plugin-owned per-player teardown runs. Its round state is otherwise independent of warmup.
        ServerEvents.RoundStarted += _replacement.OnRoundStarted;
        ServerEvents.RoundEnded += _replacement.OnRoundEnded;
        ServerEvents.WaitingForPlayers += _controller.OnWaitingForPlayers;
        ServerEvents.RoundRestarted += _replacement.OnRoundRestarted;
        ServerEvents.RoundRestarted += _controller.OnRoundRestarted;
        PlayerEvents.Joined += _controller.OnPlayerJoined;
        PlayerEvents.Left += _replacement.OnPlayerLeft;
        PlayerEvents.Left += _controller.OnPlayerLeft;
        PlayerEvents.ChangingRole += _controller.OnPlayerChangingRole;
        PlayerEvents.Spawning += _controller.OnPlayerSpawning;
        // SearchingPickup (not PickingUpItem) so the selector works for ANY configured pickup type:
        // ammo/armor route through their own pickup events, but all of them fire SearchingPickup.
        PlayerEvents.SearchingPickup += _controller.OnSearchingPickup;

        // These fire INSIDE vanilla round-start in a deterministic order. OnBeforePlayersSpawned hands warmup
        // players back and arms pending-role interception; ChangingRole buffers vanilla's SCP calls and applies
        // only the final permutation before HumanSpawner; OnPlayersSpawned clears state or runs the fail-safe.
        RoleAssigner.OnBeforePlayersSpawned += _controller.OnBeforeVanillaRoleAssignment;
        RoleAssigner.OnPlayersSpawned += _controller.OnVanillaRolesAssigned;

        Logger.Info($"{Name} {Version} enabled.");
    }

    public override void Disable()
    {
        ServerEvents.RoundStarted -= _replacement.OnRoundStarted;
        ServerEvents.RoundEnded -= _replacement.OnRoundEnded;
        ServerEvents.WaitingForPlayers -= _controller.OnWaitingForPlayers;
        ServerEvents.RoundRestarted -= _replacement.OnRoundRestarted;
        ServerEvents.RoundRestarted -= _controller.OnRoundRestarted;
        PlayerEvents.Joined -= _controller.OnPlayerJoined;
        PlayerEvents.Left -= _replacement.OnPlayerLeft;
        PlayerEvents.Left -= _controller.OnPlayerLeft;
        PlayerEvents.ChangingRole -= _controller.OnPlayerChangingRole;
        PlayerEvents.Spawning -= _controller.OnPlayerSpawning;
        PlayerEvents.SearchingPickup -= _controller.OnSearchingPickup;
        RoleAssigner.OnBeforePlayersSpawned -= _controller.OnBeforeVanillaRoleAssignment;
        RoleAssigner.OnPlayersSpawned -= _controller.OnVanillaRolesAssigned;

        _replacement.Cleanup();
        _controller.Cleanup();
        _hints?.Disable();
        _replacement = null!;
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
