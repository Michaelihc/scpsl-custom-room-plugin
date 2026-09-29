using System;

namespace WarmupScpSelector.Activities.Parkour
{
    internal enum ParkourPhase
    {
        Idle,
        Arming,
        Countdown,
        Active,
        Finished,
    }

    /// <summary>Pure per-player state for one warmup parkour attempt.</summary>
    internal sealed class ParkourRunState
    {
        public ParkourRunState(string userKey, int token)
        {
            UserKey = userKey ?? string.Empty;
            Token = token;
        }

        public string UserKey { get; }

        public int Token { get; set; }

        public ParkourPhase Phase { get; private set; }

        public int NextGateIndex { get; private set; }

        public double ArmStartedAt { get; private set; }

        public double CountdownEndsAt { get; private set; }

        public double RunStartedAt { get; private set; }

        public double FinishedAt { get; private set; }

        public double RecoveryGraceEndsAt { get; set; }

        public bool RequireStartExit { get; set; }

        public void BeginArming(double now)
        {
            if (Phase == ParkourPhase.Idle && !RequireStartExit)
            {
                Phase = ParkourPhase.Arming;
                ArmStartedAt = now;
            }
        }

        public void CancelArming()
        {
            if (Phase == ParkourPhase.Arming)
            {
                Phase = ParkourPhase.Idle;
                ArmStartedAt = 0d;
            }
        }

        public bool TryBeginCountdown(double now, double holdSeconds, double countdownSeconds)
        {
            if (Phase != ParkourPhase.Arming || now - ArmStartedAt < Math.Max(0d, holdSeconds))
            {
                return false;
            }

            Phase = ParkourPhase.Countdown;
            CountdownEndsAt = now + Math.Max(0d, countdownSeconds);
            return true;
        }

        public bool TryStartRun(double now)
        {
            if (Phase != ParkourPhase.Countdown || now < CountdownEndsAt)
            {
                return false;
            }

            Phase = ParkourPhase.Active;
            RunStartedAt = now;
            NextGateIndex = 0;
            FinishedAt = 0d;
            return true;
        }

        public bool TryAdvanceGate(int gateIndex)
        {
            if (Phase != ParkourPhase.Active || gateIndex != NextGateIndex)
            {
                return false;
            }

            NextGateIndex++;
            return true;
        }

        public bool TryFinish(double now, int gateCount)
        {
            if (Phase != ParkourPhase.Active || NextGateIndex != Math.Max(0, gateCount))
            {
                return false;
            }

            Phase = ParkourPhase.Finished;
            FinishedAt = now;
            RequireStartExit = true;
            return true;
        }

        public double Elapsed(double now)
        {
            if (RunStartedAt <= 0d)
            {
                return 0d;
            }

            double end = Phase == ParkourPhase.Finished ? FinishedAt : now;
            return Math.Max(0d, end - RunStartedAt);
        }

        public void Reset(bool requireStartExit)
        {
            Phase = ParkourPhase.Idle;
            NextGateIndex = 0;
            ArmStartedAt = 0d;
            CountdownEndsAt = 0d;
            RunStartedAt = 0d;
            FinishedAt = 0d;
            RecoveryGraceEndsAt = 0d;
            RequireStartExit = requireStartExit;
        }
    }
}
