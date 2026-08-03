using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities.AimRange
{
    public enum AimRangeOccupancyTransition
    {
        None,
        Entered,
        Left,
    }

    public enum AimRangeLethalResetResult
    {
        NotPending,
        Reinitialized,
        EmergencyRestoreRequired,
    }

    /// <summary>Pure per-human Aim Range state. LabAPI item operations remain in the lane/controllers.</summary>
    public sealed class AimRangeSessions
    {
        public sealed class Session
        {
            internal Session(string userKey, int token)
            {
                UserKey = userKey;
                Token = token;
            }

            public string UserKey { get; }
            public int Token { get; internal set; }
            public string PresetId { get; internal set; } = string.Empty;
            public ushort OwnedItemSerial { get; internal set; }
            public int TrackedReserveAmmo { get; internal set; }
            public int Shots { get; internal set; }
            public int TargetHits { get; internal set; }
            public int BotHits { get; internal set; }
            public int IncomingHits { get; internal set; }
            public bool PendingRangeResetSpawn { get; internal set; }
            public int PendingResetOriginalLifeId { get; internal set; }
            public bool IsLeaving { get; internal set; }
        }

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.Ordinal);

        public int Count => _sessions.Count;

        public IEnumerable<Session> All => _sessions.Values;

        public AimRangeOccupancyTransition UpdateOccupancy(string userKey, bool insideVerifiedBounds, Func<int> beginSession)
        {
            if (string.IsNullOrEmpty(userKey))
            {
                return AimRangeOccupancyTransition.None;
            }

            bool active = _sessions.ContainsKey(userKey);
            if (insideVerifiedBounds == active)
            {
                return AimRangeOccupancyTransition.None;
            }

            if (insideVerifiedBounds)
            {
                int token = beginSession == null ? 0 : beginSession();
                if (token == 0)
                {
                    return AimRangeOccupancyTransition.None;
                }

                _sessions[userKey] = new Session(userKey, token);
                return AimRangeOccupancyTransition.Entered;
            }

            _sessions[userKey].IsLeaving = true;
            return AimRangeOccupancyTransition.Left;
        }

        public bool TryGet(string userKey, out Session session)
        {
            if (!string.IsNullOrEmpty(userKey) && _sessions.TryGetValue(userKey, out Session found))
            {
                session = found;
                return true;
            }

            session = null!;
            return false;
        }

        public bool IsCurrent(string userKey, int token) => TryGet(userKey, out Session session) && session.Token == token && !session.IsLeaving;

        public void TrackWeapon(string userKey, string presetId, ushort serial, int reserveAmmo)
        {
            if (!TryGet(userKey, out Session session))
            {
                return;
            }

            session.PresetId = presetId ?? string.Empty;
            session.OwnedItemSerial = serial;
            session.TrackedReserveAmmo = Math.Max(0, reserveAmmo);
        }

        public bool OwnsWeapon(string userKey, ushort serial) =>
            serial != 0 && TryGet(userKey, out Session session) && session.OwnedItemSerial == serial;

        public bool TryFindWeaponOwner(ushort serial, out Session session)
        {
            if (serial != 0)
            {
                foreach (Session candidate in _sessions.Values)
                {
                    if (candidate.OwnedItemSerial == serial)
                    {
                        session = candidate;
                        return true;
                    }
                }
            }

            session = null!;
            return false;
        }

        public void ClearWeapon(string userKey)
        {
            if (TryGet(userKey, out Session session))
            {
                session.PresetId = string.Empty;
                session.OwnedItemSerial = 0;
                session.TrackedReserveAmmo = 0;
            }
        }

        public bool MarkPendingReset(string userKey) => BeginLethalReset(userKey, 0);

        public bool BeginLethalReset(string userKey, int originalLifeId)
        {
            if (!TryGet(userKey, out Session session) || session.IsLeaving)
            {
                return false;
            }

            session.PendingRangeResetSpawn = true;
            session.PendingResetOriginalLifeId = originalLifeId;
            return true;
        }

        public AimRangeLethalResetResult CompleteLethalReset(string userKey, bool isTutorial, int currentLifeId)
        {
            if (!TryGet(userKey, out Session session) ||
                (!session.PendingRangeResetSpawn && session.PendingResetOriginalLifeId == 0))
            {
                return AimRangeLethalResetResult.NotPending;
            }

            int oldLifeId = session.PendingResetOriginalLifeId;
            session.PendingRangeResetSpawn = false;
            session.PendingResetOriginalLifeId = 0;
            return isTutorial && oldLifeId > 0 && currentLifeId != oldLifeId
                ? AimRangeLethalResetResult.Reinitialized
                : AimRangeLethalResetResult.EmergencyRestoreRequired;
        }

        public bool ConsumePendingReset(string userKey)
        {
            if (!TryGet(userKey, out Session session) || !session.PendingRangeResetSpawn)
            {
                return false;
            }

            session.PendingRangeResetSpawn = false;
            return true;
        }

        public Session? Remove(string userKey)
        {
            if (!string.IsNullOrEmpty(userKey) && _sessions.TryGetValue(userKey, out Session session))
            {
                _sessions.Remove(userKey);
                return session;
            }

            return null;
        }

        public List<Session> InvalidateAndRemoveAll()
        {
            List<Session> all = new List<Session>(_sessions.Values);
            foreach (Session session in all)
            {
                session.IsLeaving = true;
                session.Token = 0;
                session.PendingRangeResetSpawn = false;
                session.PendingResetOriginalLifeId = 0;
            }

            _sessions.Clear();
            return all;
        }
    }
}
