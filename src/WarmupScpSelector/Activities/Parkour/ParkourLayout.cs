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

        /// <summary>Sector-boundary landing; the world paints these differently so progress reads at a glance.</summary>
        public bool GoldCut { get; }

        public float SurfaceY => Center.y + Size.y / 2f;

        public Vector3 RecoveryPosition => new Vector3(Center.x, SurfaceY + 0.5f, Center.z);

        public Bounds GateBounds => new Bounds(
            new Vector3(Center.x, SurfaceY + 0.55f, Center.z),
            new Vector3(Math.Max(0.8f, Size.x), 1.4f, Math.Max(0.8f, Size.z)));
    }

    /// <summary>One authored hop, kept so tests and the HUD can talk about difficulty without re-deriving it.</summary>
    internal readonly struct ParkourHop
    {
        public ParkourHop(int index, float gap, float rise, float difficulty)
        {
            Index = index;
            Gap = gap;
            Rise = rise;
            Difficulty = difficulty;
        }

        /// <summary>Index of the landing this hop arrives at.</summary>
        public int Index { get; }

        /// <summary>Edge-to-edge horizontal distance the feet must cover.</summary>
        public float Gap { get; }

        /// <summary>Height gained (negative when the route steps down).</summary>
        public float Rise { get; }

        /// <summary>Fraction of the reachable distance this hop uses at the model's reference speed.</summary>
        public float Difficulty { get; }
    }

    /// <summary>
    /// The Pulse Line: a generated climb up the station's parkour shaft.
    ///
    /// The route is not hand-placed. Landings are emitted by walking up the shaft, and every hop's
    /// horizontal gap is derived from <see cref="ParkourJumpModel"/> for a target difficulty that ramps
    /// from an easy opener to a committed finish. That is what keeps the course honest against whatever
    /// jump/movement constants the game actually ships: the geometry adapts, so a balance patch cannot
    /// silently turn the route into either a walk-over or an impossibility.
    ///
    /// Difficulty is measured against a reference speed BETWEEN a walk and a sprint, so the opening hops
    /// are clearable at walking pace and the closing hops genuinely require committing to a sprint.
    /// </summary>
    internal sealed class ParkourLayout
    {
        /// <summary>Never author a step taller than this fraction of the jump apex.</summary>
        private const float MaxStepRiseFraction = 0.55f;

        /// <summary>
        /// Difficulty of the first hop and of the last, as a fraction of a sprinter's reach. The closing
        /// pads are also deliberately SHALLOW: a deep pad hands a walker both a longer run-up before the
        /// edge and a larger area to land in, and in game that slack was enough for a walking dummy to
        /// clear hops the numbers said needed a sprint.
        /// </summary>
        private const float OpeningDifficulty = 0.45f;
        private const float ClosingDifficulty = 0.92f;

        /// <summary>Hard ceiling on authored difficulty; the layout refuses to build above it.</summary>
        public const float MaximumDifficulty = 0.96f;

        /// <summary>A course whose hardest hop is below this is a walk-over and is also refused.</summary>
        public const float MinimumPeakDifficulty = 0.72f;

        private const float RouteEntryInset = 4f;
        private const float RouteExitInset = 7.5f;
        private const float StartPadInset = 2f;

        /// <summary>Clearance kept between the finish pad's far edge and the shaft's end bulkhead.</summary>
        private const float FinishWallClearance = 1.2f;

        private const float BaseHeight = 1f;
        private const float TopHeight = 9.4f;
        private const float SlabThickness = 0.24f;
        private const int MaximumLandings = 48;

        private const float StartPadHeight = 0.5f;

        private const float OpeningPadWidth = 2f;
        private const float ClosingPadWidth = 1.2f;
        private const float OpeningPadDepth = 1.65f;
        private const float ClosingPadDepth = 0.85f;

        /// <summary>
        /// Sideways travel as a fraction of the whole hop. Sign alternates every landing, so a hop's
        /// lateral delta is this fraction of its length; the rest becomes forward progress up the shaft.
        /// Deriving it from the hop budget (rather than from a fixed metre offset) is what stops a wide
        /// zig-zag from silently consuming the entire jump and making late hops unclearable.
        /// </summary>
        private const float OpeningLateralFraction = 0.45f;
        private const float ClosingLateralFraction = 0.72f;

        /// <summary>Hard cap on how far a landing may sit from the shaft centreline.</summary>
        private const float LateralLimit = 2.8f;

        /// <summary>Vertical slack below the last cleared landing before a runner counts as fallen.</summary>
        public const float FallRecoveryDrop = 1.75f;

        private readonly List<ParkourPlatform> _platforms = new();
        private readonly List<ParkourHop> _hops = new();

        public ParkourLayout(WarmupHallLayout hall, ParkourJumpModel model)
        {
            if (hall == null)
            {
                throw new ArgumentNullException(nameof(hall));
            }

            if (!model.IsFinite)
            {
                throw new InvalidOperationException("parkour jump model is not usable");
            }

            Hall = hall;
            Model = model;
            StationZone shaft = hall.ParkourShaft;
            Origin = hall.Origin;

            // Occupancy bounds are the shaft interior with a little vertical slack: a runner standing on
            // the deck sits exactly on the interior floor plane, and physics jitter must not read as
            // "left the lane" and tear their run down mid-jump.
            Bounds interior = hall.InteriorBounds(shaft);
            interior.Expand(new Vector3(0f, 1.2f, 0f));
            ShaftBounds = interior;

            float routeStartZ = shaft.MinZ + RouteEntryInset;
            float routeEndZ = shaft.MaxZ - RouteExitInset;
            if (routeEndZ - routeStartZ < 12f)
            {
                throw new InvalidOperationException("parkour shaft is too short for a route");
            }

            StartPlate = new ParkourPlatform(
                hall.World(0f, StartPadHeight / 2f, shaft.MinZ + StartPadInset),
                new Vector3(2.6f, StartPadHeight, 1.9f));
            ResetCoinPosition = hall.World(2.9f, 1.05f, shaft.MinZ + StartPadInset);

            Generate(hall, routeStartZ, routeEndZ);
            FinishPlate = PlaceFinish(hall, shaft);
            AppendFinishHop();
            Validate();
        }

        /// <summary>
        /// Builds the route from landings an author placed, rather than generating one.
        ///
        /// The author's difficulty is taken as intent and is NOT re-tuned: the generated course is
        /// deliberately gentle and a human reported crossing it without sprinting, so a hand-made route
        /// being harder is the point. Only genuine impossibility is rejected - a hop no jump can reach is
        /// not "hard", it is a course that cannot be finished.
        /// </summary>
        public ParkourLayout(WarmupHallLayout hall, ParkourJumpModel model, IReadOnlyList<ParkourPlatform> authored)
        {
            if (hall == null)
            {
                throw new ArgumentNullException(nameof(hall));
            }

            if (!model.IsFinite)
            {
                throw new InvalidOperationException("parkour jump model is not usable");
            }

            if (authored == null || authored.Count < 3)
            {
                throw new InvalidOperationException("an authored parkour route needs a start, a finish, and at least one landing");
            }

            Hall = hall;
            Model = model;
            Origin = hall.Origin;
            IsAuthored = true;

            Bounds interior = hall.InteriorBounds(hall.ParkourShaft);
            interior.Expand(new Vector3(0f, 1.2f, 0f));
            ShaftBounds = interior;

            // Nearest the hatch is the start, furthest is the finish, everything between is an ordered gate.
            StartPlate = authored[0];
            FinishPlate = authored[authored.Count - 1];
            for (int i = 1; i < authored.Count - 1; i++)
            {
                _platforms.Add(authored[i]);
            }

            ResetCoinPosition = new Vector3(
                StartPlate.Center.x + 2.9f,
                StartPlate.SurfaceY + 0.55f,
                StartPlate.Center.z);

            ParkourPlatform previous = StartPlate;
            for (int i = 0; i < _platforms.Count; i++)
            {
                _hops.Add(MeasureHop(i, previous, _platforms[i]));
                previous = _platforms[i];
            }

            AppendFinishHop();
            ValidateAuthored();
        }

        /// <summary>Whether this route came from an authored schematic rather than the generator.</summary>
        public bool IsAuthored { get; }

        public WarmupHallLayout Hall { get; }

        public ParkourJumpModel Model { get; }

        /// <summary>Station origin; the shaft deck sits at <c>Origin.y</c>.</summary>
        public Vector3 Origin { get; }

        /// <summary>Interior volume of the whole shaft. Occupancy uses this.</summary>
        public Bounds ShaftBounds { get; }

        public ParkourPlatform StartPlate { get; }

        public ParkourPlatform FinishPlate { get; }

        public Vector3 ResetCoinPosition { get; }

        public IReadOnlyList<ParkourPlatform> Platforms => _platforms;

        /// <summary>One entry per landing plus the finish, describing what that hop demands.</summary>
        public IReadOnlyList<ParkourHop> Hops => _hops;

        /// <summary>Hardest authored hop, as a fraction of reachable distance at the reference speed.</summary>
        public float PeakDifficulty { get; private set; }

        /// <summary>Deck level of the shaft; a runner at or below this has definitively fallen.</summary>
        public float DeckY => Origin.y;

        public bool Contains(Vector3 position) => WarmupHallLayout.ContainsPoint(ShaftBounds, position);

        public bool ContainsStart(Vector3 position) => WarmupHallLayout.ContainsPoint(StartPlate.GateBounds, position);

        public bool ContainsFinish(Vector3 position) => WarmupHallLayout.ContainsPoint(FinishPlate.GateBounds, position);

        /// <summary>
        /// Whether a runner has dropped off the route. Catching the fall EARLY (rather than waiting for
        /// the deck) is what keeps a miss from the top of the shaft off the fall-damage table: the runner
        /// is recovered about a fifth of a second into the fall, long before impact.
        /// </summary>
        public bool HasFallenOffRoute(Vector3 position, int lastCompletedGate)
        {
            float reference = lastCompletedGate >= 0 && lastCompletedGate < _platforms.Count
                ? _platforms[lastCompletedGate].SurfaceY
                : StartPlate.SurfaceY;
            return position.y < reference - FallRecoveryDrop || position.y <= DeckY + 0.35f;
        }

        public Vector3 RecoveryPosition(int lastCompletedGate) =>
            lastCompletedGate >= 0 && lastCompletedGate < _platforms.Count
                ? _platforms[lastCompletedGate].RecoveryPosition
                : StartPlate.RecoveryPosition;

        public int SectorForGate(int nextGateIndex)
        {
            if (_platforms.Count == 0)
            {
                return 1;
            }

            int clamped = Math.Max(0, Math.Min(_platforms.Count - 1, nextGateIndex));
            return Math.Min(4, clamped * 4 / _platforms.Count + 1);
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

        // ---- Generation ----------------------------------------------------------------------

        /// <summary>
        /// Walks up the shaft emitting landings. Each hop's length comes from the jump model, so the
        /// landing COUNT is an output of the physics rather than a hand-tuned constant.
        /// </summary>
        private void Generate(WarmupHallLayout hall, float routeStartZ, float routeEndZ)
        {
            float span = routeEndZ - routeStartZ;
            float maxStepRise = Model.MaxRise * MaxStepRiseFraction;

            // Generation runs entirely in shaft-local metres (deck top = 0); only the final landing centre
            // is lifted into world space. Mixing the two frames here is the easy way to author a course
            // that is correct at the origin and wrong everywhere else.
            ParkourPlatform previous = StartPlate;
            float previousX = 0f;
            float previousZ = StartPlate.Center.z - Origin.z;
            float previousSurface = StartPadHeight;
            float previousDepth = StartPlate.Size.z;
            int side = -1;

            while (_platforms.Count < MaximumLandings)
            {
                SolvedHop hop = SolveHop(previousZ, previousX, previousSurface, previousDepth, routeStartZ, span, side, maxStepRise);
                if (hop.Z > routeEndZ)
                {
                    break;
                }

                int index = _platforms.Count;
                ParkourPlatform landing = new ParkourPlatform(
                    hall.World(hop.X, hop.SurfaceY - SlabThickness / 2f, hop.Z),
                    new Vector3(hop.Width, SlabThickness, hop.Depth),
                    index > 0 && index % 5 == 0);
                _platforms.Add(landing);
                _hops.Add(MeasureHop(index, previous, landing));

                previous = landing;
                previousX = hop.X;
                previousZ = hop.Z;
                previousSurface = hop.SurfaceY;
                previousDepth = hop.Depth;
                side = -side;
            }
        }

        /// <summary>Where one solved hop lands, in shaft-local coordinates.</summary>
        private readonly struct SolvedHop
        {
            public SolvedHop(float x, float surfaceY, float z, float width, float depth)
            {
                X = x;
                SurfaceY = surfaceY;
                Z = z;
                Width = width;
                Depth = depth;
            }

            public float X { get; }
            public float SurfaceY { get; }
            public float Z { get; }
            public float Width { get; }
            public float Depth { get; }
        }

        /// <summary>
        /// Solves where the next landing belongs. The hop's length depends on how much it climbs, and how
        /// much it climbs depends on where along the shaft it ends up, so the two are settled with a short
        /// fixed-point iteration. The sideways component is taken out of the same budget, which guarantees
        /// the resulting hop is exactly as hard as the difficulty ramp asked for.
        /// </summary>
        private SolvedHop SolveHop(
            float fromZ,
            float fromX,
            float fromSurface,
            float fromDepth,
            float routeStartZ,
            float span,
            int side,
            float maxStepRise)
        {
            float candidateZ = fromZ + 2.5f;
            float x = fromX;
            float surfaceY = fromSurface;
            float width = OpeningPadWidth;
            float depth = OpeningPadDepth;

            for (int iteration = 0; iteration < 4; iteration++)
            {
                float t = Mathf.Clamp01((candidateZ - routeStartZ) / span);

                // The step cap is applied to the PLACEMENT, not just to the gap arithmetic. Capping only
                // the number used to size the hop would leave the landing where the profile wanted it and
                // quietly author a step taller than the cap promises.
                surfaceY = Mathf.Min(HeightAt(t), fromSurface + maxStepRise);
                width = Mathf.Lerp(OpeningPadWidth, ClosingPadWidth, t);
                depth = Mathf.Lerp(OpeningPadDepth, ClosingPadDepth, t);

                float rise = surfaceY - fromSurface;
                float difficulty = Mathf.Lerp(OpeningDifficulty, ClosingDifficulty, t);
                float centerDistance = Model.GapForDifficulty(difficulty, rise) + (fromDepth + depth) / 2f;

                x = side * Mathf.Min(
                    0.5f * Mathf.Lerp(OpeningLateralFraction, ClosingLateralFraction, t) * centerDistance,
                    LateralLimit);
                float lateral = x - fromX;
                float forward = Mathf.Sqrt(Mathf.Max(0.35f, centerDistance * centerDistance - lateral * lateral));
                candidateZ = fromZ + forward;
            }

            return new SolvedHop(x, surfaceY, candidateZ, width, depth);
        }

        /// <summary>
        /// Puts the finish pad exactly one solved hop past the last landing rather than at a fixed Z, so
        /// the closing jump is as hard as the ramp intends instead of being whatever distance was left
        /// over. If that would put the pad through the end bulkhead, the last landing is dropped and the
        /// finish is re-solved from the one before it.
        /// </summary>
        private ParkourPlatform PlaceFinish(WarmupHallLayout hall, StationZone shaft)
        {
            const float finishWidth = 3.2f;
            const float finishDepth = 2.2f;
            float limit = shaft.MaxZ - FinishWallClearance - finishDepth / 2f;
            float maxStepRise = Model.MaxRise * MaxStepRiseFraction;

            while (_platforms.Count > 0)
            {
                ParkourPlatform last = _platforms[_platforms.Count - 1];
                float lastSurface = last.SurfaceY - Origin.y;
                float lastX = last.Center.x - Origin.x;
                float finishSurface = Mathf.Min(TopHeight, lastSurface + maxStepRise);
                float rise = Mathf.Max(0f, finishSurface - lastSurface);
                float gap = Model.GapForDifficulty(ClosingDifficulty, rise);
                float centerDistance = gap + (last.Size.z + finishDepth) / 2f;
                float lateral = 0f - lastX;
                float forward = Mathf.Sqrt(Mathf.Max(0.35f, centerDistance * centerDistance - lateral * lateral));
                float z = last.Center.z - Origin.z + forward;
                if (z <= limit)
                {
                    return new ParkourPlatform(
                        hall.World(0f, finishSurface - SlabThickness / 2f, z),
                        new Vector3(finishWidth, SlabThickness, finishDepth));
                }

                _platforms.RemoveAt(_platforms.Count - 1);
                _hops.RemoveAt(_hops.Count - 1);
            }

            throw new InvalidOperationException("parkour shaft cannot fit a finish pad");
        }

        /// <summary>Linear climb: a constant rise per metre keeps every hop's vertical demand predictable.</summary>
        private static float HeightAt(float t) => Mathf.Lerp(BaseHeight, TopHeight, Mathf.Clamp01(t));

        /// <summary>
        /// Measures what one hop actually demands of the runner. Both platforms are in world space, so
        /// this is the authoritative reading of the built course rather than of the solver's intent.
        /// </summary>
        private ParkourHop MeasureHop(int index, ParkourPlatform from, ParkourPlatform to)
        {
            Vector3 delta = to.Center - from.Center;
            float centerDistance = new Vector2(delta.x, delta.z).magnitude;
            float gap = Mathf.Max(0.1f, centerDistance - (from.Size.z + to.Size.z) / 2f);
            float rise = to.SurfaceY - from.SurfaceY;
            return new ParkourHop(index, gap, rise, Model.Difficulty(gap, rise));
        }

        private void AppendFinishHop()
        {
            if (_platforms.Count > 0)
            {
                _hops.Add(MeasureHop(_platforms.Count, _platforms[_platforms.Count - 1], FinishPlate));
            }
        }

        /// <summary>
        /// Fails the whole lane closed when the generated route would be impossible, trivial, out of the
        /// shaft, or short enough that it is not a course. Callers treat a throw as "parkour stays shut".
        /// </summary>
        private void Validate()
        {
            if (_platforms.Count < 6)
            {
                throw new InvalidOperationException($"parkour route produced only {_platforms.Count} landings");
            }

            float peak = 0f;
            float stepCap = Model.MaxRise * MaxStepRiseFraction + 0.01f;
            foreach (ParkourHop hop in _hops)
            {
                if (hop.Rise > stepCap)
                {
                    throw new InvalidOperationException(
                        $"parkour hop {hop.Index} climbs {hop.Rise:0.###}m, above the {stepCap:0.###}m step cap");
                }

                if (float.IsNaN(hop.Difficulty) || float.IsInfinity(hop.Difficulty) || hop.Difficulty > MaximumDifficulty)
                {
                    throw new InvalidOperationException(
                        $"parkour hop {hop.Index} is unclearable (gap {hop.Gap:0.##}m, rise {hop.Rise:0.##}m, difficulty {hop.Difficulty:0.##})");
                }

                peak = Mathf.Max(peak, hop.Difficulty);
            }

            if (peak < MinimumPeakDifficulty)
            {
                throw new InvalidOperationException($"parkour route is trivial (peak difficulty {peak:0.##})");
            }

            PeakDifficulty = peak;

            Bounds shaft = ShaftBounds;
            float headroom = Origin.y + Hall.ParkourShaft.CeilingHeight;
            foreach (ParkourPlatform platform in _platforms)
            {
                if (platform.Center.x - platform.Size.x / 2f < shaft.min.x + 0.1f ||
                    platform.Center.x + platform.Size.x / 2f > shaft.max.x - 0.1f ||
                    platform.Center.z - platform.Size.z / 2f < shaft.min.z + 0.1f ||
                    platform.Center.z + platform.Size.z / 2f > shaft.max.z - 0.1f)
                {
                    throw new InvalidOperationException("parkour landing left the shaft");
                }

                // A runner standing on a landing must still be able to jump without hitting the overhead.
                if (platform.SurfaceY + 1.9f + Model.MaxRise > headroom - 0.2f)
                {
                    throw new InvalidOperationException("parkour landing has insufficient headroom");
                }
            }
        }

        /// <summary>
        /// Rejects only what cannot be played: a hop beyond the jump's reach at full sprint, or one that
        /// climbs above the apex. Everything else is the author's call, including a course far harder
        /// than the generator would ever produce.
        /// </summary>
        private void ValidateAuthored()
        {
            float peak = 0f;
            foreach (ParkourHop hop in _hops)
            {
                float reachable = Model.MaxGap(hop.Rise, Model.SprintSpeed);
                if (reachable <= 0.001f || hop.Gap > reachable)
                {
                    throw new InvalidOperationException(
                        $"authored parkour hop {hop.Index} cannot be cleared even at a full sprint " +
                        $"(gap {hop.Gap:0.##}m, rise {hop.Rise:0.##}m, reach {reachable:0.##}m)");
                }

                peak = Mathf.Max(peak, hop.Difficulty);
            }

            PeakDifficulty = peak;
        }

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
