using PlayerRoles;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// Who the warmup hands back to vanilla at round start, and why.
///
/// Vanilla's own gate is <c>RoleAssigner.CheckPlayer</c>, which returns false for anyone who
/// <c>IsAlive()</c> (and for a spectator that is not ready to respawn). So every player the selector
/// moved in must be returned to <see cref="RoleTypeId.None"/> - dead, not spectating - before the
/// assigner runs, or vanilla skips them and they keep whatever role they were holding.
///
/// The bug this exists to prevent: the hand-back used to require the player still be
/// <see cref="RoleTypeId.Tutorial"/>. An admin who RA-set a warmup participant to an SCP mid-lobby left
/// them ALIVE under a role the selector never gave them, which failed that check, so they were skipped
/// twice over - once here and once by vanilla - and carried that SCP into the round IN ADDITION to the
/// full multiset vanilla handed everyone else. The draft's swap math, which reads vanilla's assignment,
/// never saw them either.
///
/// Note that Spectator is NOT a safe hand-back target: <c>CheckPlayer</c> explicitly excludes a
/// <c>SpectatorRole</c> with <c>ReadyToRespawn == false</c>, so parking players there can drop them
/// from assignment. None is the role the assigner accepts.
/// </summary>
internal static class WarmupHandoffPolicy
{
    /// <summary>The role warmup participants are returned to so vanilla will assign them.</summary>
    public const RoleTypeId HandoffRole = RoleTypeId.None;

    /// <summary>
    /// Whether this player must be handed back. Ownership still gates it: only players THIS selector
    /// moved in are touched, so a player another plugin or an admin is deliberately managing outside the
    /// warmup is left alone. Within that set, the current role is irrelevant - that is the whole fix.
    /// </summary>
    public static bool ShouldHandBack(bool isMovedIn, RoleTypeId currentRole) =>
        isMovedIn && currentRole != HandoffRole;

    /// <summary>Whether a hand-back attempt actually landed; anything else was blocked by another plugin.</summary>
    public static bool HandBackSucceeded(RoleTypeId roleAfterAttempt) => roleAfterAttempt == HandoffRole;
}
