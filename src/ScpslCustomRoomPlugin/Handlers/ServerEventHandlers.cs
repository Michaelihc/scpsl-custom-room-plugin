using System.Linq;
using Exiled.API.Features;

namespace ScpslCustomRoomPlugin.Handlers
{
    internal sealed class ServerEventHandlers
    {
        private readonly Plugin plugin;

        public ServerEventHandlers(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void OnWaitingForPlayers()
        {
            if (plugin.Config.Debug)
            {
                Log.Debug($"{plugin.Name} is loaded and waiting for players.");
            }
        }

        public void OnRoundStarted()
        {
            if (!plugin.Config.SpawnRoomsOnRoundStart)
            {
                return;
            }

            int enabledRoomCount = plugin.Config.CustomRooms.Count(room => room.Enabled);

            if (enabledRoomCount == 0)
            {
                Log.Debug("No enabled custom rooms are configured.");
                return;
            }

            Log.Info($"Custom room setup requested for {enabledRoomCount} room(s). Add spawn/build logic here.");
        }
    }
}
