using System;
using System.Globalization;
using System.Text;
using WarmupScpSelector.Activities.Parkour;

namespace WarmupScpSelector.Text
{
    internal readonly struct ParkourViewState
    {
        public ParkourViewState(ParkourPhase phase, int sector, int passedGates, int totalGates, double elapsed, double countdown, double? personalBest)
        {
            Phase = phase;
            Sector = sector;
            PassedGates = passedGates;
            TotalGates = totalGates;
            Elapsed = elapsed;
            Countdown = countdown;
            PersonalBest = personalBest;
        }

        public ParkourPhase Phase { get; }
        public int Sector { get; }
        public int PassedGates { get; }
        public int TotalGates { get; }
        public double Elapsed { get; }
        public double Countdown { get; }
        public double? PersonalBest { get; }
    }

    internal static class ParkourText
    {
        public static string BuildHero(ParkourViewState state, bool chinese)
        {
            string title = chinese ? "脉冲路线" : "PULSE LINE";
            if (state.Phase == ParkourPhase.Idle || state.Phase == ParkourPhase.Arming)
            {
                string action = state.Phase == ParkourPhase.Arming
                    ? (chinese ? "保持站立" : "HOLD POSITION")
                    : (chinese ? "踏上青色起点板" : "STEP ON THE CYAN START");
                return $"<align=center><size=76%><color=#8B95A6>{title}</color></size>\n" +
                    $"<size=150%><b><color=#4FCBFF>{action}</color></b></size>\n" +
                    "<size=78%><color=#232A37>01━━02━━03━━04━━FIN</color></size></align>";
            }

            if (state.Phase == ParkourPhase.Countdown)
            {
                int seconds = Math.Max(1, (int)Math.Ceiling(state.Countdown));
                return $"<align=center><size=76%><color=#8B95A6>{title}</color></size>\n" +
                    $"<size=190%><b><color=#FFD24D>{seconds}</color></b></size>\n" +
                    $"<size=72%><color=#E7ECF3>{(chinese ? "开始时闸门开启" : "GATE OPENS ON GO")}</color></size></align>";
            }

            string time = state.Elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            string spine = BuildSpine(state.Sector, state.Phase == ParkourPhase.Finished);
            string pb = state.PersonalBest.HasValue
                ? (chinese ? "最佳 " : "PB ") + state.PersonalBest.Value.ToString("0.000", CultureInfo.InvariantCulture)
                : (chinese ? "暂无纪录" : "NO PB YET");
            string eyebrow = state.Phase == ParkourPhase.Finished
                ? (chinese ? "完成" : "FINISH")
                : (chinese ? $"第 {state.Sector} / 4 段" : $"SECTOR {state.Sector} / 4");
            return $"<align=center><size=72%><color=#8B95A6>{eyebrow}</color></size>\n" +
                $"<size=180%><b><color={(state.Phase == ParkourPhase.Finished ? "#5BFF80" : "#FFD24D")}>{time}</color></b></size>\n" +
                $"<size=80%>{spine}</size>\n" +
                $"<size=72%><color=#8B95A6>{pb}　│　{state.PassedGates}/{state.TotalGates}</color></size></align>";
        }

        public static string BuildFooter(ParkourPhase phase, bool chinese)
        {
            string text;
            switch (phase)
            {
                case ParkourPhase.Countdown:
                    text = chinese ? "冲刺起跳 · 收尾段必须冲刺" : "Sprint into every jump; the last hops need it";
                    break;
                case ParkourPhase.Active:
                    text = chinese ? "按顺序踩过亮边平台 · 抓硬币重置" : "Land in order · grab RESET to restart";
                    break;
                case ParkourPhase.Finished:
                    text = chinese ? "离开再踏上起点即可重跑" : "Step off, then return to START to rerun";
                    break;
                default:
                    text = chinese ? "青色是路线 · 金色是分段点" : "Cyan marks the route · gold marks a split";
                    break;
            }

            return $"<align=center><size=72%><color=#A8B0BC>{text}</color></size></align>";
        }

        public static string BuildFlash(string kind, bool chinese)
        {
            switch (kind)
            {
                case "go": return $"<align=center><size=118%><b><color=#4FCBFF>{(chinese ? "开始" : "GO")}</color></b></size></align>";
                case "split": return $"<align=center><size=96%><b><color=#4FCBFF>{(chinese ? "分段" : "SPLIT")}</color></b></size></align>";
                case "recover": return $"<align=center><size=92%><b><color=#FFD24D>{(chinese ? "恢复" : "RECOVER")}</color></b></size></align>";
                case "best": return $"<align=center><size=108%><b><color=#5BFF80>{(chinese ? "新的最佳" : "NEW BEST")}</color></b></size></align>";
                case "finish": return $"<align=center><size=108%><b><color=#5BFF80>{(chinese ? "完成" : "FINISH")}</color></b></size></align>";
                default: return string.Empty;
            }
        }

        private static string BuildSpine(int sector, bool finished)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 1; i <= 4; i++)
            {
                if (i > 1)
                {
                    builder.Append("<color=#5B6270>━━</color>");
                }

                string color = finished || i < sector ? "#5BFF80" : i == sector ? "#4FCBFF" : "#232A37";
                string value = i.ToString("00", CultureInfo.InvariantCulture);
                builder.Append(i == sector && !finished
                    ? $"<b><color={color}>[{value}]</color></b>"
                    : $"<color={color}>{value}</color>");
            }

            builder.Append("<color=#5B6270>━━</color>");
            builder.Append(finished ? "<color=#5BFF80>FIN</color>" : "<color=#232A37>FIN</color>");
            return builder.ToString();
        }
    }
}
