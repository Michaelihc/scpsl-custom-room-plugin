namespace WarmupScpSelector.Activities
{
    /// <summary>
    /// A warmup activity lane (Aim / Dodgeball / Parkour / Duel). The shared <see cref="ActivityManager"/>
    /// owns only the cross-lane lifecycle; each lane owns its own world objects, any granted items, and its
    /// HSM hints, and must tear them down idempotently and WITHOUT throwing when the manager calls these
    /// hooks — <see cref="StopForRoundStart"/> in particular runs inside the core round-start path, before
    /// Tutorial players are flipped to None, so a leak or an exception here would corrupt the live round.
    ///
    /// Deliberately free of Unity/LabAPI types so the lifecycle core stays headless-testable; a lane resolves
    /// its own <c>Player</c> references from the stable string key it was handed when the session began.
    /// </summary>
    public interface IActivityLane
    {
        /// <summary>Stable lane identifier, e.g. <see cref="LaneHintIds.Aim"/>. Used for hint IDs and routing.</summary>
        string LaneId { get; }

        /// <summary>Whether this lane is currently offered (its own config gate, independent of the master switch).</summary>
        bool Enabled { get; }

        /// <summary>
        /// Destroy/disarm EVERY hazard this lane created — granted weapons, dropped pickups, the SCP-018,
        /// pooled toys/lights, and all lane HSM IDs — before the round-start handoff. Must be idempotent and
        /// must never throw.
        /// </summary>
        void StopForRoundStart();

        /// <summary>
        /// Full teardown for any other reason (plugin disable, round restart, empty lobby). Idempotent and
        /// non-throwing, same guarantees as <see cref="StopForRoundStart"/>.
        /// </summary>
        void StopAll();

        /// <summary>Release only the given player's per-lane state. Idempotent and non-throwing.</summary>
        void OnPlayerLeft(string userKey);
    }
}
