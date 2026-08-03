using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities
{
    /// <summary>
    /// The suite's cross-lane lifecycle core. It holds one <b>exclusive</b> session per player (a player is only
    /// ever in one lane), hands out a monotonic <b>generation token</b> per session so a late/stale callback can
    /// be dropped, and drives the idempotent, never-throwing teardown paths every lane must honour. It owns no
    /// Unity/LabAPI types and never touches world state itself — so this critical-path logic is headless-testable
    /// — delegating the actual hazard/hint teardown to each registered <see cref="IActivityLane"/>.
    ///
    /// Threading: like the rest of the plugin, all calls come from the SCP:SL main thread.
    /// </summary>
    public sealed class ActivityManager
    {
        private sealed class Session
        {
            public Session(string laneId, int generation)
            {
                LaneId = laneId;
                Generation = generation;
            }

            public string LaneId { get; }

            public int Generation { get; }
        }

        private readonly Dictionary<string, IActivityLane> _lanes = new Dictionary<string, IActivityLane>(StringComparer.Ordinal);
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private int _generationCounter;

        /// <summary>Number of lanes currently registered (test/diagnostic visibility).</summary>
        public int LaneCount => _lanes.Count;

        /// <summary>Register a lane so it participates in teardown. Registering the same id again replaces it.</summary>
        public void RegisterLane(IActivityLane lane)
        {
            if (lane == null || string.IsNullOrEmpty(lane.LaneId))
            {
                return;
            }

            _lanes[lane.LaneId] = lane;
        }

        /// <summary>
        /// Claim the given player for a lane, ending any prior session first (exclusivity). Returns a fresh
        /// generation token, or 0 when the key is empty or the lane is not registered — callers must treat a
        /// 0 token as "no session" and never guard on it.
        /// </summary>
        public int BeginSession(string userKey, string laneId)
        {
            if (string.IsNullOrEmpty(userKey) || string.IsNullOrEmpty(laneId) || !_lanes.ContainsKey(laneId))
            {
                return 0;
            }

            // Exclusivity: if the player is already in a DIFFERENT lane, let that lane release its per-player
            // state FIRST (idempotent, non-throwing) before the session is overwritten — otherwise the prior
            // lane would leak that player's hazards/hints. Re-entering the SAME lane just re-arms the generation
            // token (the lane is deliberately refreshing its own run), so its state is left intact.
            if (_sessions.TryGetValue(userKey, out Session? prior)
                && prior != null
                && !string.Equals(prior.LaneId, laneId, StringComparison.Ordinal)
                && _lanes.TryGetValue(prior.LaneId, out IActivityLane? priorLane)
                && priorLane != null)
            {
                try
                {
                    priorLane.OnPlayerLeft(userKey);
                }
                catch
                {
                    // Never throw: a misbehaving prior lane must not block the switch.
                }
            }

            int generation = NextGeneration();
            _sessions[userKey] = new Session(laneId, generation);
            return generation;
        }

        /// <summary>The lane the player is currently in, or null.</summary>
        public string? CurrentLane(string userKey)
        {
            if (!string.IsNullOrEmpty(userKey) && _sessions.TryGetValue(userKey, out Session? session) && session != null)
            {
                return session.LaneId;
            }

            return null;
        }

        /// <summary>
        /// True only when the player still holds exactly this lane + generation. A newer <see cref="BeginSession"/>
        /// (even for the same lane) or any teardown invalidates an earlier token, so a delayed callback carrying
        /// an old token becomes a safe no-op.
        /// </summary>
        public bool IsCurrent(string userKey, string laneId, int generation)
        {
            return !string.IsNullOrEmpty(userKey)
                && _sessions.TryGetValue(userKey, out Session? session)
                && session != null
                && session.Generation == generation
                && string.Equals(session.LaneId, laneId, StringComparison.Ordinal);
        }

        /// <summary>End one player's session without notifying the lane (the caller already handled teardown).</summary>
        public void EndSession(string userKey)
        {
            if (!string.IsNullOrEmpty(userKey))
            {
                _sessions.Remove(userKey);
            }
        }

        /// <summary>
        /// A player disconnected: drop their session and let their lane release per-player state. Idempotent
        /// (a second call finds no session and does nothing) and non-throwing even if the lane misbehaves.
        /// </summary>
        public void OnPlayerLeft(string userKey)
        {
            if (string.IsNullOrEmpty(userKey))
            {
                return;
            }

            string? laneId = _sessions.TryGetValue(userKey, out Session? session) && session != null ? session.LaneId : null;
            try
            {
                if (laneId != null && _lanes.TryGetValue(laneId, out IActivityLane? lane) && lane != null)
                {
                    lane.OnPlayerLeft(userKey);
                }
            }
            catch
            {
                // Never throw: OnPlayerLeft is driven from a LabAPI event; a misbehaving lane must not escape it.
            }
            finally
            {
                // Keep ownership current during lane cleanup, then invalidate it even if the lane faults.
                _sessions.Remove(userKey);
            }
        }

        /// <summary>
        /// Round-start teardown. Runs INSIDE the pre-assignment hook, before Tutorial players flip to None, so
        /// it must be idempotent and must never throw: each lane's hazard cleanup is isolated and sessions are
        /// cleared regardless of any lane fault.
        /// </summary>
        public void StopForRoundStart()
        {
            StopLanes(roundStart: true);
        }

        /// <summary>Full teardown for any non-round-start reason (disable, restart, empty lobby). Same guarantees.</summary>
        public void StopAll()
        {
            StopLanes(roundStart: false);
        }

        private void StopLanes(bool roundStart)
        {
            foreach (IActivityLane lane in _lanes.Values)
            {
                try
                {
                    if (roundStart)
                    {
                        lane.StopForRoundStart();
                    }
                    else
                    {
                        lane.StopAll();
                    }
                }
                catch
                {
                    // Never throw: a fault in one lane must neither stop the others nor escape into the core hook.
                }
            }

            _sessions.Clear();
        }

        private int NextGeneration()
        {
            _generationCounter = _generationCounter == int.MaxValue ? 1 : _generationCounter + 1;
            return _generationCounter;
        }
    }
}
