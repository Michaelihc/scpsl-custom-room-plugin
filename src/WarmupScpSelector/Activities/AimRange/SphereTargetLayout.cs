using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarmupScpSelector.Activities.AimRange
{
    /// <summary>
    /// Authored Aim-Lab-style point cloud for the widened third lane. The factory's lane origin is the
    /// center of that lane at its shooting line; local -Z points downrange. Integration owns the surrounding
    /// shell/collision geometry and must reserve at least <see cref="RequiredClearWidth"/> metres of clear width.
    /// </summary>
    public sealed class SphereTargetLayout
    {
        public const float RequiredClearWidth = 5.5f;
        public const float RequiredClearDepth = 12.5f;

        private static readonly Vector3[] AuthoredOffsets =
        {
            new Vector3(-2.05f, 1.05f, -3.0f),
            new Vector3(-0.70f, 2.15f, -3.8f),
            new Vector3( 1.45f, 1.40f, -4.7f),
            new Vector3( 2.10f, 3.25f, -5.6f),
            new Vector3(-1.55f, 3.55f, -6.5f),
            new Vector3( 0.20f, 1.00f, -7.2f),
            new Vector3( 1.75f, 2.25f, -8.0f),
            new Vector3(-2.15f, 2.55f, -8.9f),
            new Vector3(-0.25f, 3.75f, -9.8f),
            new Vector3( 2.00f, 1.20f, -10.7f),
            new Vector3(-1.20f, 1.55f, -11.5f),
            new Vector3( 0.85f, 2.95f, -12.2f),
        };

        public SphereTargetLayout(IReadOnlyList<Vector3> authoredPoints)
        {
            if (authoredPoints == null || authoredPoints.Count < 2)
            {
                throw new ArgumentException("Sphere target layout requires at least two authored points.", nameof(authoredPoints));
            }

            List<Vector3> copy = new List<Vector3>(authoredPoints.Count);
            for (int i = 0; i < authoredPoints.Count; i++)
            {
                Vector3 point = authoredPoints[i];
                if (!IsFinite(point))
                {
                    throw new ArgumentException("Sphere target layout contains a non-finite point.", nameof(authoredPoints));
                }

                for (int j = 0; j < copy.Count; j++)
                {
                    if ((copy[j] - point).sqrMagnitude < 0.01f)
                    {
                        throw new ArgumentException("Sphere target authored points must be distinct.", nameof(authoredPoints));
                    }
                }

                copy.Add(point);
            }

            Points = copy;
        }

        public IReadOnlyList<Vector3> Points { get; }

        public static SphereTargetLayout CreateWidenedThirdLane(Vector3 laneOrigin, Quaternion laneRotation)
        {
            List<Vector3> world = new List<Vector3>(AuthoredOffsets.Length);
            for (int i = 0; i < AuthoredOffsets.Length; i++)
            {
                world.Add(laneOrigin + laneRotation * AuthoredOffsets[i]);
            }

            return new SphereTargetLayout(world);
        }

        private static bool IsFinite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    }
}
