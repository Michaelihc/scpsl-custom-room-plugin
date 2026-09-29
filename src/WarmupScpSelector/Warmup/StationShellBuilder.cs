using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// Emits the pressurised shell of a <see cref="WarmupHallLayout"/>: deck, overhead, bulkheads with
/// hatches carved out, hatch frames, threshold striping, wayfinding, and deck lighting.
///
/// Walls are produced per compartment with the shared <see cref="StationOpening"/> rectangles
/// subtracted, which is what keeps a shared face from being built twice: the narrower compartment's
/// face is entirely inside the hatch, so it contributes only the panel above the hatch (if its ceiling
/// is higher), while the wider compartment contributes the jambs either side.
///
/// Everything it creates is appended to the caller's toy list, so the owner keeps a single teardown path.
/// </summary>
internal sealed class StationShellBuilder
{
    private const float DeckThickness = 0.4f;
    private const float WallThickness = 0.3f;
    private const float OverheadThickness = 0.3f;
    private const float SeamProud = 0.02f;
    private const float RibHeight = 0.16f;
    private const float RibY = 2.55f;
    private const float FrameThickness = 0.14f;

    /// <summary>
    /// How far a constant-X bulkhead pokes up into the overhead slab above it.
    ///
    /// Compartment faces are built as boxes centred on their own plane, so at every corner two
    /// perpendicular walls cross in a 0.3 x 0.3 column - and both their top faces sit exactly on the
    /// ceiling plane. That is a 15 cm square of z-fighting at the top of every corner in the station,
    /// which reads in game as a small grey cube embedded in the wall. Lifting ONE of the two families
    /// (the constant-X walls) parts them; the extra 2 mm is buried inside the overhead, so it opens no
    /// gap the way lowering the other family would.
    ///
    /// TWO CENTIMETRES, not two millimetres. The depth buffer's error at the far end of a 100 m station
    /// is on the order of millimetres, so a hairline offset still fights from across the room even
    /// though it is no longer exactly coplanar - which is how a scan that only looked for exact
    /// coplanarity kept passing while the flicker was still there in game. The overhead slab is 0.3 m
    /// thick, so 2 cm still vanishes inside it.
    /// </summary>
    private const float WallTopLift = 0.02f;

    // Hatch signage backplate. The panel hangs clear of the bulkhead behind it and the frame clear of the
    // panel, so no two of the three share a plane.
    private const float SignPlateThickness = 0.04f;
    private const float SignPanelDepth = 0.12f;
    private const float SignFrameGap = 0.045f;
    private const float SignPanelWidth = 3.2f;
    private const float SignPanelHeight = 1.05f;
    private const float SignFrameWidth = 3.5f;
    private const float SignFrameHeight = 1.3f;

    /// <summary>
    /// Deck slabs ABUT their neighbours exactly - they must not overlap.
    ///
    /// This used to be 0.06 m of overlap "so tiled compartments never show a seam", and it bought a far
    /// worse artifact than the seam it was avoiding: two slabs' top faces on one plane across the width
    /// of every doorway, plus the same band on the station's underside. Staggering them in Y instead only
    /// moved the problem, because every piece of code-built furniture is placed on y = 0 and would then
    /// float above its own floor. The overhead slabs have always abutted with no overlap and no seam;
    /// the deck does the same.
    /// </summary>
    private const float DeckOverlap = 0f;

    private const float LightSpacing = 11f;
    // sqrt(3) on each setting gives a combined multiplier of 3, not 9.
    private const float LightIntensity = 24f * 1.7320508f;
    private const float LightRange = 18f * 1.7320508f;

    private readonly WarmupHallLayout _layout;
    private readonly List<AdminToy> _sink;
    private readonly bool _chinese;

    public StationShellBuilder(WarmupHallLayout layout, List<AdminToy> sink, bool chinese)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _chinese = chinese;
    }

    public void Build()
    {
        foreach (StationZone zone in _layout.Zones)
        {
            BuildZone(zone);
        }

        foreach (StationOpening opening in _layout.Openings)
        {
            BuildHatchFrame(opening);
        }

        BuildWayfinding();
    }

    /// <summary>
    /// The pressurised shell of one compartment: deck, overhead, walls, lighting.
    ///
    /// Exposed on its own because an authored schematic supplies most of the station but NEVER the
    /// parkour shaft - that compartment stays code-owned so its landings and its gates cannot drift
    /// apart. Without building the shaft's shell here too, skipping the authored shaft blocks left it
    /// with no floor at all and players fell straight out of the station.
    /// </summary>
    public void BuildZone(StationZone zone)
    {
        BuildDeck(zone);
        BuildOverhead(zone);
        BuildWalls(zone);
        BuildLighting(zone);
    }

    /// <summary>Frames and threshold striping for the hatches that open into the given compartment.</summary>
    public void BuildHatchesFor(StationZone zone)
    {
        foreach (StationOpening opening in _layout.Openings)
        {
            bool touchesZone = opening.InConstantXWall
                ? Mathf.Abs(opening.Plane - zone.MinX) < 0.01f || Mathf.Abs(opening.Plane - zone.MaxX) < 0.01f
                : Mathf.Abs(opening.Plane - zone.MinZ) < 0.01f || Mathf.Abs(opening.Plane - zone.MaxZ) < 0.01f;
            if (touchesZone)
            {
                BuildHatchFrame(opening);
            }
        }
    }

    // ---- Compartment structure -----------------------------------------------------------------

    private void BuildDeck(StationZone zone)
    {
        float width = zone.Width + DeckOverlap * 2f;
        float depth = zone.Depth + DeckOverlap * 2f;

        AddBox(_layout.World(zone.CenterX, -DeckThickness / 2f, zone.CenterZ),
            new Vector3(width, DeckThickness, depth), StationPalette.Deck, collidable: true);

        // A slightly lighter inset plate reads as deck panelling and gives the floor a visible edge.
        AddBox(_layout.World(zone.CenterX, SeamProud, zone.CenterZ),
            new Vector3(zone.Width - 0.8f, 0.04f, zone.Depth - 0.8f), StationPalette.DeckPanel, collidable: false);
    }

    private void BuildOverhead(StationZone zone)
    {
        AddBox(_layout.World(zone.CenterX, zone.CeilingHeight + OverheadThickness / 2f, zone.CenterZ),
            new Vector3(zone.Width, OverheadThickness, zone.Depth), StationPalette.Overhead, collidable: true);
    }

    /// <summary>Spawns the panels <see cref="WarmupHallLayout.BuildWallSegments"/> already worked out.</summary>
    private void BuildWalls(StationZone zone)
    {
        foreach (StationWallSegment segment in _layout.BuildWallSegments(zone))
        {
            // Constant-X walls own the corner: they reach WallTopLift higher so their top face is not on
            // the ceiling plane the constant-Z walls share with them.
            float lift = segment.InConstantXWall ? WallTopLift : 0f;
            Vector3 position = segment.InConstantXWall
                ? _layout.World(segment.Plane, segment.MidY + lift / 2f, segment.Center)
                : _layout.World(segment.Center, segment.MidY, segment.Plane);
            Vector3 size = segment.InConstantXWall
                ? new Vector3(WallThickness, segment.Height + lift, segment.Span)
                : new Vector3(segment.Span, segment.Height, WallThickness);
            AddBox(position, size, StationPalette.Bulkhead, collidable: true);

            // One recessed rib per panel breaks up an otherwise flat bulkhead without a per-panel toy grid.
            if (segment.BottomY <= RibY && segment.TopY >= RibY + RibHeight && segment.Span > 0.5f)
            {
                Vector3 ribPosition = segment.InConstantXWall
                    ? _layout.World(segment.Plane, RibY, segment.Center)
                    : _layout.World(segment.Center, RibY, segment.Plane);
                Vector3 ribSize = segment.InConstantXWall
                    ? new Vector3(WallThickness + 0.04f, RibHeight, segment.Span - 0.3f)
                    : new Vector3(segment.Span - 0.3f, RibHeight, WallThickness + 0.04f);
                AddBox(ribPosition, ribSize, StationPalette.BulkheadRib, collidable: false);
            }
        }
    }

    // ---- Hatches -----------------------------------------------------------------------------

    private void BuildHatchFrame(StationOpening hatch)
    {
        float center = (hatch.From + hatch.To) / 2f;
        float span = hatch.To - hatch.From;

        // Jambs either side plus a lintel, all inside the clear opening so they never block movement. The
        // jambs stop UNDER the lintel rather than running past it: full height put their top faces on the
        // lintel's plane and their sides on its ends, which flickered in the top corners of every doorway.
        // Inset by half a wall so the jamb stands in the clear opening. Sitting at the opening's very
        // edge put it INSIDE the perpendicular compartment wall, with its outer face 1 cm off that
        // wall's own face over most of a square metre - grey against white, and 1 cm is still inside the
        // depth buffer's error from across the station. This is the flicker that survived three passes.
        float jambInset = WallThickness / 2f + FrameThickness / 2f;
        float jambHeight = hatch.Height - FrameThickness;
        foreach (float edge in new[] { hatch.From + jambInset, hatch.To - jambInset })
        {
            Vector3 position = hatch.InConstantXWall
                ? _layout.World(hatch.Plane, jambHeight / 2f, edge)
                : _layout.World(edge, jambHeight / 2f, hatch.Plane);
            Vector3 size = hatch.InConstantXWall
                ? new Vector3(WallThickness + 0.08f, jambHeight, FrameThickness)
                : new Vector3(FrameThickness, jambHeight, WallThickness + 0.08f);
            AddBox(position, size, StationPalette.Frame, collidable: false);
        }

        // Dropped by WallTopLift so the lintel's top clears the ceiling plane the compartment walls on
        // either side of the doorway share. It hangs inside the opening, so the 2 mm is invisible.
        float lintelMidY = hatch.Height - FrameThickness / 2f - WallTopLift;
        Vector3 lintelPosition = hatch.InConstantXWall
            ? _layout.World(hatch.Plane, lintelMidY, center)
            : _layout.World(center, lintelMidY, hatch.Plane);
        float lintelSpan = span - WallThickness;
        Vector3 lintelSize = hatch.InConstantXWall
            ? new Vector3(WallThickness + 0.08f, FrameThickness, lintelSpan)
            : new Vector3(lintelSpan, FrameThickness, WallThickness + 0.08f);
        AddBox(lintelPosition, lintelSize, StationPalette.Frame, collidable: false);

        // Amber threshold striping on the deck, the way a real airlock sill is painted.
        Vector3 sillPosition = hatch.InConstantXWall
            ? _layout.World(hatch.Plane, SeamProud + 0.01f, center)
            : _layout.World(center, SeamProud + 0.01f, hatch.Plane);
        // Short of the jambs at both ends: run to the full opening and the sill's ends land on the jambs'
        // outer faces.
        float sillSpan = span - WallThickness - FrameThickness * 2f;
        Vector3 sillSize = hatch.InConstantXWall
            ? new Vector3(0.5f, 0.03f, sillSpan)
            : new Vector3(sillSpan, 0.03f, 0.5f);
        AddBox(sillPosition, sillSize, StationPalette.Caution, collidable: false);
    }

    // ---- Wayfinding --------------------------------------------------------------------------

    /// <summary>
    /// A single teal guide strip runs the arrival route (gallery aisle to hub) and then branches to each
    /// activity compartment, so a player who does nothing but follow the floor still finds every room.
    /// </summary>
    private void BuildWayfinding()
    {
        // These two meet at the connector mouth (z = -25.5) and must ABUT there. Overlapping them left a
        // metre of strip with two coplanar top faces, which flickers all the more for being a saturated
        // colour on a white deck.
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, -35.5f), new Vector3(0.35f, 0.03f, 20f));
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, -8f), new Vector3(0.35f, 0.03f, 35f));
        AddGuideStrip(_layout.World(17f, SeamProud + 0.02f, 0f), new Vector3(24f, 0.03f, 0.35f));
        AddGuideStrip(_layout.World(-16f, SeamProud + 0.02f, 0f), new Vector3(10f, 0.03f, 0.35f));
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, 30f), new Vector3(0.35f, 0.03f, 24f));

        foreach (StationZone destination in _layout.Zones)
        {
            BuildHatchSign(destination);
        }
    }

    private void AddGuideStrip(Vector3 center, Vector3 size) =>
        AddBox(center, size, StationPalette.Guide, collidable: false);

    /// <summary>
    /// The signed doorway into one compartment: a framed dark plate with the destination's name on it.
    ///
    /// Exposed per compartment because an authored station supplies its own signage for every
    /// compartment EXCEPT the code-owned Aim Bay - whose sign sits inside the skipped volume, so without
    /// this the one doorway a player is meant to be drawn to was the only unlabelled one.
    ///
    /// Each sign is read by someone in the hub walking TOWARD that compartment, so its facing comes from
    /// that approach direction. An earlier boolean flag got all four backwards and rendered the signage
    /// mirrored.
    /// </summary>
    public void BuildHatchSign(StationZone destination)
    {
        if (!TryHatchSignAnchor(destination, out Vector3 position, out Vector3 approach))
        {
            return; // the hub itself has no doorway of its own to sign
        }

        // Cyan on a white bulkhead has almost no contrast; the plate is what makes the sign read from
        // across the hub, and it reuses the gallery's frame-behind-panel treatment.
        AddBox(position + approach * (SignPanelDepth + SignFrameGap), SignPlateSize(approach, SignFrameWidth, SignFrameHeight),
            StationPalette.Frame, collidable: false);
        AddBox(position + approach * SignPanelDepth, SignPlateSize(approach, SignPanelWidth, SignPanelHeight),
            StationPalette.DisplayPanel, collidable: false);

        string text = _chinese
            ? $"<color=#1B87C9>{destination.SignCn}</color>"
            : $"<color=#1B87C9>{destination.SignEn}</color>";
        AddLabel(position, text, WarmupHallLayout.FacingViewer(approach), 420f);
    }

    /// <summary>Where a compartment's sign hangs, and which way someone reading it is walking.</summary>
    private bool TryHatchSignAnchor(StationZone destination, out Vector3 position, out Vector3 approach)
    {
        const float y = 3.9f;
        const float inset = 0.45f;
        if (destination == _layout.ParkourShaft)
        {
            position = _layout.World(0f, y, WarmupHallLayout.HubHalfDepth - inset);
            approach = Vector3.forward;
        }
        else if (destination == _layout.Gallery)
        {
            position = _layout.World(0f, y, -WarmupHallLayout.HubHalfDepth + inset);
            approach = Vector3.back;
        }
        else if (destination == _layout.AimBay)
        {
            position = _layout.World(WarmupHallLayout.HubHalfWidth - inset, y, 0f);
            approach = Vector3.right;
        }
        else if (destination == _layout.ObservationDeck)
        {
            position = _layout.World(-WarmupHallLayout.HubHalfWidth + inset, y, 0f);
            approach = Vector3.left;
        }
        else
        {
            position = Vector3.zero;
            approach = Vector3.zero;
            return false;
        }

        return true;
    }

    /// <summary>A flat plate facing back down the approach: thin on that axis, sized on the other two.</summary>
    private static Vector3 SignPlateSize(Vector3 approach, float width, float height) =>
        Mathf.Abs(approach.x) > 0.5f
            ? new Vector3(SignPlateThickness, height, width)
            : new Vector3(width, height, SignPlateThickness);

    // ---- Lighting ----------------------------------------------------------------------------

    /// <summary>
    /// Deck lighting on a coarse grid. Lights stay near-white and deliberately few: the SCP models are
    /// muted primitives and a dense tinted grid both washes them out and costs network toys.
    /// </summary>
    private void BuildLighting(StationZone zone)
    {
        float lightY = Mathf.Min(zone.CeilingHeight - 0.6f, 4.4f);
        foreach (float x in GridPositions(zone.MinX, zone.MaxX))
        {
            foreach (float z in GridPositions(zone.MinZ, zone.MaxZ))
            {
                AddLight(_layout.World(x, lightY, z), LightIntensity, LightRange);
            }
        }
    }

    /// <summary>Evenly spaced interior samples with a hard minimum of one (the compartment centre).</summary>
    private static IEnumerable<float> GridPositions(float min, float max)
    {
        float length = max - min;
        int count = Mathf.Max(1, Mathf.RoundToInt(length / LightSpacing));
        float step = length / (count + 1);
        for (int i = 1; i <= count; i++)
        {
            yield return min + step * i;
        }
    }

    // ---- Primitives --------------------------------------------------------------------------

    public PrimitiveObjectToy AddBox(Vector3 center, Vector3 size, Color color, bool collidable)
    {
        PrimitiveObjectToy toy = PrimitiveObjectToy.Create(center, Quaternion.identity, size, networkSpawn: false);
        _sink.Add(toy); // track before configure/spawn so a throw mid-setup is still torn down
        toy.Type = PrimitiveType.Cube;
        toy.Color = color;
        toy.Flags = collidable ? PrimitiveFlags.Visible | PrimitiveFlags.Collidable : PrimitiveFlags.Visible;
        toy.IsStatic = true;
        toy.Spawn();
        return toy;
    }

    public void AddLight(Vector3 center, float intensity, float range)
    {
        LightSourceToy light = LightSourceToy.Create(center, Quaternion.identity, Vector3.one, networkSpawn: false);
        _sink.Add(light);
        light.Color = StationPalette.DeckLight;
        light.Intensity = intensity;
        light.Range = range;
        light.Type = LightType.Point;
        light.ShadowType = LightShadows.None;
        light.IsStatic = true;
        light.Spawn();
    }

    public void AddLabel(Vector3 center, string markup, Quaternion facing, float width, float scale = 0.18f)
    {
        TextToy label = TextToy.Create(center, facing, Vector3.one * scale, networkSpawn: false);
        _sink.Add(label);
        label.TextFormat = "<align=center><b>" + markup + "</b></align>";
        label.DisplaySize = new Vector2(width, 48f);
        label.IsStatic = true;
        label.Spawn();
    }
}
