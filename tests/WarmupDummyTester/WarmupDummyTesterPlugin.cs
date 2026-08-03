using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using MapGeneration;
using MEC;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupDummyTester;

/// <summary>
/// DEV-ONLY test harness (never ship). The real <c>WarmupScpSelector</c> intentionally ignores dummies:
/// they aren't in <c>Player.ReadyList</c> and have no client to grab a coin, so they stay spectators.
///
/// This separate plugin pulls any admin-spawned dummies (created with the <c>dummy</c> command) into the
/// selector room, gives each a RANDOM pick from the live selector config, keeps them stood in the room,
/// and broadcasts which dummy "chose" what — so the draft room + selection UX can be eyeballed with bots.
///
/// It does not modify the shipped plugin and does NOT drive the real round-start swap (that path is keyed
/// off ReadyList; the swap logic itself is covered by the headless planner tests). At round start it just
/// releases its dummies back to Spectator so they don't litter the live round.
/// </summary>
public sealed class WarmupDummyTesterPlugin : Plugin<DummyTesterConfig>
{
    private static readonly RoleTypeId[] FallbackRoles =
    {
        RoleTypeId.Scp049, RoleTypeId.Scp079, RoleTypeId.Scp096, RoleTypeId.Scp106,
        RoleTypeId.Scp173, RoleTypeId.Scp939, RoleTypeId.Scp3114,
    };

    private readonly Dictionary<int, Assignment> _assignments = new();
    private readonly System.Random _random = new();
    private CoroutineHandle _loop;
    private bool _active;

    public override string Name => "WarmupDummyTester";

    public override string Description =>
        "DEV ONLY: fills the warmup selector with dummies that auto-pick random SCPs and logs the picks on screen. Not for production.";

    public override string Author => "Michael";

    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(LabApiProperties.CompiledVersion);

    public override void Enable()
    {
        ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
        ServerEvents.RoundStarted += OnRoundStarted;
        ServerEvents.RoundRestarted += OnRoundRestarted;
        Logger.Warn($"{Name} enabled — DEV TEST PLUGIN. Remove it before shipping to production.");
    }

    public override void Disable()
    {
        ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
        ServerEvents.RoundStarted -= OnRoundStarted;
        ServerEvents.RoundRestarted -= OnRoundRestarted;
        Stop();
        _assignments.Clear();
    }

    // ---- Lifecycle ------------------------------------------------------------------------------

    private void OnWaitingForPlayers()
    {
        if (!Config.IsEnabled)
        {
            return;
        }

        _assignments.Clear();
        _active = true;
        Timing.KillCoroutines(_loop);
        _loop = Timing.RunCoroutine(TickLoop());
    }

    private void OnRoundStarted()
    {
        // The selector room despawns at round start; release our dummies so they don't litter the live round.
        Stop();
        foreach (Player dummy in CurrentDummies())
        {
            try
            {
                if (dummy.Role == RoleTypeId.Tutorial)
                {
                    dummy.SetRole(RoleTypeId.Spectator, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DummyTester] Could not reset a dummy at round start: {ex.Message}");
            }
        }

        _assignments.Clear();
    }

    private void OnRoundRestarted()
    {
        Stop();
        _assignments.Clear();
    }

    private void Stop()
    {
        _active = false;
        Timing.KillCoroutines(_loop);

        // Drop our display-only picks from the real selector's live count so they don't linger past warmup.
        try
        {
            WarmupScpSelectorPlugin.Instance?.SetTestSelections(Array.Empty<KeyValuePair<Player, RoleTypeId>>());
        }
        catch (Exception ex)
        {
            Logger.Warn($"[DummyTester] Could not clear selector picks: {ex.Message}");
        }
    }

    // ---- Core loop ------------------------------------------------------------------------------

    private IEnumerator<float> TickLoop()
    {
        while (_active)
        {
            float refresh = Mathf.Clamp(SafeFloat(Config.RefreshSeconds, 1f), 0.25f, 10f);
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DummyTester] Tick failed: {ex.Message}");
            }

            yield return Timing.WaitForSeconds(refresh);
        }
    }

    private void Tick()
    {
        List<RoleTypeId> offered = OfferedRoles();
        if (offered.Count == 0)
        {
            return;
        }

        Vector3 spawn = ResolveSpawn();
        List<Player> dummies = CurrentDummies().ToList();
        HashSet<int> live = new();

        foreach (Player dummy in dummies)
        {
            int id = dummy.PlayerId;
            live.Add(id);
            if (!_assignments.TryGetValue(id, out Assignment assignment))
            {
                // New dummy: lock in a random pick (stable for the rest of the warmup) and a layout slot.
                assignment = new Assignment(_assignments.Count, offered[_random.Next(offered.Count)]);
                _assignments[id] = assignment;
                Logger.Info($"[DummyTester] Dummy ({id}) picked {FormatRole(assignment.Role)}.");
            }

            try
            {
                if (dummy.Role != RoleTypeId.Tutorial)
                {
                    dummy.SetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                }

                // Keep them parked in the room (re-place every tick in case they fell/wandered, like the
                // real selector's MaintainPlayers sweep does for human warmup players).
                dummy.Position = SpreadPosition(spawn, assignment.Index);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DummyTester] Could not place dummy ({id}): {ex.Message}");
            }
        }

        // Forget dummies that have disappeared so their layout slots free up.
        foreach (int gone in _assignments.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _assignments.Remove(gone);
        }

        // Feed the dummy picks into the real selector's live count (display-only; never drives the swap) so
        // the "N picks" badge next to a human's selection can be eyeballed with bots.
        PushPicksToSelector(dummies);

        BroadcastLog(dummies);
    }

    // Mirror the dummies' current picks into the product plugin's display-only test-selection overlay.
    private void PushPicksToSelector(List<Player> dummies)
    {
        try
        {
            WarmupScpSelectorPlugin.Instance?.SetTestSelections(
                dummies.Where(d => d != null && _assignments.ContainsKey(d.PlayerId))
                       .Select(d => new KeyValuePair<Player, RoleTypeId>(d, _assignments[d.PlayerId].Role)));
        }
        catch (Exception ex)
        {
            Logger.Warn($"[DummyTester] Could not push picks to selector: {ex.Message}");
        }
    }

    // ---- On-screen logging ----------------------------------------------------------------------

    private void BroadcastLog(List<Player> dummies)
    {
        string text = BuildLog(dummies);
        ushort duration = (ushort)Math.Max(2, (int)SafeFloat(Config.RefreshSeconds, 1f) + 1);

        foreach (Player viewer in Player.ReadyList.Where(p => !p.IsHost && !p.IsDummy))
        {
            try
            {
                viewer.SendBroadcast(text, duration, global::Broadcast.BroadcastFlags.Normal, shouldClearPrevious: true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[DummyTester] Broadcast failed: {ex.Message}");
            }
        }
    }

    private string BuildLog(List<Player> dummies)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("<b><color=#FF4655>假人 SCP 选择（测试）</color></b>\n");

        if (dummies.Count == 0)
        {
            sb.Append("<color=#9AA0AA>用 \"dummy\" 指令生成假人</color>");
            return sb.ToString();
        }

        foreach (Player dummy in dummies.OrderBy(d => Slot(d)))
        {
            RoleTypeId role = _assignments.TryGetValue(dummy.PlayerId, out Assignment a) ? a.Role : RoleTypeId.None;
            sb.Append("<color=#9AA0AA>(").Append(dummy.PlayerId).Append(")</color> ")
              .Append("<color=#FFC83D>→</color> ")
              .Append("<b><color=#FFFFFF>").Append(FormatRole(role)).Append("</color></b>\n");
        }

        return sb.ToString();
    }

    private int Slot(Player dummy)
    {
        return _assignments.TryGetValue(dummy.PlayerId, out Assignment a) ? a.Index : int.MaxValue;
    }

    // ---- Selector config bridge -----------------------------------------------------------------

    // Read the SCPs the LIVE selector offers so dummy picks match the real coins; fall back to the built-in
    // default set if the product plugin isn't loaded for some reason.
    private static List<RoleTypeId> OfferedRoles()
    {
        Config? config = WarmupScpSelectorPlugin.Instance?.Config;
        IEnumerable<ScpOption>? options = config?.ScpOptions;
        if (options == null)
        {
            return FallbackRoles.ToList();
        }

        List<RoleTypeId> roles = options
            .Where(o => o != null && ScpOption.IsScpRole(o.Role))
            .Select(o => o.Role)
            .Distinct()
            .ToList();

        return roles.Count > 0 ? roles : FallbackRoles.ToList();
    }

    // Mirror SelectorRoom.ResolveOrigin + SpawnPosition so dummies land where human warmup players do.
    private static Vector3 ResolveSpawn()
    {
        Config? config = WarmupScpSelectorPlugin.Instance?.Config;
        Vector3 origin;
        try
        {
            Room surface = Room.Get(FacilityZone.Surface).FirstOrDefault();
            if (surface != null)
            {
                float clearance = Mathf.Clamp(SafeFloat(config?.SurfaceClearance ?? 20f, 20f), 5f, 80f);
                origin = surface.Position + new Vector3(0f, clearance, 0f);
            }
            else
            {
                origin = config?.RoomOrigin ?? new Vector3(0f, 1015f, 0f);
            }
        }
        catch
        {
            origin = config?.RoomOrigin ?? new Vector3(0f, 1015f, 0f);
        }

        return origin + new Vector3(0f, 0.5f, -3f); // SelectorRoom.SpawnZ == -3
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static IEnumerable<Player> CurrentDummies()
    {
        return Player.DummyList.Where(d => d != null && d.ReferenceHub != null && !d.IsHost);
    }

    private Vector3 SpreadPosition(Vector3 spawn, int index)
    {
        float radius = Mathf.Clamp(SafeFloat(Config.SpreadRadius, 3f), 0f, 12f);
        if (index <= 0 || radius <= 0f)
        {
            return spawn;
        }

        // Phyllotaxis spread: even, non-overlapping placement that grows gently outward.
        const float goldenAngle = 2.399963f;
        float angle = index * goldenAngle;
        float r = radius * Mathf.Sqrt(index) * 0.5f;
        return spawn + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
    }

    private static string FormatRole(RoleTypeId role)
    {
        if (role == RoleTypeId.None)
        {
            return "—";
        }

        string name = role.ToString();
        return name.StartsWith("Scp", StringComparison.Ordinal) ? "SCP-" + name.Substring(3) : name;
    }

    private static float SafeFloat(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private readonly struct Assignment
    {
        public Assignment(int index, RoleTypeId role)
        {
            Index = index;
            Role = role;
        }

        public int Index { get; }

        public RoleTypeId Role { get; }
    }
}
