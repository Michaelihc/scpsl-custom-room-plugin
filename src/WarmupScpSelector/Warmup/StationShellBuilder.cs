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

    /// <summary>Deck slabs overlap their neighbours slightly so tiled compartments never show a seam.</summary>
    private const float DeckOverlap = 0.06f;

    private const float LightSpacing = 11f;
    private const float LightIntensity = 24f;
    private const float LightRange = 18f;

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
            Vector3 position = segment.InConstantXWall
                ? _layout.World(segment.Plane, segment.MidY, segment.Center)
                : _layout.World(segment.Center, segment.MidY, segment.Plane);
            Vector3 size = segment.InConstantXWall
                ? new Vector3(WallThickness, segment.Height, segment.Span)
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

        // Jambs either side plus a lintel, all inside the clear opening so they never block movement.
        foreach (float edge in new[] { hatch.From + FrameThickness / 2f, hatch.To - FrameThickness / 2f })
        {
            Vector3 position = hatch.InConstantXWall
                ? _layout.World(hatch.Plane, hatch.Height / 2f, edge)
                : _layout.World(edge, hatch.Height / 2f, hatch.Plane);
            Vector3 size = hatch.InConstantXWall
                ? new Vector3(WallThickness + 0.08f, hatch.Height, FrameThickness)
                : new Vector3(FrameThickness, hatch.Height, WallThickness + 0.08f);
            AddBox(position, size, StationPalette.Frame, collidable: false);
        }

        Vector3 lintelPosition = hatch.InConstantXWall
            ? _layout.World(hatch.Plane, hatch.Height - FrameThickness / 2f, center)
            : _layout.World(center, hatch.Height - FrameThickness / 2f, hatch.Plane);
        Vector3 lintelSize = hatch.InConstantXWall
            ? new Vector3(WallThickness + 0.08f, FrameThickness, span)
            : new Vector3(span, FrameThickness, WallThickness + 0.08f);
        AddBox(lintelPosition, lintelSize, StationPalette.Frame, collidable: false);

        // Amber threshold striping on the deck, the way a real airlock sill is painted.
        Vector3 sillPosition = hatch.InConstantXWall
            ? _layout.World(hatch.Plane, SeamProud + 0.01f, center)
            : _layout.World(center, SeamProud + 0.01f, hatch.Plane);
        Vector3 sillSize = hatch.InConstantXWall
            ? new Vector3(0.5f, 0.03f, span)
            : new Vector3(span, 0.03f, 0.5f);
        AddBox(sillPosition, sillSize, StationPalette.Caution, collidable: false);
    }

    // ---- Wayfinding --------------------------------------------------------------------------

    /// <summary>
    /// A single teal guide strip runs the arrival route (gallery aisle to hub) and then branches to each
    /// activity compartment, so a player who does nothing but follow the floor still finds every room.
    /// </summary>
    private void BuildWayfinding()
    {
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, -35f), new Vector3(0.35f, 0.03f, 21f));
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, -8f), new Vector3(0.35f, 0.03f, 35f));
        AddGuideStrip(_layout.World(17f, SeamProud + 0.02f, 0f), new Vector3(24f, 0.03f, 0.35f));
        AddGuideStrip(_layout.World(-16f, SeamProud + 0.02f, 0f), new Vector3(10f, 0.03f, 0.35f));
        AddGuideStrip(_layout.World(0f, SeamProud + 0.02f, 30f), new Vector3(0.35f, 0.03f, 24f));

        // Each sign is read by someone in the hub walking TOWARD that compartment, so its facing comes
        // from that approach direction. An earlier boolean flag got all four backwards and rendered the
        // signage mirrored.
        AddHatchSign(_layout.World(0f, 3.9f, WarmupHallLayout.HubHalfDepth - 0.45f), _layout.ParkourShaft, Vector3.forward);
        AddHatchSign(_layout.World(0f, 3.9f, -WarmupHallLayout.HubHalfDepth + 0.45f), _layout.Gallery, Vector3.back);
        AddHatchSign(_layout.World(WarmupHallLayout.HubHalfWidth - 0.45f, 3.9f, 0f), _layout.AimBay, Vector3.right);
        AddHatchSign(_layout.World(-WarmupHallLayout.HubHalfWidth + 0.45f, 3.9f, 0f), _layout.ObservationDeck, Vector3.left);
    }

    private void AddGuideStrip(Vector3 center, Vector3 size) =>
        AddBox(center, size, StationPalette.Guide, collidable: false);

    private void AddHatchSign(Vector3 position, StationZone destination, Vector3 approachDirection)
    {
        string text = _chinese
            ? $"<color=#1B87C9>{destination.SignCn}</color>"
            : $"<color=#1B87C9>{destination.SignEn}</color>";
        AddLabel(position, text, WarmupHallLayout.FacingViewer(approachDirection), 420f);
    }

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
