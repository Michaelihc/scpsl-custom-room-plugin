using System;
using HsmAdapter;
using LabApi.Features.Wrappers;
using WarmupScpSelector.Activities;
using Logger = LabApi.Features.Console.Logger;

namespace WarmupScpSelector.Services;

// The station owns its lane names and positions; HsmAdapter owns HSM handles,
// unchanged-update suppression, expiry and teardown.
internal sealed class HsmHintDisplayProvider
{
    private const string DefaultTagPrefix = "warmupscp.";
    private readonly HintDisplayConfig _config;
    private HintScope? _scope;

    public HsmHintDisplayProvider(HintDisplayConfig config) => _config = config ?? new HintDisplayConfig();

    public void Enable()
    {
        _scope ??= HsmAdapter.Hints.Acquire("WarmupScpSelector");
        Logger.Info("[WarmupScpSelector:Hints] Using HsmAdapter.");
    }

    public void Disable()
    {
        _scope?.Dispose();
        _scope = null;
    }

    public void ShowPrompt(Player player, string tagId, float y, string message, float? xOverride = null)
        => Show(player, tagId, y, message, xOverride, duration: 0);

    public void Remove(Player player, string tagId)
    {
        if (player?.ReferenceHub != null)
            _scope?.Remove(player, NormalizeTagId(tagId));
    }

    public void ShowLaneHero(Player player, string laneId, float y, string message, float? xOverride = null)
        => ShowPrompt(player, LaneHintIds.Hero(laneId), y, message, xOverride);

    public void ShowLaneFooter(Player player, string laneId, float y, string message, float? xOverride = null)
        => ShowPrompt(player, LaneHintIds.Footer(laneId), y, message, xOverride);

    public void ShowFlash(Player player, string laneId, float y, string message, float durationSeconds, float? xOverride = null)
    {
        if (!IsDisplayable(player) || _scope == null)
            return;

        string key = NormalizeTagId(LaneHintIds.Flash(laneId));
        // A repeat verdict must restart its visible flash even when the text is identical.
        // The adapter owns the new deadline, so an earlier expiry cannot clear this one.
        _scope.Remove(player, key);
        Show(player, key, y, message, xOverride,
            Math.Max(0.05f, Math.Min(10f, Sanitize(durationSeconds, 0.7f))));
    }

    public void RemoveLane(Player player, string laneId)
    {
        Remove(player, LaneHintIds.Hero(laneId));
        Remove(player, LaneHintIds.Flash(laneId));
        Remove(player, LaneHintIds.Footer(laneId));
    }

    private void Show(Player player, string tagId, float y, string message, float? xOverride, float duration)
    {
        if (!IsDisplayable(player) || _scope == null)
            return;

        float x = Sanitize(xOverride ?? _config.DefaultX, Sanitize(_config.DefaultX, 0f));
        var layout = new HsmHintLayout(message ?? string.Empty, x, Sanitize(y, 220f),
            fontSize: Math.Max(6, _config.PromptTextSize), anchor: VerticalAnchor.Middle,
            syncSpeed: HsmSyncSpeed.Fast, fastUpdate: _config.ForceFastUpdates,
            lineHeight: Math.Max(0f, _config.LineHeight), forceUpdate: _config.ForceFastUpdates);
        _scope.ShowHsm(player, NormalizeTagId(tagId), layout, duration);
    }

    private string NormalizeTagId(string tagId)
    {
        string prefix = string.IsNullOrWhiteSpace(_config.TagPrefix) ? DefaultTagPrefix : _config.TagPrefix;
        string id = string.IsNullOrWhiteSpace(tagId) ? "status" : tagId.Trim();
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id : prefix + id;
    }

    private static float Sanitize(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private static bool IsDisplayable(Player player)
        => player?.ReferenceHub != null && (player.IsDummy || (player.IsPlayer && player.IsReady));
}
