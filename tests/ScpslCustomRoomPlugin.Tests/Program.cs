using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace ScpslCustomRoomPlugin.Tests
{
    internal static class Program
    {
        private static readonly RoleTypeId[] ScpOrder =
        {
            RoleTypeId.Scp079,
            RoleTypeId.Scp096,
            RoleTypeId.Scp106,
            RoleTypeId.Scp173,
            RoleTypeId.Scp3114,
        };

        private static int Main()
        {
            List<Action> tests = new List<Action>
            {
                SelectedPlayerSwapsWithVanillaScpHolderAndPreservesOriginalClass,
                UnspawnedSelectionIsSkipped,
                NaturalHolderKeepsRole,
                MultipleSelectorsOneChosen,
                AlreadyChosenCannotWinSecondPool,
                CrossPoolSwapsUseOriginalRoles,
                NonHumanParticipantIsNotFilteredByPlanner,
                MissingSelectedRoleResolutionIsSkipped,
                MultipleVanillaSlotsCanFillMultipleSelectors,
                NaturalHolderIsPreferredOverOtherPoolCandidate,
                EmptySelectionPoolsLeaveRolesUnchanged,
                Selected173DoesNotStayVanilla049When173Spawned,
                LiveRoundRoleBeatsStaleCapturedRole,
                CapturedRoleIsFallbackWhenLiveRoleIsSpectator,
                TutorialRoleWithoutCaptureIsUnresolved,
                EnglishWarmupTextStaysDefault,
                ChineseWarmupTextUsesSimplifiedChinese,
                CompatibilityDefaultsAvoidForcedRoundControl,
            };

            int failed = 0;
            foreach (Action test in tests)
            {
                try
                {
                    test();
                    Console.WriteLine($"PASS {test.Method.Name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {test.Method.Name}: {ex.Message}");
                }
            }

            Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed.");
            return failed == 0 ? 0 : 1;
        }

        private static void SelectedPlayerSwapsWithVanillaScpHolderAndPreservesOriginalClass()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selected"] = RoleTypeId.ClassD,
                    ["vanilla106"] = RoleTypeId.Scp106,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp106] = new[] { "selected" },
                });

            AssertRole(plan, "selected", RoleTypeId.Scp106);
            AssertRole(plan, "vanilla106", RoleTypeId.ClassD);
            AssertEqual(1, plan.Swaps.Count, "swap count");
        }

        private static void UnspawnedSelectionIsSkipped()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selected"] = RoleTypeId.ClassD,
                    ["vanilla079"] = RoleTypeId.Scp079,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp106] = new[] { "selected" },
                });

            AssertRole(plan, "selected", RoleTypeId.ClassD);
            AssertRole(plan, "vanilla079", RoleTypeId.Scp079);
            AssertSequence(new[] { RoleTypeId.Scp106 }, plan.SkippedUnspawnedRoles, "skipped roles");
        }

        private static void NaturalHolderKeepsRole()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selected"] = RoleTypeId.Scp079,
                    ["classD"] = RoleTypeId.ClassD,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "selected" },
                });

            AssertRole(plan, "selected", RoleTypeId.Scp079);
            AssertRole(plan, "classD", RoleTypeId.ClassD);
            AssertEqual(1, plan.NaturalSelections.Count, "natural selection count");
            AssertEqual(0, plan.Swaps.Count, "swap count");
        }

        private static void MultipleSelectorsOneChosen()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["first"] = RoleTypeId.ClassD,
                    ["second"] = RoleTypeId.Scientist,
                    ["vanilla079"] = RoleTypeId.Scp079,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "first", "second" },
                },
                candidates => candidates[candidates.Count - 1]);

            AssertRole(plan, "first", RoleTypeId.ClassD);
            AssertRole(plan, "second", RoleTypeId.Scp079);
            AssertRole(plan, "vanilla079", RoleTypeId.Scientist);
        }

        private static void AlreadyChosenCannotWinSecondPool()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selected"] = RoleTypeId.ClassD,
                    ["vanilla079"] = RoleTypeId.Scp079,
                    ["vanilla096"] = RoleTypeId.Scp096,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "selected" },
                    [RoleTypeId.Scp096] = new[] { "selected" },
                });

            AssertRole(plan, "selected", RoleTypeId.Scp079);
            AssertRole(plan, "vanilla079", RoleTypeId.ClassD);
            AssertRole(plan, "vanilla096", RoleTypeId.Scp096);
            AssertEqual(1, plan.Swaps.Count, "swap count");
        }

        private static void CrossPoolSwapsUseOriginalRoles()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selected079"] = RoleTypeId.Scp096,
                    ["selected096"] = RoleTypeId.ClassD,
                    ["vanilla079"] = RoleTypeId.Scp079,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "selected079" },
                    [RoleTypeId.Scp096] = new[] { "selected096" },
                });

            AssertRole(plan, "selected079", RoleTypeId.Scp079);
            AssertRole(plan, "selected096", RoleTypeId.Scp096);
            AssertRole(plan, "vanilla079", RoleTypeId.ClassD);
            AssertEqual(2, plan.Swaps.Count, "swap count");
        }

        private static void NonHumanParticipantIsNotFilteredByPlanner()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["dummy"] = RoleTypeId.ClassD,
                    ["vanilla3114"] = RoleTypeId.Scp3114,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp3114] = new[] { "dummy" },
                });

            AssertRole(plan, "dummy", RoleTypeId.Scp3114);
            AssertRole(plan, "vanilla3114", RoleTypeId.ClassD);
        }

        private static void MissingSelectedRoleResolutionIsSkipped()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["vanilla106"] = RoleTypeId.Scp106,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp106] = new[] { "missing" },
                });

            AssertRole(plan, "vanilla106", RoleTypeId.Scp106);
            AssertEqual(1, plan.UnresolvedSelections.Count, "unresolved count");
            AssertEqual(0, plan.Swaps.Count, "swap count");
        }

        private static void MultipleVanillaSlotsCanFillMultipleSelectors()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["selectedA"] = RoleTypeId.ClassD,
                    ["selectedB"] = RoleTypeId.Scientist,
                    ["holderA"] = RoleTypeId.Scp106,
                    ["holderB"] = RoleTypeId.Scp106,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp106] = new[] { "selectedA", "selectedB" },
                });

            AssertRole(plan, "selectedA", RoleTypeId.Scp106);
            AssertRole(plan, "selectedB", RoleTypeId.Scp106);
            AssertRole(plan, "holderA", RoleTypeId.ClassD);
            AssertRole(plan, "holderB", RoleTypeId.Scientist);
            AssertEqual(2, plan.Swaps.Count, "swap count");
        }

        private static void NaturalHolderIsPreferredOverOtherPoolCandidate()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["naturalHolder"] = RoleTypeId.Scp079,
                    ["otherSelector"] = RoleTypeId.ClassD,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "otherSelector", "naturalHolder" },
                },
                candidates => "otherSelector");

            AssertRole(plan, "naturalHolder", RoleTypeId.Scp079);
            AssertRole(plan, "otherSelector", RoleTypeId.ClassD);
            AssertEqual(1, plan.NaturalSelections.Count, "natural selection count");
            AssertEqual(0, plan.Swaps.Count, "swap count");
        }

        private static void EmptySelectionPoolsLeaveRolesUnchanged()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["classD"] = RoleTypeId.ClassD,
                    ["vanilla079"] = RoleTypeId.Scp079,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>());

            AssertRole(plan, "classD", RoleTypeId.ClassD);
            AssertRole(plan, "vanilla079", RoleTypeId.Scp079);
            AssertEqual(0, plan.Swaps.Count, "swap count");
            AssertEqual(0, plan.SkippedUnspawnedRoles.Count, "skipped count");
        }

        private static void Selected173DoesNotStayVanilla049When173Spawned()
        {
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["you"] = RoleTypeId.Scp049,
                    ["vanilla173"] = RoleTypeId.Scp173,
                    ["classD"] = RoleTypeId.ClassD,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp173] = new[] { "you" },
                });

            AssertRole(plan, "you", RoleTypeId.Scp173);
            AssertRole(plan, "vanilla173", RoleTypeId.Scp049);
            AssertRole(plan, "classD", RoleTypeId.ClassD);
            AssertEqual(1, plan.Swaps.Count, "swap count");
        }

        private static void LiveRoundRoleBeatsStaleCapturedRole()
        {
            bool resolved = VanillaRoleAssignmentResolver.TryResolve(RoleTypeId.Scp173, RoleTypeId.ClassD, out RoleTypeId resolvedRole);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(RoleTypeId.Scp173, resolvedRole, "resolved role");
        }

        private static void CapturedRoleIsFallbackWhenLiveRoleIsSpectator()
        {
            bool resolved = VanillaRoleAssignmentResolver.TryResolve(RoleTypeId.Spectator, RoleTypeId.Scp106, out RoleTypeId resolvedRole);

            AssertEqual(true, resolved, "resolved");
            AssertEqual(RoleTypeId.Scp106, resolvedRole, "resolved role");
        }

        private static void TutorialRoleWithoutCaptureIsUnresolved()
        {
            bool resolved = VanillaRoleAssignmentResolver.TryResolve(RoleTypeId.Tutorial, null, out RoleTypeId resolvedRole);

            AssertEqual(false, resolved, "resolved");
            AssertEqual(RoleTypeId.None, resolvedRole, "resolved role");
        }

        private static void EnglishWarmupTextStaysDefault()
        {
            string countdown = WarmupText.BuildCountdownLine(12, 3, 50, false);
            string hint = WarmupText.BuildWarmupStatusHint(countdown, RoleTypeId.Scp173, false);

            AssertEqual("Countdown: 12s\nPlayers: 3/50", countdown, "countdown");
            AssertEqual("Countdown: 12s\nPlayers: 3/50\nSelected SCP: SCP-173\nInteract with a coin to change selection.", hint, "hint");
            AssertEqual("None", WarmupText.SelectionName(null, false), "none selection");
        }

        private static void ChineseWarmupTextUsesSimplifiedChinese()
        {
            string waiting = WarmupText.BuildCountdownLine(-2, 1, 50, true);
            string starting = WarmupText.BuildCountdownLine(0, 4, 50, true);
            string hint = WarmupText.BuildWarmupStatusHint(waiting, null, true);

            AssertEqual("倒计时：等待玩家\n玩家：1/50", waiting, "waiting countdown");
            AssertEqual("倒计时：回合即将开始\n玩家：4/50", starting, "starting countdown");
            AssertEqual("倒计时：等待玩家\n玩家：1/50\n已选择SCP：无\n与硬币互动可更改选择。", hint, "hint");
            AssertEqual("与硬币互动来选择SCP职业。", WarmupText.InitialSelectionHint(true), "initial hint");
        }

        private static void CompatibilityDefaultsAvoidForcedRoundControl()
        {
            Config config = new Config();

            AssertEqual(false, config.LockLobbyDuringWarmup, "lock lobby default");
            AssertEqual(false, config.ForceRoundStartForWarmup, "force round start default");
            AssertEqual(true, config.ShowCountdownHints, "show countdown hints default");
            AssertEqual(1f, config.HintRefreshIntervalSeconds, "hint refresh interval default");
            AssertEqual(1.25f, config.HintDurationSeconds, "hint duration default");
            AssertEqual(true, config.EnableCompatibilityDiagnostics, "compatibility diagnostics default");
            AssertEqual(45f, config.RoundStartWatchdogSeconds, "round start watchdog default");
        }

        private static SelectionSwapPlan<string> BuildPlan(
            IReadOnlyDictionary<string, RoleTypeId> originalRoles,
            IReadOnlyDictionary<RoleTypeId, IReadOnlyList<string>> selectedPools,
            Func<IReadOnlyList<string>, string>? choosePlayer = null)
        {
            return SelectionSwapPlanner.BuildPlan(
                ScpOrder,
                originalRoles,
                selectedPools,
                choosePlayer ?? (candidates => candidates[0]));
        }

        private static void AssertRole(SelectionSwapPlan<string> plan, string player, RoleTypeId expectedRole)
        {
            if (!plan.FinalRoles.TryGetValue(player, out RoleTypeId actualRole))
            {
                throw new InvalidOperationException($"missing player {player}");
            }

            AssertEqual(expectedRole, actualRole, $"{player} role");
        }

        private static void AssertEqual<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
            }
        }

        private static void AssertSequence<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string label)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
            }
        }
    }
}
