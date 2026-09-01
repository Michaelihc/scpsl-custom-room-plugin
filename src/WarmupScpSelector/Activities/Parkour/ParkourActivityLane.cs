using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using UnityEngine;
using WarmupScpSelector.Services;
using WarmupScpSelector.Text;
using WarmupScpSelector.Warmup;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Activities.Parkour
{
    /// <summary>Timed ordered-gate parkour running in the station's dedicated parkour shaft.</summary>
    internal sealed class ParkourActivityLane : IActivityLane
    {
        private sealed class Session
        {
            public Session(ParkourRunState run, Vector3 position)
            {
                Run = run;
                PreviousPosition = position;
            }

            public ParkourRunState Run { get; }
            public Vector3 PreviousPosition { get; set; }
            public int LastCompletedGate { get; set; } = -1;
            public double NextHudAt { get; set; }
        }

        private readonly WarmupScpSelectorPlugin _plugin;
        private readonly HsmHintDisplayProvider _hints;
        private readonly ActivityManager _activities;
        private readonly Func<Player, bool> _isOwnedWarmupHuman;
        private readonly ParkourWorld _world = new ParkourWorld();
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _personalBests = new Dictionary<string, double>(StringComparer.Ordinal);
        private CoroutineHandle _loop;
        private bool _running;
        private bool _subscribed;

        public ParkourActivityLane(
            WarmupScpSelectorPlugin plugin,
            HsmHintDisplayProvider hints,
            ActivityManager activities,
            Func<Player, bool> isOwnedWarmupHuman)
        {
            _plugin = plugin;
            _hints = hints;
            _activities = activities;
            _isOwnedWarmupHuman = isOwnedWarmupHuman;
        }

        public string LaneId => LaneHintIds.Parkour;

        // The shaft is its own compartment, so Pulse Line no longer depends on the Aim range being open.
        public bool Enabled => _plugin.Config.ActivitiesEnabled &&
            _plugin.Config.Activities?.Parkour?.Enabled == true;

        public bool IsRunning => _running;

        /// <summary>Live generated route while the lane runs; null otherwise. Used by the schematic export.</summary>
        internal ParkourLayout? Layout => _running ? _world.Layout : null;

        private ParkourActivityConfig Config => _plugin.Config.Activities?.Parkour ?? new ParkourActivityConfig();

        private bool UseChinese => string.Equals(_plugin.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

        private float FlashDuration => Sanitize(_plugin.Config.Activities?.FlashDurationSeconds ?? 0.7f, 0.05f, 10f, 0.7f);

        public bool Start(WarmupHallLayout? hall, ParkourJumpModel model, Import.StationAsset? authored = null)
        {
            StopInternal(clearBests: true);
            if (!Enabled || hall == null)
            {
                return false;
            }

            try
            {
                if (!_world.Build(hall, model, UseChinese, authored) || _world.Layout == null)
                {
                    return false;
                }

                _plugin.LogDebug(
                    $"Pulse Line built: {_world.Layout.Platforms.Count} landings, peak difficulty " +
                    $"{_world.Layout.PeakDifficulty:0.00} ({model}).");

                Subscribe();
                _running = true;
                float rate = Sanitize(Config.SchedulerRateHz, 5f, 30f, 20f);
                _loop = Timing.RunCoroutine(Run(1f / rate));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WarmupScpSelector] Parkour startup failed: {ex}");
                StopInternal(clearBests: true);
                return false;
            }
        }

        public bool Contains(Vector3 position) => _running && _world.Layout?.Contains(position) == true;

        public void StopForRoundStart() => StopInternal(clearBests: true);

        public void StopAll() => StopInternal(clearBests: true);

        public void OnPlayerLeft(string userKey) => ReleaseSession(userKey);

        private IEnumerator<float> Run(float interval)
        {
            while (_running)
            {
                try
                {
                    Tick(Time.realtimeSinceStartupAsDouble);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WarmupScpSelector] Parkour tick failed: {ex.Message}");
                }

                yield return Timing.WaitForSeconds(interval);
            }
        }

        private void Tick(double now)
        {
            ParkourLayout? layout = _world.Layout;
            if (!_running || layout == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Player player in Player.ReadyList.ToList())
            {
                if (!IsEligible(player))
                {
                    continue;
                }

                string key = SelectorController.Key(player);
                bool inside = layout.Contains(player.Position);
                if (!inside)
                {
                    if (_sessions.ContainsKey(key))
                    {
                        ReleaseSession(key);
                    }
                    continue;
                }

                seen.Add(key);
                if (!_sessions.TryGetValue(key, out Session? session))
                {
                    int token = _activities.BeginSession(key, LaneId);
                    if (token == 0)
                    {
                        continue;
                    }

                    session = new Session(new ParkourRunState(key, token), player.Position);
                    _sessions[key] = session;
                    _plugin.LogDebug($"Parkour occupancy entered: {key}.");
                }

                if (!_activities.IsCurrent(key, LaneId, session.Run.Token))
                {
                    ReleaseSession(key);
                    continue;
                }

                UpdateSession(player, session, layout, now);
            }

            foreach (string key in _sessions.Keys.ToList())
            {
                if (!seen.Contains(key))
                {
                    ReleaseSession(key);
                }
            }
        }

        private void UpdateSession(Player player, Session session, ParkourLayout layout, double now)
        {
            ParkourRunState run = session.Run;
            Vector3 position = player.Position;
            bool onStart = layout.ContainsStart(position);

            if (run.RequireStartExit && !onStart)
            {
                run.RequireStartExit = false;
            }

            if (run.Phase == ParkourPhase.Idle)
            {
                if (onStart && !run.RequireStartExit)
                {
                    run.BeginArming(now);
                }
            }
            else if (run.Phase == ParkourPhase.Arming)
            {
                if (!onStart)
                {
                    run.CancelArming();
                }
                else if (run.TryBeginCountdown(now, Config.StartHoldSeconds, Config.CountdownSeconds))
                {
                    // Snap to the plate only when a countdown will hold them there anyway. Without one
                    // the player is already running, and the clock should start under their feet.
                    if (Config.CountdownSeconds > 0f)
                    {
                        player.Position = layout.StartPlate.RecoveryPosition;
                    }

                    session.PreviousPosition = player.Position;
                }
            }
            else if (run.Phase == ParkourPhase.Countdown)
            {
                // Holding the player on the plate is what a countdown IS - so only do it while one is
                // actually configured. At the default of zero the run starts on the same tick it arms,
                // and pinning them here first would rip a metre off a player already moving.
                if (Config.CountdownSeconds > 0f)
                {
                    player.Position = layout.StartPlate.RecoveryPosition;
                    session.PreviousPosition = player.Position;
                }

                if (run.TryStartRun(now))
                {
                    run.RecoveryGraceEndsAt = now + 0.45d;
                    Flash(player, "go");
                }
            }
            else if (run.Phase == ParkourPhase.Active)
            {
                UpdateActiveRun(player, session, layout, now, position);
            }
            else if (run.Phase == ParkourPhase.Finished && now - run.FinishedAt >= 1.2d)
            {
                run.Reset(requireStartExit: true);
            }

            if (now >= session.NextHudAt)
            {
                session.NextHudAt = now + 0.2d;
                ShowHud(player, session, layout, now);
            }
        }

        private void UpdateActiveRun(Player player, Session session, ParkourLayout layout, double now, Vector3 position)
        {
            ParkourRunState run = session.Run;
            if (now < run.RecoveryGraceEndsAt)
            {
                session.PreviousPosition = position;
                return;
            }

            // A miss is caught EARLY - about a fifth of a second into the fall - rather than on impact, so a
            // slip from the top of the shaft never reaches the fall-damage table. Elapsed time keeps running,
            // so the lost height is the whole penalty.
            if (layout.HasFallenOffRoute(position, session.LastCompletedGate))
            {
                Recover(player, session, layout, now);
                return;
            }

            int gate = run.NextGateIndex;
            if (gate < layout.Platforms.Count &&
                ParkourLayout.SegmentIntersectsBounds(session.PreviousPosition, position, layout.Platforms[gate].GateBounds) &&
                run.TryAdvanceGate(gate))
            {
                session.LastCompletedGate = gate;
                int before = layout.SectorForGate(gate);
                int after = layout.SectorForGate(run.NextGateIndex);
                if (after != before || run.NextGateIndex == layout.Platforms.Count)
                {
                    Flash(player, "split");
                }
            }

            if (run.NextGateIndex == layout.Platforms.Count &&
                ParkourLayout.SegmentIntersectsBounds(session.PreviousPosition, position, layout.FinishPlate.GateBounds) &&
                run.TryFinish(now, layout.Platforms.Count))
            {
                double elapsed = run.Elapsed(now);
                bool best = !_personalBests.TryGetValue(run.UserKey, out double prior) || elapsed < prior;
                if (best)
                {
                    _personalBests[run.UserKey] = elapsed;
                }

                Flash(player, best ? "best" : "finish");
                _plugin.LogDebug($"Parkour finish: {run.UserKey} {elapsed:0.000}s{(best ? " PB" : string.Empty)}.");
            }

            session.PreviousPosition = position;
        }

        private void Recover(Player player, Session session, ParkourLayout layout, double now)
        {
            Vector3 recovery = layout.RecoveryPosition(session.LastCompletedGate);
            player.Position = recovery;
            session.PreviousPosition = recovery;
            session.Run.RecoveryGraceEndsAt = now + Sanitize(Config.RecoveryGraceSeconds, 0.15f, 1f, 0.35f);
            Flash(player, "recover");
        }

        private void ShowHud(Player player, Session session, ParkourLayout layout, double now)
        {
            ParkourRunState run = session.Run;
            _personalBests.TryGetValue(run.UserKey, out double best);
            double? pb = best > 0d ? best : (double?)null;
            ParkourViewState view = new ParkourViewState(
                run.Phase,
                layout.SectorForGate(run.NextGateIndex),
                run.NextGateIndex,
                layout.Platforms.Count,
                run.Elapsed(now),
                Math.Max(0d, run.CountdownEndsAt - now),
                pb);
            try { _hints.ShowLaneHero(player, LaneId, SanitizeY(Config.HeroY, 700f), ParkourText.BuildHero(view, UseChinese), Sanitize(Config.HudX, -1745f, 1745f, -1077f)); }
            catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Parkour hero failed: {ex.Message}"); }
            try { _hints.ShowLaneFooter(player, LaneId, SanitizeY(Config.FooterY, 805f), ParkourText.BuildFooter(run.Phase, UseChinese), Sanitize(Config.HudX, -1745f, 1745f, -1077f)); }
            catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Parkour footer failed: {ex.Message}"); }
        }

        private void Flash(Player player, string kind)
        {
            string text = ParkourText.BuildFlash(kind, UseChinese);
            if (text.Length == 0)
            {
                return;
            }

            try
            {
                _hints.ShowFlash(
                    player,
                    LaneId,
                    SanitizeY(Config.FlashY, 592f),
                    text,
                    FlashDuration,
                    Sanitize(Config.HudX, -1745f, 1745f, -1077f));
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Parkour flash failed: {ex.Message}");
            }
        }

        private void OnSearchingPickup(PlayerSearchingPickupEventArgs ev)
        {
            if (!_running || ev.Player == null || ev.Pickup == null || ev.Pickup.Serial != _world.ResetCoinSerial || !IsEligible(ev.Player))
            {
                return;
            }

            ev.IsAllowed = false;
            string key = SelectorController.Key(ev.Player);
            if (!_sessions.TryGetValue(key, out Session? session) || _world.Layout == null ||
                !_activities.IsCurrent(key, LaneId, session.Run.Token))
            {
                return;
            }

            session.Run.Reset(requireStartExit: false);
            session.LastCompletedGate = -1;
            ev.Player.Position = _world.Layout.StartPlate.RecoveryPosition;
            session.PreviousPosition = ev.Player.Position;
            session.NextHudAt = 0d;
        }

        private void ReleaseSession(string userKey)
        {
            if (string.IsNullOrEmpty(userKey) || !_sessions.TryGetValue(userKey, out Session? session))
            {
                return;
            }

            _sessions.Remove(userKey);
            Player? player = ResolvePlayer(userKey);
            try { if (player != null) _hints.RemoveLane(player, LaneId); else _hints.ForgetLane(userKey, LaneId); }
            catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Parkour hint cleanup failed: {ex.Message}"); }
            try
            {
                if (_activities.IsCurrent(userKey, LaneId, session.Run.Token))
                {
                    _activities.EndSession(userKey);
                }
            }
            catch { }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            PlayerEvents.SearchingPickup += OnSearchingPickup;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            PlayerEvents.SearchingPickup -= OnSearchingPickup;
            _subscribed = false;
        }

        private void StopInternal(bool clearBests)
        {
            _running = false;
            try { Timing.KillCoroutines(_loop); } catch { }
            _loop = default;
            try { Unsubscribe(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Parkour event cleanup failed: {ex.Message}"); }
            foreach (string key in _sessions.Keys.ToList())
            {
                try { ReleaseSession(key); } catch { }
            }

            _sessions.Clear();
            if (clearBests)
            {
                _personalBests.Clear();
            }
            try { _world.Despawn(); } catch (Exception ex) { Logger.Warn($"[WarmupScpSelector] Parkour world cleanup failed: {ex.Message}"); }
        }

        private bool IsEligible(Player player) => player != null && _isOwnedWarmupHuman(player);

        private Player? ResolvePlayer(string userKey)
        {
            return Player.ReadyList.FirstOrDefault(player => IsEligible(player) && string.Equals(SelectorController.Key(player), userKey, StringComparison.Ordinal));
        }

        private static float SanitizeY(float value, float fallback) => Sanitize(value, 0f, 1080f, fallback);

        private static float Sanitize(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
    }
}
