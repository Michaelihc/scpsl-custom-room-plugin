using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>An authored lateral track in world space. Motion parameters are derived deterministically at start.</summary>
    public readonly struct SlidingTargetTrackDefinition
    {
        public SlidingTargetTrackDefinition(
            int slotId,
            Vector3 endpointA,
            Vector3 endpointB,
            Quaternion rotation,
            Vector3 scale,
            float minimumSpeed,
            float maximumSpeed)
        {
            SlotId = slotId;
            EndpointA = endpointA;
            EndpointB = endpointB;
            Rotation = rotation;
            Scale = scale;
            MinimumSpeed = minimumSpeed;
            MaximumSpeed = maximumSpeed;
        }

        public int SlotId { get; }
        public Vector3 EndpointA { get; }
        public Vector3 EndpointB { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public float MinimumSpeed { get; }
        public float MaximumSpeed { get; }
    }

    /// <summary>Immutable deterministic motion for one persistent target.</summary>
    public readonly struct SlidingTargetMotion
    {
        internal SlidingTargetMotion(
            SlidingTargetTrackDefinition track,
            float speed,
            double phase,
            bool reversed,
            float speedVariation,
            double speedVariationPeriod,
            double speedVariationPhase)
        {
            Track = track;
            Speed = speed;
            Phase = phase;
            Reversed = reversed;
            SpeedVariation = speedVariation;
            SpeedVariationPeriod = speedVariationPeriod;
            SpeedVariationPhase = speedVariationPhase;
            Distance = Vector3.Distance(track.EndpointA, track.EndpointB);
        }

        public SlidingTargetTrackDefinition Track { get; }
        public float Speed { get; }
        public double Phase { get; }
        public bool Reversed { get; }
        public float SpeedVariation { get; }
        public double SpeedVariationPeriod { get; }
        public double SpeedVariationPhase { get; }
        public float Distance { get; }

        public float EvaluateSpeed(double elapsedSeconds)
        {
            double elapsed = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d
                : Math.Max(0d, elapsedSeconds);
            double omega = Math.PI * 2d / SpeedVariationPeriod;
            return Speed * (1f + SpeedVariation * (float)Math.Sin(omega * elapsed + SpeedVariationPhase));
        }

        /// <summary>
        /// Evaluates a looped ping-pong path directly from absolute elapsed time. No prior position or tick delta
        /// participates, so scheduler jitter cannot accumulate into drift.
        /// </summary>
        public Vector3 Evaluate(double elapsedSeconds)
        {
            if (Distance <= 0.0001f)
            {
                return Track.EndpointA;
            }

            double elapsed = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d
                : Math.Max(0d, elapsedSeconds);

            // Analytically integrate a sinusoidally varying speed. This gives each target visible acceleration and
            // deceleration while retaining absolute-time evaluation, so scheduler jitter never accumulates drift.
            double omega = Math.PI * 2d / SpeedVariationPeriod;
            double travelled = Speed * elapsed +
                Speed * SpeedVariation / omega *
                (Math.Cos(SpeedVariationPhase) - Math.Cos(omega * elapsed + SpeedVariationPhase));
            double traversals = travelled / Distance;
            if (Reversed)
            {
                traversals = -traversals;
            }

            // Phase covers the full out-and-back cycle. Triangle-wave progress remains in [0, 1].
            double cycle = traversals + Phase * 2d;
            cycle -= Math.Floor(cycle / 2d) * 2d;
            double progress = cycle <= 1d ? cycle : 2d - cycle;
            return Vector3.LerpUnclamped(Track.EndpointA, Track.EndpointB, (float)progress);
        }
    }

    /// <summary>Pure validation and deterministic plan construction for sliding targets.</summary>
    public static class SlidingTargetLogic
    {
        private const float MinimumAllowedSpeed = 0.05f;
        private const float MaximumAllowedSpeed = 20f;
        private const float MinimumTrackLength = 0.1f;

        public static bool TryBuildMotion(
            SlidingTargetTrackDefinition track,
            int mapSeed,
            int rangeGeneration,
            int ordinal,
            int seedSalt,
            out SlidingTargetMotion motion)
        {
            motion = default;
            if (track.SlotId < 0 || !Finite(track.EndpointA) || !Finite(track.EndpointB) ||
                !Finite(track.Rotation) || !Finite(track.Scale) ||
                track.Scale.x <= 0f || track.Scale.y <= 0f || track.Scale.z <= 0f ||
                Vector3.Distance(track.EndpointA, track.EndpointB) < MinimumTrackLength)
            {
                return false;
            }

            float minimumSpeed = SanitizeSpeed(track.MinimumSpeed, 0.8f);
            float maximumSpeed = SanitizeSpeed(track.MaximumSpeed, minimumSpeed);
            if (maximumSpeed < minimumSpeed)
            {
                float swap = minimumSpeed;
                minimumSpeed = maximumSpeed;
                maximumSpeed = swap;
            }

            int seed = AimRangeDeterminism.CombineSeed(
                mapSeed,
                rangeGeneration,
                track.SlotId,
                ordinal,
                seedSalt);
            System.Random random = new System.Random(seed);
            float speed = minimumSpeed + (maximumSpeed - minimumSpeed) * (float)random.NextDouble();
            double phase = random.NextDouble();
            float speedVariation = 0.22f + 0.23f * (float)random.NextDouble();
            double speedVariationPeriod = 2.4d + 2.6d * random.NextDouble();
            double speedVariationPhase = random.NextDouble() * Math.PI * 2d;

            // Slot parity guarantees adjacent authored tracks start with opposite traversal orientation; the range
            // generation and salt deterministically flip the entire pattern between runs.
            bool reversed = ((track.SlotId + rangeGeneration + seedSalt) & 1) != 0;
            motion = new SlidingTargetMotion(
                track,
                speed,
                phase,
                reversed,
                speedVariation,
                speedVariationPeriod,
                speedVariationPhase);
            return true;
        }

        private static float SanitizeSpeed(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = fallback;
            }

            return Mathf.Clamp(value, MinimumAllowedSpeed, MaximumAllowedSpeed);
        }

        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static bool Finite(Quaternion value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Default central-bay authoring at three distinct depths; callers may supply their own tracks instead.</summary>
    public static class SlidingTargetAuthoring
    {
        public static IReadOnlyList<SlidingTargetTrackDefinition> CreateDefaultTracks(
            Vector3 doorPlaneOrigin,
            Vector3 entrancePosition)
        {
            return new[]
            {
                Create(0, doorPlaneOrigin, entrancePosition, 1.35f, -9.5f, 1.45f, 1.10f, 1.35f),
                Create(1, doorPlaneOrigin, entrancePosition, 2.05f, -13.5f, 1.65f, 1.40f, 1.70f),
                Create(2, doorPlaneOrigin, entrancePosition, 1.55f, -17.5f, 1.50f, 1.75f, 2.10f),
            };
        }

        private static SlidingTargetTrackDefinition Create(
            int slotId,
            Vector3 origin,
            Vector3 entrance,
            float height,
            float depth,
            float halfWidth,
            float minimumSpeed,
            float maximumSpeed)
        {
            Vector3 center = origin + new Vector3(0f, height, depth);
            Vector3 towardEntrance = entrance - center;
            towardEntrance.y = 0f;
            Quaternion rotation = (towardEntrance.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(towardEntrance)
                : Quaternion.identity) * Quaternion.Euler(0f, 90f, 0f);
            return new SlidingTargetTrackDefinition(
                slotId,
                center + Vector3.left * halfWidth,
                center + Vector3.right * halfWidth,
                rotation,
                Vector3.one,
                minimumSpeed,
                maximumSpeed);
        }
    }
}
