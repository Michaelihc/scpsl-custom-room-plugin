using System.ComponentModel;
using UnityEngine;

namespace WarmupScpSelector.News;

/// <summary>Settings for the recent-updates board in the warmup station.</summary>
public sealed class NewsBoardConfig
{
    [Description("Show the recent-updates board in the warmup station. The board is hidden while no feed has ever been read.")]
    public bool Enabled { get; set; } = true;

    [Description("Where the board's entries come from: the wiki's updates.json over http(s), or a file path (relative paths resolve under this plugin's config folder). Read every time the station is built, so publishing the wiki changes the board without a plugin release. The last good http(s) copy is kept in news-board-cache.json and shown first, and whenever the site is unreachable.")]
    public string FeedUrl { get; set; } = "https://scpslservers.com/sr/wiki/updates.json";

    [Description("Seconds to wait for the feed before giving up and keeping the cached copy.")]
    public float FetchTimeoutSeconds { get; set; } = 8f;

    [Description("Most updates shown, newest first. Fewer are shown when long summaries would overflow the board.")]
    public int MaxEntries { get; set; } = 4;

    [Description("Board centre on its wall, in station-local metres from the deck centre. The default is the gallery's north wall east of the hatch, behind the spawn and facing the SCP aisle.")]
    public Vector3 Position { get; set; } = new(7.75f, 2.65f, -25.65f);

    [Description("Compass direction the board faces, in degrees around the vertical axis: 0 faces +Z (north), 180 faces -Z (south, into the gallery).")]
    public float FacingYaw { get; set; } = 180f;
}
