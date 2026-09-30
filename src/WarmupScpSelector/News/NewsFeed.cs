using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WarmupScpSelector.News;

/// <summary>One dated update, as the wiki publishes it.</summary>
public readonly struct NewsEntry
{
    public NewsEntry(DateTime date, string title, string summary)
    {
        Date = date;
        Title = title;
        Summary = summary;
    }

    public DateTime Date { get; }

    public string Title { get; }

    public string Summary { get; }
}

/// <summary>
/// The wiki's <c>updates.json</c>: entries newest first plus the address of the full update log.
///
/// The board is the only consumer, so parsing is deliberately forgiving about extra fields and strict
/// about the three it renders: an entry without a valid date, title and summary is dropped rather than
/// shown half-empty. Text is flattened to one plain line because it ends up inside TextMeshPro markup.
/// </summary>
public sealed class NewsFeed
{
    public const int SupportedVersion = 1;

    private NewsFeed(string link, IReadOnlyList<NewsEntry> entries)
    {
        Link = link;
        Entries = entries;
    }

    /// <summary>Human-readable address of the full update log, without the scheme; empty when unknown.</summary>
    public string Link { get; }

    public IReadOnlyList<NewsEntry> Entries { get; }

    /// <summary>Reads a document already parsed into dictionaries, lists, strings and doubles.</summary>
    public static bool TryRead(object? document, out NewsFeed? feed, out string error)
    {
        feed = null;
        if (document is not Dictionary<string, object?> root)
        {
            error = "the feed is not a JSON object";
            return false;
        }

        if (root.TryGetValue("version", out object? version) && version is double number && (int)number != SupportedVersion)
        {
            error = $"feed version {number} is not supported (expected {SupportedVersion})";
            return false;
        }

        if (!root.TryGetValue("entries", out object? rawEntries) || rawEntries is not List<object?> list)
        {
            error = "the feed has no entries array";
            return false;
        }

        List<NewsEntry> entries = new();
        foreach (object? raw in list)
        {
            if (raw is Dictionary<string, object?> entry &&
                TryDate(entry, out DateTime date) &&
                TryText(entry, "title", out string title) &&
                TryText(entry, "summary", out string summary))
            {
                entries.Add(new NewsEntry(date, title, summary));
            }
        }

        // Newest first regardless of how the file was ordered; OrderBy is stable for same-day entries.
        string link = root.TryGetValue("link", out object? rawLink) && rawLink is string text ? DisplayLink(text) : string.Empty;
        feed = new NewsFeed(link, entries.OrderByDescending(entry => entry.Date).ToList());
        error = string.Empty;
        return true;
    }

    /// <summary>Same content, so a refresh that changed nothing does not re-send the board text.</summary>
    public bool SameContentAs(NewsFeed? other)
    {
        if (other == null || other.Link != Link || other.Entries.Count != Entries.Count)
        {
            return false;
        }

        for (int i = 0; i < Entries.Count; i++)
        {
            NewsEntry a = Entries[i], b = other.Entries[i];
            if (a.Date != b.Date || a.Title != b.Title || a.Summary != b.Summary)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collapses whitespace and control characters to single spaces and neutralises angle brackets, which
    /// TextMeshPro would otherwise read as rich-text tags.
    /// </summary>
    public static string Flatten(string value)
    {
        StringBuilder builder = new(value.Length);
        bool pendingSpace = false;
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c switch
            {
                '<' => '＜',
                '>' => '＞',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static string DisplayLink(string url)
    {
        string link = Flatten(url);
        foreach (string scheme in new[] { "https://", "http://" })
        {
            if (link.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                return link.Substring(scheme.Length);
            }
        }

        return link;
    }

    private static bool TryDate(Dictionary<string, object?> entry, out DateTime date)
    {
        date = default;
        return entry.TryGetValue("date", out object? raw) && raw is string text &&
            DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryText(Dictionary<string, object?> entry, string key, out string text)
    {
        text = entry.TryGetValue(key, out object? raw) && raw is string value ? Flatten(value) : string.Empty;
        return text.Length > 0;
    }
}
