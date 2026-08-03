using System;
using System.Collections.Generic;
using HintServiceMeow.Core.Enum;
using HintServiceMeow.Core.Models.Hints;
using HintServiceMeow.Core.Utilities;
using LabApi.Features.Wrappers;
using MEC;
using WarmupScpSelector.Activities;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Services;

internal sealed class HsmHintDisplayProvider
{
    private const string LogPrefix = "[WarmupScpSelector:Hints]";
    private const string DefaultTagPrefix = "warmupscp.";

    private readonly HintDisplayConfig _config;

    // Activity-lane flash bookkeeping (see ShowFlash). The token tracker is pure/headless-tested; the handle
    // map lets a re-armed flash cancel the previous expiry coroutine so timers can't accumulate.
    private readonly LaneFlashTracker _flash = new();
    private readonly Dictionary<string, CoroutineHandle> _flashTimers = new(StringComparer.Ordinal);

    // Change-skip cache for every NON-flash stable hint (status / lane hero / lane footer), keyed by
    // player + normalized hint id, holding the last Y+text signature pushed. An identical resubmission is
    // skipped so it never reaches HSM or the network (the per-second hint loop goes quiet unless something
    // actually changed). Flash deliberately bypasses this (ShowFlash never touches it) so an identical repeat
    // verdict genuinely reappears. A fresh PlayerDisplay (reconnect) has no existing hint, so the "does the hint
    // already exist" check below still forces a re-push even on a cache hit. Remove/Disable clear entries.
    private readonly HintChangeCache _promptCache = new();

    public HsmHintDisplayProvider(HintDisplayConfig config)
    {
        _config = config ?? new HintDisplayConfig();
    }

    public void Enable()
    {
        Logger.Info($"{LogPrefix} Using HintServiceMeow.");
    }

    public void Disable()
    {
        // Cancel any outstanding flash-expiry coroutines so a delayed removal can't fire after teardown.
        foreach (CoroutineHandle handle in _flashTimers.Values)
        {
            Timing.KillCoroutines(handle);
        }

        _flashTimers.Clear();
        _promptCache.Clear();
    }

    /// <summary>
    /// Draw/refresh a persistent hint in place. <paramref name="xOverride"/> lets a caller place an individual
    /// hint on a different HSM X than the global <see cref="HintDisplayConfig.DefaultX"/> — e.g. the warmup status
    /// panel keeps the centered default outside an activity, but collapses into the narrow left Aim lane while a
    /// player is inside the range. Pass <c>null</c> for the global default. X is folded into the change-skip
    /// signature, so a hint that only moves horizontally still re-pushes.
    /// </summary>
    public void ShowPrompt(Player player, string tagId, float y, string message, float? xOverride = null)
    {
        if (!IsDisplayable(player))
        {
            return;
        }

        message ??= string.Empty;
        string id = NormalizeTagId(tagId);
        string cacheKey = PlayerKey(player) + "|" + id;
        float x = ResolveX(xOverride);
        string signature = HintChangeCache.Signature(x, SanitizeCoordinate(y, 220f), message);

        PlayerDisplay display = PlayerDisplay.Get(player);
        AbstractHint? existingHint = display.GetHint(id);

        if (existingHint is Hint existing)
        {
            // Skip only when the hint STILL EXISTS and its last X+Y+text signature is unchanged. A reconnect
            // recreates the PlayerDisplay (existing == null below), so a stale cache entry can never suppress
            // a genuinely-needed first push.
            if (_promptCache.Matches(cacheKey, signature))
            {
                return;
            }

            UpdateHint(existing, x, y, message);
            _promptCache.Set(cacheKey, signature);
            ForceUpdate(display, _config.ForceFastUpdates);
            return;
        }

        if (existingHint != null)
        {
            display.RemoveHint(existingHint);
        }

        Hint hint = new Hint { Id = id };
        UpdateHint(hint, x, y, message);
        display.AddHint(hint);
        _promptCache.Set(cacheKey, signature);
        ForceUpdate(display, useFastUpdate: true);
    }

    public void Remove(Player player, string tagId)
    {
        if (player == null)
        {
            return;
        }

        string id = NormalizeTagId(tagId);
        _promptCache.Remove(PlayerKey(player) + "|" + id);
        if (player.ReferenceHub == null)
        {
            return;
        }

        PlayerDisplay display = PlayerDisplay.Get(player);
        display.RemoveHint(id);
        ForceUpdate(display, useFastUpdate: true);
    }

    // ---- Activity-lane zones (hero / flash / footer) --------------------------------------------------
    // Each active lane owns three stable HSM IDs at fixed Y — warmupscp.<lane>.hero/.flash/.footer — alongside
    // the shipped warmupscp.status draft panel. Hero and footer are ordinary in-place updates (they reuse the
    // caller's change-skip cache); the flash needs a dedicated force-show + self-clear path so an identical
    // repeat verdict genuinely reappears.

    /// <summary>Update the lane's persistent hero line (score / clock / round score) in place, on the lane X.</summary>
    public void ShowLaneHero(Player player, string laneId, float y, string message, float? xOverride = null)
    {
        ShowPrompt(player, LaneHintIds.Hero(laneId), y, message, xOverride);
    }

    /// <summary>Update the lane's single muted coaching footer in place, on the lane X.</summary>
    public void ShowLaneFooter(Player player, string laneId, float y, string message, float? xOverride = null)
    {
        ShowPrompt(player, LaneHintIds.Footer(laneId), y, message, xOverride);
    }

    /// <summary>
    /// Force-show a verdict on the lane's <c>.flash</c> id and auto-clear it after <paramref name="durationSeconds"/>.
    /// Unlike <see cref="ShowPrompt"/> this always re-pushes (so an identical repeat judgment reappears) and the
    /// removal is token-guarded: re-arming bumps the token, so an in-flight expiry from an earlier flash becomes
    /// a no-op and never clears the newer one early.
    /// </summary>
    public void ShowFlash(Player player, string laneId, float y, string message, float durationSeconds, float? xOverride = null)
    {
        if (!IsDisplayable(player))
        {
            return;
        }

        string key = PlayerKey(player);
        string timerKey = key + "|" + laneId;
        int token = _flash.Arm(key, laneId);
        float x = ResolveX(xOverride);

        try
        {
            string id = NormalizeTagId(LaneHintIds.Flash(laneId));
            PlayerDisplay display = PlayerDisplay.Get(player);
            AbstractHint? existingHint = display.GetHint(id);
            if (existingHint is Hint existing)
            {
                UpdateHint(existing, x, y, message);
            }
            else
            {
                if (existingHint != null)
                {
                    display.RemoveHint(existingHint);
                }

                Hint hint = new Hint { Id = id };
                UpdateHint(hint, x, y, message);
                display.AddHint(hint);
            }

            ForceUpdate(display, useFastUpdate: true);
        }
        catch (Exception ex)
        {
            Logger.Warn($"{LogPrefix} Flash show failed: {ex.Message}");
        }

        float duration = SanitizeCoordinate(durationSeconds, 0.7f);
        duration = Math.Max(0.05f, Math.Min(10f, duration));

        if (_flashTimers.TryGetValue(timerKey, out CoroutineHandle previous))
        {
            Timing.KillCoroutines(previous);
        }

        _flashTimers[timerKey] = Timing.CallDelayed(duration, () =>
        {
            _flashTimers.Remove(timerKey);
            try
            {
                if (_flash.ShouldClear(key, laneId, token) && player?.ReferenceHub != null)
                {
                    _flash.Clear(key, laneId);
                    PlayerDisplay display = PlayerDisplay.Get(player);
                    display.RemoveHint(NormalizeTagId(LaneHintIds.Flash(laneId)));
                    ForceUpdate(display, useFastUpdate: true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"{LogPrefix} Flash expiry failed: {ex.Message}");
            }
        });
    }

    /// <summary>Remove all three of a lane's zones for one player and cancel any pending flash expiry.</summary>
    public void RemoveLane(Player player, string laneId)
    {
        if (player == null)
        {
            return;
        }

        ForgetLane(PlayerKey(player), laneId);
        if (player.ReferenceHub == null)
        {
            return;
        }

        Remove(player, LaneHintIds.Hero(laneId));
        Remove(player, LaneHintIds.Flash(laneId));
        Remove(player, LaneHintIds.Footer(laneId));
    }

    /// <summary>
    /// Clear provider-only lane state when the departing wrapper can no longer be resolved through ReadyList.
    /// The key is the same authenticated player key used while the hints were shown.
    /// </summary>
    public void ForgetLane(string playerKey, string laneId)
    {
        if (string.IsNullOrEmpty(playerKey) || string.IsNullOrEmpty(laneId))
        {
            return;
        }

        string timerKey = playerKey + "|" + laneId;
        if (_flashTimers.TryGetValue(timerKey, out CoroutineHandle handle))
        {
            Timing.KillCoroutines(handle);
            _flashTimers.Remove(timerKey);
        }

        _flash.Clear(playerKey, laneId);
        _promptCache.Remove(playerKey + "|" + NormalizeTagId(LaneHintIds.Hero(laneId)));
        _promptCache.Remove(playerKey + "|" + NormalizeTagId(LaneHintIds.Flash(laneId)));
        _promptCache.Remove(playerKey + "|" + NormalizeTagId(LaneHintIds.Footer(laneId)));
    }

    private void UpdateHint(Hint hint, float x, float y, string message)
    {
        hint.Id = string.IsNullOrWhiteSpace(hint.Id) ? NormalizeTagId("status") : hint.Id;
        hint.Text = message ?? string.Empty;
        hint.XCoordinate = x;
        hint.YCoordinate = SanitizeCoordinate(y, 220f);
        hint.Alignment = HintAlignment.Center;
        hint.YCoordinateAlign = HintVerticalAlign.Middle;
        hint.SyncSpeed = HintSyncSpeed.Fast;
        hint.FontSize = Math.Max(6, _config.PromptTextSize);
        hint.LineHeight = Math.Max(0f, _config.LineHeight);
    }

    // Resolve the HSM X a hint should render at: an explicit per-hint override when supplied (the narrow left
    // Aim lane), otherwise the global centered default. Always sanitized so a bad config/override can't push a
    // NaN/Infinity coordinate into HSM.
    private float ResolveX(float? xOverride)
    {
        return SanitizeCoordinate(xOverride ?? _config.DefaultX, SanitizeCoordinate(_config.DefaultX, 0f));
    }

    private void ForceUpdate(PlayerDisplay display, bool useFastUpdate)
    {
        if (useFastUpdate)
        {
            display.ForceUpdate(useFastUpdate: true);
        }
    }

    private string NormalizeTagId(string tagId)
    {
        string prefix = string.IsNullOrWhiteSpace(_config.TagPrefix) ? DefaultTagPrefix : _config.TagPrefix;
        string safeTagId = string.IsNullOrWhiteSpace(tagId) ? "status" : tagId.Trim();
        return safeTagId.StartsWith(prefix, StringComparison.Ordinal) ? safeTagId : prefix + safeTagId;
    }

    private static float SanitizeCoordinate(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static bool IsDisplayable(Player player)
    {
        return player?.ReferenceHub != null && (player.IsDummy || (player.IsPlayer && player.IsReady));
    }

    // Stable per-player key for flash bookkeeping, mirroring SelectorController.Key: UserId when available,
    // else the ReferenceHub instance id, so a reconnecting or dummy player is never confused with another.
    private static string PlayerKey(Player player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(player.UserId))
        {
            return player.UserId;
        }

        return player.ReferenceHub != null
            ? player.ReferenceHub.GetInstanceID().ToString()
            : player.GetHashCode().ToString();
    }
}
