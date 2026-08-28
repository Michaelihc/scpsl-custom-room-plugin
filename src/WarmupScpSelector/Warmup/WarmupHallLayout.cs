using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarmupScpSelector.Warmup;

/// <summary>One rectangular pressurised compartment of the warmup station.</summary>
public sealed class StationZone
{
    public StationZone(string id, string signEn, string signCn, float minX, float maxX, float minZ, float maxZ, float ceilingHeight)
    {
        Id = id;
        SignEn = signEn;
        SignCn = signCn;
        MinX = Math.Min(minX, maxX);
        MaxX = Math.Max(minX, maxX);
        MinZ = Math.Min(minZ, maxZ);
        MaxZ = Math.Max(minZ, maxZ);
        CeilingHeight = ceilingHeight;
    }

    /// <summary>Stable identifier used by signage, logs, and tests.</summary>
    public string Id { get; }

    public string SignEn { get; }

    public string SignCn { get; }

    /// <summary>Interior extents in station-local metres (deck top is local y = 0).</summary>
    public float MinX { get; }

    public float MaxX { get; }

    public float MinZ { get; }

    public float MaxZ { get; }

    public float CeilingHeight { get; }

    public float Width => MaxX - MinX;

    public float Depth => MaxZ - MinZ;

    public float CenterX => (MinX + MaxX) / 2f;

    public float CenterZ => (MinZ + MaxZ) / 2f;
}

/// <summary>
/// A hatch cut through one compartment face. Both neighbouring zones subtract the same rectangle, so a
/// shared face produces exactly one set of wall segments and never doubles geometry.
/// </summary>
public readonly struct StationOpening
{
    public StationOpening(bool inConstantXWall, float plane, float from, float to, float height)
    {
        InConstantXWall = inConstantXWall;
        Plane = plane;
        From = Math.Min(from, to);
        To = Math.Max(from, to);
        Height = height;
    }

    /// <summary>True when the hatch lies in a constant-X wall, so its span is measured in Z.</summary>
    public bool InConstantXWall { get; }

    /// <summary>Constant coordinate of the wall this hatch is cut through.</summary>
    public float Plane { get; }

    public float From { get; }

    public float To { get; }

    /// <summary>Clear height above the deck.</summary>
    public float Height { get; }
}

/// <summary>One solid panel of a compartment face, after every hatch has been cut out of it.</summary>
public readonly struct StationWallSegment
{
    public StationWallSegment(bool inConstantXWall, float plane, float from, float to, float bottomY, float topY)
    {
        InConstantXWall = inConstantXWall;
        Plane = plane;
        From = from;
        To = to;
        BottomY = bottomY;
        TopY = topY;
    }

    public bool InConstantXWall { get; }

    public float Plane { get; }

    public float From { get; }

    public float To { get; }

    public float BottomY { get; }

    public float TopY { get; }

    public float Span => To - From;

    public float Height => TopY - BottomY;

    public float Center => (From + To) / 2f;

    public float MidY => (BottomY + TopY) / 2f;
}

/// <summary>One SCP display stand in the gallery: where the model, its label, and its coin belong.</summary>
public readonly struct GalleryDisplaySlot
{
    public GalleryDisplaySlot(Vector3 standTopCenter, float standWidth, float standDepth, Quaternion facing, Vector3 coinPosition)
    {
        StandTopCenter = standTopCenter;
        StandWidth = standWidth;
        StandDepth = standDepth;
        Facing = facing;
        CoinPosition = coinPosition;
    }

    /// <summary>World centre of the low stand's top face, i.e. the model's base.</summary>
    public Vector3 StandTopCenter { get; }

    public float StandWidth { get; }

    public float StandDepth { get; }

    /// <summary>Rotation applied to the authored model so it looks at approaching players.</summary>
    public Quaternion Facing { get; }

    /// <summary>World position of this display's selection coin, in front of the model.</summary>
    public Vector3 CoinPosition { get; }
}

/// <summary>
/// Single source of truth for the whole warmup station: compartments, hatches, the SCP gallery stands,
/// and the anchors the Aim range and parkour shaft build from.
///
/// Derived from the authored ProjectMER room (DT.json) - same cross plan, same gallery-to-the-south and
/// shaft-to-the-north topology - with the numbers regularised so the station is a handful of named
/// constants instead of exported floats.
///
/// <code>
///                                       +Z
///          z=71    +---------+          ^
///                  | PARKOUR |          |   9 m wide, 13.5 m tall shaft
///                  |  SHAFT  |
///        z=17.5    +--+   +--+
///  +---------------+  |   |  +--------------------------------+
///  |  OBSERVATION  |    HUB    |          AIM BAY             |   z = +/-9.5
///  |     DECK      |  |   |  |                                |
///  +---------------+  |   |  +--------------------------------+
///  x=-22        x=-11 +--+---+ x=11                       x=36
///                  |CONNECTOR|
///        z=-25.5   +--+   +--+
///                  |         |
///                  | GALLERY |   SCP draft, 7.5 m ceiling
///          z=-46   +---------+
/// </code>
///
/// Extending the station is deliberately cheap: add a <see cref="StationZone"/>, add the
/// <see cref="StationOpening"/> that connects it, and the shell builder walls and lights it for free.
/// </summary>
public sealed class WarmupHallLayout
{
    // ---- Hub ---------------------------------------------------------------------------------
    public const float HubHalfWidth = 11f;
    public const float HubHalfDepth = 17.5f;
    public const float DeckHeight = 5f;

    // ---- Side arms ---------------------------------------------------------------------------
    public const float ArmHalfDepth = 9.5f;
    public const float AimBayFarX = 36f;
    public const float ObservationFarX = -22f;

    // ---- Gallery (south) ---------------------------------------------------------------------
    public const float ConnectorHalfWidth = 4.5f;
    public const float ConnectorFarZ = -25.5f;
    public const float GalleryFarZ = -46f;
    public const float GalleryCeilingHeight = 7.5f;

    // ---- Parkour shaft (north) ---------------------------------------------------------------
    public const float ShaftHalfWidth = 4.5f;
    public const float ShaftFarZ = 71f;
    public const float ShaftCeilingHeight = 13.5f;

    /// <summary>Clear height of every hatch between compartments.</summary>
    public const float HatchHeight = 5f;

    // ---- Gallery display grid ----------------------------------------------------------------
    /// <summary>Back rank of stands, facing arriving players.</summary>
    public const float BackRankZ = -43.5f;

    /// <summary>Half-width of the clear centre aisle kept open for the wall logo.</summary>
    private const float GalleryAisleHalfWidth = 1.85f;

    /// <summary>Side stands hug the gallery walls so the aisle and back rank stay unobstructed.</summary>
    private const float SideRankX = 8.6f;

    private const float SideRankFirstZ = -38.9f;
    private const float SideRankSpacing = 4.6f;
    private const float SideRankLastZ = -29.7f;

    public const float StandHeight = 0.28f;
    private const float StandDepthMeters = 1.5f;
    private const float CoinHeight = 1.05f;
    private const float CoinStandClearance = 0.95f;

    private readonly List<StationZone> _zones = new();
    private readonly List<StationOpening> _openings = new();

    public WarmupHallLayout(Vector3 origin)
    {
        Origin = origin;

        Hub = Add(new StationZone("hub", "CENTRAL HUB", "中枢大厅",
            -HubHalfWidth, HubHalfWidth, -HubHalfDepth, HubHalfDepth, DeckHeight));
        AimBay = Add(new StationZone("aim", "AIM BAY", "瞄准训练舱",
            HubHalfWidth, AimBayFarX, -ArmHalfDepth, ArmHalfDepth, DeckHeight));
        ObservationDeck = Add(new StationZone("observation", "OBSERVATION DECK", "观景舱",
            ObservationFarX, -HubHalfWidth, -ArmHalfDepth, ArmHalfDepth, DeckHeight));
        Connector = Add(new StationZone("connector", "GALLERY ACCESS", "展厅通道",
            -ConnectorHalfWidth, ConnectorHalfWidth, ConnectorFarZ, -HubHalfDepth, DeckHeight));
        Gallery = Add(new StationZone("gallery", "SCP GALLERY", "SCP 展厅",
            -HubHalfWidth, HubHalfWidth, GalleryFarZ, ConnectorFarZ, GalleryCeilingHeight));
        ParkourShaft = Add(new StationZone("shaft", "PULSE LINE", "脉冲路线",
            -ShaftHalfWidth, ShaftHalfWidth, HubHalfDepth, ShaftFarZ, ShaftCeilingHeight));

        // Hub to both arms, hub to connector, connector to gallery, hub to shaft.
        _openings.Add(new StationOpening(true, HubHalfWidth, -ArmHalfDepth, ArmHalfDepth, HatchHeight));
        _openings.Add(new StationOpening(true, -HubHalfWidth, -ArmHalfDepth, ArmHalfDepth, HatchHeight));
        _openings.Add(new StationOpening(false, -HubHalfDepth, -ConnectorHalfWidth, ConnectorHalfWidth, HatchHeight));
        _openings.Add(new StationOpening(false, ConnectorFarZ, -ConnectorHalfWidth, ConnectorHalfWidth, HatchHeight));
        _openings.Add(new StationOpening(false, HubHalfDepth, -ShaftHalfWidth, ShaftHalfWidth, HatchHeight));
    }

    /// <summary>World position of the station deck centre (hub centre, deck top surface).</summary>
    public Vector3 Origin { get; }

    public StationZone Hub { get; }

    public StationZone AimBay { get; }

    public StationZone ObservationDeck { get; }

    public StationZone Connector { get; }

    public StationZone Gallery { get; }

    public StationZone ParkourShaft { get; }

    public IReadOnlyList<StationZone> Zones => _zones;

    public IReadOnlyList<StationOpening> Openings => _openings;

    /// <summary>Where warmup players arrive: in the gallery aisle, looking down it at the SCP stands.</summary>
    public Vector3 SpawnPosition => World(0f, 0.5f, -31.5f);

    /// <summary>Horizontal spawn rotation (degrees) that points arrivals at the back rank.</summary>
    public float SpawnYaw => 180f;

    /// <summary>Wall anchor for the primitive server logo, centred above the gallery's clear aisle.</summary>
    public Vector3 LogoAnchor => World(0f, 4.55f, GalleryFarZ + 0.34f);

    /// <summary>Welcome / QQ line, directly under the logo and still above every model silhouette.</summary>
    public Vector3 BannerAnchor => World(0f, 2.6f, GalleryFarZ + 0.36f);

    /// <summary>Deck plane in world space.</summary>
    public float DeckY => Origin.y;

    public Vector3 World(float x, float y, float z) => new(Origin.x + x, Origin.y + y, Origin.z + z);

    /// <summary>
    /// Rotation that makes a flat, front-facing object readable to someone looking along
    /// <paramref name="viewDirection"/>.
    ///
    /// A <c>TextToy</c>'s readable face points along its LOCAL -Z, so it must be turned to point back
    /// against the viewer's line of sight. <c>LookRotation(d)</c> sends local +Z to <c>d</c>, hence
    /// local -Z to <c>-d</c>, which is exactly the face a viewer looking along <c>d</c> sees.
    ///
    /// Getting this backwards renders the text mirrored rather than invisible, which is easy to miss
    /// until someone reads it in game: the old single-hall room faced its viewers along +Z, so every
    /// label there was correct on <c>Quaternion.identity</c>. The station's gallery is entered from
    /// the opposite side, and reusing identity silently mirrored every label, the wordmark, and the
    /// logo. Always derive the rotation from where the reader actually stands.
    /// </summary>
    public static Quaternion FacingViewer(Vector3 viewDirection) =>
        Quaternion.LookRotation(viewDirection.normalized, Vector3.up);

    /// <summary>
    /// Inclusive axis-aligned containment. Deliberately NOT <c>Bounds.Contains</c>: that is a native
    /// ECall in the dedicated server's UnityEngine, so calling it makes every occupancy rule in this
    /// plugin untestable outside a running game. This is the same test in managed arithmetic.
    /// </summary>
    public static bool ContainsPoint(Bounds bounds, Vector3 point)
    {
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        return Math.Abs(point.x - center.x) <= size.x / 2f &&
            Math.Abs(point.y - center.y) <= size.y / 2f &&
            Math.Abs(point.z - center.z) <= size.z / 2f;
    }

    public Vector3 ZoneCenter(StationZone zone, float y = 0f) => World(zone.CenterX, y, zone.CenterZ);

    /// <summary>Interior volume of a compartment, from the deck to its ceiling.</summary>
    public Bounds InteriorBounds(StationZone zone) => new(
        World(zone.CenterX, zone.CeilingHeight / 2f, zone.CenterZ),
        new Vector3(zone.Width, zone.CeilingHeight, zone.Depth));

    public bool Contains(StationZone zone, Vector3 worldPosition, float verticalSlack = 0.5f)
    {
        Vector3 local = worldPosition - Origin;
        return local.x >= zone.MinX && local.x <= zone.MaxX &&
            local.z >= zone.MinZ && local.z <= zone.MaxZ &&
            local.y >= -verticalSlack && local.y <= zone.CeilingHeight + verticalSlack;
    }

    /// <summary>
    /// The solid panels of one compartment's four faces, with every hatch cut out.
    ///
    /// Hatches on the same face are walked in span order: each clear run between them becomes a
    /// full-height panel, and each hatch contributes the panel above it when this compartment's ceiling
    /// is higher than the hatch. That is also what stops a face shared by two compartments from being
    /// built twice - the narrower compartment's face lies entirely inside the hatch, so it contributes
    /// only its overhead panel, while the wider one contributes the jambs either side.
    ///
    /// Pure geometry on purpose: sealing the station is the one thing here that players fall out of if
    /// it is wrong, so it must be checkable without a running game.
    /// </summary>
    public IReadOnlyList<StationWallSegment> BuildWallSegments(StationZone zone)
    {
        List<StationWallSegment> segments = new();
        AddFace(segments, zone, inConstantXWall: true, plane: zone.MinX, spanMin: zone.MinZ, spanMax: zone.MaxZ);
        AddFace(segments, zone, inConstantXWall: true, plane: zone.MaxX, spanMin: zone.MinZ, spanMax: zone.MaxZ);
        AddFace(segments, zone, inConstantXWall: false, plane: zone.MinZ, spanMin: zone.MinX, spanMax: zone.MaxX);
        AddFace(segments, zone, inConstantXWall: false, plane: zone.MaxZ, spanMin: zone.MinX, spanMax: zone.MaxX);
        return segments;
    }

    private void AddFace(
        List<StationWallSegment> segments,
        StationZone zone,
        bool inConstantXWall,
        float plane,
        float spanMin,
        float spanMax)
    {
        List<StationOpening> hatches = new();
        foreach (StationOpening opening in _openings)
        {
            if (opening.InConstantXWall == inConstantXWall &&
                Math.Abs(opening.Plane - plane) < 0.01f &&
                opening.To > spanMin + 0.01f &&
                opening.From < spanMax - 0.01f)
            {
                hatches.Add(opening);
            }
        }

        hatches.Sort((a, b) => a.From.CompareTo(b.From));

        float cursor = spanMin;
        foreach (StationOpening hatch in hatches)
        {
            float from = Math.Max(spanMin, hatch.From);
            float to = Math.Min(spanMax, hatch.To);
            if (from - cursor > 0.01f)
            {
                segments.Add(new StationWallSegment(inConstantXWall, plane, cursor, from, 0f, zone.CeilingHeight));
            }

            if (zone.CeilingHeight - hatch.Height > 0.01f)
            {
                segments.Add(new StationWallSegment(inConstantXWall, plane, from, to, hatch.Height, zone.CeilingHeight));
            }

            cursor = Math.Max(cursor, to);
        }

        if (spanMax - cursor > 0.01f)
        {
            segments.Add(new StationWallSegment(inConstantXWall, plane, cursor, spanMax, 0f, zone.CeilingHeight));
        }
    }

    /// <summary>
    /// Stands for <paramref name="count"/> SCP displays, back rank first. The centre of the back rank is
    /// deliberately left empty so the wall logo stays visible straight down the arrival aisle; anything
    /// beyond the back rank fills symmetric wall pairs walking back toward the entrance.
    /// </summary>
    public IReadOnlyList<GalleryDisplaySlot> BuildDisplaySlots(int count, float spacing, float standWidth)
    {
        List<GalleryDisplaySlot> slots = new();
        if (count <= 0)
        {
            return slots;
        }

        // The authored models already face +Z and the gallery is entered from +Z, so no yaw is needed.
        Quaternion facing = Quaternion.identity;
        float clampedSpacing = Mathf.Clamp(spacing, 2.4f, 6f);
        float backRankLimit = HubHalfWidth - 1.4f;

        foreach (float x in BackRankOffsets(clampedSpacing, backRankLimit))
        {
            if (slots.Count >= count)
            {
                return slots;
            }

            slots.Add(Slot(x, BackRankZ, standWidth, facing));
        }

        for (float z = SideRankFirstZ; z <= SideRankLastZ + 0.001f; z += SideRankSpacing)
        {
            foreach (float x in new[] { -SideRankX, SideRankX })
            {
                if (slots.Count >= count)
                {
                    return slots;
                }

                slots.Add(Slot(x, z, standWidth, facing));
            }
        }

        return slots;
    }

    /// <summary>How many SCP displays the gallery can stand up before it runs out of authored floor.</summary>
    public int DisplayCapacity(float spacing, float standWidth) =>
        BuildDisplaySlots(int.MaxValue, spacing, standWidth).Count;

    private GalleryDisplaySlot Slot(float x, float z, float standWidth, Quaternion facing) => new(
        World(x, StandHeight, z),
        standWidth,
        StandDepthMeters,
        facing,
        World(x, CoinHeight, z + StandDepthMeters / 2f + CoinStandClearance));

    /// <summary>Back-rank X offsets, mirrored outward from the clear centre aisle.</summary>
    private static IEnumerable<float> BackRankOffsets(float spacing, float limit)
    {
        for (int step = 0; ; step++)
        {
            float x = GalleryAisleHalfWidth + step * spacing;
            if (x > limit)
            {
                yield break;
            }

            yield return -x;
            yield return x;
        }
    }

    private StationZone Add(StationZone zone)
    {
        _zones.Add(zone);
        return zone;
    }
}
