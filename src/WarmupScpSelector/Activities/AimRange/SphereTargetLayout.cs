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

        // Generated once with seed 37013, then checked in as an irregular 3D deck. Every coordinate varies in
        // width, height, and depth so the targets do not collapse into visible rows or depth planes. Twenty are
        // live at once; credited hits cycle directly through all 50 without runtime random generation or placement searches.
        private static readonly Vector3[] AuthoredOffsets =
        {
            new Vector3(-2.25f, 1.13f,  -8.20f),
            new Vector3( 1.00f, 0.88f, -11.38f),
            new Vector3( 1.48f, 1.70f, -11.79f),
            new Vector3( 1.61f, 3.80f, -12.40f),
            new Vector3(-1.09f, 3.04f, -12.52f),
            new Vector3( 0.60f, 3.91f,  -4.18f),
            new Vector3(-0.02f, 2.00f,  -8.65f),
            new Vector3(-1.42f, 2.98f,  -8.15f),
            new Vector3(-1.18f, 0.78f,  -2.99f),
            new Vector3( 0.28f, 3.91f, -11.69f),
            new Vector3( 1.92f, 3.43f,  -3.25f),
            new Vector3( 1.00f, 3.11f,  -2.59f),
            new Vector3(-2.01f, 1.53f,  -3.53f),
            new Vector3( 2.18f, 3.51f,  -7.24f),
            new Vector3( 1.89f, 3.98f,  -9.50f),
            new Vector3(-1.87f, 1.43f,  -6.48f),
            new Vector3(-0.02f, 1.05f, -12.62f),
            new Vector3(-0.92f, 3.32f,  -4.27f),
            new Vector3( 1.01f, 2.75f,  -9.86f),
            new Vector3(-1.13f, 1.73f,  -1.66f),
            new Vector3( 1.98f, 1.86f,  -2.63f),
            new Vector3(-1.31f, 3.12f,  -2.28f),
            new Vector3(-1.62f, 2.71f,  -4.88f),
            new Vector3( 2.00f, 2.47f,  -6.43f),
            new Vector3( 1.71f, 1.63f,  -4.70f),
            new Vector3(-1.86f, 3.07f, -11.12f),
            new Vector3( 0.01f, 1.85f,  -5.47f),
            new Vector3( 0.19f, 3.25f,  -6.95f),
            new Vector3( 0.11f, 4.09f,  -5.77f),
            new Vector3( 0.63f, 1.94f,  -6.97f),
            new Vector3(-0.73f, 1.92f, -10.09f),
            new Vector3(-1.42f, 1.27f,  -9.83f),
            new Vector3( 0.81f, 2.81f,  -7.87f),
            new Vector3(-0.20f, 3.98f, -10.34f),
            new Vector3(-1.64f, 3.67f,  -4.82f),
            new Vector3( 1.60f, 1.14f, -12.83f),
            new Vector3(-2.05f, 1.94f,  -9.68f),
            new Vector3(-1.84f, 3.13f,  -9.76f),
            new Vector3(-0.82f, 3.26f,  -9.71f),
            new Vector3( 1.27f, 3.81f,  -6.86f),
            new Vector3( 1.75f, 3.54f,  -4.57f),
            new Vector3(-0.94f, 2.05f, -11.58f),
            new Vector3( 1.89f, 2.63f,  -4.17f),
            new Vector3(-0.11f, 1.20f,  -3.60f),
            new Vector3(-0.37f, 0.97f, -10.44f),
            new Vector3(-0.05f, 3.69f,  -3.45f),
            new Vector3( 0.14f, 3.62f,  -9.27f),
            new Vector3( 2.19f, 3.31f,  -1.68f),
            new Vector3(-1.52f, 1.31f, -11.29f),
            new Vector3( 2.24f, 0.91f,  -7.70f),
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

        /// <summary>
        /// Maps a global spawn ordinal into the fixed point deck. The controller advances this ordinal only after
        /// a successful spawn and performs a short linear scan when the next point is still occupied.
        /// </summary>
        public static int GetSequencePointIndex(long spawnOrdinal, int pointCount)
        {
            if (spawnOrdinal < 0L || pointCount <= 0)
            {
                return -1;
            }

            return (int)(spawnOrdinal % pointCount);
        }

        public static SphereTargetLayout CreateWidenedThirdLane(
            Vector3 laneOrigin,
            Quaternion laneRotation,
            float availableWidth = RequiredClearWidth)
        {
            float clearWidth = float.IsNaN(availableWidth) || float.IsInfinity(availableWidth)
                ? RequiredClearWidth
                : Math.Max(RequiredClearWidth, availableWidth);
            float horizontalScale = (clearWidth - 0.5f) / 4.5f;
            List<Vector3> world = new List<Vector3>(AuthoredOffsets.Length);
            for (int i = 0; i < AuthoredOffsets.Length; i++)
            {
                Vector3 offset = AuthoredOffsets[i];
                offset.x *= horizontalScale;
                world.Add(laneOrigin + laneRotation * offset);
            }

            return new SphereTargetLayout(world);
        }

        private static bool IsFinite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    }
}
