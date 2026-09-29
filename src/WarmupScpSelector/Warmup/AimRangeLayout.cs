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

    /// <summary>
    /// Single source of truth for the three training lanes inside the station's Aim Bay - the long east
    /// arm of the hub.
    ///
    /// The range fires ALONG +X, down the arm's 21.5 m length, with the three lanes stacked across Z:
    /// <code>
    ///   z +9.5  +--------------------------------------------+
    ///           |  lane 3   spheres                          |
    ///   z +2.9  +--------------------------------------------+
    ///           |  lane 2   sliding targets                  |
    ///   z -3.1  +--------------------------------------------+
    ///           |  lane 1   bots and cover                   |
    ///   z -9.5  +--------------------------------------------+
    ///          x=11      14.5 (counter)  -- downrange -->   x=36
    /// </code>
    ///
    /// Everything is authored in a single range frame - <see cref="Range"/>(lateral, up, downrange) -
    /// so lateral offsets are Z, downrange is X, and no call site has to remember the axis mapping.
    /// That frame is also why the target and workstation rotations are what they are: the whole range is
    /// the old -Z-downrange layout turned by <see cref="RangeRotation"/>, and every authored orientation
    /// is that same turn applied to a facing.
    /// </summary>
    public sealed class AimRangeLayout
    {
        /// <summary>Interior height of the bay.</summary>
        public const float Height = 5f;

        /// <summary>Total width across the three lanes (the bay's Z extent).</summary>
        public const float Width = 19f;

        /// <summary>Usable downrange length, counter to backstop.</summary>
        public const float Depth = 21.5f;

        public const float LaneWidth = Width / 3f;
        public const float ShootingCounterHeight = 1.1f;

        /// <summary>How far downrange of the bay entrance the shooting counter sits.</summary>
        public const float ShootingCounterDepth = 3.5f;

        /// <summary>How far in from the bay's side wall a workstation stands, so its body clears the bulkhead.</summary>
        public const float AttachmentWorkstationWallInset = 0.9f;

        /// <summary>
        /// Workstations sit BEHIND the shooting line, on the shooter's side of the counter.
        ///
        /// The counter is a solid barrier the full width of the bay bar 0.3 m at each end, so anything
        /// downrange of it can be seen and never reached: the pair used to stand 4 m downrange and a
        /// player could only look at them. This keeps them in the 3.5 m entrance apron, clear of the
        /// counter, the doorway, and every lane's furniture.
        /// </summary>
        private const float AttachmentWorkstationDownrange = -1.7f;

        // Lane boundaries across Z. Lane 3 is the widest because the sphere cloud needs the most clear width.
        private const float LaneOneToTwoZ = -3.1f;
        private const float LaneTwoToThreeZ = 2.9f;
        private const float LaneOneCenterZ = -6.3f;
        private const float LaneTwoCenterZ = -0.1f;
        private const float LaneThreeCenterZ = 6.2f;

        /// <summary>Clear width the sphere cloud may spread across inside lane 3, leaving a wall margin.</summary>
        private const float SphereClearWidth = 6f;

        public AimRangeLayout(WarmupHallLayout hall)
        {
            Hall = hall;
            StationZone bay = hall.AimBay;
            DeckY = hall.Origin.y;
            EntranceX = bay.MinX;
            BackstopX = bay.MaxX;
            ShootingLineX = bay.MinX + ShootingCounterDepth;
            BayMinZ = bay.MinZ;
            BayMaxZ = bay.MaxZ;
            ShellWidth = bay.Depth;
            ShootingCounterWidth = ShellWidth - 0.6f;
            RangeCenter = hall.World((ShootingLineX + BackstopX) / 2f, 0f, bay.CenterZ);
            EntranceSpawn = hall.World(EntranceX + 1.4f, 0.5f, bay.CenterZ);
            MovingTargetOrigin = hall.World(ShootingLineX, 0f, 0f);

            // Ownership deliberately reaches back through the hatch: a player standing in the hub doorway
            // can already shoot into the bay, so the session that governs damage routing must own them there.
            VerifiedBounds = new Bounds(
                hall.World((EntranceX - 3f + BackstopX) / 2f, Height / 2f, bay.CenterZ),
                new Vector3(BackstopX - EntranceX + 3f, Height + 0.5f, ShellWidth - 0.3f));
            MaintenanceBounds = new Bounds(
                hall.World((EntranceX + BackstopX) / 2f, Height / 2f, bay.CenterZ),
                new Vector3(BackstopX - EntranceX + 8f, Height + 8f, ShellWidth + 6f));
            UiBounds = new Bounds(
                hall.World((EntranceX + BackstopX) / 2f, Height / 2f, bay.CenterZ),
                new Vector3(BackstopX - EntranceX, Height + 0.5f, ShellWidth));

            // Six dispensers on the low counter, spread across all three lanes so no lane owns the guns.
            float pickupY = ShootingCounterHeight + 0.28f;
            ShelfAnchors = new[]
            {
                new AimShelfAnchor(0, Range(-7.6f, pickupY, 0f), DownrangeFacing),
                new AimShelfAnchor(1, Range(-4.6f, pickupY, 0f), DownrangeFacing),
                new AimShelfAnchor(2, Range(-1.6f, pickupY, 0f), DownrangeFacing),
                new AimShelfAnchor(3, Range(1.6f, pickupY, 0f), DownrangeFacing),
                new AimShelfAnchor(4, Range(4.6f, pickupY, 0f), DownrangeFacing),
                new AimShelfAnchor(5, Range(7.6f, pickupY, 0f), DownrangeFacing),
            };

            // Two native attachment workstations against the long side walls, facing into the bay, in the
            // entrance apron where a shooter can actually walk up to them.
            float workstationLateral = ShellWidth / 2f - AttachmentWorkstationWallInset;
            AttachmentWorkstationAnchors = new[]
            {
                new AimWorkstationAnchor(
                    Range(-workstationLateral, 0f, AttachmentWorkstationDownrange),
                    MerWorldTransformComposer.QuaternionFromEuler(Vector3.zero)),
                new AimWorkstationAnchor(
                    Range(workstationLateral, 0f, AttachmentWorkstationDownrange),
                    MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 180f, 0f))),
            };

            // Lane 2: three sliding targets at different depths, travelling across the lane in Z.
            SlidingTargetTracks = new[]
            {
                SlidingTrack(0, up: 1.30f, downrange: 6.0f, halfWidth: 2.35f, minimumSpeed: 1.05f, maximumSpeed: 1.35f),
                SlidingTrack(1, up: 2.15f, downrange: 10.5f, halfWidth: 2.15f, minimumSpeed: 1.35f, maximumSpeed: 1.75f),
                SlidingTrack(2, up: 1.55f, downrange: 15.0f, halfWidth: 2.40f, minimumSpeed: 1.70f * 1.35f, maximumSpeed: 2.15f * 1.35f),
            };

            // Lane 1 cover: tall blocks against the outer wall, low blocks against the lane 2 divider, with
            // a clear patrol corridor left between them. Sizes are thin downrange and wide across, because
            // the shooter is always looking along +X.
            BotCovers = new[]
            {
                Cover(0, lateral: -8.5f, downrange: 5.5f, tall: true),
                Cover(0, lateral: -3.9f, downrange: 8.5f, tall: false),
                Cover(0, lateral: -8.5f, downrange: 11.0f, tall: true),
                Cover(1, lateral: -8.5f, downrange: 14.0f, tall: true),
                Cover(1, lateral: -3.9f, downrange: 16.5f, tall: false),
                Cover(1, lateral: -8.5f, downrange: 19.0f, tall: true),
            };

            // Patrol corridors run down the middle of lane 1, clear of every cover block.
            BotPaths = new[]
            {
                new AimBotPath(0, Patrol(fromDownrange: 5.5f, toDownrange: 11.5f)),
                new AimBotPath(1, Patrol(fromDownrange: 14f, toDownrange: 19.5f)),
            };

            SphereBayWidth = SphereClearWidth;
            SphereLaneOrigin = Range(LaneThreeCenterZ, 0f, 0.5f);
            SphereLaneRotation = RangeRotation;
        }

        /// <summary>
        /// Turn that maps the authored range frame onto the bay: local -Z (downrange) becomes world +X,
        /// and local +X (lateral) becomes world +Z.
        /// </summary>
        public static Quaternion RangeRotation => MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, -90f, 0f));

        /// <summary>
        /// Rotation for anything downrange that must FACE BACK AT THE SHOOTER.
        ///
        /// A <c>ShootingTargetToy</c>'s visible face is its local -X. In the old -Z-downrange range the
        /// authored value was Euler(0, 90, 0), which points local -X at world +Z, back up the range. The
        /// bay is that same range turned by <see cref="RangeRotation"/>, so the correct value here is the
        /// COMPOSITION of the two - not the raw turn.
        ///
        /// Passing <see cref="RangeRotation"/> on its own leaves every sliding target 90 degrees off,
        /// edge-on to the shooter. That is exactly what shipped, so this is expressed as the composition
        /// rather than as a baked constant: if the range is ever turned again, the facing follows.
        /// </summary>
        public static Quaternion DownrangeFacing =>
            RangeRotation * MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f));

        public WarmupHallLayout Hall { get; }

        public float DeckY { get; }

        /// <summary>Local X of the hatch plane between the hub and the bay.</summary>
        public float EntranceX { get; }

        /// <summary>Local X of the shooting counter; downrange offsets are measured from here.</summary>
        public float ShootingLineX { get; }

        /// <summary>Local X of the far bulkhead the lanes fire into.</summary>
        public float BackstopX { get; }

        public float BayMinZ { get; }

        public float BayMaxZ { get; }

        /// <summary>Width across the lanes. Named for continuity with the counter and validation code.</summary>
        public float ShellWidth { get; }

        public float ShootingCounterWidth { get; }

        public Vector3 RangeCenter { get; }

        public Vector3 EntranceSpawn { get; }

        /// <summary>Origin bot path points and lane geometry are expressed relative to.</summary>
        public Vector3 MovingTargetOrigin { get; }

        public Bounds VerifiedBounds { get; }

        public Bounds MaintenanceBounds { get; }

        public Bounds UiBounds { get; }

        public float RequiredRetaliationDistance => VerifiedBounds.size.magnitude;

        public IReadOnlyList<AimShelfAnchor> ShelfAnchors { get; }

        public IReadOnlyList<AimWorkstationAnchor> AttachmentWorkstationAnchors { get; }

        public IReadOnlyList<SlidingTargetTrackDefinition> SlidingTargetTracks { get; }

        public IReadOnlyList<AimBotCover> BotCovers { get; }

        public IReadOnlyList<AimBotPath> BotPaths { get; }

        public float SphereBayWidth { get; }

        public Vector3 SphereLaneOrigin { get; }

        public Quaternion SphereLaneRotation { get; }

        /// <summary>Lane divider positions across Z, used by the world builder.</summary>
        public float DividerOneZ => LaneOneToTwoZ;

        public float DividerTwoZ => LaneTwoToThreeZ;

        public float LaneOneCenter => LaneOneCenterZ;

        public float LaneTwoCenter => LaneTwoCenterZ;

        public float LaneThreeCenter => LaneThreeCenterZ;

        public bool ContainsVerified(Vector3 worldPosition) => WarmupHallLayout.ContainsPoint(VerifiedBounds, worldPosition);

        /// <summary>
        /// Whether the Aim HUD should replace the SCP draft panel. This is the bay proper, while gameplay
        /// ownership uses the larger <see cref="VerifiedBounds"/>: a player in the hatch can shoot into the
        /// bay without the HUD swapping in under them.
        /// </summary>
        public bool ContainsAimUi(Vector3 worldPosition) => WarmupHallLayout.ContainsPoint(UiBounds, worldPosition);

        /// <summary>World position from range-frame coordinates: lateral across Z, downrange along +X.</summary>
        public Vector3 Range(float lateral, float up, float downrange) =>
            Hall.World(ShootingLineX + downrange, up, lateral);

        private SlidingTargetTrackDefinition SlidingTrack(
            int slotId,
            float up,
            float downrange,
            float halfWidth,
            float minimumSpeed,
            float maximumSpeed)
        {
            Vector3 center = Range(LaneTwoCenterZ, up, downrange);
            return new SlidingTargetTrackDefinition(
                slotId,
                center + new Vector3(0f, 0f, -halfWidth),
                center + new Vector3(0f, 0f, halfWidth),
                DownrangeFacing,
                Vector3.one,
                minimumSpeed,
                maximumSpeed);
        }

        private AimBotCover Cover(int slotId, float lateral, float downrange, bool tall)
        {
            float height = tall ? 2.2f : 1.7f;
            float across = tall ? 1.9f : 1.6f;
            return new AimBotCover(
                slotId,
                Range(lateral, height / 2f, downrange),
                new Vector3(0.5f, height, across),
                Range(lateral, 0f, downrange + 0.9f),
                tall);
        }

        /// <summary>
        /// A closed patrol loop down lane 1: out along the corridor, a short strafe across it, and back.
        /// Points are world deltas from <see cref="MovingTargetOrigin"/>, which is what the bot controller
        /// composes them against.
        /// </summary>
        private static RangePathSegment[] Patrol(float fromDownrange, float toDownrange)
        {
            const float nearZ = -7f;
            const float farZ = -5.4f;
            float length = toDownrange - fromDownrange;
            double runSeconds = Mathf.Max(1.2f, length / 1.75f);
            const double strafeSeconds = 1.2d;
            return new[]
            {
                new RangePathSegment(Point(fromDownrange, nearZ), Point(toDownrange, nearZ), runSeconds),
                new RangePathSegment(Point(toDownrange, nearZ), Point(toDownrange, farZ), strafeSeconds),
                new RangePathSegment(Point(toDownrange, farZ), Point(fromDownrange, farZ), runSeconds),
                new RangePathSegment(Point(fromDownrange, farZ), Point(fromDownrange, nearZ), strafeSeconds),
            };
        }

        private static RangePoint Point(float downrange, float lateral) => new RangePoint(downrange, 0f, lateral);
    }
}
