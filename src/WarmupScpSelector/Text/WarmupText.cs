using System;
using System.Collections.Generic;
using System.Text;
using PlayerRoles;

namespace WarmupScpSelector.Text
{
    /// <summary>One SCP shown in the live status panel: the role plus its short label.</summary>
    public readonly struct WarmupOption
    {
        public WarmupOption(RoleTypeId role, string label)
        {
            Role = role;
            Label = label;
        }

        public RoleTypeId Role { get; }

        public string Label { get; }
    }

    /// <summary>
    /// Builds the bilingual warmup status panel. The text is TextMeshPro rich-text (the same markup the
    /// floating room labels use) so HSM renders it as a colored, formatted card. Everything here is pure
    /// string work — no Unity/LabAPI types — so it stays headless-testable.
    /// </summary>
    public static class WarmupText
    {
        // 莺歌傲然 brand palette (hex with leading '#'); alpha is appended where a tag needs RGBA. Matches the
        // room theme and the server identity: cyan accent + gold + brand green, with red retired to the
        // urgent-countdown state only. The title uses one calm brand blue; each selected chip uses one
        // position-derived color on both brackets so the option row sweeps green→blue from left to right.
        private const string Accent = "#4FCBFF";       // brand blue: title + current pick
        private const string Gold = "#FFD24D";         // countdown while there is still time / headings
        private const string Urgent = "#FF5555";       // countdown in the final few seconds (the only red left)
        private const string Ready = "#5BFF80";        // round about to start (brand green)
        private const string White = "#E7ECF3";
        private const string Muted = "#8B95A6";        // labels
        private const string Dim = "#5B6270";          // secondary / unselected
        private const string DividerColor = "#232A37";

        private const string Divider = "<color=" + DividerColor + ">———————————————</color>";

        // Seven authored stops match the default left-to-right SCP order. Non-default option counts sample
        // evenly across the same sweep, so the framing remains position-based rather than role-hardcoded.
        private static readonly string[] OptionGradient =
        {
            "#6BFF6B", "#66F684", "#62EE9C", "#5DE5B5", "#58DCCE", "#54D4E6", Accent,
        };

        public static string SelectionName(RoleTypeId? selectedRole, bool useChineseLocalization)
        {
            if (!selectedRole.HasValue)
            {
                return useChineseLocalization ? "无" : "None";
            }

            return FormatRoleName(selectedRole.Value);
        }

        public static string BuildWarmupStatusHint(
            short nativeTimer,
            int playerCount,
            int maxPlayers,
            IReadOnlyList<WarmupOption> options,
            RoleTypeId? selectedRole,
            IReadOnlyDictionary<RoleTypeId, int>? selectionCounts,
            bool useChineseLocalization)
        {
            StringBuilder sb = new StringBuilder(320);
            sb.Append("<align=center>");

            // Title, followed by a slim blank line so the title has breathing room above the status row.
            sb.Append("<size=165%><b>")
              .Append(BrandTitle(useChineseLocalization))
              .Append("</b></size>\n<size=70%> </size>\n");

            // Countdown + player count on one line, separated by a thin divider glyph.
            sb.Append(CountdownChunk(nativeTimer, useChineseLocalization))
              .Append("   <color=").Append(Dim).Append(">|</color>   ")
              .Append(PlayersChunk(playerCount, maxPlayers, useChineseLocalization))
              .Append('\n');

            sb.Append(Divider).Append('\n');

            // Current selection.
            sb.Append("<color=").Append(Muted).Append('>')
              .Append(useChineseLocalization ? "已选择" : "SELECTED")
              .Append("</color>　");
            if (selectedRole.HasValue)
            {
                sb.Append("<b>").Append(Colored(Accent, FormatRoleName(selectedRole.Value))).Append("</b>");
                sb.Append(SelectionCountBadge(selectedRole.Value, selectionCounts, useChineseLocalization));
            }
            else
            {
                sb.Append("<i>").Append(Colored(Dim, useChineseLocalization ? "尚未选择" : "none yet")).Append("</i>");
            }

            // Live option row: every offered SCP, with the current pick highlighted as a filled chip and a
            // small tally after any chip that has been picked.
            string chips = OptionChips(options, selectedRole, selectionCounts);
            if (chips.Length > 0)
            {
                sb.Append('\n').Append(chips);
            }

            // Slim blank line so the footer hint clears the chip row instead of colliding with it.
            sb.Append("\n<size=70%> </size>\n")
              .Append("<size=80%>")
              .Append(Colored(Muted, useChineseLocalization ? "抓取硬币以选择或更改" : "Grab a coin to pick or change"))
              .Append("</size>");

            sb.Append("</align>");
            return sb.ToString();
        }

        /// <summary>
        /// The one-line collapsed status strip shown while a player is inside an activity lane (e.g. the Aim
        /// Range): current SCP pick + its pick count + the round countdown, all on a single line. It renders on
        /// the lane's narrow left HUD X, so the whole strip is wrapped in one outer <c>&lt;size&gt;</c> span
        /// (never nested) to stay inside the ~284px lane even at the widest role name plus a two-digit pick count,
        /// and the countdown is kept terse ("12s" / "12秒", "Wait"/"Start") for the same reason. Threaded through
        /// <see cref="ActivityGlyphs"/> so the glyph fallback is preserved. The full draft panel is restored
        /// automatically once the player leaves the lane.
        /// </summary>
        public static string BuildCollapsedStatusStrip(
            short nativeTimer,
            RoleTypeId? selectedRole,
            int selectionCount,
            bool useChineseLocalization,
            bool useAsciiGlyphs)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("<align=center><size=88%>");
            sb.Append(Colored(Muted, useChineseLocalization ? "已选" : "PICK")).Append(' ');
            if (selectedRole.HasValue)
            {
                sb.Append("<b>").Append(Colored(Accent, FormatRoleName(selectedRole.Value))).Append("</b>");
                if (selectionCount > 0)
                {
                    sb.Append(Colored(Dim, " ·" + selectionCount.ToString()));
                }
            }
            else
            {
                sb.Append("<i>").Append(Colored(Dim, useChineseLocalization ? "尚未选择" : "none yet")).Append("</i>");
            }

            sb.Append(Colored(Dim, " | "));
            sb.Append(CompactCountdown(nativeTimer, useChineseLocalization));
            sb.Append("</size></align>");
            return ActivityGlyphs.Resolve(sb.ToString(), useAsciiGlyphs);
        }

        // Terse countdown for the collapsed strip (no leading glyph or verb prefix) so it fits the narrow lane.
        private static string CompactCountdown(short nativeTimer, bool cn)
        {
            if (nativeTimer == -2)
            {
                return Colored(Muted, cn ? "等待" : "Wait");
            }

            if (nativeTimer <= 0)
            {
                return "<b>" + Colored(Ready, cn ? "开始" : "Start") + "</b>";
            }

            string color = nativeTimer <= 5 ? Urgent : Gold;
            string seconds = "<b>" + Colored(color, nativeTimer.ToString()) + "</b>";
            return seconds + Colored(Muted, cn ? "秒" : "s");
        }

        private static string CountdownChunk(short nativeTimer, bool cn)
        {
            if (nativeTimer == -2)
            {
                return Colored(Muted, cn ? "● 等待玩家中" : "● Waiting for players");
            }

            if (nativeTimer <= 0)
            {
                return "<b>" + Colored(Ready, cn ? "▶ 即将开始" : "▶ Starting") + "</b>";
            }

            string color = nativeTimer <= 5 ? Urgent : Gold;
            string seconds = "<b>" + Colored(color, nativeTimer.ToString()) + "</b>";
            return cn
                ? Colored(Muted, "倒计时 ") + seconds + Colored(Muted, "秒")
                : Colored(Muted, "Starts in ") + seconds + Colored(Muted, "s");
        }

        private static string PlayersChunk(int playerCount, int maxPlayers, bool cn)
        {
            return Colored(Muted, cn ? "玩家 " : "Players ")
                + "<b>" + Colored(White, playerCount.ToString()) + "</b>"
                + Colored(Dim, " / " + maxPlayers);
        }

        private static string OptionChips(
            IReadOnlyList<WarmupOption> options,
            RoleTypeId? selectedRole,
            IReadOnlyDictionary<RoleTypeId, int>? selectionCounts)
        {
            if (options == null || options.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=92%>");
            for (int index = 0; index < options.Count; index++)
            {
                if (index > 0)
                {
                    sb.Append("  ");
                }

                WarmupOption option = options[index];
                string code = ShortCode(option.Role, option.Label);
                if (selectedRole.HasValue && selectedRole.Value == option.Role)
                {
                    // Bracket-framed chip for the live pick (re-frames the instant a coin is grabbed). Both
                    // brackets use this option's position on the green→blue sweep, making each role distinct
                    // without coloring every title letter. Avoid TMP <mark>: its highlight rectangle spans the
                    // whole text-block height, not just the glyph, which painted a giant vertical bar down the screen.
                    string frameColor = OptionColor(index, options.Count);
                    sb.Append("<b><color=").Append(frameColor).Append(">[</color><color=").Append(White).Append('>')
                      .Append(code).Append("</color><color=").Append(frameColor).Append(">]</color></b>");
                }
                else
                {
                    sb.Append("<color=").Append(Dim).Append('>').Append(code).Append("</color>");
                }

                sb.Append(ChipCount(option.Role, selectionCounts));
            }

            sb.Append("</size>");
            return sb.ToString();
        }

        private static string OptionColor(int index, int optionCount)
        {
            if (optionCount <= 1)
            {
                return OptionGradient[0];
            }

            int last = OptionGradient.Length - 1;
            int paletteIndex = (int)Math.Round((double)index * last / (optionCount - 1), MidpointRounding.AwayFromZero);
            return OptionGradient[Math.Max(0, Math.Min(last, paletteIndex))];
        }

        // Small muted "·N" tally trailing a chip, shown only when at least one player picked that SCP. Kept
        // tiny (70% of the base size) and dim so the offered-role row stays scannable; the "·" separates the
        // count from the code so "096·3" never reads as "0963".
        private static string ChipCount(RoleTypeId role, IReadOnlyDictionary<RoleTypeId, int>? selectionCounts)
        {
            if (selectionCounts == null || !selectionCounts.TryGetValue(role, out int count) || count <= 0)
            {
                return string.Empty;
            }

            return "<size=70%><color=" + Muted + ">·" + count.ToString() + "</color></size>";
        }

        // Concise, muted "how many players currently want this SCP" badge trailing the live selection,
        // e.g. "SCP-096　·　5 picks". Rendered at the SELECTED line's own size (no <size> override) so it
        // matches the role name rather than looking larger/smaller. Count includes the viewer; omitted
        // when no tally is available.
        private static string SelectionCountBadge(
            RoleTypeId role, IReadOnlyDictionary<RoleTypeId, int>? selectionCounts, bool cn)
        {
            if (selectionCounts == null || !selectionCounts.TryGetValue(role, out int count) || count <= 0)
            {
                return string.Empty;
            }

            return Colored(Dim, "　·　")
                + "<b>" + Colored(White, count.ToString()) + "</b>"
                + Colored(Muted, cn ? " 人" : (count == 1 ? " pick" : " picks"));
        }

        private static string ShortCode(RoleTypeId role, string label)
        {
            string name = role.ToString();
            if (name.StartsWith("Scp", StringComparison.Ordinal))
            {
                return name.Substring(3);
            }

            return string.IsNullOrWhiteSpace(label) ? name : label;
        }

        private static string Colored(string hex, string text)
        {
            return "<color=" + hex + ">" + text + "</color>";
        }

        // Branded panel title: a single blue keeps "SCP" crisp at HUD scale while the gold
        // "选择"/"SELECTION" suffix preserves the server identity's blue-and-gold hierarchy.
        private static string BrandTitle(bool useChineseLocalization)
        {
            return Colored(Accent, "SCP")
                + Colored(Gold, useChineseLocalization ? " 选择" : " SELECTION");
        }

        private static string FormatRoleName(RoleTypeId role)
        {
            string roleName = role.ToString();
            return roleName.StartsWith("Scp", StringComparison.Ordinal)
                ? "SCP-" + roleName.Substring(3)
                : roleName;
        }
    }
}
