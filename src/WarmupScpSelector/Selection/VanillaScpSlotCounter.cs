using System;
using PlayerRoles;

namespace WarmupScpSelector.Selection
{
    /// <summary>
    /// Mirrors the small SCP-slot counting loop at the start of RoleAssigner.OnRoundStarted. Keeping this
    /// calculation pure lets the selector know which ChangingRole callback is the final vanilla SCP assignment,
    /// so every pending SCP can be remapped before HumanSpawner counts the remaining players.
    /// </summary>
    public static class VanillaScpSlotCounter
    {
        public static int Count(
            string teamRespawnQueue,
            int eligiblePlayerCount,
            int maxSpawnableScps,
            bool allowScpOverflow)
        {
            if (eligiblePlayerCount <= 0 || string.IsNullOrEmpty(teamRespawnQueue))
            {
                return 0;
            }

            Team[] queue = new Team[teamRespawnQueue.Length];
            int queueLength = 0;
            foreach (char entry in teamRespawnQueue)
            {
                Team team = (Team)(entry - '0');
                if (Enum.IsDefined(typeof(Team), team))
                {
                    queue[queueLength++] = team;
                }
            }

            if (queueLength == 0)
            {
                return 0;
            }

            int scpCount = 0;
            for (int playerIndex = 0; playerIndex < eligiblePlayerCount; playerIndex++)
            {
                if (queue[playerIndex % queueLength] != Team.SCPs)
                {
                    continue;
                }

                scpCount++;
                if (!allowScpOverflow && scpCount == maxSpawnableScps)
                {
                    break;
                }
            }

            return scpCount;
        }
    }
}
