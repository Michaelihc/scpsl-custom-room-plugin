using System;
using System.Text;

namespace WarmupScpSelector.Text
{
    /// <summary>Current normal-target type shown in the Aim Range eyebrow.</summary>
    public enum AimTargetKind
    {
        None,
        Static,
        Moving,
    }

    /// <summary>Aggregate owned-bot phase shown in the Aim Range eyebrow/footer.</summary>
    public enum AimBotPhase
    {
        None,
        Passive,
        Retaliating,
        Respawning,
    }

    /// <summary>The single force-shown Aim Range flash verdict.</summary>
    public enum AimFlashKind
    {
        None,
        TargetHit,
        BotProvoked,
        IncomingHit,
        BotDown,
        BotRespawn,
        Reset,
        Handoff,
    }

    /// <summary>
    /// Pure, per-human snapshot of the Aim Range state the HUD renders. The lane populates it from the player's
    /// session plus the live target/bot state; <see cref="AimRangeText"/> turns it into the collision-free hero
    /// card + footer. No Unity/LabAPI types, so both the snapshot and the text stay headless-testable.
    /// </summary>
    public readonly struct AimRangeViewState
    {
        public AimRangeViewState(
            bool hasWeapon,
            string presetId,
            AimTargetKind target,
            AimBotPhase bots,
            int shots,
            int targetHits,
            int botHits,
            int incomingHits)
        {
            HasWeapon = hasWeapon;
            PresetId = presetId ?? string.Empty;
            Target = target;
            Bots = bots;
            Shots = Math.Max(0, shots);
            TargetHits = Math.Max(0, targetHits);
            BotHits = Math.Max(0, botHits);
            IncomingHits = Math.Max(0, incomingHits);
        }

        public bool HasWeapon { get; }

        public string PresetId { get; }

        public AimTargetKind Target { get; }

        public AimBotPhase Bots { get; }

        public int Shots { get; }

        public int TargetHits { get; }

        public int BotHits { get; }

        public int IncomingHits { get; }

        /// <summary>Session hit count shown as the hero when a gun is held.</summary>
        public int SessionHits => TargetHits + BotHits;
    }

    /// <summary>
    /// Builds the bilingual, collision-free Aim Range HUD for the narrow left HUD lane. The whole in-range HUD
    /// renders on one configurable HSM center-X (<see cref="WarmupScpSelector.Activities.AimRange.AimRangeActivityConfig.HudX"/>,
    /// default -1077) so its rendered boxes sit in the ~px216..496 corridor between the native inventory list and
    /// the inventory wheel at 1920x1080 — clear of both while TAB is held. Because the lane is narrow, the HUD is
    /// deliberately compact: a force-shown flash, a persistent three-line hero (a tiny weapon/target/bot eyebrow,
    /// then the short signature state rail, then a compact hits/accuracy strip), and one short active-voice footer.
    /// Every line owns exactly one <c>&lt;size&gt;</c> span — NO nested sizes — so TMP never resolves a nested
    /// scale. Color obeys the plan's HUD law: teal = live/you (your marker + filled rail), white = values,
    /// dim/line = the empty "not-yet" track and dividers, red = incoming only, green = a clean handoff/success
    /// flash. Gold (time) and green (personal best) are deferred — no stored PB exists yet, so the rail reads only
    /// honest live session data (accuracy fill + hit count). Exactly one language is rendered per call. Everything
    /// is pure string work threaded through <see cref="ActivityGlyphs"/> so the glyph-reality fallback is preserved.
    /// </summary>
    public static class AimRangeText
    {
        // 莺歌傲然 palette (mirrors WarmupText): cyan accent for live/you, brand green for the success/handoff
        // verdicts, one red kept for incoming only. Muted/Dim label greys plus the near-black Line keep the rail
        // scannable against the dark facility HUD; Line is the plan's "empty / not-yet" cell color.
        private const string Accent = "#4FCBFF";
        private const string Urgent = "#FF5555";
        private const string Ready = "#5BFF80";
        private const string White = "#E7ECF3";
        private const string Muted = "#8B95A6";
        private const string Dim = "#5B6270";
        private const string Line = "#232A37";

        // The signature rail is a fixed-width track: a teal "you"-filled span (session accuracy) with a teal
        // marker at the frontier, then the dim/Line "not-yet" remainder. Five cells keep the rail (plus marker)
        // comfortably inside the ~284px left HUD lane even when the ASCII fallback doubles each cell.
        private const int RailCells = 5;

        /// <summary>Prettify a shelf/bot preset id into a short weapon label (markup-safe).</summary>
        public static string WeaponLabel(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                return string.Empty;
            }

            string id = Sanitize(presetId).Trim();
            if (id.StartsWith("bot-", StringComparison.OrdinalIgnoreCase))
            {
                id = id.Substring(4);
            }

            switch (id.ToLowerInvariant())
            {
                case "com15": return "COM-15";
                case "com18": return "COM-18";
                case "com45": return "COM-45";
                case "fsp9": return "FSP-9";
                case "crossvec": return "Crossvec";
                case "ak": return "AK";
                case "e11sr": return "E11-SR";
                case "logicer": return "Logicer";
                case "revolver": return "Revolver";
                case "shotgun": return "Shotgun";
                default: return id.ToUpperInvariant();
            }
        }

        /// <summary>
        /// The persistent hero (HSM <c>aim.hero</c>): three stacked lines sized for the narrow left lane — a tiny
        /// weapon / target / bot eyebrow, the short signature state rail (accuracy fill + "you" marker), and a
        /// compact hits/accuracy strip (or TAKE A GUN when unarmed). Three sibling size spans, never nested, each
        /// short enough to stay inside the ~284px lane in both languages.
        /// </summary>
        public static string BuildHero(AimRangeViewState state, bool useChineseLocalization, bool useAsciiGlyphs)
        {
            StringBuilder sb = new StringBuilder(256);
            sb.Append("<align=center>");
            sb.Append("<size=58%>").Append(Eyebrow(state, useChineseLocalization)).Append("</size>\n");
            sb.Append("<size=105%>").Append(BuildRail(state)).Append("</size>\n");
            sb.Append("<size=66%>").Append(StatLine(state, useChineseLocalization)).Append("</size>");
            sb.Append("</align>");
            return ActivityGlyphs.Resolve(sb.ToString(), useAsciiGlyphs);
        }

        /// <summary>The single active-voice coaching footer (HSM <c>aim.footer</c>). One size span, one line.</summary>
        public static string BuildFooter(AimRangeViewState state, bool useChineseLocalization, bool useAsciiGlyphs)
        {
            string text = FooterInstruction(state, useChineseLocalization);
            string built = "<align=center><size=85%>" + Colored(Muted, text) + "</size></align>";
            return ActivityGlyphs.Resolve(built, useAsciiGlyphs);
        }

        /// <summary>The force-shown event verdict (HSM <c>aim.flash</c>). One size span, one line, empty when None.</summary>
        public static string BuildFlash(AimFlashKind kind, bool useChineseLocalization, bool useAsciiGlyphs)
        {
            if (kind == AimFlashKind.None)
            {
                return string.Empty;
            }

            string color;
            string text;
            switch (kind)
            {
                case AimFlashKind.TargetHit:
                    color = Ready;
                    text = useChineseLocalization ? "命中！" : "HIT!";
                    break;
                case AimFlashKind.BotProvoked:
                    color = Accent;
                    text = useChineseLocalization ? "机器人被激怒" : "BOT ENGAGED";
                    break;
                case AimFlashKind.IncomingHit:
                    color = Urgent;
                    text = useChineseLocalization ? "你被击中！" : "INCOMING!";
                    break;
                case AimFlashKind.BotDown:
                    color = Ready;
                    text = useChineseLocalization ? "机器人倒下" : "BOT DOWN";
                    break;
                case AimFlashKind.BotRespawn:
                    color = Accent;
                    text = useChineseLocalization ? "机器人重生" : "BOT BACK";
                    break;
                case AimFlashKind.Reset:
                    color = Accent;
                    text = useChineseLocalization ? "靶场重置" : "RANGE RESET";
                    break;
                case AimFlashKind.Handoff:
                    color = Ready;
                    text = useChineseLocalization ? "回合开始" : "ROUND START";
                    break;
                default:
                    return string.Empty;
            }

            string built = "<align=center><size=120%><b>" + Colored(color, text) + "</b></size></align>";
            return ActivityGlyphs.Resolve(built, useAsciiGlyphs);
        }

        private static string Eyebrow(AimRangeViewState state, bool cn)
        {
            StringBuilder sb = new StringBuilder(160);

            // Weapon: the held gun in white/bold, or a dim "no gun" cue that pairs with the TAKE A GUN hero.
            if (state.HasWeapon)
            {
                string label = WeaponLabel(state.PresetId);
                if (label.Length == 0)
                {
                    label = cn ? "武器" : "Gun";
                }

                sb.Append("<b>").Append(Colored(White, label)).Append("</b>");
            }
            else
            {
                sb.Append(Colored(Dim, cn ? "未持枪" : "No gun"));
            }

            sb.Append(Separator());
            sb.Append(Colored(Muted, TargetText(state.Target, cn)));

            string bots = BotText(state.Bots, cn, out string botColor);
            if (bots.Length > 0)
            {
                sb.Append(Separator()).Append(Colored(botColor, bots));
            }

            return sb.ToString();
        }

        // Line 3 of the hero: the honest live values beside the rail — the session hit count, plus session
        // accuracy once a shot is on record. Unarmed shows the teal call to grab a gun instead. No gold/green
        // here — time and personal-best are deferred, so nothing invents a stored baseline. Rendered value-first
        // ("12 HITS · 40% ACC") so both languages read the same and stay short inside the narrow lane.
        private static string StatLine(AimRangeViewState state, bool cn)
        {
            if (!state.HasWeapon)
            {
                return "<b>" + Colored(Accent, cn ? "去取一把枪" : "TAKE A GUN") + "</b>";
            }

            StringBuilder sb = new StringBuilder(120);
            string hits = "<b>" + Colored(White, state.SessionHits.ToString()) + "</b>";
            sb.Append(hits).Append(Colored(Muted, cn ? " 命中" : " HITS"));

            if (state.Shots > 0)
            {
                int accuracy = Clamp((int)Math.Round((double)state.SessionHits / state.Shots * 100.0, MidpointRounding.AwayFromZero), 0, 100);
                string accValue = "<b>" + Colored(White, accuracy.ToString() + "%") + "</b>";
                sb.Append(Colored(Dim, " · "));
                sb.Append(accValue).Append(Colored(Muted, cn ? " 命中率" : " ACC"));
            }

            return sb.ToString();
        }

        // The filled span is the session accuracy (teal = you/live), a teal marker sits at the frontier, and the
        // remainder is the dim/Line "not-yet" track. Total glyphs = RailCells + 1 (one marker).
        private static string BuildRail(AimRangeViewState state)
        {
            int filled = 0;
            if (state.Shots > 0)
            {
                filled = (int)Math.Round((double)state.SessionHits / state.Shots * RailCells, MidpointRounding.AwayFromZero);
            }

            filled = Clamp(filled, 0, RailCells);
            string track = ActivityGlyphs.Track.Unicode;

            StringBuilder sb = new StringBuilder(48);
            if (filled > 0)
            {
                sb.Append(Colored(Accent, Repeat(track, filled)));
            }

            sb.Append(Colored(Accent, ActivityGlyphs.You.Unicode));
            if (filled < RailCells)
            {
                sb.Append(Colored(Line, Repeat(track, RailCells - filled)));
            }

            return sb.ToString();
        }

        private static string Repeat(string unit, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(unit.Length * count);
            for (int i = 0; i < count; i++)
            {
                sb.Append(unit);
            }

            return sb.ToString();
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        // Short, active-voice coaching that fits the narrow lane on one line in both languages.
        private static string FooterInstruction(AimRangeViewState state, bool cn)
        {
            if (!state.HasWeapon)
            {
                return cn ? "去武器架取一把枪" : "Grab a gun off the rack";
            }

            if (state.Bots == AimBotPhase.Retaliating || state.IncomingHits > 0 && state.Bots != AimBotPhase.None)
            {
                return cn ? "切断视线躲子弹" : "Break line of sight";
            }

            if (state.Target != AimTargetKind.None)
            {
                return cn ? "击中亮起的靶子" : "Hit the lit target";
            }

            return cn ? "射击机器人激怒它" : "Shoot a bot to engage it";
        }

        private static string TargetText(AimTargetKind kind, bool cn)
        {
            switch (kind)
            {
                case AimTargetKind.Static:
                    return cn ? "静态靶" : "Static";
                case AimTargetKind.Moving:
                    return cn ? "移动靶" : "Moving";
                default:
                    return cn ? "无靶" : "No target";
            }
        }

        private static string BotText(AimBotPhase phase, bool cn, out string color)
        {
            switch (phase)
            {
                case AimBotPhase.Passive:
                    color = Muted;
                    return cn ? "机器人待机" : "Bots idle";
                case AimBotPhase.Retaliating:
                    color = Urgent;
                    return cn ? "机器人还击" : "Bots firing";
                case AimBotPhase.Respawning:
                    color = Muted;
                    return cn ? "机器人重生" : "Bot respawning";
                default:
                    color = Muted;
                    return string.Empty;
            }
        }

        private static string Separator()
        {
            return Colored(Dim, " · ");
        }

        private static string Colored(string hex, string text)
        {
            return "<color=" + hex + ">" + text + "</color>";
        }

        // Strip angle brackets so a config-authored preset id can never inject or unbalance HUD markup.
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf('<') < 0 && text.IndexOf('>') < 0))
            {
                return text ?? string.Empty;
            }

            return text.Replace("<", string.Empty).Replace(">", string.Empty);
        }
    }
}
