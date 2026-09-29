using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WarmupScpSelector.Activities.Parkour;
using WarmupScpSelector.Export;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.Import;

/// <summary>
/// Recovers the Pulse Line's landings from an authored schematic, so a route someone rearranged in the
/// map editor drives the real gates instead of being overridden by the generated one.
///
/// Landings are identified by what makes a landing a landing - a collidable, standable, roughly flat
/// surface inside the shaft, above the deck and clear of the overhead - rather than by name, because the
/// map editor does not preserve names or the exported <c>marker_*</c> anchors.
///
/// Two traps this has to survive, both hit on real files:
///
/// - DOUBLE COUNTING. Every pad is drawn as a slab plus a thin emissive lip a centimetre above it. Naive
///   extraction returns each landing twice, which turns one real hop into a pair of 5 cm hops and makes
///   a hard course look trivial. Candidates that overlap in plan and sit at nearly the same height are
///   therefore collapsed to the largest one.
/// - CEILING PANELS. The shaft's overhead is also a big flat collidable slab. Including it produced a
///   9.9 m "hop" that no jump can make. Anything within <see cref="OverheadClearance"/> of the ceiling
///   is not a landing.
/// </summary>
internal static class AuthoredParkourRoute
{
    /// <summary>A landing has to be at least this far above the deck to be part of the route.</summary>
    private const float MinimumHeight = 0.35f;

    /// <summary>Surfaces this close to the overhead are structure, not landings.</summary>
    private const float OverheadClearance = 2f;

    /// <summary>Smallest footprint a player can be expected to land on.</summary>
    private const float MinimumFootprint = 0.6f;

    /// <summary>Thicker than this is a wall or a column, not a pad.</summary>
    private const float MaximumThickness = 1.2f;

    /// <summary>Two surfaces this close in plan and height are the same landing drawn twice.</summary>
    private const float DuplicateRadius = 0.7f;
    private const float DuplicateHeight = 0.25f;

    /// <summary>Fewer than this and the file plainly does not contain a route.</summary>
    private const int MinimumLandings = 6;

    /// <summary>
    /// Ordered landings in world space, first (nearest the hatch) to last. Empty when the asset does not
    /// describe a usable route, which the caller treats as "generate one instead".
    /// </summary>
    public static IReadOnlyList<ParkourPlatform> Extract(StationAsset asset, WarmupHallLayout hall)
    {
        StationZone shaft = hall.ParkourShaft;
        float ceiling = shaft.CeilingHeight;
        List<ParkourPlatform> candidates = new();

        foreach (StationAssetBlock block in asset.Blocks)
        {
            if (block.BlockType != SchematicBlockType.Primitive)
            {
                continue;
            }

            // Collidable only: the lit traces between pads are visible-but-not-solid and must never be
            // mistaken for somewhere to stand.
            if ((block.Integer("PrimitiveFlags", 0) & 2) == 0)
            {
                continue;
            }

            Vector3 local = block.ApproximateRootPosition;
            Vector3 size = new(Mathf.Abs(block.Scale.x), Mathf.Abs(block.Scale.y), Mathf.Abs(block.Scale.z));
            if (local.x < shaft.MinX || local.x > shaft.MaxX || local.z < shaft.MinZ || local.z > shaft.MaxZ)
            {
                continue;
            }

            float top = local.y + size.y / 2f;
            if (top < MinimumHeight || top > ceiling - OverheadClearance)
            {
                continue;
            }

            if (size.x < MinimumFootprint || size.z < MinimumFootprint || size.y > MaximumThickness)
            {
                continue;
            }

            candidates.Add(new ParkourPlatform(hall.Origin + local, size));
        }

        List<ParkourPlatform> merged = Collapse(candidates);
        merged.Sort((a, b) => a.Center.z.CompareTo(b.Center.z));
        return merged.Count >= MinimumLandings ? merged : Array.Empty<ParkourPlatform>();
    }

    /// <summary>Collapses each pad-plus-lip pair (and any other coincident surfaces) to its largest slab.</summary>
    private static List<ParkourPlatform> Collapse(List<ParkourPlatform> candidates)
    {
        List<ParkourPlatform> merged = new();
        foreach (ParkourPlatform candidate in candidates.OrderByDescending(p => p.Size.x * p.Size.z))
        {
            bool duplicate = merged.Any(kept =>
                Mathf.Abs(kept.SurfaceY - candidate.SurfaceY) <= DuplicateHeight &&
                new Vector2(kept.Center.x - candidate.Center.x, kept.Center.z - candidate.Center.z).magnitude <= DuplicateRadius);
            if (!duplicate)
            {
                merged.Add(candidate);
            }
        }

        return merged;
    }
}
