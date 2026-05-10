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
