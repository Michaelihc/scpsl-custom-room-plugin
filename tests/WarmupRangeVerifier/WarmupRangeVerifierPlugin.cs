using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AdminToys;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Activities.AimRange;
using WarmupScpSelector.Models;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupRangeVerifier;

/// <summary>
/// Disposable, dev-only, automatic live verifier for the Full Aim Range. It observes product state through
/// reflection, owns exactly one native probe dummy, and invokes only the lane's normal StopForRoundStart/Start
/// lifecycle methods. It never destroys ambient pickups, toys, or dummies.
/// </summary>
public sealed class WarmupRangeVerifierPlugin : Plugin<RangeVerifierConfig>
{
    private const string Prefix = "[AimRangeLiveTest]";
    private const string DummyName = "AIM-RANGE-LIVE-PROBE";
    private CoroutineHandle _run;
    private ReferenceHub? _dummyHub;
    private Player? _dummy;
    private bool _priorLobbyLock;
    private bool _ownsLobbyLock;
    private bool _roundStartedDuringRun;
    private bool _completed;
    private int _checks;

    public override string Name => "WarmupRangeVerifier";
    public override string Description => "DEV ONLY: automatic no-client live verification harness for WarmupScpSelector Full Aim Range.";
    public override string Author => "Michael";
    public override Version Version => new Version(1, 0, 0);
    public override Version RequiredApiVersion => new Version(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
        ServerEvents.RoundStarted += OnRoundStarted;
        ServerEvents.RoundRestarted += OnRoundRestarted;
        Logger.Warn($"{Prefix} DEV-ONLY verifier enabled; never deploy this DLL to production.");
    }

    public override void Disable()
    {
        ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
        ServerEvents.RoundStarted -= OnRoundStarted;
        ServerEvents.RoundRestarted -= OnRoundRestarted;
        Timing.KillCoroutines(_run);
        CleanupHarnessDummy();
        RestoreLobbyLock();
    }

    private void OnWaitingForPlayers()
    {
        if (!Config.IsEnabled)
        {
            return;
        }

        Timing.KillCoroutines(_run);
        CleanupHarnessDummy();
        RestoreLobbyLock();
        _completed = false;
        _roundStartedDuringRun = false;
        _checks = 0;
        _run = Timing.RunCoroutine(GuardedRun());
    }

    private void OnRoundStarted()
    {
        if (!_completed)
        {
            _roundStartedDuringRun = true;
        }
    }

    private void OnRoundRestarted()
    {
        Timing.KillCoroutines(_run);
        CleanupHarnessDummy();
        RestoreLobbyLock();
        _completed = false;
    }

    private IEnumerator<float> GuardedRun()
    {
        IEnumerator<float> body = RunVerification();
        while (true)
        {
            bool moved;
            float current = Timing.WaitForOneFrame;
            try
            {
                moved = body.MoveNext();
                if (moved)
                {
                    current = body.Current;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"{Prefix} FAIL summary checks={_checks} error={OneLine(ex.GetBaseException().Message)}");
                _completed = true;
                CleanupHarnessDummy();
                RestoreLobbyLock();
                yield break;
            }

            if (!moved)
            {
                _completed = true;
                yield break;
            }

            yield return current;
        }
    }

    private IEnumerator<float> RunVerification()
    {
        Logger.Info($"{Prefix} START mode=automatic noHumanClient=true product={WarmupScpSelectorPlugin.Instance?.Version}");
        AcquireLobbyLock();

        LiveRangeProbe? probe = null;
        double startupDeadline = Now + Clamp(Config.StartupTimeoutSeconds, 2f, 60f, 15f);
        while (Now < startupDeadline)
        {
            if (LiveRangeProbe.TryCapture(out probe) && probe != null && probe.IsReady)
            {
                break;
            }

            RequireRoundStillWaiting("range-startup-poll");
            yield return Timing.WaitForOneFrame;
        }

        Require(probe != null && probe.IsReady, "range-startup", "Aim lane running with widened world, shelves, three sliding targets, sphere drill, and scheduler", probe?.Describe() ?? "product/lane unavailable");
        probe = LiveRangeProbe.Capture();
        ValidateStartup(probe);

        // Wake the dedicated server's physics scene before probing AdminToy colliders. A zero-client server can
        // remain in idle mode even though spawned collider components report enabled bounds; one harness-owned
        // Tutorial dummy exits that idle state without becoming a canonical human or permitting product bots.
        IEnumerator<float> dummyRoutine = ValidateHarnessDummy(probe);
        while (dummyRoutine.MoveNext())
        {
            yield return dummyRoutine.Current;
        }

        IEnumerator<float> botRoutine = ValidateProductBots(probe);
        while (botRoutine.MoveNext())
        {
            yield return botRoutine.Current;
        }

        IEnumerator<float> doorRoutine = WaitForPhysicalDoorway(probe);
        while (doorRoutine.MoveNext())
        {
            yield return doorRoutine.Current;
        }

        ValidateRaycasts(probe.Layout);
        ValidateShelves(probe);

        IEnumerator<float> targetRoutine = ValidateTargetsAndSpheres(probe);
        while (targetRoutine.MoveNext())
        {
            yield return targetRoutine.Current;
        }

        CleanupHarnessDummy();
        double dummyCleanupDeadline = Now + 2d;
        while (HarnessDummies().Count != 0 && Now < dummyCleanupDeadline)
        {
            yield return Timing.WaitForOneFrame;
        }
        Require(HarnessDummies().Count == 0, "dummy-cleanup", "zero harness-owned dummies after destroy", $"remaining={HarnessDummies().Count}");
        RequireRoundStillWaiting("post-dummy-cleanup");

        RangeOwnershipSnapshot ownership = RangeOwnershipSnapshot.Capture(probe);
        ReflectionAccess.Invoke(probe.Lane, "StopForRoundStart");

        double cleanupDeadline = Now + 3d;
        while (!ownership.IsGone(probe) && Now < cleanupDeadline)
        {
            yield return Timing.WaitForOneFrame;
        }
        ValidateCleanup(probe, ownership);

        if (Config.RestartRangeAfterVerification)
        {
            ReflectionAccess.Invoke(probe.Lane, "Start", probe.Room);
            double restartDeadline = Now + Clamp(Config.StartupTimeoutSeconds, 2f, 60f, 15f);
            while (Now < restartDeadline)
            {
                LiveRangeProbe refreshed = LiveRangeProbe.Capture();
                if (refreshed.IsReady)
                {
                    probe = refreshed;
                    break;
                }

                RequireRoundStillWaiting("range-restart-poll");
                yield return Timing.WaitForOneFrame;
            }

            Require(probe.IsReady, "manual-qa-restart", "Aim lane restarted with door open and six shelf slots", probe.Describe());
        }

        RestoreLobbyLock();
        RequireRoundStillWaiting("final-round-state");
        Logger.Info($"{Prefix} PASS summary checks={_checks}/{_checks} cleanup=verified manualQaRange={(Config.RestartRangeAfterVerification ? "restarted" : "stopped")}");
    }

    private void ValidateStartup(LiveRangeProbe probe)
    {
        WarmupScpSelector.Config productConfig = probe.Product.Config;
        Require(productConfig.IsEnabled && productConfig.ActivitiesEnabled && productConfig.Activities?.Aim?.Enabled == true,
            "config", "product + activities + Aim enabled", $"product={productConfig.IsEnabled} activities={productConfig.ActivitiesEnabled} aim={productConfig.Activities?.Aim?.Enabled}");
        Require(probe.ControllerActive, "controller-active", "selector controller active", $"active={probe.ControllerActive}");
        Require(probe.Room.IsSpawned && probe.Room.AimRangeDoorPrepared && probe.DoorOpen,
            "door-open", "selector spawned, doorway prepared, gate removed", $"room={probe.Room.IsSpawned} prepared={probe.Room.AimRangeDoorPrepared} gateOpen={probe.DoorOpen}");
        Require(probe.World.IsSpawned && probe.World.Layout != null && probe.World.ShelfAnchors.Count == 6,
            "world-layout", "world/layout spawned with six shelf anchors", $"spawned={probe.World.IsSpawned} anchors={probe.World.ShelfAnchors.Count}");
        Require(probe.ShelfRunning && probe.ShelfState.Slots.Count() == 6 && probe.ShelfState.Slots.All(slot => slot.Phase == ShelfSlotPhase.Available),
            "shelf-start", "six available shelf slots", $"running={probe.ShelfRunning} slots={probe.ShelfState.Slots.Count()} phases={string.Join(",", probe.ShelfState.Slots.OrderBy(slot => slot.SlotId).Select(slot => slot.Phase))}");
        Require(probe.SlidingTargetCount == 3 && probe.SphereTargetCount == probe.Product.Config.Activities.Aim.SphereActiveCount,
            "three-lane-targets", "three persistent sliding targets plus configured active sphere targets", $"sliding={probe.SlidingTargetCount} spheres={probe.SphereTargetCount}");
        Require(probe.SchedulerRunning, "scheduler-start", "MEC scheduler handle running", $"running={probe.SchedulerRunning}");

        List<LabApi.Features.Wrappers.LightSourceToy> rangeLights = ReflectionAccess.Items(ReflectionAccess.Field(probe.World, "_toys"))
            .OfType<LabApi.Features.Wrappers.LightSourceToy>()
            .ToList();
        Vector3[] expectedLights = new[] { 3.0f, 10.5f, 18.2f }
            .SelectMany(depth => new[]
            {
                new Vector3(probe.Layout.GalleryOrigin.x - AimRangeLayout.LaneWidth, probe.Layout.GalleryOrigin.y + 3.9f, probe.Layout.DoorPlaneZ - depth),
                new Vector3(probe.Layout.GalleryOrigin.x, probe.Layout.GalleryOrigin.y + 3.9f, probe.Layout.DoorPlaneZ - depth),
                new Vector3(probe.Layout.GalleryOrigin.x + AimRangeLayout.LaneWidth, probe.Layout.GalleryOrigin.y + 3.9f, probe.Layout.DoorPlaneZ - depth),
            })
            .ToArray();
        bool lightingValid = rangeLights.Count == 9 && rangeLights.All(light =>
            !light.IsDestroyed && AdminToy.List.Contains(light) && light.Type == UnityEngine.LightType.Point &&
            light.Intensity >= 5f && light.Range >= 10.5f &&
            light.Color.r >= 0.9f && light.Color.g >= 0.9f && light.Color.b >= 0.85f) &&
            expectedLights.All(expected => rangeLights.Any(light => Vector3.Distance(light.Position, expected) <= 0.05f));
        Require(lightingValid,
            "range-lighting", "nine live bright near-white point lights covering all three widened lanes", $"count={rangeLights.Count} values={string.Join(",", rangeLights.Select(light => $"{F(light.Position)}:{light.Type}/{light.Intensity:0.##}/{light.Range:0.##}"))}");

        List<object> instances = probe.MerInstances;
        int staticInstances = instances.Count(instance => ReflectionAccess.Field<bool>(instance, "_isStatic"));
        int staticVisualToys = instances.Where(instance => ReflectionAccess.Field<bool>(instance, "_isStatic"))
            .Sum(instance => ReflectionAccess.Count(ReflectionAccess.Field(instance, "_toys")));
        Require(staticInstances == 2 && staticVisualToys > 0,
            "rack-visuals", "exactly two static MER rack instances with visible primitives", $"staticInstances={staticInstances} toys={staticVisualToys}");

        int canonicalHumans = Player.ReadyList.Count(IsCanonicalHuman);
        int productBots = probe.ProductOwnedBotCount;
        int namedProductBots = Player.DummyList.Count(player => player.Nickname.StartsWith("AIM-RANGE-", StringComparison.Ordinal) && player.Nickname != DummyName);
        Require(canonicalHumans == 0 && productBots == 0 && namedProductBots == 0,
            "zero-human-bot-policy", "zero canonical humans and zero product-owned bots", $"canonicalHumans={canonicalHumans} registryBots={productBots} namedBots={namedProductBots}");
    }

    private IEnumerator<float> WaitForPhysicalDoorway(LiveRangeProbe probe)
    {
        AimRangeLayout layout = probe.Layout;
        Vector3 origin = new Vector3(layout.GalleryOrigin.x, layout.GalleryOrigin.y + 1.8f, layout.DoorPlaneZ + 0.8f);
        const float expectedDistance = 22.5f;
        const float tolerance = 0.22f;
        double deadline = Now + 3d;
        RaycastHit observed = default;
        bool cleared = false;
        while (Now < deadline)
        {
            if (TryRay(origin, Vector3.back, expectedDistance + 5f, _dummyHub, out observed) &&
                Math.Abs(observed.distance - expectedDistance) <= tolerance)
            {
                cleared = true;
                break;
            }

            RequireRoundStillWaiting("door-physics-poll");
            yield return Timing.WaitForOneFrame;
        }

        Require(cleared, "door-physics-open", $"center doorway physically clear to backstop at {expectedDistance:0.###}+/-{tolerance:0.###}m",
            observed.collider == null
                ? $"origin={F(origin)} rayMiss=true {DescribeRangeColliders(probe)}"
                : $"origin={F(origin)} distance={observed.distance:0.###} hit={F(observed.point)} collider={observed.collider.name} {DescribeRangeColliders(probe)}");
    }

    private static string DescribeRangeColliders(LiveRangeProbe probe)
    {
        int primitives = 0;
        int collidableFlags = 0;
        int colliderComponents = 0;
        int enabledColliders = 0;
        string backstop = "backstop=missing";
        foreach (object item in ReflectionAccess.Items(ReflectionAccess.Field(probe.World, "_toys")))
        {
            if (item is not LabApi.Features.Wrappers.PrimitiveObjectToy primitive || primitive.IsDestroyed)
            {
                continue;
            }

            primitives++;
            if ((primitive.Flags & AdminToys.PrimitiveFlags.Collidable) != 0)
            {
                collidableFlags++;
            }

            Collider[] colliders = primitive.GameObject.GetComponents<Collider>();
            colliderComponents += colliders.Length;
            enabledColliders += colliders.Count(collider => collider != null && collider.enabled);
            if (Math.Abs(primitive.Position.z - (probe.Layout.DoorPlaneZ - AimRangeLayout.Depth)) <= 0.05f &&
                Math.Abs(primitive.Position.x - probe.Layout.GalleryOrigin.x) <= 0.05f)
            {
                Collider? first = colliders.FirstOrDefault();
                backstop = first == null
                    ? $"backstopPos={F(primitive.Position)} scale={F(primitive.Transform.localScale)} flags={primitive.Flags} colliders=0 layer={primitive.GameObject.layer}"
                    : $"backstopPos={F(primitive.Position)} scale={F(primitive.Transform.localScale)} flags={primitive.Flags} colliders={colliders.Length} enabled={colliders.Count(collider => collider != null && collider.enabled)} trigger={first.isTrigger} boundsCenter={F(first.bounds.center)} boundsSize={F(first.bounds.size)} active={primitive.GameObject.activeInHierarchy} layer={primitive.GameObject.layer}";
            }
        }

        return $"worldPrimitives={primitives} collidableFlags={collidableFlags} colliders={colliderComponents} enabledColliders={enabledColliders} {backstop}";
    }

    private void ValidateRaycasts(AimRangeLayout layout)
    {
        float y = layout.GalleryOrigin.y;
        float door = layout.DoorPlaneZ;
        List<Vector3> floorPoints = new List<Vector3>();

        AddGrid(floorPoints, new[] { -1f, 0f, 1f }, new[] { door + 0.40f, door + 0.05f, door - 0.05f, door - 0.40f }, layout);
        AddGrid(floorPoints, new[] { -1f, 0f, 1f }, new[] { door - 1.60f }, layout);
        AddGrid(floorPoints, new[] { -8.4f, 8.4f }, new[] { door - 1.6f, door - 2.8f, door - 4.0f }, layout);
        AddGrid(floorPoints, new[] { -8f, -6.4f, -4f, 0f, 4f, 6.4f, 8f }, new[] { door - 5.5f, door - 6.5f }, layout);
        AddGrid(floorPoints, new[] { -6.4f, -5.7f }, new[] { door - 10.2f, door - 14.5f, door - 18.7f }, layout);
        AddGrid(floorPoints, new[] { -2.4f, 0f, 2.4f }, new[] { door - 10.2f, door - 14.7f, door - 19f }, layout);
        AddGrid(floorPoints, new[] { 4f, 6.4f, 8.5f }, new[] { door - 10f, door - 14f, door - 19f }, layout);
        AddGrid(floorPoints, new[] { -8f, -2f, 2f, 8f }, new[] { door - 21.2f }, layout);

        foreach (Vector3 point in floorPoints)
        {
            RaycastHit hit = FloorRay(point + Vector3.up * 3f, y, _dummyHub);
            if (Math.Abs(hit.distance - 3f) > 0.16f || Math.Abs(hit.point.y - y) > 0.12f)
            {
                throw Failure("raycast-down-grid", "floor hit distance 3.00m +/-0.16 and floor Y +/-0.12", $"point={F(point)} distance={hit.distance:0.###} hit={F(hit.point)} collider={hit.collider?.name}");
            }
        }
        Pass("raycast-down-grid", $"rays={floorPoints.Count} regions=gallery-doorway,entrance,shelves,shooting-line,bots,sliding,spheres,backstop");

        AssertRay("doorway-open", new Vector3(layout.GalleryOrigin.x, y + 1.8f, door + 0.8f), Vector3.back, 22.5f, 0.22f);
        AssertRay("door-stub-left", new Vector3(layout.GalleryOrigin.x - 3f, y + 1.8f, door + 0.8f), Vector3.back, 0.65f, 0.16f);
        AssertRay("door-stub-right", new Vector3(layout.GalleryOrigin.x + 3f, y + 1.8f, door + 0.8f), Vector3.back, 0.65f, 0.16f);
        AssertRay("door-lintel", new Vector3(layout.GalleryOrigin.x, y + 4f, door + 0.8f), Vector3.back, 0.65f, 0.16f);
        AssertRay("side-wall-left", new Vector3(layout.GalleryOrigin.x, y + 3.5f, door - 8f), Vector3.left, 9.45f, 0.18f);
        AssertRay("side-wall-right", new Vector3(layout.GalleryOrigin.x, y + 3.5f, door - 8f), Vector3.right, 9.45f, 0.18f);
        AssertRay("divider-left", new Vector3(layout.GalleryOrigin.x, y + 1.4f, door - 12f), Vector3.left, 3.075f, 0.16f);
        AssertRay("divider-right", new Vector3(layout.GalleryOrigin.x, y + 1.4f, door - 12f), Vector3.right, 3.075f, 0.16f);
        AssertRay("shelf-collider-left", new Vector3(layout.GalleryOrigin.x, y + 1.65f, door - 2.8f), Vector3.left, 8.705f, 0.18f);
        AssertRay("shelf-collider-right", new Vector3(layout.GalleryOrigin.x, y + 1.65f, door - 2.8f), Vector3.right, 8.705f, 0.18f);
        AssertRay("shooting-line", new Vector3(layout.GalleryOrigin.x, y + 0.55f, door - 5f), Vector3.back, 1.675f, 0.16f);
        AssertRay("backstop", new Vector3(layout.GalleryOrigin.x, y + 1.5f, door - 20f), Vector3.back, 1.70f, 0.18f);
        Pass("raycast-horizontal-suite", "rays=12 doorwayStubs=true sideWalls=true dividers=true shelves=true shootingLine=true backstop=true");
    }

    private IEnumerator<float> ValidateHarnessDummy(LiveRangeProbe probe)
    {
        AimRangeLayout layout = probe.Layout;
        Require(HarnessDummies().Count == 0, "dummy-precondition", "no pre-existing harness-owned dummy", $"count={HarnessDummies().Count}");
        _dummyHub = DummyUtils.SpawnDummy(DummyName) ?? throw Failure("dummy-spawn", "DummyUtils.SpawnDummy returns one native hub", "returned null");
        yield return Timing.WaitForOneFrame;
        _dummy = Player.Get(_dummyHub) ?? throw Failure("dummy-wrap", "LabAPI wrapper available after one frame", "wrapper null");
        _dummy.SetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);

        double roleDeadline = Now + Clamp(Config.DummySettleTimeoutSeconds, 1f, 15f, 4f);
        while (_dummy.Role != RoleTypeId.Tutorial && Now < roleDeadline)
        {
            RequireRoundStillWaiting("dummy-role-poll");
            yield return Timing.WaitForOneFrame;
        }
        Require(_dummy.Role == RoleTypeId.Tutorial, "dummy-role", "harness dummy role Tutorial", $"role={_dummy.Role}");
        Require(HarnessDummies().Count == 1, "dummy-exactly-one", "exactly one harness-owned native dummy", $"count={HarnessDummies().Count} playerId={_dummy.PlayerId}");
        ValidateFirearmPreload(probe);

        List<Vector3> authored = new List<Vector3>
        {
            layout.EntranceSpawn,
            new Vector3(layout.GalleryOrigin.x - 8.4f, layout.GalleryOrigin.y + 0.5f, layout.DoorPlaneZ - 2.8f),
            new Vector3(layout.GalleryOrigin.x - 6.4f, layout.GalleryOrigin.y + 0.5f, layout.DoorPlaneZ - 5.2f),
            new Vector3(layout.GalleryOrigin.x, layout.GalleryOrigin.y + 0.5f, layout.DoorPlaneZ - 10f),
            new Vector3(layout.GalleryOrigin.x + 6.4f, layout.GalleryOrigin.y + 0.5f, layout.DoorPlaneZ - 13f),
            new Vector3(layout.GalleryOrigin.x, layout.GalleryOrigin.y + 0.5f, layout.DoorPlaneZ - 21.1f),
        };

        IEnumerator<float> settle = SettleDummy(authored[0], layout, "entrance");
        while (settle.MoveNext()) yield return settle.Current;

        Vector3 walkEnd = authored[0] + Vector3.right;
        for (int step = 1; step <= 8; step++)
        {
            _dummy.Position = Vector3.Lerp(authored[0], walkEnd, step / 8f);
            yield return Timing.WaitForOneFrame;
            ValidateDummyPose(layout, $"entrance-walk-{step}");
        }
        Pass("dummy-walk", $"from={F(authored[0])} to={F(walkEnd)} steps=8");

        for (int i = 1; i < authored.Count; i++)
        {
            settle = SettleDummy(authored[i], layout, $"authored-{i}");
            while (settle.MoveNext()) yield return settle.Current;
        }

        Require(!_roundStartedDuringRun && !Round.IsRoundStarted, "dummy-no-round-start", "one harness dummy does not start a round during the probe", $"event={_roundStartedDuringRun} roundStarted={Round.IsRoundStarted} lobbyLocked={Round.IsLobbyLocked}");
        Pass("dummy-floor-suite", $"settledPositions={authored.Count} saneY=true noFall=true noPenetration=true");
    }

    private IEnumerator<float> ValidateProductBots(LiveRangeProbe probe)
    {
        if (_dummy == null || _dummy.ReferenceHub == null)
        {
            throw Failure("product-bots", "live verifier dummy", "dummy unavailable");
        }

        Func<Player, bool> originalCanonical = ReflectionAccess.Field<Func<Player, bool>>(probe.Bots, "_isCanonicalHuman");
        Func<Player, bool> originalEligible = ReflectionAccess.Field<Func<Player, bool>>(probe.Bots, "_isEligibleHuman");
        Func<Player, bool> originalOwnedWarmupHuman = ReflectionAccess.Field<Func<Player, bool>>(probe.Lane, "_isOwnedWarmupHuman");
        Func<Player, string> keyFor = ReflectionAccess.Field<Func<Player, string>>(probe.Bots, "_key");
        Func<Player, bool> probeOnly = player => player?.ReferenceHub == _dummy.ReferenceHub;
        AimRangeSessions sessions = ReflectionAccess.Field<AimRangeSessions>(probe.Lane, "_sessions");
        ActivityManager activities = ReflectionAccess.Field<ActivityManager>(probe.Lane, "_activities");
        string userKey = keyFor(_dummy);
        bool sessionAdded = false;
        Item? attackerWeapon = null;

        try
        {
            ReflectionAccess.SetField(probe.Bots, "_isCanonicalHuman", probeOnly);
            ReflectionAccess.SetField(probe.Bots, "_isEligibleHuman", probeOnly);
            ReflectionAccess.SetField(probe.Lane, "_isOwnedWarmupHuman", probeOnly);

            double spawnDeadline = Now + 10d;
            while (probe.ProductOwnedBotCount < 2 && Now < spawnDeadline)
            {
                RequireRoundStillWaiting("product-bot-spawn-poll");
                yield return Timing.WaitForOneFrame;
            }

            Require(probe.ProductOwnedBotCount == 2, "product-bot-count", "two product-owned native bots initialized", $"count={probe.ProductOwnedBotCount}");
            List<object> slots = DictionaryValues(ReflectionAccess.Field(probe.Bots, "_slots"));
            Require(slots.Count == 2, "product-bot-slots", "two authored bot slot runtimes", $"slots={slots.Count}");

            double readyDeadline = Now + 5d;
            while (Now < readyDeadline && slots.Any(slot => ReflectionAccess.Property<RangeBotLifecycle>(slot, "Lifecycle").State != RangeBotState.PassivePatrol))
            {
                RequireRoundStillWaiting("product-bot-ready-poll");
                yield return Timing.WaitForOneFrame;
            }

            Dictionary<int, Vector3> starts = new Dictionary<int, Vector3>();
            Dictionary<int, int> initialJumpOrdinals = new Dictionary<int, int>();
            HashSet<ItemType> allowedBotGuns = new HashSet<ItemType>
            {
                ItemType.GunE11SR,
                ItemType.GunLogicer,
                ItemType.GunAK,
            };
            string[] requiredPassiveActions = { "Shoot->Hold", "Reload->Click", "Zoom->Hold", "Jump" };
            foreach (object slot in slots)
            {
                RangeBotLifecycle lifecycle = ReflectionAccess.Property<RangeBotLifecycle>(slot, "Lifecycle");
                ReferenceHub? hub = ReflectionAccess.Property(slot, "Hub") as ReferenceHub;
                FirearmItem? firearm = ReflectionAccess.Property(slot, "Firearm") as FirearmItem;
                AimWeaponPresetConfig? preset = ReflectionAccess.Property(slot, "Preset") as AimWeaponPresetConfig;
                Require(hub != null && hub.gameObject != null && lifecycle.State == RangeBotState.PassivePatrol,
                    $"product-bot-{lifecycle.SlotId}-ready", "live passive native bot", $"state={lifecycle.State} hub={(hub == null ? "null" : hub.GetInstanceID())}");
                string[] actions = DummyActionCollector.ServerGetActions(hub!).Where(action => action.Action != null).Select(action => action.Name).ToArray();
                Require(firearm != null && preset != null && allowedBotGuns.Contains(firearm.Type) &&
                        AimWeaponPresetRules.IsBotAutomatic(preset) && requiredPassiveActions.All(required =>
                            actions.Any(actual => string.Equals(actual, required, StringComparison.OrdinalIgnoreCase))),
                    $"product-bot-{lifecycle.SlotId}-native-actions",
                    "E11-SR/Logicer/AK equipped with Shoot->Hold, Reload->Click, Zoom->Hold, and Jump",
                    $"firearm={firearm?.Type} preset={preset?.Id} current={hub?.inventory.CurInstance?.ItemTypeId} actions={string.Join(",", actions)}");
                Require(!ReflectionAccess.Property<bool>(slot, "ShootHeld") && !ReflectionAccess.Property<bool>(slot, "ZoomHeld"),
                    $"product-bot-{lifecycle.SlotId}-passive-released", "passive bot has no held Shoot/Zoom input",
                    $"shoot={ReflectionAccess.Property<bool>(slot, "ShootHeld")} zoom={ReflectionAccess.Property<bool>(slot, "ZoomHeld")}");
                starts[lifecycle.SlotId] = hub!.transform.position;
                initialJumpOrdinals[lifecycle.SlotId] = ReflectionAccess.Property<int>(slot, "JumpOrdinal");
            }

            double walkDeadline = Now + 4d;
            while (Now < walkDeadline)
            {
                RequireRoundStillWaiting("product-bot-walk-poll");
                yield return Timing.WaitForOneFrame;
            }

            foreach (object slot in slots)
            {
                RangeBotLifecycle lifecycle = ReflectionAccess.Property<RangeBotLifecycle>(slot, "Lifecycle");
                ReferenceHub hub = (ReferenceHub)(ReflectionAccess.Property(slot, "Hub") ?? throw Failure("product-bot-hub", "live hub after walk", "null"));
                float moved = HorizontalDistance(starts[lifecycle.SlotId], hub.transform.position);
                int jumpOrdinal = ReflectionAccess.Property<int>(slot, "JumpOrdinal");
                Require(moved >= 0.20f, $"product-bot-{lifecycle.SlotId}-native-walk", "at least 0.20 m real world movement in four seconds", $"moved={moved:0.###} start={F(starts[lifecycle.SlotId])} end={F(hub.transform.position)}");
                Require(jumpOrdinal > initialJumpOrdinals[lifecycle.SlotId], $"product-bot-{lifecycle.SlotId}-native-jump",
                    "bounded native jump occurs while passive movement continues",
                    $"ordinal={initialJumpOrdinals[lifecycle.SlotId]}->{jumpOrdinal} nextAt={ReflectionAccess.Property<double>(slot, "NextJumpAt"):0.###}");
            }

            AimRangeOccupancyTransition transition = AimRangeOccupancyTransition.None;
            if (!sessions.TryGet(userKey, out _))
            {
                int requestedToken = activities.BeginSession(userKey, LaneHintIds.Aim);
                transition = sessions.UpdateOccupancy(userKey, true, () => requestedToken);
            }

            bool hasSession = sessions.TryGet(userKey, out AimRangeSessions.Session session);
            int token = hasSession ? session.Token : 0;
            sessionAdded = hasSession;
            Require(token > 0 && sessionAdded && activities.IsCurrent(userKey, LaneHintIds.Aim, token),
                "product-bot-session", "current synthetic Aim session for native retaliation", $"token={token} transition={transition}");
            attackerWeapon = _dummy.AddItem(ItemType.GunCOM15, InventorySystem.Items.ItemAddReason.PickedUp);
            Require(attackerWeapon != null, "product-bot-attacker-weapon", "real native verifier firearm", "AddItem returned null");
            ushort ownedWeaponSerial = attackerWeapon!.Serial;
            sessions.TrackWeapon(userKey, "verifier", ownedWeaponSerial, 0);
            _dummy.MaxHealth = 1000f;
            _dummy.Health = 1000f;

            object attackSlot = slots[0];
            RangeBotLifecycle attackLifecycle = ReflectionAccess.Property<RangeBotLifecycle>(attackSlot, "Lifecycle");
            ReferenceHub attackHub = (ReferenceHub)(ReflectionAccess.Property(attackSlot, "Hub") ?? throw Failure("product-bot-retaliation-hub", "live bot hub", "null"));
            Player attackBot = Player.Get(attackHub) ?? throw Failure("product-bot-retaliation-wrap", "live bot wrapper", "null");
            FirearmItem attackFirearm = (FirearmItem)(ReflectionAccess.Property(attackSlot, "Firearm") ?? throw Failure("product-bot-retaliation-gun", "live bot firearm", "null"));

            Vector3 botPosition = attackHub.transform.position;
            Vector3[] offsets = { Vector3.back * 3f, Vector3.right * 3f, Vector3.left * 3f, Vector3.forward * 3f };
            Type nativeType = typeof(WarmupScpSelectorPlugin).Assembly.GetType("WarmupScpSelector.Activities.AimRange.RangeBotNative")
                ?? throw Failure("product-bot-native-type", "RangeBotNative type", "missing");
            bool clear = false;
            foreach (Vector3 offset in offsets)
            {
                Vector3 candidate = botPosition + offset;
                candidate.y = probe.Layout.GalleryOrigin.y + 0.5f;
                if (!probe.Layout.ContainsVerified(candidate))
                {
                    continue;
                }

                _dummy.Position = candidate;
                yield return Timing.WaitForOneFrame;
                Vector3 aimPoint = (Vector3)(ReflectionAccess.InvokeStatic(nativeType, "AimPoint", _dummy.ReferenceHub) ?? candidate + Vector3.up);
                clear = (bool)(ReflectionAccess.InvokeStatic(nativeType, "HasLineOfSight", attackHub, _dummy.ReferenceHub, aimPoint) ?? false);
                if (clear)
                {
                    break;
                }
            }

            Require(clear, "product-bot-retaliation-los", "one clear verifier position around the live bot", $"bot={F(botPosition)} target={F(_dummy.Position)}");
            Vector3 selectedAimPoint = (Vector3)(ReflectionAccess.InvokeStatic(nativeType, "AimPoint", _dummy.ReferenceHub) ?? _dummy.Position + Vector3.up);
            HitboxIdentity? headshot = HitboxIdentity.Instances.FirstOrDefault(hitbox =>
                hitbox != null && hitbox.TargetHub == _dummy.ReferenceHub && hitbox.HitboxType == HitboxType.Headshot);
            Require(headshot == null || Vector3.Distance(selectedAimPoint, headshot.CenterOfMass) <= 0.02f,
                "product-bot-head-aim", "native aim point selects the attacker's Headshot hitbox when available",
                $"headshot={(headshot == null ? "unavailable-camera-fallback" : F(headshot.CenterOfMass))} selected={F(selectedAimPoint)}");
            bool stillHasSession = sessions.TryGet(userKey, out AimRangeSessions.Session currentSession);
            bool activityCurrent = activities.IsCurrent(userKey, LaneHintIds.Aim, session.Token);
            Require(stillHasSession && currentSession.Token == session.Token && currentSession.OwnedItemSerial == ownedWeaponSerial && activityCurrent && probeOnly(_dummy),
                "product-bot-provoke-preconditions", "current session, real owned serial, activity token, and eligible attacker",
                $"hasSession={stillHasSession} token={currentSession?.Token}/{session.Token} serial={currentSession?.OwnedItemSerial}/{ownedWeaponSerial} activity={activityCurrent} eligible={probeOnly(_dummy)}");
            int ammoBefore = attackFirearm.StoredAmmo + attackFirearm.ChamberedAmmo;
            double provokeAt = probe.LaneNow;
            bool provoked = (bool)(ReflectionAccess.Invoke(probe.Bots, "TryProvoke", attackBot, _dummy, session.Token, ownedWeaponSerial, provokeAt) ?? false);
            double leaseExpiry = attackLifecycle.AggroExpiresAt;
            Require(provoked && Math.Abs(leaseExpiry - provokeAt - 12d) <= 0.05d,
                "product-bot-provoke", "current session provokes a fixed 12-second first-attacker lease",
                $"slot={attackLifecycle.SlotId} token={session.Token} lease={leaseExpiry - provokeAt:0.###}s");
            yield return Timing.WaitForOneFrame;
            bool repeated = (bool)(ReflectionAccess.Invoke(probe.Bots, "TryProvoke", attackBot, _dummy, session.Token, ownedWeaponSerial, probe.LaneNow) ?? false);
            Require(repeated && Math.Abs(attackLifecycle.AggroExpiresAt - leaseExpiry) <= 0.001d,
                "product-bot-lease-no-refresh", "same attacker repeat hit does not refresh the active lease",
                $"expiry={leaseExpiry:0.###}->{attackLifecycle.AggroExpiresAt:0.###}");

            Vector3 engagementStart = attackHub.transform.position;
            float maximumEngagedMovement = 0f;
            bool sawShootHeld = false;
            bool sawShootReleaseAction = false;
            double fireDeadline = Now + 10d;
            int ammoAfter = ammoBefore;
            int resolvedAggressorTicks = 0;
            int lineOfSightTicks = 0;
            float minimumAimError = float.PositiveInfinity;
            string lastAimStage = "unknown";
            HashSet<string> observedStates = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> observedAimStages = new HashSet<string>(StringComparer.Ordinal);
            while (Now < fireDeadline)
            {
                ammoAfter = attackFirearm.StoredAmmo + attackFirearm.ChamberedAmmo;
                observedStates.Add(attackLifecycle.State.ToString());
                object? aimStage = ReflectionAccess.Property(attackSlot, "AimStage");
                lastAimStage = aimStage?.ToString() ?? "null";
                observedAimStages.Add(lastAimStage);
                if (ReflectionAccess.Invoke(probe.Bots, "ResolveCurrentAggressor", attackSlot) is Player)
                {
                    resolvedAggressorTicks++;
                }

                Vector3 liveAimPoint = (Vector3)(ReflectionAccess.InvokeStatic(nativeType, "AimPoint", _dummy.ReferenceHub) ?? _dummy.Position + Vector3.up);
                if ((bool)(ReflectionAccess.InvokeStatic(nativeType, "HasLineOfSight", attackHub, _dummy.ReferenceHub, liveAimPoint) ?? false))
                {
                    lineOfSightTicks++;
                }

                float aimError = (float)(ReflectionAccess.InvokeStatic(nativeType, "AimErrorDegrees", attackHub, liveAimPoint) ?? float.PositiveInfinity);
                minimumAimError = Math.Min(minimumAimError, aimError);
                maximumEngagedMovement = Math.Max(maximumEngagedMovement, HorizontalDistance(engagementStart, attackHub.transform.position));
                bool shootHeld = ReflectionAccess.Property<bool>(attackSlot, "ShootHeld");
                sawShootHeld |= shootHeld;
                sawShootReleaseAction |= DummyActionCollector.ServerGetActions(attackHub).Any(action =>
                    action.Action != null && string.Equals(action.Name, "Shoot->Release", StringComparison.OrdinalIgnoreCase));
                if (ammoAfter <= ammoBefore - 2)
                {
                    break;
                }

                RequireRoundStillWaiting("product-bot-fire-poll");
                yield return Timing.WaitForOneFrame;
            }

            Require(ammoAfter <= ammoBefore - 2 && sawShootHeld && sawShootReleaseAction,
                "product-bot-native-held-fire", "native Shoot->Hold consumes multiple rounds and exposes Shoot->Release",
                $"ammo={ammoBefore}->{ammoAfter} held={sawShootHeld} releaseAction={sawShootReleaseAction} state={attackLifecycle.State} states={string.Join(",", observedStates)} stages={string.Join(",", observedAimStages)} lastStage={lastAimStage} resolvedTicks={resolvedAggressorTicks} losTicks={lineOfSightTicks} minAimError={minimumAimError:0.###}");
            Require(maximumEngagedMovement >= 0.15f,
                "product-bot-engaged-strafe", "bot keeps moving laterally while aiming/firing",
                $"maxMovement={maximumEngagedMovement:0.###} start={F(engagementStart)} current={F(attackHub.transform.position)}");
            Require(attackLifecycle.State != RangeBotState.RetaliationUnavailable,
                "product-bot-fire-verification", "ShotWeapon/ammo verification keeps retaliation available", $"state={attackLifecycle.State}");

            bool adsObserved = false;
            foreach (Vector3 candidate in new[]
                     {
                         new Vector3(probe.Layout.GalleryOrigin.x - 4f, probe.Layout.GalleryOrigin.y + 0.5f, probe.Layout.DoorPlaneZ - 1.5f),
                         new Vector3(probe.Layout.GalleryOrigin.x - 5f, probe.Layout.GalleryOrigin.y + 0.5f, probe.Layout.DoorPlaneZ - 1.5f),
                     })
            {
                _dummy.Position = candidate;
                yield return Timing.WaitForOneFrame;
                Vector3 farAim = (Vector3)(ReflectionAccess.InvokeStatic(nativeType, "AimPoint", _dummy.ReferenceHub) ?? candidate + Vector3.up);
                float farDistance = Vector3.Distance(attackHub.PlayerCameraReference.position, farAim);
                bool farLos = (bool)(ReflectionAccess.InvokeStatic(nativeType, "HasLineOfSight", attackHub, _dummy.ReferenceHub, farAim) ?? false);
                if (farDistance < 10f || !farLos)
                {
                    continue;
                }

                double adsDeadline = Now + 1d;
                while (Now < adsDeadline && attackLifecycle.HasAggressor)
                {
                    if (ReflectionAccess.Property<bool>(attackSlot, "ZoomHeld") && DummyActionCollector.ServerGetActions(attackHub).Any(action =>
                            action.Action != null && string.Equals(action.Name, "Zoom->Release", StringComparison.OrdinalIgnoreCase)))
                    {
                        adsObserved = true;
                        break;
                    }

                    yield return Timing.WaitForOneFrame;
                }

                if (adsObserved) break;
            }
            Require(adsObserved, "product-bot-far-ads", "distance>=10m holds native Zoom and exposes Zoom->Release",
                $"zoomHeld={ReflectionAccess.Property<bool>(attackSlot, "ZoomHeld")} bot={F(attackHub.transform.position)} target={F(_dummy.Position)}");

            // Force the low-ammo tactical branch while the fixed lease remains active. A valid authored cover must
            // be selected and reached before Reload->Click is attempted; this observes product state without relying
            // on a particular native reload animation duration.
            attackFirearm.StoredAmmo = Math.Min(3, attackFirearm.StoredAmmo);
            attackFirearm.ChamberedAmmo = 0;
            bool sawReloadCover = false;
            bool sawReloadInvoke = false;
            float nearestCoverDistance = float.PositiveInfinity;
            double reloadDeadline = Math.Min(Now + 5d, Now + Math.Max(0.5d, leaseExpiry - probe.LaneNow - 0.5d));
            while (Now < reloadDeadline && attackLifecycle.HasAggressor)
            {
                object? coverObject = ReflectionAccess.Property(attackSlot, "ReloadCover");
                if (coverObject is AimBotCover cover)
                {
                    sawReloadCover = cover.FullHeight && Math.Abs(cover.Size.y - 2.2f) <= 0.01f && cover.SlotId == attackLifecycle.SlotId;
                    nearestCoverDistance = Math.Min(nearestCoverDistance, HorizontalDistance(attackHub.transform.position, cover.ReloadPoint));
                }

                sawReloadInvoke |= ReflectionAccess.Property<bool>(attackSlot, "ReloadInvoked") ||
                    ReflectionAccess.Property<bool>(attackSlot, "ReloadStarted") || attackFirearm.IsReloadingOrUnloading;
                if (sawReloadCover && sawReloadInvoke)
                {
                    break;
                }

                RequireRoundStillWaiting("product-bot-reload-cover-poll");
                yield return Timing.WaitForOneFrame;
            }

            Require(sawReloadCover && sawReloadInvoke && nearestCoverDistance <= 0.65f,
                "product-bot-reload-cover", "low-ammo bot reaches its slot's 2.2m full-height cover before native reload",
                $"cover={sawReloadCover} invoked={sawReloadInvoke} nearest={nearestCoverDistance:0.###} stage={ReflectionAccess.Property(attackSlot, "ReloadStage")}");

            double leaseClearDeadline = Now + Math.Max(1d, leaseExpiry - probe.LaneNow + 1d);
            while (attackLifecycle.HasAggressor && Now < leaseClearDeadline)
            {
                RequireRoundStillWaiting("product-bot-lease-clear-poll");
                yield return Timing.WaitForOneFrame;
            }
            Require(!attackLifecycle.HasAggressor && attackLifecycle.State == RangeBotState.PassivePatrol &&
                    !ReflectionAccess.Property<bool>(attackSlot, "ShootHeld") && !ReflectionAccess.Property<bool>(attackSlot, "ZoomHeld"),
                "product-bot-lease-release", "12-second lease clears to passive with Shoot/Zoom released",
                $"hasAggressor={attackLifecycle.HasAggressor} state={attackLifecycle.State} shoot={ReflectionAccess.Property<bool>(attackSlot, "ShootHeld")} zoom={ReflectionAccess.Property<bool>(attackSlot, "ZoomHeld")} now={probe.LaneNow:0.###} expiry={leaseExpiry:0.###}");
            Pass("product-bot-suite", $"bots=2 passiveWalk=true engagedStrafe=true nativeJump=true heldFireAmmo={ammoBefore}->{ammoAfter} reloadCover=true adsObserved={adsObserved} lease=12s");
        }
        finally
        {
            if (attackerWeapon != null)
            {
                try { _dummy.RemoveItem(attackerWeapon); } catch { }
            }

            try { ReflectionAccess.Invoke(probe.Bots, "ClearAggroFor", userKey); } catch { }
            if (sessionAdded)
            {
                try { ReflectionAccess.Invoke(probe.Lane, "ReleaseSession", userKey); } catch { }
            }

            ReflectionAccess.SetField(probe.Bots, "_isCanonicalHuman", originalCanonical);
            ReflectionAccess.SetField(probe.Bots, "_isEligibleHuman", originalEligible);
            ReflectionAccess.SetField(probe.Lane, "_isOwnedWarmupHuman", originalOwnedWarmupHuman);
        }

        double cleanupDeadline = Now + 5d;
        while (probe.ProductOwnedBotCount != 0 && Now < cleanupDeadline)
        {
            RequireRoundStillWaiting("product-bot-cleanup-poll");
            yield return Timing.WaitForOneFrame;
        }

        Require(probe.ProductOwnedBotCount == 0, "product-bot-cleanup", "temporary product bots removed after human predicate restore", $"count={probe.ProductOwnedBotCount}");
    }

    private void ValidateFirearmPreload(LiveRangeProbe probe)
    {
        if (_dummy == null)
        {
            throw Failure("firearm-preload", "live harness dummy", "dummy null");
        }

        int checkedCount = 0;
        foreach (AimWeaponPresetConfig preset in probe.Product.Config.Activities.Aim.WeaponPresets.Take(6))
        {
            Item? item = null;
            try
            {
                item = _dummy.AddItem(preset.Firearm, InventorySystem.Items.ItemAddReason.PickedUp);
                Require(item is FirearmItem, $"firearm-preload-{preset.Id}", "native picked-up firearm item created", $"item={item?.GetType().Name ?? "null"}");
                FirearmItem firearm = (FirearmItem)item!;
                if (preset.AttachmentsCode != 0)
                {
                    firearm.AttachmentsCode = preset.AttachmentsCode;
                }

                firearm.StoredAmmo = 0;
                firearm.ChamberedAmmo = 0;
                firearm.Cocked = false;
                firearm.BoltLocked = true;
                Require(firearm.StoredAmmo == 0 && !firearm.Cocked,
                    $"firearm-preload-{preset.Id}-precondition", "empty and not ready before product preload", $"loaded={firearm.StoredAmmo} cocked={firearm.Cocked} boltLocked={firearm.BoltLocked}");

                bool preloaded = (bool)(ReflectionAccess.InvokeStatic(probe.Shelves.GetType(), "TryPreload", firearm) ?? false);
                bool ready = firearm.StoredAmmo == firearm.MaxAmmo && firearm.StoredAmmo > 0 && firearm.Cocked && !firearm.BoltLocked &&
                    (firearm.OpenBolt || firearm.ChamberedAmmo > 0);
                Require(preloaded && ready, $"firearm-preload-{preset.Id}", "full magazine and ready native action state", $"loaded={firearm.StoredAmmo}/{firearm.MaxAmmo} openBolt={firearm.OpenBolt} chamber={firearm.ChamberedAmmo} cocked={firearm.Cocked} boltLocked={firearm.BoltLocked}");
                checkedCount++;
            }
            finally
            {
                if (item != null)
                {
                    try { _dummy.RemoveItem(item); } catch { }
                }
            }
        }

        Pass("firearm-preload-suite", $"presets={checkedCount} fullMagazines=true readyActions=true");
    }

    private IEnumerator<float> SettleDummy(Vector3 position, AimRangeLayout layout, string label)
    {
        if (_dummy == null)
        {
            throw Failure("dummy-settle", "live harness dummy", "dummy null");
        }

        _dummy.Position = position;
        double deadline = Now + Clamp(Config.DummySettleTimeoutSeconds, 1f, 15f, 4f);
        int stableFrames = 0;
        Vector3 previous = _dummy.Position;
        while (Now < deadline)
        {
            yield return Timing.WaitForOneFrame;
            RequireRoundStillWaiting("dummy-settle-poll");
            ValidateDummyPose(layout, label);
            Vector3 current = _dummy.Position;
            if (Math.Abs(current.y - previous.y) < 0.02f && HorizontalDistance(current, position) < 0.75f)
            {
                stableFrames++;
            }
            else
            {
                stableFrames = 0;
            }

            previous = current;
            if (stableFrames >= 6)
            {
                Pass("dummy-settle-" + label, $"position={F(current)} stableFrames={stableFrames}");
                yield break;
            }
        }

        throw Failure("dummy-settle-" + label, "six stable frames before timeout", $"position={F(_dummy.Position)} stableFrames={stableFrames}");
    }

    private void ValidateDummyPose(AimRangeLayout layout, string label)
    {
        if (_dummy == null || _dummy.IsDestroyed || _dummy.ReferenceHub == null)
        {
            throw Failure("dummy-pose-" + label, "live dummy wrapper/hub", "destroyed or null");
        }

        Vector3 position = _dummy.Position;
        float floorY = layout.GalleryOrigin.y;
        if (position.y < floorY + 0.20f || position.y > floorY + 1.30f)
        {
            throw Failure("dummy-pose-" + label, $"Y in {floorY + 0.20f:0.##}..{floorY + 1.30f:0.##}", $"position={F(position)}");
        }

        RaycastHit floor = Ray(position + Vector3.up * 1.2f, Vector3.down, 3f, _dummy.ReferenceHub);
        if (Math.Abs(floor.point.y - floorY) > 0.14f || floor.distance < 1.2f || floor.distance > 2.6f)
        {
            throw Failure("dummy-pose-" + label, "floor beneath body at authored Y without penetration", $"position={F(position)} hit={F(floor.point)} distance={floor.distance:0.###} collider={floor.collider?.name}");
        }
    }

    private void ValidateShelves(LiveRangeProbe probe)
    {
        ItemType[] expected = probe.Product.Config.Activities.Aim.WeaponPresets.Take(6).Select(preset => preset.Firearm).ToArray();
        WeaponShelfState.Slot[] slots = probe.ShelfState.Slots.OrderBy(slot => slot.SlotId).ToArray();
        Require(slots.Length == 6, "shelf-count", "six shelf state slots", $"count={slots.Length}");
        HashSet<ushort> serials = new HashSet<ushort>();
        for (int i = 0; i < slots.Length; i++)
        {
            WeaponShelfState.Slot slot = slots[i];
            Require(slot.Phase == ShelfSlotPhase.Available && slot.PickupSerial != 0,
                $"shelf-{i}-available", "available slot with nonzero serial", $"phase={slot.Phase} serial={slot.PickupSerial}");
            Require(Pickup.TryGet(slot.PickupSerial, out Pickup? pickup) && pickup != null && !pickup.IsDestroyed,
                $"shelf-{i}-pickup", "live pickup wrapper", $"serial={slot.PickupSerial}");
            Require(pickup!.Type == expected[i] && AimWeaponPresetRules.IsConventional(probe.Product.Config.Activities.Aim.WeaponPresets[i]),
                $"shelf-{i}-type", $"conventional {expected[i]}", $"actual={pickup.Type}");
            Require(pickup.Rigidbody != null && pickup.Rigidbody.isKinematic,
                $"shelf-{i}-kinematic", "standard kinematic rigidbody", $"rigidbody={(pickup.Rigidbody == null ? "null" : "present")} kinematic={pickup.Rigidbody?.isKinematic}");
            AimShelfAnchor anchor = probe.World.ShelfAnchors.Single(value => value.SlotId == slot.SlotId);
            Require(Vector3.Distance(pickup.Position, anchor.LocalPosition) <= 0.12f,
                $"shelf-{i}-anchor", "pickup within 0.12m of authored/marker anchor", $"pickup={F(pickup.Position)} anchor={F(anchor.LocalPosition)} distance={Vector3.Distance(pickup.Position, anchor.LocalPosition):0.###}");
            Require(serials.Add(slot.PickupSerial), $"shelf-{i}-serial", "serial distinct from previous shelf pickups", $"serial={slot.PickupSerial}");
            Require(!probe.Room.CoinRoles.ContainsKey(slot.PickupSerial),
                $"shelf-{i}-coin-isolation", "shelf serial absent from selector CoinRoles", $"serial={slot.PickupSerial} coinRoles={probe.Room.CoinRoles.Count}");
        }
        Pass("shelf-suite", $"pickups=6 distinctSerials={serials.Count} coinContamination=0");
    }

    private IEnumerator<float> ValidateTargetsAndSpheres(LiveRangeProbe probe)
    {
        List<object> slidingSlots = DictionaryValues(ReflectionAccess.Field(probe.SlidingTargets, "_slots"));
        Require(slidingSlots.Count == 3, "sliding-target-count", "three simultaneous native targets", $"count={slidingSlots.Count}");
        Dictionary<int, List<Vector3>> samples = slidingSlots.ToDictionary(
            slot => ReflectionAccess.Property<SlidingTargetMotion>(slot, "Motion").Track.SlotId,
            _ => new List<Vector3>());

        foreach (double elapsed in new[] { 0.15d, 0.65d, 1.25d, 2.1d })
        {
            RequireRoundStillWaiting("sliding-target-sample");
            ReflectionAccess.Invoke(probe.SlidingTargets, "Tick", elapsed);
            Physics.SyncTransforms();
            foreach (object slot in slidingSlots)
            {
                SlidingTargetMotion motion = ReflectionAccess.Property<SlidingTargetMotion>(slot, "Motion");
                ShootingTargetToy target = ReflectionAccess.Property<ShootingTargetToy>(slot, "Target");
                Vector3 expected = motion.Evaluate(elapsed);
                Vector3 actual = target.Position;
                Require(!target.IsDestroyed && Vector3.Distance(actual, expected) <= 0.04f,
                    $"sliding-{motion.Track.SlotId}-determinism", "live target matches absolute-time motion", $"t={elapsed:0.##} actual={F(actual)} expected={F(expected)} drift={Vector3.Distance(actual, expected):0.###}");
                Require(target.Base.NetworkMovementSmoothing == 60 && Math.Abs(target.SyncInterval - 1f / 15f) <= 0.001f,
                    $"sliding-{motion.Track.SlotId}-smoothing", "raw client smoothing 60 with 15 Hz network keyframes", $"raw={target.Base.NetworkMovementSmoothing} sync={target.SyncInterval:0.####}");
                Require(actual.x >= Math.Min(motion.Track.EndpointA.x, motion.Track.EndpointB.x) - 0.02f &&
                        actual.x <= Math.Max(motion.Track.EndpointA.x, motion.Track.EndpointB.x) + 0.02f,
                    $"sliding-{motion.Track.SlotId}-bounds", "target remains inside its parallel track", $"position={F(actual)} a={F(motion.Track.EndpointA)} b={F(motion.Track.EndpointB)} speed={motion.Speed:0.###}");
                samples[motion.Track.SlotId].Add(actual);
            }

            yield return Timing.WaitForOneFrame;
        }

        foreach (object slot in slidingSlots)
        {
            SlidingTargetMotion motion = ReflectionAccess.Property<SlidingTargetMotion>(slot, "Motion");
            ShootingTargetToy target = ReflectionAccess.Property<ShootingTargetToy>(slot, "Target");
            Vector3 towardEntrance = probe.Layout.EntranceSpawn - target.Position;
            towardEntrance.y = 0f;
            Vector3 lateral = Vector3.Cross(Vector3.up, towardEntrance.normalized).normalized;
            Bounds colliderBounds = TargetColliderBounds(target);
            bool leftFace = TargetRayHits(target, colliderBounds.center - lateral * 0.18f, towardEntrance);
            bool centerFace = TargetRayHits(target, colliderBounds.center, towardEntrance);
            bool rightFace = TargetRayHits(target, colliderBounds.center + lateral * 0.18f, towardEntrance);
            float span = samples[motion.Track.SlotId].Max(point => point.x) - samples[motion.Track.SlotId].Min(point => point.x);
            Require(leftFace && centerFace && rightFace,
                $"sliding-{motion.Track.SlotId}-broad-face", "three firing-line rays hit the persistent target face", $"hits={leftFace}/{centerFace}/{rightFace} bounds={F(colliderBounds.size)}");
            Require(span >= 0.15f, $"sliding-{motion.Track.SlotId}-motion", "sampled target moves laterally", $"xSpan={span:0.###} speed={motion.Speed:0.###}");
        }
        Pass("sliding-target-suite", $"targets={slidingSlots.Count} persistent=true varyingSpeeds={string.Join(",", slidingSlots.Select(slot => ReflectionAccess.Property<SlidingTargetMotion>(slot, "Motion").Speed.ToString("0.###", CultureInfo.InvariantCulture)))}");

        List<object> sphereSlots = DictionaryValues(ReflectionAccess.Field(probe.SphereTargets, "_slots"));
        SphereTargetLayout authored = SphereTargetLayout.CreateWidenedThirdLane(probe.Layout.SphereLaneOrigin, probe.Layout.SphereLaneRotation);
        HashSet<int> sphereToyIds = new HashSet<int>();
        foreach (object slot in sphereSlots)
        {
            object? toyObject = ReflectionAccess.Property(slot, "Toy");
            Require(toyObject is LabApi.Features.Wrappers.PrimitiveObjectToy, "sphere-live-toy", "live PrimitiveObjectToy sphere", $"toy={toyObject?.GetType().Name ?? "null"}");
            LabApi.Features.Wrappers.PrimitiveObjectToy toy = (LabApi.Features.Wrappers.PrimitiveObjectToy)toyObject!;
            bool atAuthoredPoint = authored.Points.Any(point => Vector3.Distance(point, toy.Position) <= 0.05f);
            Require(!toy.IsDestroyed && toy.Type == PrimitiveType.Sphere &&
                    (toy.Flags & AdminToys.PrimitiveFlags.Visible) != 0 &&
                    (toy.Flags & AdminToys.PrimitiveFlags.Collidable) != 0 && atAuthoredPoint,
                "sphere-contract", "visible collidable sphere at an authored lane-3 point", $"position={F(toy.Position)} type={toy.Type} flags={toy.Flags}");
            Require(sphereToyIds.Add(toy.Base.GetInstanceID()), "sphere-distinct", "each active sphere owns a distinct toy", $"toyId={toy.Base.GetInstanceID()}");
        }
        Require(sphereSlots.Count == probe.Product.Config.Activities.Aim.SphereActiveCount,
            "sphere-active-count", "configured number of simultaneous sphere targets", $"count={sphereSlots.Count}");
        Pass("sphere-target-suite", $"active={sphereSlots.Count} authoredPoints={authored.Points.Count} popRespawnControllerRunning={probe.SphereTargetsRunning}");
    }

    private void ValidateCleanup(LiveRangeProbe probe, RangeOwnershipSnapshot ownership)
    {
        Require(!ReflectionAccess.Field<bool>(probe.Lane, "_running") && !probe.World.IsSpawned && probe.World.Layout == null,
            "cleanup-lane-world", "lane stopped and world/layout despawned", $"laneRunning={ReflectionAccess.Field<bool>(probe.Lane, "_running")} world={probe.World.IsSpawned} layout={(probe.World.Layout == null ? "null" : "present")}");
        Require(probe.ShelfState.Slots.All(slot => slot.PickupSerial == 0 && slot.Phase == ShelfSlotPhase.Disabled),
            "cleanup-shelves", "all range shelf slots disabled with zero serials", string.Join(",", probe.ShelfState.Slots.OrderBy(slot => slot.SlotId).Select(slot => $"{slot.SlotId}:{slot.Phase}/{slot.PickupSerial}")));
        Require(ownership.PickupSerials.All(serial => !Pickup.TryGet(serial, out _)),
            "cleanup-pickups", "all exact range-owned shelf pickup serials absent", $"owned={ownership.PickupSerials.Count} remaining={ownership.PickupSerials.Count(serial => Pickup.TryGet(serial, out _))}");
        Require(ownership.TargetIds.All(id => ShootingTargetToy.List.All(target => target.Base.GetInstanceID() != id)),
            "cleanup-targets", "all exact range target toys absent", $"owned={ownership.TargetIds.Count}");
        Require(ownership.ToyIds.All(id => AdminToy.List.All(toy => toy.Base.GetInstanceID() != id)),
            "cleanup-world-toys", "all exact range-owned world/rack/carrier toys absent", $"owned={ownership.ToyIds.Count} remaining={ownership.ToyIds.Count(id => AdminToy.List.Any(toy => toy.Base.GetInstanceID() == id))}");
        Require(ownership.BotHubIds.All(id => Player.DummyList.All(player => player.ReferenceHub.GetInstanceID() != id)) && ReflectionAccess.Property<int>(probe.Bots, "OwnedBotCount") == 0,
            "cleanup-bots", "all exact range-owned bots absent and registry empty", $"owned={ownership.BotHubIds.Count} registry={ReflectionAccess.Property<int>(probe.Bots, "OwnedBotCount")}");
        Require(ownership.AmbientPickupSerials.All(serial => Pickup.TryGet(serial, out _)) &&
                ownership.AmbientToyIds.All(id => AdminToy.List.Any(toy => toy.Base.GetInstanceID() == id)) &&
                ownership.AmbientDummyHubIds.All(id => Player.DummyList.Any(player => player.ReferenceHub.GetInstanceID() == id)),
            "cleanup-ambient-isolation", "all non-range pickups, admin toys, and dummies captured before cleanup remain present",
            $"pickups={ownership.AmbientPickupSerials.Count(serial => Pickup.TryGet(serial, out _))}/{ownership.AmbientPickupSerials.Count} toys={ownership.AmbientToyIds.Count(id => AdminToy.List.Any(toy => toy.Base.GetInstanceID() == id))}/{ownership.AmbientToyIds.Count} dummies={ownership.AmbientDummyHubIds.Count(id => Player.DummyList.Any(player => player.ReferenceHub.GetInstanceID() == id))}/{ownership.AmbientDummyHubIds.Count}");
        Require(probe.RangeSessionCount == 0 && probe.RangeHintEntryCount == 0,
            "cleanup-hints", "zero range sessions and zero provider aim hint/cache entries", $"sessions={probe.RangeSessionCount} aimHintEntries={probe.RangeHintEntryCount}");
        Require(!probe.DoorOpen, "cleanup-door", "range gate restored after StopForRoundStart", $"gateOpen={probe.DoorOpen}");
        Pass("cleanup-suite", $"pickups={ownership.PickupSerials.Count} targets={ownership.TargetIds.Count} carriersAndWorldToys={ownership.ToyIds.Count} bots={ownership.BotHubIds.Count} hints=0");
    }

    private static List<object> DictionaryValues(object dictionary)
    {
        List<object> values = new List<object>();
        foreach (object entry in ReflectionAccess.Items(dictionary))
        {
            object? value = entry.GetType().GetProperty("Value")?.GetValue(entry, null);
            if (value != null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static void AddGrid(List<Vector3> points, IEnumerable<float> localXs, IEnumerable<float> worldZs, AimRangeLayout layout)
    {
        foreach (float x in localXs)
        {
            foreach (float z in worldZs)
            {
                points.Add(new Vector3(layout.GalleryOrigin.x + x, layout.GalleryOrigin.y, z));
            }
        }
    }

    private void AssertRay(string check, Vector3 origin, Vector3 direction, float expected, float tolerance)
    {
        RaycastHit hit = Ray(origin, direction, expected + 5f, _dummyHub);
        if (Math.Abs(hit.distance - expected) > tolerance)
        {
            throw Failure(check, $"distance={expected:0.###}+/-{tolerance:0.###}", $"origin={F(origin)} direction={F(direction)} distance={hit.distance:0.###} hit={F(hit.point)} collider={hit.collider?.name}");
        }
    }

    private static Bounds TargetColliderBounds(ShootingTargetToy target)
    {
        Collider[] colliders = target.Base.GetComponentsInChildren<Collider>(includeInactive: true)
            .Where(collider => collider != null && collider.enabled && !collider.isTrigger)
            .ToArray();
        if (colliders.Length == 0)
        {
            return new Bounds(target.Position, Vector3.zero);
        }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider collider in colliders.Skip(1))
        {
            bounds.Encapsulate(collider.bounds);
        }

        return bounds;
    }

    private static bool TargetRayHits(ShootingTargetToy target, Vector3 samplePoint, Vector3 towardEntrance)
    {
        if (target == null || target.IsDestroyed || towardEntrance.sqrMagnitude < 0.001f)
        {
            return false;
        }

        Physics.SyncTransforms();
        Vector3 origin = samplePoint + towardEntrance.normalized * 5f;
        Vector3 direction = -towardEntrance.normalized;
        Ray ray = new Ray(origin, direction);
        foreach (Collider collider in target.Base.GetComponentsInChildren<Collider>(includeInactive: true))
        {
            if (collider != null && collider.enabled && !collider.isTrigger && collider.Raycast(ray, out _, 7f))
            {
                return true;
            }
        }

        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, 7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Transform? transform = hit.collider?.transform;
            if (transform != null && (transform == target.Base.transform || transform.IsChildOf(target.Base.transform)))
            {
                return true;
            }
        }

        return false;
    }

    private static RaycastHit FloorRay(Vector3 origin, float floorY, ReferenceHub? ignoredHub)
    {
        Physics.SyncTransforms();
        Ray ray = new Ray(origin, Vector3.down);
        bool found = false;
        RaycastHit best = default;
        foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (collider == null || !collider.enabled || collider.isTrigger ||
                !collider.Raycast(ray, out RaycastHit hit, 6f) || Math.Abs(hit.point.y - floorY) > 0.12f)
            {
                continue;
            }

            ReferenceHub? hub = collider.GetComponentInParent<ReferenceHub>();
            if (ignoredHub != null && hub == ignoredHub)
            {
                continue;
            }

            if (!found || hit.distance < best.distance)
            {
                best = hit;
                found = true;
            }
        }

        if (found)
        {
            return best;
        }

        throw new InvalidOperationException($"floor raycast miss origin={F(origin)} floorY={floorY:0.###}");
    }

    private static RaycastHit Ray(Vector3 origin, Vector3 direction, float distance, ReferenceHub? ignoredHub)
    {
        if (TryRay(origin, direction, distance, ignoredHub, out RaycastHit hit))
        {
            return hit;
        }

        throw new InvalidOperationException($"raycast miss origin={F(origin)} direction={F(direction)} maxDistance={distance:0.###}");
    }

    private static bool TryRay(Vector3 origin, Vector3 direction, float distance, ReferenceHub? ignoredHub, out RaycastHit nearest)
    {
        // A zero-client local server can sit in idle mode without a normal physics step immediately after the
        // AdminToy shell spawns. Synchronize authored transforms, then combine the scene broadphase with direct
        // Collider.Raycast probes of live AdminToys. The direct path is still a real Unity collider raycast; it
        // avoids treating an idle broadphase omission as missing authored collision while the settling dummy below
        // independently exercises actual FPC gravity/collision.
        Physics.SyncTransforms();
        Vector3 normalized = direction.normalized;
        Ray ray = new Ray(origin, normalized);
        bool found = false;
        RaycastHit best = default;

        foreach (RaycastHit hit in Physics.RaycastAll(origin, normalized, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Consider(hit);
        }

        foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            if (collider.Raycast(ray, out RaycastHit hit, distance))
            {
                Consider(hit);
            }
        }

        nearest = best;
        return found;

        void Consider(RaycastHit hit)
        {
            if (hit.collider == null || hit.distance <= 0.001f || hit.distance > distance)
            {
                return;
            }

            ReferenceHub? hub = hit.collider.GetComponentInParent<ReferenceHub>();
            if (ignoredHub != null && hub == ignoredHub)
            {
                return;
            }

            if (!found || hit.distance < best.distance)
            {
                best = hit;
                found = true;
            }
        }
    }

    private void AcquireLobbyLock()
    {
        _priorLobbyLock = Round.IsLobbyLocked;
        Round.IsLobbyLocked = true;
        _ownsLobbyLock = true;
        Pass("lobby-lock", $"temporary=true prior={_priorLobbyLock}");
    }

    private void RestoreLobbyLock()
    {
        if (!_ownsLobbyLock)
        {
            return;
        }

        Round.IsLobbyLocked = _priorLobbyLock;
        _ownsLobbyLock = false;
    }

    private void CleanupHarnessDummy()
    {
        ReferenceHub? ownedHub = _dummyHub;
        _dummyHub = null;
        _dummy = null;
        if (ownedHub?.gameObject != null)
        {
            try { NetworkServer.Destroy(ownedHub.gameObject); } catch { }
        }
    }

    private static List<Player> HarnessDummies() => Player.DummyList
        .Where(player => player != null && player.ReferenceHub != null && string.Equals(player.Nickname, DummyName, StringComparison.Ordinal))
        .ToList();

    private static bool IsCanonicalHuman(Player player)
    {
        return player != null && player.ReferenceHub != null && !player.IsHost && !player.IsDummy && player.IsPlayer && player.IsReady && !string.IsNullOrWhiteSpace(player.UserId);
    }

    private void RequireRoundStillWaiting(string check)
    {
        if (_roundStartedDuringRun || Round.IsRoundStarted)
        {
            throw Failure(check, "round remains in WaitingForPlayers", $"event={_roundStartedDuringRun} roundStarted={Round.IsRoundStarted}");
        }
    }

    private void Require(bool condition, string check, string expected, string observed)
    {
        if (!condition)
        {
            throw Failure(check, expected, observed);
        }

        Pass(check, observed);
    }

    private void Pass(string check, string observed)
    {
        _checks++;
        Logger.Info($"{Prefix} PASS check={check} observed={OneLine(observed)}");
    }

    private static Exception Failure(string check, string expected, string observed)
    {
        string message = $"check={check} expected={OneLine(expected)} observed={OneLine(observed)}";
        Logger.Error($"{Prefix} FAIL {message}");
        return new InvalidOperationException(message);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static float Clamp(float value, float min, float max, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    private static string F(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###},{2:0.###})", value.x, value.y, value.z);
    private static string OneLine(string value) => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
    private static double Now => Time.realtimeSinceStartupAsDouble;

    private sealed class LiveRangeProbe
    {
        private LiveRangeProbe(WarmupScpSelectorPlugin product, object controller, SelectorRoom room, object lane, AimRangeWorld world,
            object shelves, object scheduler, object bots, object slidingTargets, object sphereTargets, object hints)
        {
            Product = product;
            Controller = controller;
            Room = room;
            Lane = lane;
            World = world;
            Shelves = shelves;
            Scheduler = scheduler;
            Bots = bots;
            SlidingTargets = slidingTargets;
            SphereTargets = sphereTargets;
            Hints = hints;
        }

        public WarmupScpSelectorPlugin Product { get; }
        public object Controller { get; }
        public SelectorRoom Room { get; }
        public object Lane { get; }
        public AimRangeWorld World { get; }
        public object Shelves { get; }
        public object Scheduler { get; }
        public object Bots { get; }
        public object SlidingTargets { get; }
        public object SphereTargets { get; }
        public object Hints { get; }
        public AimRangeLayout Layout => World.Layout!;
        public bool ControllerActive => ReflectionAccess.Field<bool>(Controller, "_active");
        public bool LaneRunning => ReflectionAccess.Field<bool>(Lane, "_running");
        public bool ShelfRunning => ReflectionAccess.Field<bool>(Shelves, "_running");
        public WeaponShelfState ShelfState => ReflectionAccess.Field<WeaponShelfState>(Shelves, "_state");
        public int SlidingTargetCount => ReflectionAccess.Property<int>(SlidingTargets, "Count");
        public int SphereTargetCount => ReflectionAccess.Property<int>(SphereTargets, "ActiveCount");
        public bool SphereTargetsRunning => ReflectionAccess.Property<bool>(SphereTargets, "IsRunning");
        public bool DoorOpen => Room.AimRangeDoorPrepared && ReflectionAccess.NullableField(Room, "_aimDoorGate") == null;
        public int ProductOwnedBotCount => ReflectionAccess.Property<int>(Bots, "OwnedBotCount");
        public double LaneNow => ReflectionAccess.Field<double>(Lane, "_now");
        public bool SchedulerRunning
        {
            get
            {
                CoroutineHandle handle = ReflectionAccess.Field<CoroutineHandle>(Scheduler, "_handle");
                return handle.IsRunning;
            }
        }
        public List<object> MerInstances
        {
            get
            {
                object spawner = ReflectionAccess.Field(World, "_merSpawner");
                return ReflectionAccess.Items(ReflectionAccess.Field(spawner, "_instances"));
            }
        }
        public int RangeSessionCount
        {
            get
            {
                AimRangeSessions sessions = ReflectionAccess.Field<AimRangeSessions>(Lane, "_sessions");
                return sessions.Count;
            }
        }
        public int RangeHintEntryCount
        {
            get
            {
                int count = 0;
                object cache = ReflectionAccess.Field(Hints, "_promptCache");
                object signatures = ReflectionAccess.Field(cache, "_signatures");
                foreach (object entry in ReflectionAccess.Items(signatures))
                {
                    object? key = entry.GetType().GetProperty("Key")?.GetValue(entry, null);
                    if (key is string text && text.IndexOf(".aim.", StringComparison.Ordinal) >= 0) count++;
                }

                object timers = ReflectionAccess.Field(Hints, "_flashTimers");
                foreach (object entry in ReflectionAccess.Items(timers))
                {
                    object? key = entry.GetType().GetProperty("Key")?.GetValue(entry, null);
                    if (key is string text && text.EndsWith("|aim", StringComparison.Ordinal)) count++;
                }

                return count;
            }
        }
        public bool IsReady => ControllerActive && LaneRunning && Room.IsSpawned && Room.AimRangeDoorPrepared && DoorOpen &&
            World.IsSpawned && World.Layout != null && ShelfRunning && ShelfState.Slots.Count() == 6 &&
            SlidingTargetCount == 3 && SphereTargetsRunning && SphereTargetCount > 0 && SchedulerRunning;

        public string Describe()
        {
            return $"controller={ControllerActive} lane={LaneRunning} room={Room.IsSpawned} prepared={Room.AimRangeDoorPrepared} doorOpen={DoorOpen} world={World.IsSpawned} layout={(World.Layout == null ? "null" : "present")} shelfRunning={ShelfRunning} slots={ShelfState.Slots.Count()} sliding={SlidingTargetCount} spheres={SphereTargetCount}/{SphereTargetsRunning} scheduler={SchedulerRunning}";
        }

        public static bool TryCapture(out LiveRangeProbe? probe)
        {
            probe = null;
            try
            {
                WarmupScpSelectorPlugin product = WarmupScpSelectorPlugin.Instance;
                if (product == null)
                {
                    return false;
                }

                object controller = ReflectionAccess.Field(product, "_controller");
                SelectorRoom room = ReflectionAccess.Field<SelectorRoom>(controller, "_room");
                object lane = ReflectionAccess.Field(controller, "_aimLane");
                AimRangeWorld world = ReflectionAccess.Field<AimRangeWorld>(lane, "_world");
                object shelves = ReflectionAccess.Field(lane, "_shelves");
                object scheduler = ReflectionAccess.Field(lane, "_scheduler");
                object bots = ReflectionAccess.Field(lane, "_bots");
                object slidingTargets = ReflectionAccess.Field(lane, "_slidingTargets");
                object sphereTargets = ReflectionAccess.Field(lane, "_sphereTargets");
                object hints = ReflectionAccess.Field(product, "_hints");
                probe = new LiveRangeProbe(product, controller, room, lane, world, shelves, scheduler, bots, slidingTargets, sphereTargets, hints);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static LiveRangeProbe Capture()
        {
            if (!TryCapture(out LiveRangeProbe? probe) || probe == null)
            {
                throw new InvalidOperationException("WarmupScpSelector live Aim Range objects could not be captured.");
            }

            return probe;
        }
    }

    private sealed class RangeOwnershipSnapshot
    {
        public HashSet<ushort> PickupSerials { get; } = new HashSet<ushort>();
        public HashSet<int> TargetIds { get; } = new HashSet<int>();
        public HashSet<int> ToyIds { get; } = new HashSet<int>();
        public HashSet<int> BotHubIds { get; } = new HashSet<int>();
        public HashSet<ushort> AmbientPickupSerials { get; } = new HashSet<ushort>();
        public HashSet<int> AmbientToyIds { get; } = new HashSet<int>();
        public HashSet<int> AmbientDummyHubIds { get; } = new HashSet<int>();

        public static RangeOwnershipSnapshot Capture(LiveRangeProbe probe)
        {
            RangeOwnershipSnapshot snapshot = new RangeOwnershipSnapshot();
            foreach (WeaponShelfState.Slot slot in probe.ShelfState.Slots)
            {
                if (slot.PickupSerial != 0) snapshot.PickupSerials.Add(slot.PickupSerial);
            }

            foreach (object slot in DictionaryValues(ReflectionAccess.Field(probe.SlidingTargets, "_slots")))
            {
                object? target = ReflectionAccess.Property(slot, "Target");
                if (target is ShootingTargetToy shooting && !shooting.IsDestroyed)
                {
                    snapshot.TargetIds.Add(shooting.Base.GetInstanceID());
                }
            }

            foreach (object slot in DictionaryValues(ReflectionAccess.Field(probe.SphereTargets, "_slots")))
            {
                object? toy = ReflectionAccess.Property(slot, "Toy");
                if (toy is AdminToy adminToy && !adminToy.IsDestroyed)
                {
                    snapshot.ToyIds.Add(adminToy.Base.GetInstanceID());
                }
            }

            AddToys(snapshot.ToyIds, ReflectionAccess.Field(probe.World, "_toys"));
            foreach (object instance in probe.MerInstances)
            {
                AddToys(snapshot.ToyIds, ReflectionAccess.Field(instance, "_toys"));
            }

            object ownedHubs = ReflectionAccess.Field(probe.Bots, "_ownedHubs");
            foreach (object entry in ReflectionAccess.Items(ownedHubs))
            {
                object? value = entry.GetType().GetProperty("Value")?.GetValue(entry, null);
                if (value is ReferenceHub hub) snapshot.BotHubIds.Add(hub.GetInstanceID());
            }

            foreach (Pickup pickup in Pickup.List)
            {
                if (pickup != null && !pickup.IsDestroyed && !snapshot.PickupSerials.Contains(pickup.Serial))
                {
                    snapshot.AmbientPickupSerials.Add(pickup.Serial);
                }
            }

            foreach (AdminToy toy in AdminToy.List)
            {
                if (toy == null || toy.IsDestroyed)
                {
                    continue;
                }

                int id = toy.Base.GetInstanceID();
                if (!snapshot.ToyIds.Contains(id) && !snapshot.TargetIds.Contains(id))
                {
                    snapshot.AmbientToyIds.Add(id);
                }
            }

            foreach (Player dummy in Player.DummyList)
            {
                if (dummy?.ReferenceHub == null)
                {
                    continue;
                }

                int id = dummy.ReferenceHub.GetInstanceID();
                if (!snapshot.BotHubIds.Contains(id))
                {
                    snapshot.AmbientDummyHubIds.Add(id);
                }
            }

            return snapshot;
        }

        public bool IsGone(LiveRangeProbe probe)
        {
            return PickupSerials.All(serial => !Pickup.TryGet(serial, out _)) &&
                TargetIds.All(id => ShootingTargetToy.List.All(target => target.Base.GetInstanceID() != id)) &&
                ToyIds.All(id => AdminToy.List.All(toy => toy.Base.GetInstanceID() != id)) &&
                BotHubIds.All(id => Player.DummyList.All(player => player.ReferenceHub.GetInstanceID() != id)) &&
                !ReflectionAccess.Field<bool>(probe.Lane, "_running") && !probe.World.IsSpawned;
        }

        private static void AddToys(HashSet<int> ids, object toys)
        {
            foreach (object item in ReflectionAccess.Items(toys))
            {
                if (item is AdminToy toy && !toy.IsDestroyed)
                {
                    ids.Add(toy.Base.GetInstanceID());
                }
            }
        }
    }
}
