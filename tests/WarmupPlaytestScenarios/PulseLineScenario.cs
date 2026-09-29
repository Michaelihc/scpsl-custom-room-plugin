using System.Collections.Generic;
using System.Linq;
using PlayerRoles;
using PlaytestHarness.Actors;
using PlaytestHarness.Core;
using UnityEngine;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.PlaytestScenarios;

/// <summary>
/// Proves the Pulse Line is a real course: a dummy driven with native movement and the native jump
/// clears every landing, and the same dummy denied its jump does not.
///
/// Three runs bracket the route, because any one of them alone proves nothing:
///   sprint + jump  must COMPLETE  - the course is possible at all
///   walk   + jump  must STOP LATE - the closing hops genuinely demand a sprint, not just a jump
///   no jump        must STOP AT 1 - the landings are real islands, not a ramp you can stroll up
/// That is the only definition of a parkour course worth shipping.
///
/// Route geometry comes from a raycast sweep of the shaft, not from the plugin's own numbers, so a
/// landing that spawns without a collider fails discovery instead of passing on paper.
/// </summary>
public sealed class PulseLineScenario : Scenario
{
    /// <summary>The dummy must clear the whole route; anything less is a broken course.</summary>
    private const float RequiredCompletion = 1f;

    /// <summary>A jumper who never sprints must still get well into the route, or the opening is a wall.</summary>
    private const float MinimumWalkerProgress = 0.4f;

    public override string Name => "warmup-pulse-line";

    public override string[] Aliases => ["pulse-line", "warmup-parkour"];

    public override string[] Suites => ["warmup"];

    public override string Description =>
        "Drives real dummies up the warmup station's parkour shaft: jumping completes it, walking does not.";

    // Native movement at native speed the whole way; there is no shortcut that would still prove
    // anything, so this scenario is honest at standard and end-to-end alike.
    public override FidelityRange Supported => new(Fidelity.Standard, Fidelity.EndToEnd);

    public override float TimeoutSeconds => 420f;

    public override IEnumerator<float> Run(ScenarioContext ctx)
    {
        // The warmup station deliberately floats ABOVE the facility, outside SCP:SL's room graph, so
        // BoundsMonitor's "actor is inside some room" test cannot apply to anyone standing in it. Declare
        // that up front rather than letting a structural fact of the feature read as a defect.
        foreach (string label in new[] { "runner", "jogger", "walker" })
        {
            ctx.ExpectViolation("the warmup station is a floating room outside the facility room graph",
                "BoundsMonitor", label, count: 8, withinSeconds: 400f);
        }

        SelectorRoom room = StationProbe.RequireRoom(ctx);
        WarmupHallLayout hall = room.Hall!;

        ctx.Require(room.ParkourDoorPrepared,
            "the parkour shaft hatch was never prepared - is Activities.Parkour enabled on this port?");

        // The hatch is fail-closed during setup and only removed once the route validated, so an open
        // hatch is itself the signal that the lane came up.
        Vector3 hatch = hall.World(0f, 1.2f, WarmupHallLayout.HubHalfDepth);
        ctx.Require(!Physics.Raycast(hatch + Vector3.back * 1.5f, Vector3.forward, 3f),
            "the parkour hatch is still sealed, so the route never validated");

        IReadOnlyList<DiscoveredLanding> landings = StationProbe.DiscoverParkourLandings(ctx, room);
        ctx.Require(landings.Count >= 8,
            $"only {landings.Count} collidable landings were found in the shaft; the route is missing or not collidable");

        // Every landing must be something a player can stand on, not just something a ray touched.
        foreach (DiscoveredLanding landing in landings)
        {
            // A guard against discovery noise (wall edges grazed by the sweep come back as ~0.25 m
            // slivers), not a design rule: the closing pads are deliberately shallow.
            ctx.Require(landing.Width >= 0.6f && landing.Depth >= 0.6f,
                $"landing at {StationProbe.Format(landing.Center)} is only {landing.Width:0.##}x{landing.Depth:0.##}m");
            ctx.Require(
                StationProbe.HasGroundUnder(landing.Center + Vector3.up * 0.5f, 1.5f, out _, out string what),
                $"landing at {StationProbe.Format(landing.Center)} has no walkable collider under it (hit {what})");
        }

        List<Vector3> route = landings.Select(landing => landing.Center).ToList();

        // Acceptance scales with each pad: "landed on it" must mean the pad, not a fixed circle. A flat
        // radius measures the wide finish pad as if it were a narrow one and reads as an impossible hop.
        List<float> acceptance = landings
            .Select(landing => Mathf.Clamp(Mathf.Min(landing.Width, landing.Depth) / 2f * 0.9f, 0.45f, 1.2f))
            .ToList();
        ctx.Info($"[PulseLine] route: {route.Count} landings, " +
            $"climb {route[route.Count - 1].y - route[0].y:0.##}m over {route[route.Count - 1].z - route[0].z:0.##}m");

        // ---- a runner who jumps must finish -------------------------------------------------
        Actor runner = ctx.SpawnActor("runner", RoleTypeId.Tutorial, SpawnSpec.Native(), useMovementProvider: false);
        yield return runner.WaitReady();

        JumpCourseResult jumping = Actor.NewJumpCourseResult(allowJump: true, sprint: true);
        yield return runner.TraverseJumpCourse(route, jumping, allowJump: true, sprint: true, arrivalRadii: acceptance);
        ctx.Info($"[PulseLine] {jumping.Summary()}");

        ctx.Require(jumping.CompletedCourse,
            $"a jumping dummy could not complete the route - {jumping.Summary()}");
        ctx.Require(jumping.Cleared >= (route.Count - 1) * RequiredCompletion,
            $"jumping run cleared only {jumping.Cleared}/{route.Count - 1} landings");

        // ---- the closing stretch must actually demand the sprint ------------------------------
        // This is the assertion that pins difficulty. A dummy that jumps but only walks should get most
        // of the way and then run out of reach; if it strolls to the finish, the route is a walk-over.
        Actor jogger = ctx.SpawnActor("jogger", RoleTypeId.Tutorial, SpawnSpec.Native(), useMovementProvider: false);
        yield return jogger.WaitReady();

        JumpCourseResult jogging = Actor.NewJumpCourseResult(allowJump: true, sprint: false);
        yield return jogger.TraverseJumpCourse(route, jogging, allowJump: true, sprint: false, arrivalRadii: acceptance);
        ctx.Info($"[PulseLine] {jogging.Summary()}");

        ctx.Require(!jogging.CompletedCourse,
            $"a dummy that never sprinted still finished the route, so the closing hops demand nothing - {jogging.Summary()}");
        ctx.Require(jogging.Cleared >= (route.Count - 1) * MinimumWalkerProgress,
            $"a walking dummy only cleared {jogging.Cleared}/{route.Count - 1} landings; the opening should be gentle, not a wall");

        // ---- and the landings must be genuine islands, not a ramp ----------------------------
        Actor walker = ctx.SpawnActor("walker", RoleTypeId.Tutorial, SpawnSpec.Native(), useMovementProvider: false);
        yield return walker.WaitReady();

        JumpCourseResult walking = Actor.NewJumpCourseResult(allowJump: false, sprint: true);
        yield return walker.TraverseJumpCourse(route, walking, allowJump: false, sprint: true, arrivalRadii: acceptance);
        ctx.Info($"[PulseLine] {walking.Summary()}");

        ctx.Require(walking.Cleared == 0,
            $"a dummy that never jumped still reached {walking.Cleared} landings, so they are not separated by a real gap");

        ctx.Info($"[PulseLine] verdict: sprint+jump {jumping.Cleared}/{route.Count - 1} (complete), " +
            $"walk+jump {jogging.Cleared}/{route.Count - 1} (stopped), " +
            $"no-jump {walking.Cleared}/{route.Count - 1} " +
            $"(apex {jumping.JumpApex:0.##}m, top speed {jumping.RunSpeed:0.##}m/s)");
    }
}
