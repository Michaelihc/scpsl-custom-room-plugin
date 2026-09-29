using System.Collections.Generic;

namespace WarmupScpSelector.Text;

/// <summary>
/// Conservative Liberation Sans/TMP horizontal advance in TextToy display units, ported from the
/// in-game-verified estimator in toy-tricks-demo (TextAdvance.cs). CJK counts a full em and ASCII about
/// 0.59 em, so it over- rather than under-estimates; keep lines under DisplaySize.x minus ~10 units.
/// </summary>
public static class TextAdvance
{
    private const string Ellipsis = "…";

    public static float Estimate(string? value, int size, bool bold)
    {
        float em = 0f;
        foreach (char character in value ?? string.Empty)
        {
            em += Em(character);
        }

        return em * size * (bold ? 1.03f : 1f);
    }

    /// <summary>Truncates with an ellipsis so the estimated advance fits <paramref name="safeAdvance"/>.</summary>
    public static string FitLine(string? value, int size, bool bold, float safeAdvance)
    {
        value ??= string.Empty;
        if (Estimate(value, size, bold) <= safeAdvance)
        {
            return value;
        }

        string candidate = value;
        while (candidate.Length > 1 && Estimate(candidate + Ellipsis, size, bold) > safeAdvance)
        {
            candidate = candidate.Substring(0, candidate.Length - 1).TrimEnd();
        }

        return candidate + Ellipsis;
    }

    /// <summary>
    /// Breaks <paramref name="value"/> into at most <paramref name="maxLines"/> lines that each fit
    /// <paramref name="safeAdvance"/>; the last line is ellipsised if text remains. CJK breaks anywhere,
    /// Latin prefers the last space so a word is not split, unless that would waste most of the line.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string value, int size, bool bold, float safeAdvance, int maxLines)
    {
        List<string> lines = new();
        string rest = value.Trim();
        while (rest.Length > 0 && lines.Count < maxLines)
        {
            if (lines.Count == maxLines - 1 || Estimate(rest, size, bold) <= safeAdvance)
            {
                lines.Add(FitLine(rest, size, bold, safeAdvance));
                break;
            }

            int fit = 0;
            float advance = 0f;
            float perEm = size * (bold ? 1.03f : 1f);
            while (fit < rest.Length && advance + Em(rest[fit]) * perEm <= safeAdvance)
            {
                advance += Em(rest[fit]) * perEm;
                fit++;
            }

            int split = fit;
            if (split < rest.Length && IsWordChar(rest[split]) && split > 0 && IsWordChar(rest[split - 1]))
            {
                int space = rest.LastIndexOf(' ', split - 1, split);
                if (space > split * 0.6f)
                {
                    split = space;
                }
            }

            split = System.Math.Max(split, 1); // a glyph wider than the line still has to advance
            lines.Add(rest.Substring(0, split).TrimEnd());
            rest = rest.Substring(split).TrimStart();
        }

        return lines;
    }

    private static float Em(char character) => character switch
    {
        ' ' => 0.34f,
        '·' or '/' or ':' or '-' => 0.48f,
        <= '\u007f' => 0.59f,
        _ => 1f,
    };

    private static bool IsWordChar(char character) => character <= '\u007f' && char.IsLetterOrDigit(character);
}
