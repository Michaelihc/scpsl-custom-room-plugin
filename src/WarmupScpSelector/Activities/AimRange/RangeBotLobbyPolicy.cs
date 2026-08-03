using System;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>
    /// Fail-closed lobby guard for counted native dummies. A solo human may use bots only while the lobby is
    /// explicitly protected from the native dummy-count start threshold. Bots never fill the final public slot.
    /// </summary>
    public static class RangeBotLobbyPolicy
    {
        public static int AllowedBotCount(
            int requestedBots,
            int canonicalHumans,
            int countedConnectionsWithoutOwnedBots,
            int maxPlayers,
            bool soloLobbyProtected)
        {
            if (requestedBots <= 0 || canonicalHumans <= 0 || maxPlayers <= 0 ||
                canonicalHumans == 1 && !soloLobbyProtected)
            {
                return 0;
            }

            int requested = Math.Min(2, requestedBots);
            int spareBeforeFull = maxPlayers - Math.Max(0, countedConnectionsWithoutOwnedBots) - 1;
            return Math.Max(0, Math.Min(requested, spareBeforeFull));
        }
    }
}
