using System.Collections.Generic;
using System.Globalization;
using WarmupScpSelector.News;

namespace WarmupScpSelector.Text;

/// <summary>
/// TextMeshPro markup for the recent-updates board. Pure string work, sized in TextToy display units:
/// every line is fitted with <see cref="TextAdvance"/> and wrapped in <c>nobr</c>, and entries are added
/// only while the estimated height still fits, so a long feed shortens the list instead of spilling off
/// the panel.
/// </summary>
public static class NewsBoardText
{
    /// <summary>Text rect width in display units; the board scales it to the panel's printable width.</summary>
    public const float DisplayWidth = 480f;

    /// <summary>Height budget in display units for heading, entries and footer together.</summary>
    public const float DisplayHeight = 300f;

    private const float SafeWidth = DisplayWidth - 12f;

    private const int HeadingSize = 30;
    private const int DateSize = 15;
    private const int TitleSize = 21;
    private const int SummarySize = 15;
    private const int FooterSize = 13;
    private const int GapSize = 9;
    private const int SummaryLines = 2;

    /// <summary>Line advance per unit of font size. Liberation Sans sets about 1.2; 1.25 keeps the budget safe.</summary>
    private const float LineAdvance = 1.25f;

    private const string HeadingColor = "#FF9228";
    private const string DateColor = "#B6A4E8";
    private const string TitleColor = "#E7ECF3";
    private const string SummaryColor = "#AEB8C6";
    private const string MutedColor = "#8B95A6";
    private const string LinkColor = "#9CC8F0";

    /// <summary>Ideographic space between the date and the title: one em at the date size.</summary>
    private const string DateGap = "　";

    public static string Build(NewsFeed feed, int maxEntries, bool chinese)
    {
        List<string> lines = new();
        AppendLine(lines, $"<size={HeadingSize}><b><color={HeadingColor}>{(chinese ? "最近更新" : "RECENT UPDATES")}</color></b></size>");
        AppendGap(lines);
        float used = Line(HeadingSize) + Line(GapSize);

        string footer = Footer(feed.Link, chinese);
        float reserved = footer.Length > 0 ? Line(GapSize) + Line(FooterSize) : 0f;

        int shown = 0;
        foreach (NewsEntry entry in feed.Entries)
        {
            if (shown >= maxEntries)
            {
                break;
            }

            string date = entry.Date.ToString("MM-dd", CultureInfo.InvariantCulture);
            float titleWidth = SafeWidth - TextAdvance.Estimate(date + DateGap, DateSize, bold: false);
            string title = TextAdvance.FitLine(entry.Title, TitleSize, bold: true, titleWidth);
            IReadOnlyList<string> summary = TextAdvance.Wrap(entry.Summary, SummarySize, bold: false, SafeWidth, SummaryLines);

            float height = (shown > 0 ? Line(GapSize) : 0f) + Line(TitleSize) + summary.Count * Line(SummarySize);
            if (shown > 0 && used + height + reserved > DisplayHeight)
            {
                break;
            }

            if (shown > 0)
            {
                AppendGap(lines);
            }

            AppendLine(lines,
                $"<size={DateSize}><color={DateColor}>{date}{DateGap}</color></size>" +
                $"<size={TitleSize}><b><color={TitleColor}>{title}</color></b></size>");
            foreach (string line in summary)
            {
                AppendLine(lines, $"<size={SummarySize}><color={SummaryColor}>{line}</color></size>");
            }

            used += height;
            shown++;
        }

        if (shown == 0)
        {
            AppendLine(lines, $"<size={SummarySize}><color={MutedColor}>{(chinese ? "暂无更新" : "No updates yet")}</color></size>");
        }

        if (footer.Length > 0)
        {
            AppendGap(lines);
            AppendLine(lines, footer);
        }

        return "<align=left>" + string.Join("\n", lines) + "</align>";
    }

    private static string Footer(string link, bool chinese)
    {
        if (link.Length == 0)
        {
            return string.Empty;
        }

        string label = (chinese ? "完整更新日志" : "Full update log") + DateGap;
        string fitted = TextAdvance.FitLine(link, FooterSize, bold: false, SafeWidth - TextAdvance.Estimate(label, FooterSize, bold: false));
        return $"<size={FooterSize}><color={MutedColor}>{label}</color><color={LinkColor}>{fitted}</color></size>";
    }

    private static float Line(int size) => size * LineAdvance;

    private static void AppendGap(List<string> lines) => lines.Add($"<size={GapSize}> </size>");

    private static void AppendLine(List<string> lines, string markup) => lines.Add("<nobr>" + markup + "</nobr>");
}
