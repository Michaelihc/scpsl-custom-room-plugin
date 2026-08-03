using System;

namespace WarmupScpSelector.Warmup
{
    /// <summary>
    /// Pure identity facts used to keep real selector participants separate from host, dummy and NPC hubs.
    /// </summary>
    public readonly struct ParticipantIdentity
    {
        public ParticipantIdentity(bool isHost, bool isDummy, bool isPlayer, bool isReady, string stableKey)
        {
            IsHost = isHost;
            IsDummy = isDummy;
            IsPlayer = isPlayer;
            IsReady = isReady;
            StableKey = stableKey ?? string.Empty;
        }

        public bool IsHost { get; }

        public bool IsDummy { get; }

        public bool IsPlayer { get; }

        public bool IsReady { get; }

        public string StableKey { get; }

        /// <summary>The sole predicate for selector-owned humans.</summary>
        public bool IsCanonicalHuman => !IsHost && !IsDummy && IsPlayer && IsReady && StableKey.Length > 0;
    }

    public static class ParticipantIdentityRules
    {
        public static bool IsCanonicalHuman(ParticipantIdentity identity) => identity.IsCanonicalHuman;

        public static string HumanKey(string userId, uint networkId, int playerId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return userId.Trim();
            }

            if (networkId != 0)
            {
                return "net:" + networkId.ToString();
            }

            return playerId > 0 ? "player:" + playerId.ToString() : string.Empty;
        }

        /// <summary>Dummies are deliberately keyed by live identity, never their shared ID_Dummy user id.</summary>
        public static string DummyInstanceKey(uint networkId, int playerId, int hubInstanceId)
        {
            if (networkId != 0)
            {
                return "dummy-net:" + networkId.ToString();
            }

            if (playerId > 0)
            {
                return "dummy-player:" + playerId.ToString();
            }

            return "dummy-hub:" + hubInstanceId.ToString();
        }
    }
}
