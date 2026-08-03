using System;

namespace WarmupScpSelector.Activities.AimRange
{
    public enum RangeBotState
    {
        Disabled,
        Spawning,
        Initializing,
        PassivePatrol,
        Alerted,
        ReturningFire,
        RetaliationUnavailable,
        Dead,
        RespawnWait,
    }

    /// <summary>Pure lifecycle/aggro state for one authored bot slot.</summary>
    public sealed class RangeBotLifecycle
    {
        public RangeBotLifecycle(int slotId)
        {
            SlotId = slotId;
        }

        public int SlotId { get; }
        public RangeBotState State { get; private set; } = RangeBotState.Disabled;
        public int SpawnGeneration { get; private set; }
        public int SpawnOrdinal { get; private set; }
        public string AggressorKey { get; private set; } = string.Empty;
        public int AggressorLifeId { get; private set; }
        public int AggressorSessionToken { get; private set; }
        public double AggroExpiresAt { get; private set; }
        public double AcquireAt { get; private set; }
        public double RespawnAt { get; private set; }

        public bool HasAggressor => !string.IsNullOrEmpty(AggressorKey);

        public void Enable()
        {
            if (State == RangeBotState.Disabled)
            {
                State = RangeBotState.Spawning;
            }
        }

        public void Disable()
        {
            ClearAggro();
            State = RangeBotState.Disabled;
            SpawnGeneration = 0;
            RespawnAt = 0d;
        }

        public void BeginSpawn(int spawnGeneration)
        {
            ClearAggro();
            SpawnGeneration = spawnGeneration;
            SpawnOrdinal = SpawnOrdinal == int.MaxValue ? 1 : SpawnOrdinal + 1;
            RespawnAt = 0d;
            State = RangeBotState.Spawning;
        }

        public bool AcceptsCallback(int spawnGeneration) =>
            spawnGeneration != 0 && SpawnGeneration == spawnGeneration &&
            State != RangeBotState.Disabled && State != RangeBotState.Dead && State != RangeBotState.RespawnWait;

        public void MarkInitializing()
        {
            if (State == RangeBotState.Spawning)
            {
                State = RangeBotState.Initializing;
            }
        }

        public void MarkPassive()
        {
            if (State == RangeBotState.Initializing || State == RangeBotState.Alerted || State == RangeBotState.ReturningFire)
            {
                ClearAggro();
                State = RangeBotState.PassivePatrol;
            }
        }

        public bool TryAggro(string attackerKey, int attackerLifeId, int sessionToken, double now, double acquireDelay, double leaseSeconds)
        {
            if (string.IsNullOrEmpty(attackerKey) || attackerLifeId <= 0 || sessionToken <= 0 ||
                State == RangeBotState.Disabled || State == RangeBotState.Spawning || State == RangeBotState.Initializing ||
                State == RangeBotState.RetaliationUnavailable || State == RangeBotState.Dead || State == RangeBotState.RespawnWait)
            {
                return false;
            }

            if (HasAggressor && now < AggroExpiresAt)
            {
                return string.Equals(AggressorKey, attackerKey, StringComparison.Ordinal) &&
                    AggressorLifeId == attackerLifeId && AggressorSessionToken == sessionToken;
            }

            AggressorKey = attackerKey;
            AggressorLifeId = attackerLifeId;
            AggressorSessionToken = sessionToken;
            AcquireAt = now + Math.Max(0d, acquireDelay);
            AggroExpiresAt = now + Math.Max(0.05d, leaseSeconds);
            State = RangeBotState.Alerted;
            return true;
        }

        public void Advance(double now)
        {
            if ((State == RangeBotState.Alerted || State == RangeBotState.ReturningFire) &&
                (!HasAggressor || now >= AggroExpiresAt))
            {
                MarkPassive();
                return;
            }

            if (State == RangeBotState.Alerted && now >= AcquireAt)
            {
                State = RangeBotState.ReturningFire;
                return;
            }

            if (State == RangeBotState.Dead)
            {
                State = RangeBotState.RespawnWait;
            }
        }

        public bool RespawnDue(double now) => State == RangeBotState.RespawnWait && now >= RespawnAt;

        public void MarkRetaliationUnavailable()
        {
            ClearAggro();
            if (State != RangeBotState.Disabled && State != RangeBotState.Dead && State != RangeBotState.RespawnWait)
            {
                State = RangeBotState.RetaliationUnavailable;
            }
        }

        public void MarkDead(double now, double respawnDelay)
        {
            ClearAggro();
            SpawnGeneration = 0;
            RespawnAt = now + Math.Max(0d, respawnDelay);
            State = RangeBotState.Dead;
        }

        public bool ClearAggroFor(string userKey)
        {
            if (!HasAggressor || !string.Equals(AggressorKey, userKey, StringComparison.Ordinal))
            {
                return false;
            }

            ClearAggro();
            if (State == RangeBotState.Alerted || State == RangeBotState.ReturningFire)
            {
                State = RangeBotState.PassivePatrol;
            }

            return true;
        }

        public void ClearAggro()
        {
            AggressorKey = string.Empty;
            AggressorLifeId = 0;
            AggressorSessionToken = 0;
            AggroExpiresAt = 0d;
            AcquireAt = 0d;
        }
    }

    public enum RangeDamageDisposition
    {
        Ignore,
        Cancel,
        AllowHumanToOwnedBot,
        AllowOwnedBotToParticipant,
    }

    /// <summary>Pure range damage firewall. Only the two explicitly-owned combat directions are allowed.</summary>
    public static class RangeDamagePolicy
    {
        public static RangeDamageDisposition Decide(
            bool attackerIsParticipant,
            bool attackerOwnsRangeWeapon,
            bool attackerIsOwnedBot,
            bool victimIsParticipant,
            bool victimIsOwnedBot)
        {
            if (victimIsOwnedBot)
            {
                return attackerIsParticipant && attackerOwnsRangeWeapon
                    ? RangeDamageDisposition.AllowHumanToOwnedBot
                    : RangeDamageDisposition.Cancel;
            }

            if (attackerIsOwnedBot)
            {
                return victimIsParticipant
                    ? RangeDamageDisposition.AllowOwnedBotToParticipant
                    : RangeDamageDisposition.Cancel;
            }

            if (attackerIsParticipant || victimIsParticipant)
            {
                return RangeDamageDisposition.Cancel;
            }

            return RangeDamageDisposition.Ignore;
        }
    }
}
