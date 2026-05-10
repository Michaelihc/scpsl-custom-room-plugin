using System;
using Exiled.API.Features;
using ServerHandlers = Exiled.Events.Handlers.Server;

namespace ScpslCustomRoomPlugin
{
    public sealed class Plugin : Plugin<Config>
    {
        private Handlers.ServerEventHandlers? serverEventHandlers;

        public override string Name => "SCP:SL Custom Room Plugin";

        public override string Author => "Michael";

        public override string Prefix => "scpsl_custom_room_plugin";

        public override Version Version => new Version(0, 1, 0);

        public override Version RequiredExiledVersion => new Version(9, 13, 3);

        public override void OnEnabled()
        {
            serverEventHandlers = new Handlers.ServerEventHandlers(this);
            ServerHandlers.WaitingForPlayers += serverEventHandlers.OnWaitingForPlayers;
            ServerHandlers.RoundStarted += serverEventHandlers.OnRoundStarted;

            base.OnEnabled();
        }

        public override void OnDisabled()
        {
            if (serverEventHandlers is not null)
            {
                ServerHandlers.WaitingForPlayers -= serverEventHandlers.OnWaitingForPlayers;
                ServerHandlers.RoundStarted -= serverEventHandlers.OnRoundStarted;
                serverEventHandlers = null;
            }

            base.OnDisabled();
        }
    }
}
