using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarmupScpSelector.Models;

namespace WarmupScpSelector.News;

/// <summary>Outcome of one feed read: a feed, or why there is none.</summary>
internal readonly struct NewsFeedResult
{
    private NewsFeedResult(NewsFeed? feed, string error)
    {
        Feed = feed;
        Error = error;
    }

    public NewsFeed? Feed { get; }

    /// <summary>Why there is no feed, or a problem worth logging that still produced one.</summary>
    public string Error { get; }

    public static NewsFeedResult Success(NewsFeed feed, string warning = "") => new(feed, warning);

    public static NewsFeedResult Failure(string error) => new(null, error);
}

/// <summary>
/// Reads the update feed from the wiki (or a local file) and keeps the last good copy on disk.
///
/// The board text lives on the website so it can change without a plugin release, which means the
/// server must never depend on the website being up: every build shows the cached copy first, the fetch
/// runs off the main thread with a hard timeout and size cap, and a failed fetch leaves the cache alone.
/// </summary>
internal sealed class NewsFeedSource
{
    public const string CacheFileName = "news-board-cache.json";

    /// <summary>The real feed is a few kilobytes; anything near this is not the feed.</summary>
    private const int MaxFeedBytes = 256 * 1024;

    // One client for the process, as HttpClient intends; the per-request timeout comes from a token.
    private static readonly HttpClient Http = CreateClient();

    private readonly string _configDirectory;

    public NewsFeedSource(string configDirectory)
    {
        _configDirectory = configDirectory;
    }

    private string CachePath => Path.Combine(_configDirectory, CacheFileName);

    public static bool IsHttp(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The last good copy, or a failure when there is none or it no longer parses.</summary>
    public NewsFeedResult LoadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? Parse(File.ReadAllText(CachePath, Encoding.UTF8))
                : NewsFeedResult.Failure("no cached copy");
        }
        catch (Exception ex)
        {
            return NewsFeedResult.Failure($"cache unreadable: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Reads <paramref name="source"/> on a worker thread. An HTTP result that parses replaces the cache;
    /// a local file is its own copy and is not cached. Never throws: failures come back as a result.
    /// </summary>
    public Task<NewsFeedResult> FetchAsync(string source, TimeSpan timeout) => Task.Run(async () =>
    {
        try
        {
            if (!IsHttp(source))
            {
                string path = Path.IsPathRooted(source) ? source : Path.Combine(_configDirectory, source);
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }

            string json = await DownloadAsync(source, timeout).ConfigureAwait(false);
            NewsFeedResult result = Parse(json);
            if (result.Feed == null)
            {
                return result;
            }

            try
            {
                WriteCache(json);
                return result;
            }
            catch (Exception ex)
            {
                // Still show what arrived; only the offline fallback is lost, and the caller logs why.
                return NewsFeedResult.Success(result.Feed, $"could not update {CacheFileName}: {ex.GetBaseException().Message}");
            }
        }
        catch (OperationCanceledException)
        {
            return NewsFeedResult.Failure($"no response within {timeout.TotalSeconds:0.#} s");
        }
        catch (Exception ex)
        {
            return NewsFeedResult.Failure(ex.GetBaseException().Message);
        }
    });

    private static async Task<string> DownloadAsync(string url, TimeSpan timeout)
    {
        using CancellationTokenSource cancel = new(timeout);
        using HttpResponseMessage response = await Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (response.Content.Headers.ContentLength > MaxFeedBytes)
        {
            throw new InvalidDataException($"feed is {response.Content.Headers.ContentLength} bytes");
        }

        using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancel.Token).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFeedBytes)
            {
                throw new InvalidDataException($"feed exceeds {MaxFeedBytes} bytes");
            }
        }

        // The web server sends application/json without a charset, so decode explicitly; the feed is UTF-8.
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static NewsFeedResult Parse(string json)
    {
        try
        {
            return NewsFeed.TryRead(MerModelLoader.ParseJson(json.TrimStart('﻿')), out NewsFeed? feed, out string error)
                ? NewsFeedResult.Success(feed!)
                : NewsFeedResult.Failure(error);
        }
        catch (FormatException ex)
        {
            return NewsFeedResult.Failure($"invalid JSON: {ex.Message}");
        }
    }

    /// <summary>Write-then-swap so a crash mid-write never leaves a truncated cache behind.</summary>
    private void WriteCache(string json)
    {
        string temp = CachePath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        if (File.Exists(CachePath))
        {
            File.Delete(CachePath);
        }

        File.Move(temp, CachePath);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WarmupScpSelector-NewsBoard/1.0");
        return client;
    }
}
