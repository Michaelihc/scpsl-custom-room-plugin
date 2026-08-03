using System.Text;

namespace WarmupScpSelector.Text
{
    /// <summary>
    /// The suite's signature glyphs with registered ASCII fallbacks (spec §1.9 "glyph reality gate"). Only
    /// <c>● ▶ | · —　</c> are proven in shipped SCP:SL TMP; the signature glyphs <c>━ ╌ │ ◆ ◇ ▲ ▼</c> are
    /// validated in <c>tools/preview/banner.html</c> and fall back to ASCII when the server font can't render
    /// them (via <c>Config.Activities.UseAsciiGlyphFallback</c>). Pure string work — no Unity/LabAPI types —
    /// so both the fallback map and any lane string it produces stay headless-testable.
    /// </summary>
    public static class ActivityGlyphs
    {
        /// <summary>One signature glyph plus its ASCII fallback.</summary>
        public sealed class Glyph
        {
            public Glyph(string unicode, string ascii)
            {
                Unicode = unicode;
                Ascii = ascii;
            }

            public string Unicode { get; }

            public string Ascii { get; }

            /// <summary>The glyph to emit: the ASCII fallback when <paramref name="useAscii"/>, else the Unicode form.</summary>
            public string For(bool useAscii) => useAscii ? Ascii : Unicode;
        }

        public static readonly Glyph Track = new Glyph("━", "--");          // line track
        public static readonly Glyph SeveredTrack = new Glyph("╌", "··");   // broken / severed track
        public static readonly Glyph Bar = new Glyph("│", "|");             // PB gate / separator
        public static readonly Glyph You = new Glyph("◆", "#");             // you (race marker)
        public static readonly Glyph Rival = new Glyph("◇", "o");           // rival (race marker)
        public static readonly Glyph Ahead = new Glyph("▲", "+");           // ahead of PB / pace
        public static readonly Glyph Behind = new Glyph("▼", "-");          // behind PB / pace

        // Ordered longest-Unicode-first is unnecessary here (each is a single code point), but keeping every
        // signature glyph in one table means a lane never hand-rolls a fallback and the gate has one source.
        private static readonly Glyph[] All = { Track, SeveredTrack, Bar, You, Rival, Ahead, Behind };

        /// <summary>
        /// Replace every signature glyph in a pre-built string with its ASCII fallback. A no-op when
        /// <paramref name="useAscii"/> is false, so a caller can thread the config flag straight through.
        /// </summary>
        public static string Resolve(string text, bool useAscii)
        {
            if (!useAscii || string.IsNullOrEmpty(text))
            {
                return text;
            }

            StringBuilder sb = new StringBuilder(text);
            foreach (Glyph glyph in All)
            {
                sb.Replace(glyph.Unicode, glyph.Ascii);
            }

            return sb.ToString();
        }
    }
}
