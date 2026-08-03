namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>Pure generation/window/one-credit gate for native target callbacks.</summary>
    public sealed class RangeTargetState
    {
        public int RangeGeneration { get; private set; }
        public int TargetGeneration { get; private set; }
        public bool IsLive { get; private set; }
        public bool Credited { get; private set; }
        public double LiveUntil { get; private set; }

        public void StartRange(int rangeGeneration)
        {
            RangeGeneration = rangeGeneration;
            InvalidateTarget();
        }

        public int Show(double now, double liveSeconds)
        {
            TargetGeneration = TargetGeneration == int.MaxValue ? 1 : TargetGeneration + 1;
            IsLive = true;
            Credited = false;
            LiveUntil = now + (liveSeconds > 0d ? liveSeconds : 0.001d);
            return TargetGeneration;
        }

        public bool TryCredit(int rangeGeneration, int targetGeneration, double now, bool currentSession, bool ownedWeapon)
        {
            if (rangeGeneration != RangeGeneration || targetGeneration != TargetGeneration || !IsLive || Credited ||
                now > LiveUntil || !currentSession || !ownedWeapon)
            {
                return false;
            }

            Credited = true;
            IsLive = false;
            return true;
        }

        public bool Expire(int rangeGeneration, int targetGeneration, double now)
        {
            if (rangeGeneration != RangeGeneration || targetGeneration != TargetGeneration || !IsLive || now < LiveUntil)
            {
                return false;
            }

            InvalidateTarget();
            return true;
        }

        public void InvalidateRange()
        {
            RangeGeneration = RangeGeneration == int.MaxValue ? 1 : RangeGeneration + 1;
            InvalidateTarget();
        }

        public void InvalidateTarget()
        {
            TargetGeneration = TargetGeneration == int.MaxValue ? 1 : TargetGeneration + 1;
            IsLive = false;
            Credited = false;
            LiveUntil = 0d;
        }
    }
}
