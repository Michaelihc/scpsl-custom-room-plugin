using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles.FirstPersonControl;
using PlaytestHarness.Core;
using UnityEngine;
using WarmupScpSelector;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.PlaytestScenarios;

/// <summary>One landing discovered by sweeping the parkour shaft with downward raycasts.</summary>
public readonly struct DiscoveredLanding
{
    public DiscoveredLanding(Vector3 center, float width, float depth, int samples)
    {
        Center = center;
        Width = width;
        Depth = depth;
        Samples = samples;
    }

    /// <summary>Centre of the landing's top surface, as the world actually reports it.</summary>
    public Vector3 Center { get; }

    public float Width { get; }

    public float Depth { get; }

    /// <summary>How many raycasts landed on this surface. A pad hit by one ray is noise, not a landing.</summary>
    public int Samples { get; }
}

/// <summary>
/// Finds the live warmup station and reads its parkour route out of the WORLD, not out of the plugin.
///
/// The route is discovered by sweeping the shaft with downward raycasts and clustering the collidable
/// surfaces that come back. That is deliberate: it means these scenarios assert on geometry a player
/// can actually stand on, so a landing that spawned without a collider - the classic AdminToy trap -
/// fails discovery instead of quietly passing a coordinate comparison.
///
/// The only thing taken from the plugin is the station's public <see cref="WarmupHallLayout"/>, used
/// as the search volume.
/// </summary>
public static class StationProbe
{
    /// <summary>Horizontal spacing of the discovery sweep. Finer than the narrowest authored landing.</summary>
    private const float SweepStep = 0.25f;

    /// <summary>Surfaces this far above the deck are route landings; anything lower is the deck itself.</summary>
    private const float DeckClearance = 0.3f;

    /// <summary>
    /// Two hits join the same landing when they are this close horizontally and this level. Both bounds
    /// must stay BELOW the smallest gap between real landings, or single-link clustering chains the whole
    /// route into one blob: consecutive pads sit about 0.8 m apart with only a ~0.34 m height difference,
    /// so a 1.4 m / 0.35 m rule merged twenty landings into a single 28 m "pad".
    /// </summary>
    private const float ClusterRadius = 0.4f;
    private const float ClusterHeightTolerance = 0.12f;

    /// <summary>A cluster needs this many hits to count; fewer means a wall edge grazed the sweep.</summary>
    private const int MinimumSamples = 4;

    public static SelectorRoom RequireRoom(ScenarioContext ctx)
    {
        WarmupScpSelectorPlugin? plugin = WarmupScpSelectorPlugin.Instance;
        if (plugin == null)
        {
            throw new RequireException("WarmupScpSelector is not loaded on this port");
        }

        SelectorRoom? room = plugin.WarmupRoom;
        if (room == null || !room.IsSpawned || room.Hall == null)
        {
            throw new RequireException("the warmup station is not standing (is the server past waiting-for-players?)");
        }

        ctx.Info($"[Station] origin={Format(room.Origin)} stands={room.OfferedOptions.Count} " +
            $"aimHatchPrepared={room.AimRangeDoorPrepared} parkourHatchPrepared={room.ParkourDoorPrepared}");
        return room;
    }

    /// <summary>
    /// Sweeps the parkour shaft and returns every collidable landing above the deck, ordered along the
    /// shaft. The route climbs monotonically away from the hatch, so Z order is route order.
    /// </summary>
    public static IReadOnlyList<DiscoveredLanding> DiscoverParkourLandings(ScenarioContext ctx, SelectorRoom room)
    {
        WarmupHallLayout hall = room.Hall!;
        StationZone shaft = hall.ParkourShaft;
        float castFrom = hall.Origin.y + shaft.CeilingHeight - 0.4f;
        float castLength = shaft.CeilingHeight;
        float deckY = hall.Origin.y;

        List<Vector3> hits = new();
        for (float x = shaft.MinX + SweepStep; x < shaft.MaxX; x += SweepStep)
        {
            for (float z = shaft.MinZ + SweepStep; z < shaft.MaxZ; z += SweepStep)
            {
                Vector3 from = hall.World(x, shaft.CeilingHeight - 0.4f, z);
                foreach (RaycastHit hit in Physics.RaycastAll(
                             from, Vector3.down, castLength, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.point.y > deckY + DeckClearance && hit.normal.y >= 0.65f)
                    {
                        hits.Add(hit.point);
                    }
                }
            }
        }

        List<DiscoveredLanding> landings = Cluster(hits);
        landings.Sort((a, b) => a.Center.z.CompareTo(b.Center.z));

        ctx.Info($"[Station] shaft sweep: {hits.Count} surface hits -> {landings.Count} landings " +
            $"from y={(landings.Count > 0 ? landings[0].Center.y - deckY : 0f):0.##} " +
            $"to y={(landings.Count > 0 ? landings[landings.Count - 1].Center.y - deckY : 0f):0.##}");
        foreach (DiscoveredLanding landing in landings)
        {
            ctx.Info($"[Station]   landing local=({landing.Center.x - hall.Origin.x:0.##}, " +
                $"{landing.Center.y - deckY:0.##}, {landing.Center.z - hall.Origin.z:0.##}) " +
                $"{landing.Width:0.##}x{landing.Depth:0.##}m ({landing.Samples} hits)");
        }

        _ = castFrom;
        return landings;
    }

    /// <summary>Groups raycast hits into landings: same height band, touching horizontally.</summary>
    private static List<DiscoveredLanding> Cluster(List<Vector3> hits)
    {
        List<List<Vector3>> clusters = new();
        foreach (Vector3 hit in hits)
        {
            List<Vector3>? match = null;
            foreach (List<Vector3> cluster in clusters)
            {
                foreach (Vector3 member in cluster)
                {
                    if (Mathf.Abs(member.y - hit.y) <= ClusterHeightTolerance &&
                        new Vector2(member.x - hit.x, member.z - hit.z).sqrMagnitude <= ClusterRadius * ClusterRadius)
                    {
                        match = cluster;
                        break;
                    }
                }

                if (match != null)
                {
                    break;
                }
            }

            if (match == null)
            {
                clusters.Add(new List<Vector3> { hit });
            }
            else
            {
                match.Add(hit);
            }
        }

        List<DiscoveredLanding> landings = new();
        foreach (List<Vector3> cluster in clusters)
        {
            if (cluster.Count < MinimumSamples)
            {
                continue;
            }

            float minX = cluster.Min(p => p.x);
            float maxX = cluster.Max(p => p.x);
            float minZ = cluster.Min(p => p.z);
            float maxZ = cluster.Max(p => p.z);
            landings.Add(new DiscoveredLanding(
                new Vector3((minX + maxX) / 2f, cluster.Max(p => p.y), (minZ + maxZ) / 2f),
                maxX - minX + SweepStep,
                maxZ - minZ + SweepStep,
                cluster.Count));
        }

        return landings;
    }

    /// <summary>Confirms there is walkable ground under a point, the way a settling player would find out.</summary>
    public static bool HasGroundUnder(Vector3 point, float maxDrop, out float groundY, out string what)
    {
        groundY = 0f;
        what = "miss";
        RaycastHit[] all = Physics.RaycastAll(
            point + Vector3.up * 0.3f, Vector3.down, maxDrop + 0.3f, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore);
        if (all.Length == 0)
        {
            return false;
        }

        RaycastHit closest = all.OrderBy(hit => hit.distance).First();
        groundY = closest.point.y;
        what = closest.collider != null ? closest.collider.name : "unnamed";
        return closest.normal.y >= 0.65f;
    }

    public static string Format(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
}
