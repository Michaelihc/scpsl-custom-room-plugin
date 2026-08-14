using System;
using System.Collections.Generic;
using UnityEngine;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.Activities.Parkour
{
    internal readonly struct ParkourPlatform
    {
        public ParkourPlatform(Vector3 center, Vector3 size, bool goldCut = false)
        {
            Center = center;
            Size = size;
            GoldCut = goldCut;
        }

        public Vector3 Center { get; }

        public Vector3 Size { get; }

        public bool GoldCut { get; }

        public float SurfaceY => Center.y + Size.y / 2f;

        public Vector3 RecoveryPosition => new Vector3(Center.x, SurfaceY + 0.5f, Center.z);

        public Bounds GateBounds => new Bounds(
            new Vector3(Center.x, SurfaceY + 0.55f, Center.z),
            new Vector3(Math.Max(0.8f, Size.x), 1.3f, Math.Max(0.8f, Size.z)));
    }

    /// <summary>Authored folded route occupying the otherwise-empty far-left wing of the Aim hall.</summary>
    internal sealed class ParkourLayout
    {
        private const float DividerX = -9.9f;

        public ParkourLayout(AimRangeLayout aim)
        {
            if (aim == null)
            {
                throw new ArgumentNullException(nameof(aim));
            }

            Origin = aim.GalleryOrigin;
            DoorPlaneZ = aim.DoorPlaneZ;
            float outerLeft = -aim.ShellWidth / 2f + 0.35f;
            if (outerLeft > -17.8f)
            {
                throw new InvalidOperationException("selector hall is too narrow for the parkour bay");
            }

            BayBounds = new Bounds(
                Local((outerLeft + DividerX) / 2f, 2.5f, -13.25f),
                new Vector3(DividerX - outerLeft, 5.5f, 17.5f));
            DeepCourseBounds = new Bounds(
                Local((outerLeft + DividerX) / 2f, 2.5f, -14.4f),
                new Vector3(DividerX - outerLeft, 5.5f, 15.0f));

            StartPlate = new ParkourPlatform(Local(-14.4f, 0.06f, -5.15f), new Vector3(2.2f, 0.12f, 1.6f));
            FinishPlate = new ParkourPlatform(Local(-16.75f, 0.06f, -5.15f), new Vector3(1.7f, 0.12f, 1.6f));
            ResetCoinPosition = Local(-11.45f, 1.0f, -5.05f);
            DividerCenter = Local(DividerX, 2.5f, -14.35f);
            DividerSize = new Vector3(0.25f, 5f, 15.1f);

            // Surface heights remain below 2.75 m, leaving generous headroom beneath the 5 m hall ceiling.
            // Consecutive landing edges are separated by 0.55-1.05 m: normal Tutorial jump + air steering,
            // never crouch boost, is sufficient. Every landing is an ordered gate, preventing route skips.
            Platforms = new[]
            {
                Platform(-14.45f, 1.05f, -7.35f, 1.8f, 1.55f),
                Platform(-12.55f, 1.32f, -9.15f, 1.55f, 1.55f),
                Platform(-14.85f, 1.58f, -10.65f, 1.65f, 1.50f),
                Platform(-16.85f, 1.82f, -12.20f, 1.55f, 1.55f),
                Platform(-14.75f, 2.05f, -13.72f, 1.55f, 1.45f, true),
                Platform(-12.35f, 2.30f, -15.05f, 1.65f, 1.55f),
                Platform(-14.45f, 2.55f, -16.70f, 1.60f, 1.50f),
                Platform(-16.70f, 2.32f, -18.25f, 1.60f, 1.55f),
                Platform(-14.35f, 2.08f, -19.85f, 1.55f, 1.50f, true),
                Platform(-12.25f, 1.82f, -21.00f, 1.60f, 1.45f),
                Platform(-14.65f, 1.55f, -21.10f, 1.65f, 1.45f),
                Platform(-16.85f, 1.30f, -19.70f, 1.60f, 1.50f),
                Platform(-16.15f, 1.05f, -17.20f, 1.55f, 1.50f, true),
                Platform(-16.90f, 0.83f, -14.75f, 1.65f, 1.50f),
                Platform(-16.15f, 0.62f, -12.10f, 1.65f, 1.55f),
                Platform(-16.75f, 0.82f, -9.25f, 1.70f, 1.55f),
                Platform(-16.70f, 1.02f, -7.35f, 1.75f, 1.55f),
            };
        }

        public Vector3 Origin { get; }

        public float DoorPlaneZ { get; }

        public Bounds BayBounds { get; }

        public Bounds DeepCourseBounds { get; }

        public ParkourPlatform StartPlate { get; }

        public ParkourPlatform FinishPlate { get; }

        public Vector3 ResetCoinPosition { get; }

        public Vector3 DividerCenter { get; }

        public Vector3 DividerSize { get; }

        public IReadOnlyList<ParkourPlatform> Platforms { get; }

        public bool Contains(Vector3 position) => BayBounds.Contains(position);

        public bool ContainsStart(Vector3 position) => StartPlate.GateBounds.Contains(position);

        public bool ContainsFinish(Vector3 position) => FinishPlate.GateBounds.Contains(position);

        public int SectorForGate(int nextGateIndex)
        {
            if (Platforms.Count == 0)
            {
                return 1;
            }

            int clamped = Math.Max(0, Math.Min(Platforms.Count - 1, nextGateIndex));
            return Math.Min(4, clamped * 4 / Platforms.Count + 1);
        }

        public static bool SegmentIntersectsBounds(Vector3 from, Vector3 to, Bounds bounds)
        {
            Vector3 direction = to - from;
            float min = 0f;
            float max = 1f;
            return Slab(from.x, direction.x, bounds.min.x, bounds.max.x, ref min, ref max) &&
                Slab(from.y, direction.y, bounds.min.y, bounds.max.y, ref min, ref max) &&
                Slab(from.z, direction.z, bounds.min.z, bounds.max.z, ref min, ref max);
        }

        private ParkourPlatform Platform(float x, float surfaceY, float depth, float width, float length, bool gold = false)
        {
            return new ParkourPlatform(Local(x, surfaceY - 0.12f, depth), new Vector3(width, 0.24f, length), gold);
        }

        private Vector3 Local(float x, float y, float depth) => new Vector3(Origin.x + x, Origin.y + y, DoorPlaneZ + depth);

        private static bool Slab(float origin, float direction, float minimum, float maximum, ref float enter, ref float exit)
        {
            if (Math.Abs(direction) < 0.00001f)
            {
                return origin >= minimum && origin <= maximum;
            }

            float inverse = 1f / direction;
            float first = (minimum - origin) * inverse;
            float second = (maximum - origin) * inverse;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            enter = Math.Max(enter, first);
            exit = Math.Min(exit, second);
            return enter <= exit;
        }
    }
}
