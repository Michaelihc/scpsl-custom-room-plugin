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

    public readonly struct AimWorkstationAnchor
    {
        public AimWorkstationAnchor(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    /// <summary>Single source of truth for the three training lanes inside the continuous selector hall.</summary>
    public sealed class AimRangeLayout
    {
        public const float Width = 19.2f;
        public const float Depth = 22f;
        public const float Height = 5f;
        public const float LaneWidth = Width / 3f;
        public const float ShootingCounterDepth = 6.8f;
        public const float ShootingCounterHeight = 1.1f;
        public const float AttachmentWorkstationDepth = 3.966f;
        public const float AttachmentWorkstationWallInset = 0.22f;

        public AimRangeLayout(Vector3 galleryOrigin, float galleryFrontZ, float galleryWidth = Width, float galleryDepth = 9f)
        {
            GalleryOrigin = galleryOrigin;
            DoorPlaneZ = galleryOrigin.z + galleryFrontZ;
            ShellWidth = Mathf.Max(Width, galleryWidth);
            ShootingCounterWidth = ShellWidth - 0.6f;
            RangeCenter = new Vector3(galleryOrigin.x, galleryOrigin.y, DoorPlaneZ - Depth / 2f);
            EntranceSpawn = new Vector3(galleryOrigin.x, galleryOrigin.y + 0.5f, DoorPlaneZ - 1.6f);
            float hallDepth = Depth + Mathf.Max(1f, galleryDepth);
            float hallCenterZ = DoorPlaneZ + (Mathf.Max(1f, galleryDepth) - Depth) / 2f;
            VerifiedBounds = new Bounds(
                new Vector3(galleryOrigin.x, galleryOrigin.y + Height / 2f, hallCenterZ - 0.05f),
                new Vector3(ShellWidth - 0.5f, Height + 0.5f, hallDepth - 0.4f));
            MaintenanceBounds = new Bounds(
                new Vector3(galleryOrigin.x, galleryOrigin.y + Height / 2f, hallCenterZ),
                new Vector3(ShellWidth + 4f, Height + 8f, hallDepth + 6f));

            // Six persistent dispensers sit visibly on the low shooting counter. They remain inside the same
            // uninterrupted hall as the selector displays; there is no rack, shelf, or separate armoury room.
            float counterZ = -ShootingCounterDepth;
            float pickupY = ShootingCounterHeight + 0.28f;
            Quaternion counterRotation = MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f));
            ShelfAnchors = new[]
            {
                new AimShelfAnchor(0, Local(-7.4f, pickupY, counterZ), counterRotation),
                new AimShelfAnchor(1, Local(-4.45f, pickupY, counterZ), counterRotation),
                new AimShelfAnchor(2, Local(-1.5f, pickupY, counterZ), counterRotation),
                new AimShelfAnchor(3, Local(1.5f, pickupY, counterZ), counterRotation),
                new AimShelfAnchor(4, Local(4.45f, pickupY, counterZ), counterRotation),
                new AimShelfAnchor(5, Local(7.4f, pickupY, counterZ), counterRotation),
            };

            // Two native attachment workstations sit symmetrically against the side walls. Their interaction space
            // is around x +/-17.17 in the standard 37 m hall; the roots stay close to the wall so the prefab faces
            // inward without narrowing the walking corridor.
            float workstationX = ShellWidth / 2f - AttachmentWorkstationWallInset;
            float workstationZ = -AttachmentWorkstationDepth;
            AttachmentWorkstationAnchors = new[]
            {
                new AimWorkstationAnchor(
                    Local(-workstationX, 0f, workstationZ),
                    MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f))),
                new AimWorkstationAnchor(
                    Local(workstationX, 0f, workstationZ),
                    MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f))),
            };

            SlidingTargetTracks = new[]
            {
                SlidingTrack(0, y: 1.30f, depth: -10.2f, halfWidth: 2.35f, minimumSpeed: 1.05f, maximumSpeed: 1.35f),
                SlidingTrack(1, y: 2.15f, depth: -14.7f, halfWidth: 2.15f, minimumSpeed: 1.35f, maximumSpeed: 1.75f),
                SlidingTrack(2, y: 1.55f, depth: -19.0f, halfWidth: 2.40f, minimumSpeed: 1.70f * 1.35f, maximumSpeed: 2.15f * 1.35f),
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

            // Lane 3 owns the entire right-hand wing, from the +3.2 m divider to the outer wall. Centering the
            // cloud in that dynamic bay lets a widened selector hall use its otherwise-empty horizontal space.
            SphereBayWidth = ShellWidth / 2f - LaneWidth / 2f;
            SphereLaneOrigin = Local(LaneWidth / 2f + SphereBayWidth / 2f, 0f, -7.0f);
            SphereLaneRotation = new Quaternion(0f, 0f, 0f, 1f);
        }

        public Vector3 GalleryOrigin { get; }
        public float DoorPlaneZ { get; }
        public float ShellWidth { get; }
        public float ShootingCounterWidth { get; }
        public Vector3 RangeCenter { get; }
        public Vector3 EntranceSpawn { get; }
        public Bounds VerifiedBounds { get; }
        public Bounds MaintenanceBounds { get; }
        public float RequiredRetaliationDistance => VerifiedBounds.size.magnitude;
        public IReadOnlyList<AimShelfAnchor> ShelfAnchors { get; }
        public IReadOnlyList<AimWorkstationAnchor> AttachmentWorkstationAnchors { get; }
        public IReadOnlyList<SlidingTargetTrackDefinition> SlidingTargetTracks { get; }
        public IReadOnlyList<AimBotCover> BotCovers { get; }
        public IReadOnlyList<AimBotPath> BotPaths { get; }
        public float SphereBayWidth { get; }
        public Vector3 SphereLaneOrigin { get; }
        public Quaternion SphereLaneRotation { get; }

        public Vector3 MovingTargetOrigin => new Vector3(GalleryOrigin.x, GalleryOrigin.y, DoorPlaneZ);

        public bool ContainsVerified(Vector3 worldPosition) => VerifiedBounds.Contains(worldPosition);

        /// <summary>The far-left wing reserved for Pulse Line when that optional lane starts successfully.</summary>
        public bool ContainsParkourBay(Vector3 worldPosition)
        {
            float leftWall = GalleryOrigin.x - ShellWidth / 2f;
            return worldPosition.x >= leftWall && worldPosition.x <= GalleryOrigin.x - 9.9f &&
                worldPosition.y >= GalleryOrigin.y - 0.25f && worldPosition.y <= GalleryOrigin.y + Height + 0.5f &&
                worldPosition.z <= DoorPlaneZ - 4.45f && worldPosition.z >= DoorPlaneZ - Depth;
        }

        /// <summary>
        /// UI-only boundary at the former room seam. Gameplay/session ownership intentionally uses the larger
        /// VerifiedBounds, so a player on the selector side can still shoot and provoke bots without seeing the
        /// Aim HUD. Crossing the floor line swaps the original SCP panel for the Aim HUD.
        /// </summary>
        public bool ContainsAimUi(Vector3 worldPosition)
        {
            float halfWidth = ShellWidth / 2f;
            return worldPosition.x >= GalleryOrigin.x - halfWidth && worldPosition.x <= GalleryOrigin.x + halfWidth &&
                worldPosition.y >= GalleryOrigin.y - 0.25f && worldPosition.y <= GalleryOrigin.y + Height + 0.25f &&
                worldPosition.z <= DoorPlaneZ && worldPosition.z >= DoorPlaneZ - Depth;
        }

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
