using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities
{
    /// <summary>
    /// Pure token bookkeeping for the repeatable, force-shown lane "flash" verdict (HIT / PERFECT / ROUND WON).
    /// Each <see cref="Arm"/> hands out a fresh token for a (player, lane); a scheduled expiry only removes the
    /// flash when its token is still the latest (<see cref="ShouldClear"/>). This is what lets an identical
    /// repeat judgment genuinely reappear — it re-arms, bumping the token, so the previous expiry timer becomes
    /// a no-op and never clears the new flash early. No Unity/LabAPI types, so it is headless-testable.
    /// </summary>
    public sealed class LaneFlashTracker
    {
        private readonly Dictionary<string, int> _tokens = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Register a new flash for (player, lane) and return its token. Monotonic per key.</summary>
        public int Arm(string userKey, string laneId)
        {
            string key = Key(userKey, laneId);
            _tokens.TryGetValue(key, out int current);
            int next = current == int.MaxValue ? 1 : current + 1;
            _tokens[key] = next;
            return next;
        }

        /// <summary>True only if <paramref name="token"/> is still the latest armed token for (player, lane).</summary>
        public bool ShouldClear(string userKey, string laneId, int token)
        {
            return _tokens.TryGetValue(Key(userKey, laneId), out int current) && current == token;
        }

        /// <summary>Forget any armed flash for (player, lane) so no pending expiry acts on it.</summary>
        public void Clear(string userKey, string laneId)
        {
            _tokens.Remove(Key(userKey, laneId));
        }

        private static string Key(string userKey, string laneId)
        {
            return (userKey ?? string.Empty) + "|" + (laneId ?? string.Empty);
        }
    }
}
