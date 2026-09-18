using System;
using System.Collections.Generic;
using PlayerRoles;

namespace WarmupScpSelector.Selection
{
    /// <summary>
    /// Pure rules for honouring an SCP-3114 pick when vanilla did not spawn SCP-3114 (it only does on holidays,
    /// see <c>Scp3114Role.EnableSpawning</c>). Large lobbies carve one SCP-3114 out of the Class-D team: one
    /// random picker becomes SCP-3114 in place of one Class-D slot, so the round has exactly one more SCP and
    /// one fewer Class-D than vanilla assigned. Every other team count is untouched.
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
    /// <c>ServerSetRole</c> at a time, in a shuffled order, and re-picks any player still holding None for later
    /// slots; so a human callback can be rewritten in place but must never be cancelled. The winner's own
    /// callback is rewritten to SCP-3114. If vanilla had given the winner a non-Class-D role, that role is owed
    /// to the next Class-D callback, which keeps every other team's count intact; if no Class-D callback follows,
    /// <see cref="TakeOwedRole"/> lets the caller move one already-spawned Class-D onto it after spawning.
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

        /// <summary>The winner's displaced non-Class-D vanilla role until a Class-D callback absorbs it.</summary>
        public RoleTypeId OwedRole { get; private set; } = RoleTypeId.None;

        /// <summary>
        /// Rewrites one round-start human assignment when the carve-out needs it. Returns false (and the
        /// unchanged role) for every callback that must pass through untouched.
        /// </summary>
        public bool TryRewrite(TPlayer player, RoleTypeId vanillaRole, out RoleTypeId rewrittenRole)
        {
            rewrittenRole = vanillaRole;
            if (!Scp3114DraftPolicy.IsHumanRoundRole(vanillaRole))
            {
                return false;
            }

            if (!WinnerPromoted)
            {
                if (!Comparer.Equals(player, Winner))
                {
                    return false;
                }

                WinnerPromoted = true;
                if (vanillaRole != RoleTypeId.ClassD)
                {
                    OwedRole = vanillaRole;
                }

                rewrittenRole = RoleTypeId.Scp3114;
                return true;
            }

            if (OwedRole != RoleTypeId.None && vanillaRole == RoleTypeId.ClassD)
            {
                rewrittenRole = OwedRole;
                OwedRole = RoleTypeId.None;
                return true;
            }

            return false;
        }

        /// <summary>
        /// After vanilla finished spawning: the role still owed to the Class-D team, or None. Clears it so the
        /// post-spawn fix is applied at most once.
        /// </summary>
        public RoleTypeId TakeOwedRole()
        {
            if (!WinnerPromoted)
            {
                return RoleTypeId.None;
            }

            RoleTypeId owed = OwedRole;
            OwedRole = RoleTypeId.None;
            return owed;
        }
    }
}
