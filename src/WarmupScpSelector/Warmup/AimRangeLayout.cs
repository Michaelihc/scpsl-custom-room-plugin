using System.Collections.Generic;
using UnityEngine;
using WarmupScpSelector.Activities.AimRange;
using WarmupScpSelector.Models;

namespace WarmupScpSelector.Warmup
{
    public readonly struct AimShelfAnchor
    {
        public AimShelfAnchor(int slotId, Vector3 localPosition, Quaternion localRotation)
        {
            SlotId = slotId;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public int SlotId { get; }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
    }

    public readonly struct AimBotPath
    {
        public AimBotPath(int slotId, IReadOnlyList<RangePathSegment> segments)
        {
            SlotId = slotId;
            Segments = segments;
        }

        public int SlotId { get; }
        public IReadOnlyList<RangePathSegment> Segments { get; }
    }

    public readonly struct AimBotCover
    {
        public AimBotCover(int slotId, Vector3 center, Vector3 size, Vector3 reloadPoint, bool fullHeight)
        {
            SlotId = slotId;
            Center = center;
            Size = size;
            ReloadPoint = reloadPoint;
            FullHeight = fullHeight;
        }

        public int SlotId { get; }
        public Vector3 Center { get; }
        public Vector3 Size { get; }
        public Vector3 ReloadPoint { get; }
        public bool FullHeight { get; }
    }

    /// <summary>Single source of truth for the authored widened three-lane rear range.</summary>
    public sealed class AimRangeLayout
    {
        public const float Width = 19.2f;
        public const float Depth = 22f;
        public const float Height = 5f;
        public const float LaneWidth = Width / 3f;
        public const float DoorWidth = 2.8f;
        public const float DoorHeight = 3.2f;

        public AimRangeLayout(Vector3 galleryOrigin, float galleryFrontZ)
        {
            GalleryOrigin = galleryOrigin;
            DoorPlaneZ = galleryOrigin.z + galleryFrontZ;
            RangeCenter = new Vector3(galleryOrigin.x, galleryOrigin.y, DoorPlaneZ - Depth / 2f);
            EntranceSpawn = new Vector3(galleryOrigin.x, galleryOrigin.y + 0.5f, DoorPlaneZ - 1.6f);
            VerifiedBounds = new Bounds(
                RangeCenter + new Vector3(0f, Height / 2f, -0.05f),
                new Vector3(Width - 0.5f, Height + 0.5f, Depth - 0.4f));
            MaintenanceBounds = new Bounds(
                RangeCenter + new Vector3(0f, Height / 2f, 0f),
                new Vector3(Width + 4f, Height + 8f, Depth + 6f));

            ShelfAnchors = new[]
            {
                new AimShelfAnchor(0, Local(-8.85f, 1.35f, -2.0f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f))),
                new AimShelfAnchor(1, Local(-8.85f, 2.15f, -2.0f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f))),
                new AimShelfAnchor(2, Local(-8.85f, 1.35f, -3.6f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f))),
                new AimShelfAnchor(3, Local(8.85f, 1.35f, -2.0f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f))),
                new AimShelfAnchor(4, Local(8.85f, 2.15f, -2.0f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f))),
                new AimShelfAnchor(5, Local(8.85f, 1.35f, -3.6f), MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f))),
            };

            SlidingTargetTracks = new[]
            {
                SlidingTrack(0, y: 1.30f, depth: -10.2f, halfWidth: 2.35f, minimumSpeed: 1.05f, maximumSpeed: 1.35f),
                SlidingTrack(1, y: 2.15f, depth: -14.7f, halfWidth: 2.15f, minimumSpeed: 1.35f, maximumSpeed: 1.75f),
                SlidingTrack(2, y: 1.55f, depth: -19.0f, halfWidth: 2.40f, minimumSpeed: 1.70f, maximumSpeed: 2.15f),
            };

            BotCovers = new[]
            {
                new AimBotCover(0, Local(-8.0f, 1.1f, -9.35f), new Vector3(1.9f, 2.2f, 0.45f), Local(-8.0f, 0f, -10.15f), true),
                new AimBotCover(0, Local(-4.75f, 0.85f, -10.9f), new Vector3(1.55f, 1.7f, 0.45f), Local(-4.75f, 0f, -11.6f), false),
                new AimBotCover(0, Local(-7.9f, 1.1f, -12.85f), new Vector3(1.8f, 2.2f, 0.5f), Local(-7.9f, 0f, -13.65f), true),
                new AimBotCover(1, Local(-8.05f, 1.1f, -15.45f), new Vector3(1.9f, 2.2f, 0.5f), Local(-8.05f, 0f, -16.25f), true),
                new AimBotCover(1, Local(-4.65f, 0.85f, -17.55f), new Vector3(1.55f, 1.7f, 0.45f), Local(-4.65f, 0f, -18.25f), false),
                new AimBotCover(1, Local(-7.75f, 1.1f, -19.55f), new Vector3(1.9f, 2.2f, 0.5f), Local(-7.75f, 0f, -20.35f), true),
            };

            BotPaths = new[]
            {
                new AimBotPath(0, new[]
                {
                    new RangePathSegment(Point(-8.25f, 0f, -10.0f), Point(-6.2f, 0f, -10.0f), 1.8d),
                    new RangePathSegment(Point(-6.2f, 0f, -10.0f), Point(-6.2f, 0f, -13.65f), 2.8d),
                    new RangePathSegment(Point(-6.2f, 0f, -13.65f), Point(-7.9f, 0f, -13.65f), 1.5d),
                    new RangePathSegment(Point(-7.9f, 0f, -13.65f), Point(-6.2f, 0f, -13.65f), 1.5d),
                    new RangePathSegment(Point(-6.2f, 0f, -13.65f), Point(-6.2f, 0f, -10.0f), 2.8d),
                    new RangePathSegment(Point(-6.2f, 0f, -10.0f), Point(-8.25f, 0f, -10.0f), 1.8d),
                }),
                new AimBotPath(1, new[]
                {
                    new RangePathSegment(Point(-8.3f, 0f, -16.25f), Point(-6.2f, 0f, -16.25f), 1.8d),
                    new RangePathSegment(Point(-6.2f, 0f, -16.25f), Point(-6.2f, 0f, -20.35f), 3.1d),
                    new RangePathSegment(Point(-6.2f, 0f, -20.35f), Point(-7.75f, 0f, -20.35f), 1.4d),
                    new RangePathSegment(Point(-7.75f, 0f, -20.35f), Point(-6.2f, 0f, -20.35f), 1.4d),
                    new RangePathSegment(Point(-6.2f, 0f, -20.35f), Point(-6.2f, 0f, -16.25f), 3.1d),
                    new RangePathSegment(Point(-6.2f, 0f, -16.25f), Point(-8.3f, 0f, -16.25f), 1.8d),
                }),
            };

            SphereLaneOrigin = Local(LaneWidth, 0f, -7.0f);
            SphereLaneRotation = new Quaternion(0f, 0f, 0f, 1f);
        }

        public Vector3 GalleryOrigin { get; }
        public float DoorPlaneZ { get; }
        public Vector3 RangeCenter { get; }
        public Vector3 EntranceSpawn { get; }
        public Bounds VerifiedBounds { get; }
        public Bounds MaintenanceBounds { get; }
        public IReadOnlyList<AimShelfAnchor> ShelfAnchors { get; }
        public IReadOnlyList<SlidingTargetTrackDefinition> SlidingTargetTracks { get; }
        public IReadOnlyList<AimBotCover> BotCovers { get; }
        public IReadOnlyList<AimBotPath> BotPaths { get; }
        public Vector3 SphereLaneOrigin { get; }
        public Quaternion SphereLaneRotation { get; }

        public Vector3 LeftRackOrigin => new Vector3(GalleryOrigin.x - 9.15f, GalleryOrigin.y, DoorPlaneZ - 2.8f);
        public Vector3 RightRackOrigin => new Vector3(GalleryOrigin.x + 9.15f, GalleryOrigin.y, DoorPlaneZ - 2.8f);
        public Quaternion LeftRackRotation => MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f));
        public Quaternion RightRackRotation => MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f));
        public Vector3 MovingTargetOrigin => new Vector3(GalleryOrigin.x, GalleryOrigin.y, DoorPlaneZ);

        public bool ContainsVerified(Vector3 worldPosition) => VerifiedBounds.Contains(worldPosition);

        private SlidingTargetTrackDefinition SlidingTrack(
            int slotId,
            float y,
            float depth,
            float halfWidth,
            float minimumSpeed,
            float maximumSpeed)
        {
            Vector3 center = Local(0f, y, depth);
            Quaternion rotation = MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f));
            return new SlidingTargetTrackDefinition(
                slotId,
                center + new Vector3(-halfWidth, 0f, 0f),
                center + new Vector3(halfWidth, 0f, 0f),
                rotation,
                Vector3.one,
                minimumSpeed,
                maximumSpeed);
        }

        private Vector3 Local(float x, float y, float depthFromDoor) =>
            new Vector3(GalleryOrigin.x + x, GalleryOrigin.y + y, DoorPlaneZ + depthFromDoor);

        private static RangePoint Point(float x, float y, float depthFromDoor) => new RangePoint(x, y, depthFromDoor);
    }
}
