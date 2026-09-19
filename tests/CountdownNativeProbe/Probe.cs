using System;
using System.Linq;
using CommandSystem;
using GameCore;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using UnityEngine;
using Version = System.Version;

namespace WarmupScpSelector.NativeTests;

// Offline arrangement and native telemetry only. No hint writes or feature invocation.
public sealed class ProbePlugin : Plugin
{
    internal static bool Active;
    public override string Name => "CountdownNativeProbe";
    public override string Description => "Offline countdown visual-test arrangement";
    public override string Author => "Local tests";
    public override Version Version => new(1, 0);
    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);
    public override void Enable()
    {
        Active = Environment.GetEnvironmentVariable("OFFLINE_LAB_OBSERVER") == "1";
        if (Active) ConfigFile.ServerConfig.SetString("lobby_waiting_time", "15");
    }
    public override void Disable() => Active = false;
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ProbeCommand : ICommand
{
    public string Command => "countdownprobe";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Offline: state | arrange hub/aim/parkour";
    public bool Execute(ArraySegment<string> args, ICommandSender sender, out string response)
    {
        if (!ProbePlugin.Active) { response = "Offline probe disabled"; return false; }
        if (!sender.CheckPermission(PlayerPermissions.ServerConfigs, out response)) return false;
        var room = WarmupScpSelectorPlugin.Instance?.WarmupRoom;
        if (args.Count == 1 && args.At(0) == "status")
        {
            bool ready = room?.Hall != null && room.IsSpawned && room.AimRangeDoorPrepared && room.ParkourDoorPrepared;
            response = ready ? "COUNTDOWN_READY" : "Countdown station not ready";
            return ready;
        }
        var player = Player.List.SingleOrDefault(p => !p.IsHost && !p.IsDummy && p.IsReady);
        if (room?.Hall == null || !room.IsSpawned || player == null)
        { response = "Expected standing station and one ready native player"; return false; }
        if (args.Count == 2 && args.At(0) == "arrange")
        {
            Vector3 local;
            switch (args.At(1))
            {
                case "hub": local = new Vector3(0, 0, 0); break;
                case "aim": local = new Vector3(9, 0, 0); break;
                case "parkour": local = new Vector3(0, 0, 16); break;
                default: response = "Unknown station approach"; return false;
            }
            var target = room.Origin + local;
            if (!Physics.Raycast(target + Vector3.up * 2, Vector3.down, out var hit, 4))
            { response = "No floor below arrangement anchor"; return false; }
            player.Position = hit.point + Vector3.up * 0.97f;
        }
        else if (args.Count == 2 && args.At(0) == "require")
        {
            var zone = args.At(1) == "aim" ? room.Hall.AimBay : args.At(1) == "parkour" ? room.Hall.ParkourShaft : args.At(1) == "hub" ? room.Hall.Hub : null;
            if (zone == null || !WarmupScpSelector.Warmup.WarmupHallLayout.ContainsPoint(room.Hall.InteriorBounds(zone), player.Position))
            { response = "Native actor did not reach required station zone"; return false; }
            response = "COUNTDOWN_NATIVE_OK " + args.At(1);
            return true;
        }
        else if (args.Count != 1 || args.At(0) != "state")
        { response = Description; return false; }
        var pos = player.Position - room.Origin;
        response = FormattableString.Invariant($"COUNTDOWN_NATIVE timer={RoundStart.singleton.Timer} actor={player.PlayerId} life={player.LifeId} local=({pos.x:F3},{pos.y:F3},{pos.z:F3}) aimOpen={room.AimRangeDoorPrepared} parkourOpen={room.ParkourDoorPrepared}");
        return true;
    }
}
