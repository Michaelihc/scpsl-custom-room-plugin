using System.Collections.Generic;
using PlayerRoles;
using PlaytestHarness.Actors;
using PlaytestHarness.Core;
using UnityEngine;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.PlaytestScenarios;

/// <summary>
/// Walks the warmup station the way a player would: is there deck under every compartment, do the
/// hatches actually open into the next room, and does a dummy dropped at the spawn point stay put?
///
/// This deliberately asserts on world state - raycasts and a settling dummy - rather than on the
/// plugin's own layout numbers. A room that is correct in code and missing a floor toy in game is the
/// failure mode worth catching, and only the world can report it.
/// </summary>
public sealed class StationShellScenario : Scenario
{
    /// <summary>How far below a probe point ground still counts as "the deck is here".</summary>
    private const float DeckSearchDrop = 2.5f;

    public override string Name => "warmup-station";

    public override string[] Aliases => ["warmup-shell"];

    public override string[] Suites => ["warmup"];

    public override string Description =>
        "Raycast-walks every warmup station compartment for deck, walls, and open hatches, then settles a dummy at spawn.";

    public override FidelityRange Supported => new(Fidelity.Quick, Fidelity.EndToEnd);

    public override IEnumerator<float> Run(ScenarioContext ctx)
    {
        // Same structural fact as the Pulse Line scenario: the station floats outside the room graph, so
        // an actor standing in it is legitimately "in no room".
        ctx.ExpectViolation("the warmup station is a floating room outside the facility room graph",
            "BoundsMonitor", "arrival", count: 8, withinSeconds: 400f);

        SelectorRoom room = StationProbe.RequireRoom(ctx);
        WarmupHallLayout hall = room.Hall!;

        // ---- every compartment has deck you can stand on ------------------------------------
        foreach (StationZone zone in hall.Zones)
        {
            foreach (Vector2 sample in InteriorSamples(zone))
            {
                Vector3 point = hall.World(sample.x, 0.6f, sample.y);
                ctx.Require(
                    StationProbe.HasGroundUnder(point, DeckSearchDrop, out float groundY, out string what),
                    $"{zone.Id} has no walkable deck at {StationProbe.Format(point)} (hit {what})");
                ctx.Require(Mathf.Abs(groundY - hall.Origin.y) < 0.6f,
                    $"{zone.Id} deck at {StationProbe.Format(point)} is at y={groundY:0.##}, expected {hall.Origin.y:0.##}");
            }

            ctx.Info($"[Station] {zone.Id}: deck confirmed across {zone.Width:0.#}x{zone.Depth:0.#}m");
        }

        // ---- the shell is closed: a ray fired outward from the middle of each arm hits a wall --
        foreach ((StationZone zone, Vector3 direction) in new[]
        {
            (hall.AimBay, Vector3.right),
            (hall.ObservationDeck, Vector3.left),
            (hall.Gallery, Vector3.back),
            (hall.ParkourShaft, Vector3.forward),
        })
        {
            Vector3 from = hall.ZoneCenter(zone, 1.5f);
            ctx.Require(Physics.Raycast(from, direction, out RaycastHit hit, 90f),
                $"{zone.Id} is open to the void along {direction}");
            ctx.Info($"[Station] {zone.Id} outer wall at {hit.distance:0.##}m ({hit.collider?.name ?? "unnamed"})");
        }

        // ---- hatches are open, and only where they should be --------------------------------
        AssertHatchOpen(ctx, hall, hall.World(WarmupHallLayout.HubHalfWidth - 2f, 1.2f, 0f), Vector3.right, "aim bay");
        AssertHatchOpen(ctx, hall, hall.World(-WarmupHallLayout.HubHalfWidth + 2f, 1.2f, 0f), Vector3.left, "observation deck");
        AssertHatchOpen(ctx, hall, hall.World(0f, 1.2f, -WarmupHallLayout.HubHalfDepth + 2f), Vector3.back, "gallery access");

        // The hub's own side walls, beyond the ends of the arm hatches, must still be solid. Fire from
        // just inside the wall line, not from the hub centre: the hub is 11 m to its own side wall.
        ctx.Require(
            Physics.Raycast(
                hall.World(WarmupHallLayout.HubHalfWidth - 2f, 1.2f, WarmupHallLayout.ArmHalfDepth + 4f),
                Vector3.right, out RaycastHit sideWall, 4f),
            "the hub's side wall is missing beyond the end of the aim bay hatch");
        ctx.Info($"[Station] hub side wall beside the hatch at {sideWall.distance:0.##}m");

        // ---- the SCP draft is actually standing ---------------------------------------------
        ctx.Require(room.OfferedOptions.Count > 0, "no SCP options were built into the gallery");
        ctx.Require(room.CoinRoles.Count == room.OfferedOptions.Count,
            $"{room.OfferedOptions.Count} stands but {room.CoinRoles.Count} selection coins");

        // ---- a dummy dropped at the spawn point stays on the deck ---------------------------
        Actor arrival = ctx.SpawnActor("arrival", RoleTypeId.Tutorial, SpawnSpec.At(room.SpawnPosition));
        yield return arrival.WaitReady();
        yield return arrival.Settle();

        float drop = room.SpawnPosition.y - arrival.Position.y;
        ctx.Require(drop < 1.5f,
            $"a dummy fell {drop:0.##}m through the gallery deck at the spawn point");
        ctx.Require(hall.Contains(hall.Gallery, arrival.Position, 2f),
            $"a dummy spawned at the arrival point ended up outside the gallery at {StationProbe.Format(arrival.Position)}");

        yield return arrival.Soak(3f);
        ctx.Info($"[Station] arrival settled at {StationProbe.Format(arrival.Position)} (dropped {drop:0.##}m)");
    }

    private static void AssertHatchOpen(ScenarioContext ctx, WarmupHallLayout hall, Vector3 from, Vector3 direction, string label)
    {
        // Fire from inside the hub, through the hatch, into the far compartment. A sealed or half-built
        // hatch stops the ray within a couple of metres.
        bool blocked = Physics.Raycast(from, direction, out RaycastHit hit, 6f);
        ctx.Require(!blocked || hit.distance > 5.5f,
            $"the {label} hatch is blocked {hit.distance:0.##}m in ({hit.collider?.name ?? "unnamed"})");
        ctx.Info($"[Station] {label} hatch is clear");
        _ = hall;
    }

    /// <summary>A handful of interior points per compartment: centre, and inset from each corner.</summary>
    private static IEnumerable<Vector2> InteriorSamples(StationZone zone)
    {
        const float inset = 1.2f;
        yield return new Vector2(zone.CenterX, zone.CenterZ);
        yield return new Vector2(zone.MinX + inset, zone.MinZ + inset);
        yield return new Vector2(zone.MaxX - inset, zone.MinZ + inset);
        yield return new Vector2(zone.MinX + inset, zone.MaxZ - inset);
        yield return new Vector2(zone.MaxX - inset, zone.MaxZ - inset);

        // Long compartments also get samples down their length, where a missing slab would hide.
        if (zone.Depth > 24f)
        {
            for (float z = zone.MinZ + 6f; z < zone.MaxZ - 3f; z += 8f)
            {
                yield return new Vector2(zone.CenterX, z);
            }
        }

        if (zone.Width > 24f)
        {
            for (float x = zone.MinX + 6f; x < zone.MaxX - 3f; x += 8f)
            {
                yield return new Vector2(x, zone.CenterZ);
            }
        }
    }
}
