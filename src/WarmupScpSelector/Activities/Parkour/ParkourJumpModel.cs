using System;

namespace WarmupScpSelector.Activities.Parkour
{
    /// <summary>
    /// Closed-form model of an SCP:SL jump, used both to lay the Pulse Line out and to prove in tests
    /// that every hop is clearable with margin (and that the course is not a walk-over).
    ///
    /// The native integrator in <c>PlayerRoles.FirstPersonControl.FpcMotor.UpdateFloating</c> applies
    /// gravity in two half-steps around the move, which is exactly symplectic for constant acceleration,
    /// so plain projectile motion predicts the real arc:
    /// <code>
    ///   y(t) = J*t - g*t^2/2          J = FirstPersonMovementModule.JumpSpeed
    ///   g    = 19.6                   FpcGravityController.DefaultGravity
    /// </code>
    /// A jump onto a landing <c>rise</c> metres higher is caught on the DESCENDING branch, which is what
    /// makes long hops possible at all: the ascending root gives barely a third of the reach.
    ///
    /// Distances here are the VOID a hop crosses: landing edge to landing edge, which is the distance
    /// that actually decides whether a jump lands or falls. Everything else - the run-up along the take-off
    /// pad, the depth of the pad being landed on, the capsule radius, the motor's step offset - is margin
    /// on top, and folding any of it into the gap makes the course easier than the numbers claim.
    /// </summary>
    internal readonly struct ParkourJumpModel
    {
        /// <summary>Native default from <c>FpcGravityController.DefaultGravity</c>.</summary>
        public const float DefaultGravity = 19.6f;

        /// <summary>Used only when the Tutorial role template cannot be read; overwritten at runtime.</summary>
        public const float FallbackJumpSpeed = 7f;

        public const float FallbackWalkSpeed = 4.5f;

        public const float FallbackSprintSpeed = 6.5f;

        public ParkourJumpModel(float jumpSpeed, float walkSpeed, float sprintSpeed, float gravity = DefaultGravity)
        {
            JumpSpeed = Positive(jumpSpeed, FallbackJumpSpeed);
            WalkSpeed = Positive(walkSpeed, FallbackWalkSpeed);
            SprintSpeed = Math.Max(Positive(sprintSpeed, FallbackSprintSpeed), WalkSpeed);
            Gravity = Positive(gravity, DefaultGravity);
        }

        public float JumpSpeed { get; }

        public float WalkSpeed { get; }

        public float SprintSpeed { get; }

        public float Gravity { get; }

        /// <summary>
        /// Speed the course is laid out against: a committed sprint. Anchoring difficulty to sprint
        /// (rather than to something between a walk and a sprint) is what makes the number mean
        /// something — a hop at 0.86 needs about 1.2x a walker's reach, so the closing stretch genuinely
        /// cannot be strolled. An earlier blended reference put the closing hops within 2% of walk reach,
        /// and a walking dummy completed the entire course in game.
        /// </summary>
        public float ReferenceSpeed => SprintSpeed;

        /// <summary>Highest landing a standing jump can reach, i.e. the apex of the arc.</summary>
        public float MaxRise => JumpSpeed * JumpSpeed / (2f * Gravity);

        /// <summary>
        /// Seconds airborne before the feet are back at <paramref name="rise"/> metres above take-off.
        /// Returns 0 when the landing is above the apex and therefore unreachable.
        /// </summary>
        public float AirTime(float rise)
        {
            float discriminant = JumpSpeed * JumpSpeed - 2f * Gravity * rise;
            if (discriminant < 0f)
            {
                return 0f;
            }

            return (JumpSpeed + (float)Math.Sqrt(discriminant)) / Gravity;
        }

        /// <summary>Furthest horizontal distance clearable onto a landing <paramref name="rise"/> metres up.</summary>
        public float MaxGap(float rise) => MaxGap(rise, ReferenceSpeed);

        public float MaxGap(float rise, float horizontalSpeed) => AirTime(rise) * Math.Max(0f, horizontalSpeed);

        /// <summary>
        /// How hard one hop is: the fraction of the reachable distance it actually uses. 0 is a step
        /// across, 1.0 is exactly at the limit, above 1.0 is impossible at the reference speed.
        /// </summary>
        public float Difficulty(float gap, float rise)
        {
            float reach = MaxGap(rise);
            return reach <= 0.001f ? float.PositiveInfinity : gap / reach;
        }

        /// <summary>Horizontal distance for a hop of the given difficulty onto a landing that high.</summary>
        public float GapForDifficulty(float difficulty, float rise) => MaxGap(rise) * Math.Max(0f, difficulty);

        public bool IsFinite =>
            !float.IsNaN(JumpSpeed) && !float.IsInfinity(JumpSpeed) && JumpSpeed > 0.5f &&
            !float.IsNaN(Gravity) && !float.IsInfinity(Gravity) && Gravity > 0.5f &&
            !float.IsNaN(WalkSpeed) && !float.IsInfinity(WalkSpeed) && WalkSpeed > 0.5f;

        public override string ToString() =>
            $"jump={JumpSpeed:0.##} walk={WalkSpeed:0.##} sprint={SprintSpeed:0.##} g={Gravity:0.##} " +
            $"apex={MaxRise:0.##}m flatReach={MaxGap(0f):0.##}m";

        private static float Positive(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0.01f ? fallback : value;
    }
}
