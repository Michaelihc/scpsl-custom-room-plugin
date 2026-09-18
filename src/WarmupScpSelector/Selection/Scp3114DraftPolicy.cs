using System;
using System.Collections.Generic;
using PlayerRoles;

namespace WarmupScpSelector.Selection
{
    /// <summary>
    /// Pure rules for honouring an SCP-3114 pick when vanilla did not spawn SCP-3114 (it only does on holidays,
    /// see <c>Scp3114Role.EnableSpawning</c>). Large lobbies hand SCP-3114 to one random picker in place of
    /// whatever human role vanilla was about to give that picker, so the round has exactly one more SCP and one
    /// fewer human than vanilla assigned. Nobody else's role changes.
    /// </summary>
    public static class Scp3114DraftPolicy
    {
        /// <summary>True when the SCP-3114 carve-out should be armed for this round start.</summary>
        public static bool ShouldArm(bool enabled, int eligiblePlayers, int minPlayers, int pickerCount)
        {
            return enabled && pickerCount > 0 && eligiblePlayers >= Math.Max(1, minPlayers);
        }

        /// <summary>
        /// A role vanilla's <c>HumanSpawner</c> hands out at round start. Excludes SCPs and every non-round
        /// pseudo-role so the warmup Tutorial-to-None handoff and spectator changes are never treated as
        /// human assignments.
        /// </summary>
        public static bool IsHumanRoundRole(RoleTypeId role)
        {
            return role is not (RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Tutorial
                or RoleTypeId.Overwatch or RoleTypeId.Filmmaker or RoleTypeId.Destroyed)
                && !ScpOption.IsScpRole(role);
        }
    }

    /// <summary>
    /// Round-start state for one SCP-3114 carve-out. <c>HumanSpawner</c> assigns humans one cancellable
    /// <c>ServerSetRole</c> at a time and re-picks any player still holding None for later slots, so a human
    /// callback can be rewritten in place but must never be cancelled. Only the winner's own callback is
    /// rewritten, to SCP-3114; every other callback passes through untouched.
    /// </summary>
    public sealed class Scp3114Draft<TPlayer>
        where TPlayer : notnull
    {
        private static readonly EqualityComparer<TPlayer> Comparer = EqualityComparer<TPlayer>.Default;

        public Scp3114Draft(TPlayer winner)
        {
            Winner = winner;
        }

        public TPlayer Winner { get; }

        /// <summary>True once the winner's round-start callback was rewritten to SCP-3114.</summary>
        public bool WinnerPromoted { get; private set; }

        /// <summary>
        /// Rewrites the winner's round-start human assignment to SCP-3114. Returns false (and the unchanged
        /// role) for every other callback.
        /// </summary>
        public bool TryRewrite(TPlayer player, RoleTypeId vanillaRole, out RoleTypeId rewrittenRole)
        {
            rewrittenRole = vanillaRole;
            if (WinnerPromoted || !Scp3114DraftPolicy.IsHumanRoundRole(vanillaRole) || !Comparer.Equals(player, Winner))
            {
                return false;
            }

            WinnerPromoted = true;
            rewrittenRole = RoleTypeId.Scp3114;
            return true;
        }
    }
}
