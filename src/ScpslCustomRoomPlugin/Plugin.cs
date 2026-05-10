using System;
using Exiled.API.Features;
using HarmonyLib;
using PlayerHandlers = Exiled.Events.Handlers.Player;
using ServerHandlers = Exiled.Events.Handlers.Server;

namespace ScpslCustomRoomPlugin
{
    public sealed class Plugin : Plugin<Config>
    {
        private WarmupSelectionController? warmupSelectionController;
        private Harmony? harmony;

        public override string Name => "SCP:SL Custom Room Plugin";

        public override string Author => "Michael";

        public override string Prefix => "scpsl_custom_room_plugin";

        public override Version Version => new Version(0, 1, 0);

        public override Version RequiredExiledVersion => new Version(9, 13, 3);

        public override void OnEnabled()
        {
            harmony = new Harmony($"scpsl_custom_room_plugin.{DateTime.UtcNow.Ticks}");
            harmony.PatchAll();

            warmupSelectionController = new WarmupSelectionController(this);
            ServerHandlers.WaitingForPlayers += warmupSelectionController.BeginWarmup;
            ServerHandlers.RoundStarted += warmupSelectionController.OnRoundStarted;
            PlayerHandlers.Verified += warmupSelectionController.OnVerified;
            PlayerHandlers.Left += warmupSelectionController.OnLeft;
            PlayerHandlers.Spawning += warmupSelectionController.OnSpawning;
            PlayerHandlers.PickingUpItem += warmupSelectionController.OnPickingUpItem;

            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            if (warmupSelectionController is not null)
            {
                ServerHandlers.WaitingForPlayers -= warmupSelectionController.BeginWarmup;
                ServerHandlers.RoundStarted -= warmupSelectionController.OnRoundStarted;
                PlayerHandlers.Verified -= warmupSelectionController.OnVerified;
                PlayerHandlers.Left -= warmupSelectionController.OnLeft;
                PlayerHandlers.Spawning -= warmupSelectionController.OnSpawning;
                PlayerHandlers.PickingUpItem -= warmupSelectionController.OnPickingUpItem;
                warmupSelectionController.CleanupRoom();
                warmupSelectionController = null;
            }

            harmony?.UnpatchAll(harmony.Id);
            harmony = null;

            base.OnDisabled();
        }
    }
}
