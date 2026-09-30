using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LabApi.Features.Wrappers;
using LabApi.Loader;
using MEC;
using UnityEngine;
using WarmupScpSelector.News;
using WarmupScpSelector.Text;
using Logger = LabApi.Features.Console.Logger;
using PrimitiveFlags = AdminToys.PrimitiveFlags;

namespace WarmupScpSelector.Warmup;

/// <summary>
/// The recent-updates board: a dark framed panel on a station wall with one TextToy listing the newest
/// entries of the wiki's update log.
///
/// The text is never compiled in. Each build shows the cached feed at once, reads the live feed off the
/// main thread, and swaps the text when it arrives; the board stays hidden until some feed has been
/// read. Its toys are kept out of the station's list so the schematic export can skip them: a static
/// copy baked into an authored station would show stale text behind the live board.
/// </summary>
internal sealed class NewsBoard
{
    private const float PanelWidth = 5.2f;
    private const float PanelHeight = 3.7f;
    private const float FrameBorder = 0.12f;
    private const float PlateThickness = 0.04f;
    private const float StripHeight = 0.06f;
    private const float StripThickness = 0.02f;

    // Distances out from the wall face. The frame clears the station's 0.02 m proud wall rib, and each
    // layer sits 0.04 m in front of the one behind it, the spacing the hatch signs use against depth fighting.
    private const float FrameOffset = 0.07f;
    private const float PanelOffset = 0.11f;
    private const float StripOffset = 0.15f;
    private const float TextOffset = 0.17f;

    /// <summary>Metres per TextToy display unit at scale 1 (in-game calibration from toy-tricks-demo).</summary>
    private const float TextUnitsToMeters = 0.05f;

    /// <summary>Printable width inside the panel's side margins.</summary>
    private const float TextWidth = PanelWidth - 0.4f;

    private const float PollSeconds = 0.5f;

    private readonly WarmupScpSelectorPlugin _plugin;
    private readonly List<AdminToy> _toys = new();
    private NewsFeedSource? _source;
    private WarmupHallLayout? _hall;
    private TextToy? _text;
    private NewsFeed? _shown;
    private CoroutineHandle _poll;
    private int _generation;

    public NewsBoard(WarmupScpSelectorPlugin plugin)
    {
        _plugin = plugin;
    }

    private NewsBoardConfig Config => _plugin.Config.NewsBoard ?? new NewsBoardConfig();

    private bool UseChinese => string.Equals(_plugin.Config.Language, "cn", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="toy"/> belongs to the board, so exports can leave it out.</summary>
    public bool Owns(AdminToy toy) => _toys.Contains(toy);

    /// <summary>Shows the cached feed now and starts reading the live one. Never throws.</summary>
    public void Spawn(WarmupHallLayout hall)
    {
        Despawn();
        NewsBoardConfig config = Config;
        string feedUrl = config.FeedUrl?.Trim() ?? string.Empty;
        if (!config.Enabled || feedUrl.Length == 0)
        {
            return;
        }

        try
        {
            _hall = hall;
            _source ??= new NewsFeedSource(_plugin.GetConfigDirectory().FullName);
            if (NewsFeedSource.IsHttp(feedUrl))
            {
                NewsFeedResult cached = _source.LoadCache();
                if (cached.Feed != null)
                {
                    Show(cached.Feed);
                }
            }

            TimeSpan timeout = TimeSpan.FromSeconds(Mathf.Clamp(config.FetchTimeoutSeconds, 1f, 30f));
            _poll = Timing.RunCoroutine(AwaitFetch(_generation, feedUrl, _source.FetchAsync(feedUrl, timeout)));
        }
        catch (Exception ex)
        {
            Logger.Warn($"[WarmupScpSelector] News board could not start: {ex.Message}");
            Despawn();
        }
    }

    public void Despawn()
    {
        _generation++; // a fetch still in flight now finds itself stale and is dropped
        Timing.KillCoroutines(_poll);
        for (int i = _toys.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!_toys[i].IsDestroyed)
                {
                    _toys[i].Destroy();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] Failed to destroy news board toy: {ex.Message}");
            }
        }

        _toys.Clear();
        _text = null;
        _shown = null;
        _hall = null;
    }

    private IEnumerator<float> AwaitFetch(int generation, string feedUrl, Task<NewsFeedResult> fetch)
    {
        while (!fetch.IsCompleted)
        {
            yield return Timing.WaitForSeconds(PollSeconds);
            if (generation != _generation)
            {
                yield break;
            }
        }

        if (generation != _generation)
        {
            yield break;
        }

        NewsFeedResult result = fetch.Result; // FetchAsync reports failures in the result, never by throwing
        if (result.Error.Length > 0)
        {
            string outcome = result.Feed != null ? "showing it anyway" :
                _shown != null ? "showing the cached copy" : "the board stays hidden";
            // Mono socket errors carry a line break and NUL padding; keep the warning on one log line.
            string error = NewsFeed.Flatten(result.Error).TrimEnd('.');
            Logger.Warn($"[WarmupScpSelector] News board feed '{feedUrl}': {error}; {outcome}.");
        }

        if (result.Feed != null)
        {
            try
            {
                Show(result.Feed);
                _plugin.LogDebug($"News board shows {result.Feed.Entries.Count} feed entries from '{feedUrl}'.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WarmupScpSelector] News board could not show the feed: {ex.Message}");
            }
        }
    }

    private void Show(NewsFeed feed)
    {
        if (_hall == null || (_text != null && feed.SameContentAs(_shown)))
        {
            return;
        }

        string markup = NewsBoardText.Build(feed, Mathf.Clamp(Config.MaxEntries, 1, 8), UseChinese);
        if (_text == null)
        {
            Build(_hall, markup);
        }
        else
        {
            _text.TextFormat = markup;
        }

        _shown = feed;
    }

    private void Build(WarmupHallLayout hall, string markup)
    {
        NewsBoardConfig config = Config;
        Vector3 center = hall.World(config.Position.x, config.Position.y, config.Position.z);
        Vector3 front = Quaternion.Euler(0f, config.FacingYaw, 0f) * Vector3.forward;

        // Readers look into the wall, along -front. TextToy text reads along its local +Z, and sharing the
        // rotation lines the plates' width up with the wall.
        Quaternion rotation = Quaternion.LookRotation(-front, Vector3.up);

        AddPlate(center + front * FrameOffset, rotation,
            new Vector3(PanelWidth + 2f * FrameBorder, PanelHeight + 2f * FrameBorder, PlateThickness), StationPalette.Frame);
        AddPlate(center + front * PanelOffset, rotation,
            new Vector3(PanelWidth, PanelHeight, PlateThickness), StationPalette.DisplayPanel);
        AddPlate(center + front * StripOffset + Vector3.up * ((PanelHeight - StripHeight) / 2f), rotation,
            new Vector3(PanelWidth, StripHeight, StripThickness), StationPalette.Guide);

        float scale = TextWidth / (NewsBoardText.DisplayWidth * TextUnitsToMeters);
        TextToy text = TextToy.Create(center + front * TextOffset, rotation, Vector3.one * scale, networkSpawn: false);
        _toys.Add(text);
        text.TextFormat = markup;
        text.DisplaySize = new Vector2(NewsBoardText.DisplayWidth, NewsBoardText.DisplayHeight);
        text.IsStatic = true;
        text.Spawn();
        _text = text;
    }

    private void AddPlate(Vector3 center, Quaternion rotation, Vector3 size, Color color)
    {
        PrimitiveObjectToy toy = PrimitiveObjectToy.Create(center, rotation, size, networkSpawn: false);
        _toys.Add(toy); // track before configure/spawn so a throw mid-setup is still torn down
        toy.Type = PrimitiveType.Cube;
        toy.Color = color;
        toy.Flags = PrimitiveFlags.Visible;
        toy.IsStatic = true;
        toy.Spawn();
    }
}
