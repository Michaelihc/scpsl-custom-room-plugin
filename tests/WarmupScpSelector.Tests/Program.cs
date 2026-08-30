using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles;
using UnityEngine;
using WarmupScpSelector.Activities;
using WarmupScpSelector.Activities.AimRange;
using WarmupScpSelector.Activities.Parkour;
using WarmupScpSelector.Models;
using WarmupScpSelector.Replacement;
using WarmupScpSelector.Selection;
using WarmupScpSelector.Services;
using WarmupScpSelector.Text;
using WarmupScpSelector.Warmup;

namespace WarmupScpSelector.Tests
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
                DisplacedHolderLaterSelectingAnotherScpPreservesRoleMultiset,
                DuplicateVanillaHolderWhoPickedItKeepsIt,
                AtomicPreSpawnPlanLeavesDisplacedHolderHumanEligible,
                AtomicDispatchNeverReentersCurrentPlayer,
                AtomicDispatchHandlesJoinStormWithoutMakingDummiesSelectors,
                VanillaScpSlotCounterMatchesRoleAssignerLoop,
                VanillaScpSlotCounterHonoursOverflow,
                LiveRoundRoleBeatsStaleCapturedRole,
                CapturedRoleIsFallbackWhenLiveRoleIsSpectator,
                TutorialRoleWithoutCaptureIsUnresolved,
                EnglishWarmupTextStaysDefault,
                ChineseWarmupTextUsesSimplifiedChinese,
                SelectedChipUsesPositionGradient,
                ActivityManagerEnforcesExclusiveLaneAndGenerationTokens,
                ActivityManagerStopForRoundStartIsIdempotentAndNonThrowing,
                ActivityManagerOnPlayerLeftNotifiesLaneAndIsIdempotent,
                ActivityManagerKeepsOwnershipCurrentDuringPlayerLeftCleanup,
                ActivityManagerSwitchingLaneTearsDownPriorLane,
                LaneFlashTrackerGuardsRepeatableExpiry,
                ActivityGlyphsProvideAsciiFallbacks,
                LaneHintIdsComposeStableZoneIds,
                AimRangeSessionsTrackOccupancyAndWeapons,
                RangeTargetStateGuardsGenerationsAndOneCredit,
                WeaponShelfStateGuardsClaimGeneration,
                AimRangeDeterminismIsStableAndTickIndependent,
                RangeBotDeterministicPresetSelectionIsStable,
                RangeBotDefaultsUseExactAutomaticPresets,
                RangeBotLifecycleTransitionsAndLocksFirstAggressor,
                RangeBotDeathRespawnClearsAggroAndRejectsStaleGeneration,
                RangeDamagePolicyAllowsOnlyOwnedCombatDirections,
                AimRangeLethalResetStateRequiresNewTutorialLife,
                RangeBotConfigDefaultsToExplicitOptIn,
                RangeBotLobbyPolicyRequiresHumansAndHeadroom,
                DuplicateShelfPresetIdsAreRejected,
                RangeBotConfigValidationClampsAndFilters,
                RangeBotRegistryUsesLiveIdentityIndexes,
                ParticipantIdentityRulesRejectNonHumans,
                MerWorldTransformComposesOneLevelParent,
                WarmupHandsBackEveryParticipantWhateverRoleTheyHold,
                DownrangeTargetsFaceTheShooter,
                EveryCompartmentIsReachableFromTheSpawnPoint,
                StationCompartmentsTileWithoutOverlapOrGap,
                StationWallPanelsSealEveryCompartmentFace,
                GalleryStandsKeepTheAisleAndFitTheRoom,
                AimBayHostsThePersistentCounterArmoury,
                AimBayDefinesThreeClearLanesAlongTheArm,
                SlidingTargetMotionIsDeterministicAndAbsolute,
                SphereTargetStateCreditsOnceAndRelocates,
                SphereTargetLayoutFitsThirdLane,
                CollapsedStatusStripIsOneLineOneLanguageAndSafe,
                AimRangeHeroRendersStateInOneLanguage,
                AimRangeFooterIsStateSpecificAndActiveVoice,
                AimRangeFlashRendersEveryEventInOneLanguage,
                AimRangeTextMarkupIsBalancedAndUnnested,
                HintChangeCacheSkipsUnchangedButResendsOnChange,
                ParkourDefaultsToExplicitOptIn,
                ParkourRunRequiresOrderedGatesAndFreezesFinishTime,
                ParkourRouteIsClearableButNotTrivial,
                ParkourRouteAdaptsToMovementConstants,
                ParkourJumpModelMatchesNativeProjectileMotion,
                ParkourSweptGateDetectionCatchesFastCrossings,
                ParkourTextIsBilingualAndMarkupSafe,
                ScpReplacementDefaultsAllowLivingAndSpectatorVolunteers,
                ScpReplacementDeparturePolicyHonoursCutoffHealthAndIgnoredRoles,
                ScpReplacementStateRejectsDuplicateStableUserIds,
                ScpReplacementDisconnectRemovalDropsVolunteerFromEverySlot,
                ScpReplacementRoundGenerationRejectsStaleLottery,
                ScpReplacementCapacityCountsPendingReservations,
                ScpReplacementParserAcceptsFriendlyScpNumbers,
                ScpReplacementWeightedHumanRolesRejectInvalidEntries,
                ScpReplacementCooldownIsSharedAndResettable,
                ScpReplacementTextIsBilingualAndMarkupSafe,
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
                    // Print the frame too: a bare message is useless for the Unity ECall failures that
                    // happen when a headless test touches a native UnityEngine method.
                    string frame = (ex.StackTrace ?? string.Empty).Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
                    Console.Error.WriteLine($"FAIL {test.Method.Name}: {ex.Message} {frame}");
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

        private static void DisplacedHolderLaterSelectingAnotherScpPreservesRoleMultiset()
        {
            // a=Scp079, b=Scp096, c=ClassD. c picks 079 (displacing a), then a picks 096.
            // Correct result preserves the role multiset {079, 096, ClassD}: c=079, a=096, b=ClassD.
            // The bug gave a's *original* 079 to b, producing two 079s and dropping ClassD.
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["a"] = RoleTypeId.Scp079,
                    ["b"] = RoleTypeId.Scp096,
                    ["c"] = RoleTypeId.ClassD,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp079] = new[] { "c" },
                    [RoleTypeId.Scp096] = new[] { "a" },
                });

            AssertRole(plan, "c", RoleTypeId.Scp079);
            AssertRole(plan, "a", RoleTypeId.Scp096);
            AssertRole(plan, "b", RoleTypeId.ClassD);
            AssertEqual(2, plan.Swaps.Count, "swap count");
        }

        private static void DuplicateVanillaHolderWhoPickedItKeepsIt()
        {
            // a and b both vanilla Scp106; b and c both pick 106. b already holds it, so b KEEPS 106 and c
            // takes the other 106 slot (displacing a), a -> ClassD. The {106, 106, ClassD} multiset is kept.
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["a"] = RoleTypeId.Scp106,
                    ["b"] = RoleTypeId.Scp106,
                    ["c"] = RoleTypeId.ClassD,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp106] = new[] { "b", "c" },
                });

            AssertRole(plan, "b", RoleTypeId.Scp106);
            AssertRole(plan, "c", RoleTypeId.Scp106);
            AssertRole(plan, "a", RoleTypeId.ClassD);
        }

        private static void AtomicPreSpawnPlanLeavesDisplacedHolderHumanEligible()
        {
            // Before HumanSpawner runs, every non-SCP is still None. The same planner can therefore move the
            // pending 096 slot to the picker while returning its vanilla holder to the eligible pool. Neither
            // player needs to be initialized as an intermediate role.
            SelectionSwapPlan<string> plan = BuildPlan(
                new Dictionary<string, RoleTypeId>
                {
                    ["picker"] = RoleTypeId.None,
                    ["vanilla096"] = RoleTypeId.Scp096,
                },
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    [RoleTypeId.Scp096] = new[] { "picker" },
                });

            AssertRole(plan, "picker", RoleTypeId.Scp096);
            AssertRole(plan, "vanilla096", RoleTypeId.None);
            AssertEqual(1, plan.Swaps.Count, "atomic swap count");
        }

        private static void AtomicDispatchNeverReentersCurrentPlayer()
        {
            var finalScps = new Dictionary<string, RoleTypeId>
            {
                ["callback-holder"] = RoleTypeId.Scp096,
                ["other-holder"] = RoleTypeId.Scp106,
            };

            AtomicRoleDispatchPlan<string> dispatch = AtomicRoleDispatchPlanner.Build(finalScps, "callback-holder");

            AssertEqual(true, dispatch.CallbackReceivesScp, "callback receives SCP");
            AssertEqual(RoleTypeId.Scp096, dispatch.CallbackRole, "callback role");
            AssertEqual(1, dispatch.ImmediateAssignments.Count, "non-reentrant assignment count");
            AssertEqual("other-holder", dispatch.ImmediateAssignments[0].Key, "only other player is assigned immediately");
            AssertEqual(RoleTypeId.Scp106, dispatch.ImmediateAssignments[0].Value, "other player's role");
        }

        private static void AtomicDispatchHandlesJoinStormWithoutMakingDummiesSelectors()
        {
            var originalRoles = new Dictionary<string, RoleTypeId>();
            for (int index = 0; index < 32; index++)
            {
                originalRoles[$"dummy-{index}"] = RoleTypeId.None;
            }

            originalRoles["dummy-vanilla-096"] = RoleTypeId.Scp096;
            originalRoles["dummy-vanilla-106"] = RoleTypeId.Scp106;
            originalRoles["human-picker"] = RoleTypeId.None;

            SelectionSwapPlan<string> selection = BuildPlan(
                originalRoles,
                new Dictionary<RoleTypeId, IReadOnlyList<string>>
                {
                    // Only the real human is in a selection pool. Dummies remain ordinary vanilla candidates.
                    [RoleTypeId.Scp096] = new[] { "human-picker" },
                });

            AssertRole(selection, "human-picker", RoleTypeId.Scp096);
            AssertRole(selection, "dummy-vanilla-096", RoleTypeId.None);
            AssertRole(selection, "dummy-vanilla-106", RoleTypeId.Scp106);
            for (int index = 0; index < 32; index++)
            {
                AssertRole(selection, $"dummy-{index}", RoleTypeId.None);
            }

            Dictionary<string, RoleTypeId> finalScps = selection.FinalRoles
                .Where(pair => pair.Value == RoleTypeId.Scp096 || pair.Value == RoleTypeId.Scp106)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            AtomicRoleDispatchPlan<string> dispatch = AtomicRoleDispatchPlanner.Build(finalScps, "dummy-vanilla-106");

            AssertEqual(true, dispatch.CallbackReceivesScp, "dummy callback retains only its vanilla SCP");
            AssertEqual(RoleTypeId.Scp106, dispatch.CallbackRole, "dummy callback role");
            AssertEqual(false, dispatch.ImmediateAssignments.Any(pair => pair.Key == "dummy-vanilla-106"),
                "current dummy is never assigned reentrantly");
        }

        private static void VanillaScpSlotCounterMatchesRoleAssignerLoop()
        {
            // Team digits: 4=ClassD, 0=SCP. With overflow disabled the vanilla loop stops at its SCP cap.
            AssertEqual(2, VanillaScpSlotCounter.Count("4x0", 8, 2, false), "capped SCP count");
            AssertEqual(0, VanillaScpSlotCounter.Count("444", 8, 2, false), "human-only queue");
            AssertEqual(0, VanillaScpSlotCounter.Count("not-a-queue", 8, 2, false), "invalid queue");
        }

        private static void VanillaScpSlotCounterHonoursOverflow()
        {
            AssertEqual(4, VanillaScpSlotCounter.Count("40", 8, 2, true), "overflow SCP count");
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

        private static readonly WarmupOption[] SampleOptions =
        {
            new WarmupOption(RoleTypeId.Scp049, "SCP-049"),
            new WarmupOption(RoleTypeId.Scp079, "SCP-079"),
            new WarmupOption(RoleTypeId.Scp173, "SCP-173"),
        };

        private static void EnglishWarmupTextStaysDefault()
        {
            var counts = new Dictionary<RoleTypeId, int> { { RoleTypeId.Scp173, 5 } };
            string hint = WarmupText.BuildWarmupStatusHint(12, 3, 50, SampleOptions, RoleTypeId.Scp173, counts, false);

            // Branded title: one blue span for "SCP", followed by the gold localized suffix.
            AssertContains(hint, "<color=#4FCBFF>SCP</color><color=#FFD24D> SELECTION</color>", "single-color title");
            AssertContains(hint, "Starts in ", "countdown label");
            AssertContains(hint, ">12<", "countdown seconds");
            AssertContains(hint, "Players ", "players label");
            AssertContains(hint, " / 50", "player cap");
            AssertContains(hint, "SELECTED", "selected label");
            AssertContains(hint, "SCP-173", "selected name");
            AssertContains(hint, "173", "chip code");
            AssertContains(
                hint,
                "<color=#4FCBFF>[</color><color=#E7ECF3>173</color><color=#4FCBFF>]</color>",
                "selected chip position-color framing");
            AssertContains(hint, ">5</color>", "selection count value");
            AssertContains(hint, "> picks</color>", "selection count unit");
            AssertContains(hint, ">·5</color>", "chip tally");
            AssertEqual("None", WarmupText.SelectionName(null, false), "none selection");

            // Singular unit and no badge when the tally is absent. The badge carries a unique "·" separator
            // (the panel's other dividers are "|" and em dashes), so it can't be confused with the
            // "to pick or change" footer copy.
            var single = new Dictionary<RoleTypeId, int> { { RoleTypeId.Scp173, 1 } };
            AssertContains(
                WarmupText.BuildWarmupStatusHint(12, 3, 50, SampleOptions, RoleTypeId.Scp173, single, false),
                "> pick</color>",
                "singular selection count unit");
            AssertOmits(
                WarmupText.BuildWarmupStatusHint(12, 3, 50, SampleOptions, RoleTypeId.Scp173, null, false),
                "·",
                "no badge without a tally");
        }

        private static void ChineseWarmupTextUsesSimplifiedChinese()
        {
            var counts = new Dictionary<RoleTypeId, int> { { RoleTypeId.Scp079, 2 } };
            string waiting = WarmupText.BuildWarmupStatusHint(-2, 1, 50, SampleOptions, null, null, true);
            string starting = WarmupText.BuildWarmupStatusHint(0, 4, 50, SampleOptions, null, null, true);
            string counting = WarmupText.BuildWarmupStatusHint(8, 4, 50, SampleOptions, RoleTypeId.Scp079, counts, true);

            // Branded title: one blue span for "SCP", followed by the gold localized suffix.
            AssertContains(waiting, "<color=#4FCBFF>SCP</color><color=#FFD24D> 选择</color>", "single-color title cn");
            AssertContains(waiting, "等待玩家", "waiting state");
            AssertContains(waiting, "玩家 ", "players label");
            AssertContains(waiting, "尚未选择", "no selection");
            AssertContains(waiting, "抓取硬币", "instruction");
            AssertContains(starting, "即将开始", "starting state");
            AssertContains(counting, "倒计时", "countdown label");
            AssertContains(counting, ">8<", "countdown seconds");
            AssertContains(counting, "SCP-079", "selected name");
            AssertContains(
                counting,
                "<color=#5DE5B5>[</color><color=#E7ECF3>079</color><color=#5DE5B5>]</color>",
                "selected chip position-color framing cn");
            AssertContains(counting, ">2</color>", "selection count value cn");
            AssertContains(counting, "> 人</color>", "selection count unit cn");
            AssertContains(counting, ">·2</color>", "chip tally cn");
            AssertEqual("无", WarmupText.SelectionName(null, true), "none selection");
        }

        private static void SelectedChipUsesPositionGradient()
        {
            WarmupOption[] options =
            {
                new WarmupOption(RoleTypeId.Scp049, "SCP-049"),
                new WarmupOption(RoleTypeId.Scp079, "SCP-079"),
                new WarmupOption(RoleTypeId.Scp096, "SCP-096"),
                new WarmupOption(RoleTypeId.Scp106, "SCP-106"),
                new WarmupOption(RoleTypeId.Scp173, "SCP-173"),
                new WarmupOption(RoleTypeId.Scp939, "SCP-939"),
                new WarmupOption(RoleTypeId.Scp3114, "SCP-3114"),
            };
            string[] expectedColors =
            {
                "#6BFF6B", "#66F684", "#62EE9C", "#5DE5B5", "#58DCCE", "#54D4E6", "#4FCBFF",
            };
            string[] expectedCodes = { "049", "079", "096", "106", "173", "939", "3114" };

            for (int index = 0; index < options.Length; index++)
            {
                string hint = WarmupText.BuildWarmupStatusHint(
                    12,
                    7,
                    50,
                    options,
                    options[index].Role,
                    null,
                    false);
                string expectedFrame = "<color=" + expectedColors[index] + ">[</color><color=#E7ECF3>"
                    + expectedCodes[index] + "</color><color=" + expectedColors[index] + ">]</color>";

                AssertContains(hint, expectedFrame, expectedCodes[index] + " position gradient");
            }
        }

        // ---- Activity-suite shared infrastructure (Task #2) ----------------------------------------------

        private sealed class FakeLane : IActivityLane
        {
            private readonly bool _throwOnStop;
            private readonly Action<string>? _onPlayerLeft;

            public FakeLane(string laneId, bool throwOnStop = false, Action<string>? onPlayerLeft = null)
            {
                LaneId = laneId;
                _throwOnStop = throwOnStop;
                _onPlayerLeft = onPlayerLeft;
            }

            public string LaneId { get; }

            public bool Enabled { get; set; } = true;

            public int StopForRoundStartCalls { get; private set; }

            public int StopAllCalls { get; private set; }

            public List<string> LeftKeys { get; } = new List<string>();

            public void StopForRoundStart()
            {
                StopForRoundStartCalls++;
                if (_throwOnStop)
                {
                    throw new InvalidOperationException("lane teardown blew up");
                }
            }

            public void StopAll()
            {
                StopAllCalls++;
                if (_throwOnStop)
                {
                    throw new InvalidOperationException("lane teardown blew up");
                }
            }

            public void OnPlayerLeft(string userKey)
            {
                LeftKeys.Add(userKey);
                _onPlayerLeft?.Invoke(userKey);
                if (_throwOnStop)
                {
                    throw new InvalidOperationException("lane player-left blew up");
                }
            }
        }

        private static void ActivityManagerEnforcesExclusiveLaneAndGenerationTokens()
        {
            ActivityManager manager = new ActivityManager();
            FakeLane aim = new FakeLane("aim");
            FakeLane duel = new FakeLane("duel");
            manager.RegisterLane(aim);
            manager.RegisterLane(duel);

            int aimToken = manager.BeginSession("u1", "aim");
            AssertEqual("aim", manager.CurrentLane("u1"), "current lane after begin");
            AssertEqual(true, manager.IsCurrent("u1", "aim", aimToken), "aim token current");

            // Exclusivity: entering a second lane replaces the first, and the old token is no longer current.
            int duelToken = manager.BeginSession("u1", "duel");
            AssertEqual("duel", manager.CurrentLane("u1"), "current lane after switch");
            AssertEqual(false, manager.IsCurrent("u1", "aim", aimToken), "old lane token stale");
            AssertEqual(true, manager.IsCurrent("u1", "duel", duelToken), "new lane token current");
            AssertEqual(false, aimToken == duelToken, "generation token advanced");

            // A stale token for the same lane is also rejected after a re-begin.
            int reAim = manager.BeginSession("u1", "aim");
            AssertEqual(false, manager.IsCurrent("u1", "duel", duelToken), "token invalidated by re-begin");
            AssertEqual(true, manager.IsCurrent("u1", "aim", reAim), "re-begin token current");

            // Unregistered lane and empty key yield a 0 (no-session) token.
            AssertEqual(0, manager.BeginSession("u2", "unregistered"), "unregistered lane token 0");
            AssertEqual(0, manager.BeginSession("", "aim"), "empty key token 0");
            AssertEqual(null, manager.CurrentLane("u2"), "no session for unregistered begin");
        }

        private static void ActivityManagerStopForRoundStartIsIdempotentAndNonThrowing()
        {
            ActivityManager manager = new ActivityManager();
            FakeLane aim = new FakeLane("aim");
            FakeLane boom = new FakeLane("duel", throwOnStop: true);
            manager.RegisterLane(aim);
            manager.RegisterLane(boom);
            manager.BeginSession("u1", "aim");

            // Must not throw even though one lane's teardown throws; every lane still gets torn down.
            manager.StopForRoundStart();
            AssertEqual(1, aim.StopForRoundStartCalls, "aim torn down once");
            AssertEqual(1, boom.StopForRoundStartCalls, "faulting lane still invoked");
            AssertEqual(null, manager.CurrentLane("u1"), "sessions cleared on round start");

            // Idempotent: a second call is safe and still non-throwing.
            manager.StopForRoundStart();
            AssertEqual(2, aim.StopForRoundStartCalls, "aim torn down again");

            // StopAll routes to the other teardown hook and is likewise non-throwing.
            manager.StopAll();
            AssertEqual(1, aim.StopAllCalls, "StopAll routed to StopAll hook");
            AssertEqual(1, boom.StopAllCalls, "faulting lane StopAll invoked");
        }

        private static void ActivityManagerOnPlayerLeftNotifiesLaneAndIsIdempotent()
        {
            ActivityManager manager = new ActivityManager();
            FakeLane aim = new FakeLane("aim");
            manager.RegisterLane(aim);
            manager.BeginSession("u1", "aim");

            manager.OnPlayerLeft("u1");
            AssertEqual(1, aim.LeftKeys.Count, "lane notified once");
            AssertEqual("u1", aim.LeftKeys[0], "lane notified with key");
            AssertEqual(null, manager.CurrentLane("u1"), "session removed on leave");

            // Idempotent: a second leave finds no session and does not re-notify.
            manager.OnPlayerLeft("u1");
            AssertEqual(1, aim.LeftKeys.Count, "no double notify");

            // Non-throwing on empty/unknown keys.
            manager.OnPlayerLeft("");
            manager.OnPlayerLeft("never-had-a-session");
            AssertEqual(1, aim.LeftKeys.Count, "unknown keys do not notify");
        }

        private static void ActivityManagerKeepsOwnershipCurrentDuringPlayerLeftCleanup()
        {
            ActivityManager manager = new ActivityManager();
            bool currentDuringCleanup = false;
            FakeLane aim = new FakeLane(
                "aim",
                onPlayerLeft: key => currentDuringCleanup = string.Equals(manager.CurrentLane(key), "aim", StringComparison.Ordinal));
            manager.RegisterLane(aim);
            manager.BeginSession("u1", "aim");

            manager.OnPlayerLeft("u1");
            AssertEqual(true, currentDuringCleanup, "lane ownership remains current during disconnect cleanup");
            AssertEqual(null, manager.CurrentLane("u1"), "lane ownership is removed after cleanup");
        }

        private static void LaneFlashTrackerGuardsRepeatableExpiry()
        {
            LaneFlashTracker tracker = new LaneFlashTracker();
            int first = tracker.Arm("u1", "aim");
            AssertEqual(true, tracker.ShouldClear("u1", "aim", first), "latest token clears");

            // Re-arming (an identical repeat verdict) invalidates the earlier expiry so it can't clear the new one.
            int second = tracker.Arm("u1", "aim");
            AssertEqual(false, first == second, "flash token advanced on re-arm");
            AssertEqual(false, tracker.ShouldClear("u1", "aim", first), "stale flash token does not clear");
            AssertEqual(true, tracker.ShouldClear("u1", "aim", second), "new flash token clears");

            // Separate lanes/players are tracked independently.
            int other = tracker.Arm("u2", "aim");
            AssertEqual(true, tracker.ShouldClear("u2", "aim", other), "independent key tracked");
            AssertEqual(true, tracker.ShouldClear("u1", "aim", second), "unrelated arm does not disturb");

            tracker.Clear("u1", "aim");
            AssertEqual(false, tracker.ShouldClear("u1", "aim", second), "cleared token no longer clears");
        }

        private static void ActivityGlyphsProvideAsciiFallbacks()
        {
            AssertEqual("--", ActivityGlyphs.Track.For(true), "track ascii fallback");
            AssertEqual("━", ActivityGlyphs.Track.For(false), "track unicode");
            AssertEqual("#", ActivityGlyphs.You.For(true), "you ascii fallback");
            AssertEqual("o", ActivityGlyphs.Rival.For(true), "rival ascii fallback");

            string unicode = ActivityGlyphs.Track.Unicode + ActivityGlyphs.Track.Unicode
                + ActivityGlyphs.You.Unicode + ActivityGlyphs.Bar.Unicode;
            AssertEqual("----#|", ActivityGlyphs.Resolve(unicode, true), "resolve replaces signature glyphs");
            AssertEqual(unicode, ActivityGlyphs.Resolve(unicode, false), "resolve is a no-op when not ascii");
        }

        private static void LaneHintIdsComposeStableZoneIds()
        {
            AssertEqual("aim.hero", LaneHintIds.Hero("aim"), "hero id");
            AssertEqual("aim.flash", LaneHintIds.Flash("aim"), "flash id");
            AssertEqual("aim.footer", LaneHintIds.Footer("aim"), "footer id");
            AssertEqual("duel.hero", LaneHintIds.Hero(LaneHintIds.Duel), "duel lane constant");
            AssertEqual("lane.hero", LaneHintIds.Hero(""), "blank lane falls back safely");
        }

        private static void ActivityManagerSwitchingLaneTearsDownPriorLane()
        {
            ActivityManager manager = new ActivityManager();
            FakeLane aim = new FakeLane("aim");
            FakeLane duel = new FakeLane("duel");
            FakeLane throwing = new FakeLane("parkour", throwOnStop: true);
            manager.RegisterLane(aim);
            manager.RegisterLane(duel);
            manager.RegisterLane(throwing);

            manager.BeginSession("u1", "aim");

            // Switching to a different lane must release the prior lane's per-player state first.
            manager.BeginSession("u1", "duel");
            AssertEqual(1, aim.LeftKeys.Count, "prior lane released on switch");
            AssertEqual("u1", aim.LeftKeys[0], "prior lane released with correct key");
            AssertEqual("duel", manager.CurrentLane("u1"), "now in new lane");

            // Re-entering the SAME lane just re-arms the token; it must NOT tear the lane's own state down.
            manager.BeginSession("u1", "duel");
            AssertEqual(0, duel.LeftKeys.Count, "same-lane re-begin does not release");

            // The prior-lane release is non-throwing even if that lane throws from OnPlayerLeft.
            manager.BeginSession("u2", "parkour");
            manager.BeginSession("u2", "aim"); // parkour.OnPlayerLeft throws; must be swallowed
            AssertEqual("aim", manager.CurrentLane("u2"), "switch completes despite prior-lane fault");
        }

        private static void AimRangeSessionsTrackOccupancyAndWeapons()
        {
            AimRangeSessions sessions = new AimRangeSessions();
            int token = 40;
            AssertEqual(
                AimRangeOccupancyTransition.Entered,
                sessions.UpdateOccupancy("u1", true, () => ++token),
                "enter transition");
            AssertEqual(1, sessions.Count, "one active range session");
            AssertEqual(true, sessions.IsCurrent("u1", 41), "fresh session token current");

            sessions.TrackWeapon("u1", "com15", 100, 60);
            AssertEqual(true, sessions.OwnsWeapon("u1", 100), "owned serial tracked");
            AssertEqual(true, sessions.TryFindWeaponOwner(100, out AimRangeSessions.Session owner), "owner found by serial");
            AssertEqual("com15", owner.PresetId, "preset tracked");

            AssertEqual(
                AimRangeOccupancyTransition.Left,
                sessions.UpdateOccupancy("u1", false, () => 999),
                "leave transition");
            AssertEqual(false, sessions.IsCurrent("u1", 41), "leaving session no longer current");
            AssertEqual(true, sessions.Remove("u1") != null, "leaving session removed by orchestrator");
            AssertEqual(0, sessions.Count, "empty after release");
        }

        private static void RangeTargetStateGuardsGenerationsAndOneCredit()
        {
            RangeTargetState state = new RangeTargetState();
            state.StartRange(7);
            int targetGeneration = state.Show(now: 10d, liveSeconds: 3d);

            AssertEqual(false, state.TryCredit(6, targetGeneration, 11d, true, true), "stale range rejected");
            AssertEqual(false, state.TryCredit(7, targetGeneration, 11d, false, true), "stale session rejected");
            AssertEqual(false, state.TryCredit(7, targetGeneration, 11d, true, false), "foreign weapon rejected");
            AssertEqual(true, state.TryCredit(7, targetGeneration, 11d, true, true), "current owned hit credited");
            AssertEqual(false, state.TryCredit(7, targetGeneration, 11d, true, true), "target credits at most once");

            int next = state.Show(now: 20d, liveSeconds: 1d);
            AssertEqual(false, targetGeneration == next, "target generation advances");
            AssertEqual(false, state.Expire(7, next, 20.5d), "target does not expire early");
            AssertEqual(true, state.Expire(7, next, 21d), "target expires at deadline");
        }

        private static void WeaponShelfStateGuardsClaimGeneration()
        {
            WeaponShelfState state = new WeaponShelfState();
            state.Define(2, "fsp9");
            state.Enable(2);
            AssertEqual(true, state.Spawned(2, 200), "shelf pickup registered");
            AssertEqual(true, state.TryResolveAvailable(200, out int persistentSlot), "persistent pickup resolves without a claim");
            AssertEqual(2, persistentSlot, "persistent pickup keeps its authored slot");
            AssertEqual(true, state.TryResolveAvailable(200, out _), "resolving does not consume the floor pickup");
            AssertEqual(true, state.BeginClaim(200, "u1", out int slotId, out int generation), "claim begins");
            AssertEqual(false, state.ConfirmClaim(slotId, generation + 1, "u1", 200, 5d, 1.5d), "stale claim generation rejected");
            AssertEqual(true, state.ConfirmClaim(slotId, generation, "u1", 200, 5d, 1.5d), "matching claim confirmed");
            AssertEqual(0, state.Due(6.49d).Count, "not replenished early");
            AssertSequence(new[] { 2 }, state.Due(6.5d), "replenishes at deadline");
        }

        private static void AimRangeDeterminismIsStableAndTickIndependent()
        {
            int seed = AimRangeDeterminism.CombineSeed(123, 4, 0, 9, 17031);
            IReadOnlyList<TargetCard> first = AimRangeDeterminism.BuildTargetDeck(3, 1, 12, seed);
            IReadOnlyList<TargetCard> second = AimRangeDeterminism.BuildTargetDeck(3, 1, 12, seed);
            AssertEqual(12, first.Count, "target deck size");
            AssertSequence(
                first.Select(card => card.Kind + ":" + card.AnchorIndex).ToList(),
                second.Select(card => card.Kind + ":" + card.AnchorIndex).ToList(),
                "same seed gives same deck");

            RangePathSegment[] path =
            {
                new RangePathSegment(new RangePoint(0f, 0f, 0f), new RangePoint(10f, 0f, 0f), 2d),
                new RangePathSegment(new RangePoint(10f, 0f, 0f), new RangePoint(0f, 0f, 0f), 2d),
            };
            AssertEqual(5f, AimRangeDeterminism.EvaluatePath(path, 1d).X, "absolute path first segment");
            AssertEqual(5f, AimRangeDeterminism.EvaluatePath(path, 3d).X, "absolute path return segment");
            AssertEqual(5f, AimRangeDeterminism.EvaluatePath(path, 5d).X, "absolute path loops without tick drift");
        }

        private static void RangeBotDeterministicPresetSelectionIsStable()
        {
            int first = AimRangeDeterminism.SelectBotPresetIndex(6, 12345, 7, 1, 3, 17031);
            int repeat = AimRangeDeterminism.SelectBotPresetIndex(6, 12345, 7, 1, 3, 17031);
            int differentOrdinal = AimRangeDeterminism.SelectBotPresetIndex(6, 12345, 7, 1, 4, 17031);
            AssertEqual(first, repeat, "same range/slot/spawn selects same bot preset");
            AssertEqual(true, first >= 0 && first < 6, "selected preset index is bounded");
            AssertEqual(true, differentOrdinal >= 0 && differentOrdinal < 6, "next ordinal also selects a bounded preset");
            AssertEqual(false,
                AimRangeDeterminism.CombineSeed(12345, 7, 1, 3, 17031) == AimRangeDeterminism.CombineSeed(12345, 7, 1, 4, 17031),
                "spawn ordinal advances deterministic seed");
            AssertEqual(-1, AimRangeDeterminism.SelectBotPresetIndex(0, 1, 1, 1, 1, 1), "empty preset list rejected");
        }

        private static void RangeBotDefaultsUseExactAutomaticPresets()
        {
            AimRangeActivityConfig config = new AimRangeActivityConfig();
            AssertEqual(1, config.BotWeaponPresets.Count, "one forced default bot preset");
            AssertSequence(
                new[] { "bot-crossvec" },
                config.BotWeaponPresets.Select(preset => preset.Id).ToArray(),
                "exact bot preset ids");
            AssertSequence(
                new[] { ItemType.GunCrossvec },
                config.BotWeaponPresets.Select(preset => preset.Firearm).ToArray(),
                "exact bot firearms");
            AssertSequence(
                new[] { ItemType.Ammo9x19 },
                config.BotWeaponPresets.Select(preset => preset.Ammo).ToArray(),
                "exact bot ammo types");
            AssertEqual(true, config.BotWeaponPresets.All(AimWeaponPresetRules.IsBotAutomatic),
                "all default bot presets pass automatic-only validation");
            AssertEqual(false,
                AimWeaponPresetRules.IsBotAutomatic(new AimWeaponPresetConfig("com15", ItemType.GunCOM15, ItemType.Ammo9x19, 30)),
                "semi-automatic COM-15 rejected for bots");
        }

        private static void RangeBotLifecycleTransitionsAndLocksFirstAggressor()
        {
            RangeBotLifecycle bot = new RangeBotLifecycle(0);
            bot.Enable();
            AssertEqual(RangeBotState.Spawning, bot.State, "enabled bot begins spawning");
            bot.BeginSpawn(11);
            bot.MarkInitializing();
            bot.MarkPassive();
            AssertEqual(RangeBotState.PassivePatrol, bot.State, "initialized bot patrols passively");

            AssertEqual(true, bot.TryAggro("first", 101, 201, 10d, 0.5d, 12d), "first attacker acquires 12-second lease");
            AssertEqual(22d, bot.AggroExpiresAt, "lease expires exactly 12 seconds after first hit");
            AssertEqual(true, bot.TryAggro("first", 101, 201, 14d, 0.5d, 12d), "same attacker repeat hit is accepted");
            AssertEqual(22d, bot.AggroExpiresAt, "same attacker repeat hit does not refresh active lease");
            AssertEqual(false, bot.TryAggro("second", 102, 202, 15d, 0.5d, 12d), "second attacker cannot steal lease");
            AssertEqual("first", bot.AggressorKey, "first attacker remains locked");
            AssertEqual(22d, bot.AggroExpiresAt, "failed lease steal does not refresh expiry");
            bot.Advance(10.49d);
            AssertEqual(RangeBotState.Alerted, bot.State, "bot remains alerted during acquire delay");
            bot.Advance(10.5d);
            AssertEqual(RangeBotState.ReturningFire, bot.State, "bot returns fire after acquire delay");
            bot.Advance(21.999d);
            AssertEqual(RangeBotState.ReturningFire, bot.State, "12-second lease remains active immediately before expiry");
            bot.Advance(22d);
            AssertEqual(RangeBotState.PassivePatrol, bot.State, "12-second aggro lease expiry returns to passive path");
        }

        private static void RangeBotDeathRespawnClearsAggroAndRejectsStaleGeneration()
        {
            RangeBotLifecycle bot = new RangeBotLifecycle(1);
            bot.Enable();
            bot.BeginSpawn(9);
            bot.MarkInitializing();
            bot.MarkPassive();
            bot.TryAggro("human", 77, 88, 2d, 0d, 5d);
            AssertEqual(true, bot.AcceptsCallback(9), "live spawn accepts callback");

            bot.MarkDead(3d, 4d);
            AssertEqual(false, bot.HasAggressor, "death clears aggressor immediately");
            AssertEqual(false, bot.AcceptsCallback(9), "old spawn callback rejected immediately");
            AssertEqual(RangeBotState.Dead, bot.State, "death state recorded");
            bot.Advance(3d);
            AssertEqual(RangeBotState.RespawnWait, bot.State, "dead advances to respawn wait");
            AssertEqual(false, bot.RespawnDue(6.99d), "respawn not early");
            AssertEqual(true, bot.RespawnDue(7d), "respawn due at configured delay");
            bot.BeginSpawn(10);
            AssertEqual(true, bot.AcceptsCallback(10), "new generation accepts callbacks");
            AssertEqual(false, bot.AcceptsCallback(9), "previous generation stays stale");
        }

        private static void RangeDamagePolicyAllowsOnlyOwnedCombatDirections()
        {
            AssertEqual(
                RangeDamageDisposition.AllowHumanToOwnedBot,
                RangeDamagePolicy.Decide(true, true, false, false, true),
                "current human owned gun can damage owned bot");
            AssertEqual(
                RangeDamageDisposition.Cancel,
                RangeDamagePolicy.Decide(true, false, false, false, true),
                "foreign human weapon cannot damage owned bot");
            AssertEqual(
                RangeDamageDisposition.AllowOwnedBotToParticipant,
                RangeDamagePolicy.Decide(false, false, true, true, false),
                "owned bot can apply real damage to participant");
            AssertEqual(
                RangeDamageDisposition.Cancel,
                RangeDamagePolicy.Decide(true, true, false, true, false),
                "range human PvP is cancelled");
            AssertEqual(
                RangeDamageDisposition.Cancel,
                RangeDamagePolicy.Decide(false, false, true, false, false),
                "owned bot cannot damage unrelated player");
            AssertEqual(
                RangeDamageDisposition.Ignore,
                RangeDamagePolicy.Decide(false, false, false, false, false),
                "unrelated damage is untouched");
        }

        private static void AimRangeLethalResetStateRequiresNewTutorialLife()
        {
            AimRangeSessions sessions = new AimRangeSessions();
            int token = 0;
            sessions.UpdateOccupancy("u1", true, () => ++token);
            sessions.TrackWeapon("u1", "com15", 55, 60);
            AssertEqual(true, sessions.BeginLethalReset("u1", 100), "lethal reset marker set before role change");
            AssertEqual(true, sessions.ConsumePendingReset("u1"), "nested spawn consumes range spawn marker");
            AssertEqual(
                AimRangeLethalResetResult.Reinitialized,
                sessions.CompleteLethalReset("u1", isTutorial: true, currentLifeId: 101),
                "Tutorial with changed LifeId completes reset");
            AssertEqual(true, sessions.OwnsWeapon("u1", 55), "tracked gun survives reset state transition");
            AssertEqual(true, sessions.TryGet("u1", out AimRangeSessions.Session session), "session remains current");
            AssertEqual(60, session.TrackedReserveAmmo, "tracked reserve survives reset");

            AssertEqual(true, sessions.BeginLethalReset("u1", 101), "second lethal reset marker set");
            AssertEqual(
                AimRangeLethalResetResult.EmergencyRestoreRequired,
                sessions.CompleteLethalReset("u1", isTutorial: false, currentLifeId: 101),
                "blocked/redirected same-life reset requires emergency restore");
            AssertEqual(false, session.PendingRangeResetSpawn, "failed reset marker cleared after decision");
        }

        private static void RangeBotConfigDefaultsToExplicitOptIn()
        {
            AssertEqual(0, new AimRangeActivityConfig().BotCount, "native bots require explicit config opt-in");
        }

        private static void RangeBotLobbyPolicyRequiresHumansAndHeadroom()
        {
            AssertEqual(0, RangeBotLobbyPolicy.AllowedBotCount(2, 0, 0, 20, false), "empty lobby rejects counted dummies");
            AssertEqual(0, RangeBotLobbyPolicy.AllowedBotCount(2, 1, 1, 20, false), "unprotected solo lobby rejects counted dummies");
            AssertEqual(2, RangeBotLobbyPolicy.AllowedBotCount(2, 1, 1, 20, true), "protected solo lobby allows requested bots");
            AssertEqual(1, RangeBotLobbyPolicy.AllowedBotCount(2, 1, 2, 4, true), "protected solo lobby still leaves the final public slot spare");
            AssertEqual(2, RangeBotLobbyPolicy.AllowedBotCount(2, 2, 2, 20, false), "two humans with headroom allow requested bots without a lock");
            AssertEqual(1, RangeBotLobbyPolicy.AllowedBotCount(2, 2, 2, 4, false), "last public slot remains spare");
            AssertEqual(0, RangeBotLobbyPolicy.AllowedBotCount(2, 2, 3, 4, false), "full-threshold connection count rejects bots");
            AssertEqual(0, RangeBotLobbyPolicy.AllowedBotCount(2, 2, 2, 0, false), "unknown capacity fails closed");
        }

        private static void DuplicateShelfPresetIdsAreRejected()
        {
            List<AimWeaponPresetConfig> presets = new List<AimWeaponPresetConfig>
            {
                new AimWeaponPresetConfig("same", ItemType.GunCOM15, ItemType.Ammo9x19, 60),
                new AimWeaponPresetConfig("unique", ItemType.GunAK, ItemType.Ammo762x39, 90),
                new AimWeaponPresetConfig("same", ItemType.GunFSP9, ItemType.Ammo9x19, 120),
                new AimWeaponPresetConfig("invalid", ItemType.GunAK, ItemType.Ammo9x19, 30),
                new AimWeaponPresetConfig("invalid", ItemType.GunAK, ItemType.Ammo9x19, 30),
            };

            HashSet<string> duplicates = AimWeaponPresetRules.FindDuplicateConventionalIds(presets);
            AssertEqual(1, duplicates.Count, "only duplicate valid preset ids are rejected");
            AssertEqual(true, duplicates.Contains("same"), "duplicate active id identified");
        }

        private static void RangeBotConfigValidationClampsAndFilters()
        {
            AimRangeActivityConfig config = new AimRangeActivityConfig
            {
                BotCount = 99,
                BotHealth = float.NaN,
                BotShotCadenceSeconds = -5f,
                BotMaxRetaliationDistance = float.PositiveInfinity,
                BotAimToleranceDegrees = 1000f,
                BotWeaponPresets = new List<AimWeaponPresetConfig>
                {
                    new AimWeaponPresetConfig("e11", ItemType.GunE11SR, ItemType.Ammo556x45, 180),
                    new AimWeaponPresetConfig("logicer", ItemType.GunLogicer, ItemType.Ammo762x39, 200),
                    new AimWeaponPresetConfig("ak", ItemType.GunAK, ItemType.Ammo762x39, 180),
                    new AimWeaponPresetConfig("semi", ItemType.GunCOM15, ItemType.Ammo9x19, 30),
                    new AimWeaponPresetConfig("wrong-ammo", ItemType.GunAK, ItemType.Ammo9x19, 30),
                    new AimWeaponPresetConfig("special", ItemType.GunShotgun, ItemType.Ammo12gauge, 30),
                },
            };

            RangeBotValidatedSettings validated = RangeBotValidatedSettings.From(config);
            AssertEqual(2, validated.Count, "bot count clamped to authored slots");
            AssertEqual(250f, validated.Health, "NaN health uses safe default");
            AssertEqual((double)0.5f, validated.ShotCadenceSeconds, "shot cadence clamped to response window");
            AssertEqual(24d, validated.MaxRetaliationDistance, "infinite distance uses safe default");
            AssertEqual(30f, validated.AimToleranceDegrees, "aim tolerance capped");
            AssertEqual((double)0.5f, validated.AcquireDelaySeconds, "default pre-trigger stage is half a second");
            AssertEqual(1, validated.Presets.Count, "legacy non-Crossvec deck migrates to one Crossvec preset");
            AssertSequence(new[] { "bot-crossvec" }, validated.Presets.Select(preset => preset.Id).ToArray(),
                "forced Crossvec fallback installed");
            AssertEqual(true, validated.Presets.All(AimWeaponPresetRules.IsBotAutomatic),
                "validated bot presets are all automatic");
        }

        private static void RangeBotRegistryUsesLiveIdentityIndexes()
        {
            RangeBotRegistry registry = new RangeBotRegistry();
            int generation = registry.BeginSpawn(3);
            RangeBotIdentity identity = new RangeBotIdentity(3, generation, hubInstanceId: 77, networkId: 88, playerId: 99);
            AssertEqual(true, registry.Register(identity), "current bot spawn registers");
            AssertEqual(true, registry.TryByHub(77, out RangeBotIdentity byHub), "hub index resolves");
            AssertEqual(3, byHub.SlotId, "hub maps to slot");
            AssertEqual(true, registry.TryByNetwork(88, out _), "network index resolves");
            AssertEqual(true, registry.TryByPlayer(99, out _), "player index resolves");

            registry.InvalidateSlot(3);
            AssertEqual(false, registry.IsCurrent(3, generation), "invalidated generation is stale");
            AssertEqual(false, registry.TryByHub(77, out _), "hub index removed on invalidation");
        }

        private static void ParticipantIdentityRulesRejectNonHumans()
        {
            AssertEqual(true, new ParticipantIdentity(false, false, true, true, "steam@steam").IsCanonicalHuman, "ready human accepted");
            AssertEqual(false, new ParticipantIdentity(true, false, true, true, "host").IsCanonicalHuman, "host rejected");
            AssertEqual(false, new ParticipantIdentity(false, true, true, true, "ID_Dummy").IsCanonicalHuman, "dummy rejected");
            AssertEqual(false, new ParticipantIdentity(false, false, true, false, "steam@steam").IsCanonicalHuman, "unready player rejected");
            AssertEqual("net:15", ParticipantIdentityRules.HumanKey("", 15, 20), "human network fallback");
            AssertEqual("dummy-net:15", ParticipantIdentityRules.DummyInstanceKey(15, 20, 25), "dummy key is live identity");
        }

        private static void MerWorldTransformComposesOneLevelParent()
        {
            MerWorldTransform root = new MerWorldTransform(
                new Vector3(10f, 2f, 20f),
                MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f)),
                new Vector3(2f, 1f, 1f));
            MerWorldTransform parent = MerWorldTransformComposer.Compose(
                root,
                new Vector3(1f, 0f, 0f),
                new Quaternion(0f, 0f, 0f, 1f),
                new Vector3(0.5f, 2f, 3f));
            MerWorldTransform child = MerWorldTransformComposer.Compose(
                parent,
                new Vector3(1f, 1f, 1f),
                MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 90f, 0f)),
                new Vector3(2f, 0.5f, 1f));

            AssertVectorNear(new Vector3(10f, 2f, 18f), parent.Position, 0.001f, "parent world position");
            AssertVectorNear(new Vector3(13f, 4f, 17f), child.Position, 0.001f, "child world position includes parent");
            AssertVectorNear(new Vector3(2f, 1f, 3f), child.Scale, 0.001f, "child world scale includes parent");
            AssertEqual(true, MerWorldTransformComposer.RotationAngleDegrees(
                MerWorldTransformComposer.QuaternionFromEuler(new Vector3(0f, 180f, 0f)), child.Rotation) < 0.01f,
                "child world rotation includes parent");
        }

        private static void WarmupHandsBackEveryParticipantWhateverRoleTheyHold()
        {
            // RoleAssigner.CheckPlayer returns false for anyone alive, so a participant left holding ANY
            // live role is skipped by vanilla and keeps it. The hand-back used to require Tutorial, which
            // let an RA-assigned SCP survive the handoff and spawn on top of vanilla's whole SCP multiset.
            foreach (RoleTypeId live in new[]
            {
                RoleTypeId.Tutorial, RoleTypeId.Scp3114, RoleTypeId.Scp049, RoleTypeId.Scp173,
                RoleTypeId.ClassD, RoleTypeId.NtfCaptain, RoleTypeId.Spectator, RoleTypeId.Overwatch,
            })
            {
                AssertEqual(true, WarmupHandoffPolicy.ShouldHandBack(true, live),
                    $"a participant holding {live} is handed back");
            }

            // Already handed back: nothing to do.
            AssertEqual(false, WarmupHandoffPolicy.ShouldHandBack(true, RoleTypeId.None),
                "a participant already at None is left alone");

            // Ownership still gates it: players this selector never moved in stay untouched, whatever
            // role they hold, so an admin or another plugin managing someone is not clobbered.
            foreach (RoleTypeId role in new[] { RoleTypeId.Tutorial, RoleTypeId.Scp3114, RoleTypeId.None })
            {
                AssertEqual(false, WarmupHandoffPolicy.ShouldHandBack(false, role),
                    $"a non-participant holding {role} is not touched");
            }

            // Spectator is NOT the hand-back target: CheckPlayer excludes a spectator that is not ready
            // to respawn, so parking players there can drop them from assignment entirely.
            AssertEqual(RoleTypeId.None, WarmupHandoffPolicy.HandoffRole, "participants are handed back as None");
            AssertEqual(true, WarmupHandoffPolicy.HandBackSucceeded(RoleTypeId.None), "None means the hand-back landed");
            AssertEqual(false, WarmupHandoffPolicy.HandBackSucceeded(RoleTypeId.Scp3114),
                "a blocked hand-back is reported, not assumed successful");
        }

        private static void DownrangeTargetsFaceTheShooter()
        {
            WarmupHallLayout hall = new WarmupHallLayout(Vector3.zero);
            AimRangeLayout layout = new AimRangeLayout(hall);

            // A ShootingTargetToy's visible face is its local -X. Downrange of the counter, that face has
            // to point back upstream at the shooter, i.e. world -X. Passing the bare frame turn instead of
            // the composition left every target 90 degrees off, edge-on to the firing line.
            Vector3 faced = AimRangeLayout.DownrangeFacing * new Vector3(-1f, 0f, 0f);
            AssertEqual(true, Vector3.Dot(faced, new Vector3(-1f, 0f, 0f)) > 0.999f,
                $"target face points back upstream at the shooter (got {faced.x:0.##},{faced.y:0.##},{faced.z:0.##})");

            // And it must NOT be the raw range turn, which is the specific bug that shipped.
            Vector3 rawTurned = AimRangeLayout.RangeRotation * new Vector3(-1f, 0f, 0f);
            AssertEqual(true, Vector3.Dot(rawTurned, new Vector3(-1f, 0f, 0f)) < 0.5f,
                "the raw range turn is a different rotation from the downrange facing");

            foreach (SlidingTargetTrackDefinition track in layout.SlidingTargetTracks)
            {
                Vector3 trackFace = track.Rotation * new Vector3(-1f, 0f, 0f);
                AssertEqual(true, Vector3.Dot(trackFace, new Vector3(-1f, 0f, 0f)) > 0.999f,
                    $"sliding track {track.SlotId} faces the shooter");
            }
        }

        private static void EveryCompartmentIsReachableFromTheSpawnPoint()
        {
            WarmupHallLayout hall = new WarmupHallLayout(new Vector3(5f, 400f, -7f));

            // Players are meant to walk the whole station. Every compartment - and the deck-level route
            // through it - must count as inside, or the maintain sweep teleports them back to spawn.
            foreach (StationZone zone in hall.Zones)
            {
                AssertEqual(true, hall.IsInsideStation(hall.ZoneCenter(zone, 0.5f)),
                    $"{zone.Id} centre is inside the station");
                AssertEqual(true, hall.IsInsideStation(hall.World(zone.MinX + 0.5f, 0.5f, zone.MinZ + 0.5f)),
                    $"{zone.Id} near corner is inside the station");
                AssertEqual(true, hall.IsInsideStation(hall.World(zone.MaxX - 0.5f, 0.5f, zone.MaxZ - 0.5f)),
                    $"{zone.Id} far corner is inside the station");
            }

            // The far ends specifically: these are what a spawn-radius leash made unreachable.
            AssertEqual(true, hall.IsInsideStation(hall.World(WarmupHallLayout.AimBayFarX - 1f, 0.5f, 0f)),
                "the far end of the aim bay is reachable");
            AssertEqual(true, hall.IsInsideStation(hall.World(0f, 0.5f, WarmupHallLayout.ShaftFarZ - 1f)),
                "the far end of the parkour shaft is reachable");
            AssertEqual(true, hall.IsInsideStation(hall.World(0f, 8f, WarmupHallLayout.ShaftFarZ - 1f)),
                "a runner high up the shaft is still inside the station");
            AssertEqual(true, hall.IsInsideStation(hall.World(WarmupHallLayout.ObservationFarX + 1f, 0.5f, 0f)),
                "the observation deck is reachable");

            // A radius around the spawn point cannot express this: the hub centre alone is out of range.
            float hubDistance = Vector3.Distance(hall.SpawnPosition, hall.ZoneCenter(hall.Hub, 0.5f));
            AssertEqual(true, hubDistance > 30f,
                $"hub centre is {hubDistance:0.#}m from spawn, so a 30m leash would fence players into the gallery");

            // Outside is still outside.
            AssertEqual(false, hall.IsInsideStation(hall.World(WarmupHallLayout.AimBayFarX + 12f, 0.5f, 0f)),
                "a point past the aim bay bulkhead is outside");
            AssertEqual(false, hall.IsInsideStation(hall.World(0f, 0.5f, WarmupHallLayout.ShaftFarZ + 12f)),
                "a point past the shaft end is outside");
            AssertEqual(false, hall.IsInsideStation(hall.World(0f, -9f, 0f)),
                "a point far below the deck is outside");
        }

        private static void StationCompartmentsTileWithoutOverlapOrGap()
        {
            WarmupHallLayout hall = new WarmupHallLayout(new Vector3(10f, 1000f, -20f));
            AssertEqual(6, hall.Zones.Count, "station compartment count");

            // No two compartments may share interior floor, or the deck would be built twice and every
            // occupancy rule would be ambiguous about which room a player is in.
            for (int i = 0; i < hall.Zones.Count; i++)
            {
                for (int j = i + 1; j < hall.Zones.Count; j++)
                {
                    StationZone a = hall.Zones[i];
                    StationZone b = hall.Zones[j];
                    bool overlaps = a.MinX < b.MaxX - 0.01f && b.MinX < a.MaxX - 0.01f &&
                        a.MinZ < b.MaxZ - 0.01f && b.MinZ < a.MaxZ - 0.01f;
                    AssertEqual(false, overlaps, $"{a.Id} and {b.Id} do not overlap");
                }
            }

            // Every hatch must lie exactly on a face shared by two compartments, or it opens onto nothing.
            foreach (StationOpening hatch in hall.Openings)
            {
                int touching = hall.Zones.Count(zone => hatch.InConstantXWall
                    ? (Math.Abs(zone.MinX - hatch.Plane) < 0.01f || Math.Abs(zone.MaxX - hatch.Plane) < 0.01f) &&
                      zone.MinZ <= hatch.From + 0.01f && zone.MaxZ >= hatch.To - 0.01f
                    : (Math.Abs(zone.MinZ - hatch.Plane) < 0.01f || Math.Abs(zone.MaxZ - hatch.Plane) < 0.01f) &&
                      zone.MinX <= hatch.From + 0.01f && zone.MaxX >= hatch.To - 0.01f);
                AssertEqual(2, touching, $"hatch at {hatch.Plane} joins exactly two compartments");
            }

            AssertEqual(5, hall.Openings.Count, "one hatch per compartment beyond the hub");
        }

        private static void StationWallPanelsSealEveryCompartmentFace()
        {
            WarmupHallLayout hall = new WarmupHallLayout(Vector3.zero);

            foreach (StationZone zone in hall.Zones)
            {
                IReadOnlyList<StationWallSegment> segments = hall.BuildWallSegments(zone);

                foreach (bool inConstantXWall in new[] { true, false })
                {
                    float spanMin = inConstantXWall ? zone.MinZ : zone.MinX;
                    float spanMax = inConstantXWall ? zone.MaxZ : zone.MaxX;
                    foreach (float plane in inConstantXWall
                        ? new[] { zone.MinX, zone.MaxX }
                        : new[] { zone.MinZ, zone.MaxZ })
                    {
                        // Sample the face on a fine grid: every point is either solid panel or inside a
                        // hatch. Anything else is a hole players fall through.
                        for (float u = spanMin + 0.05f; u < spanMax; u += 0.25f)
                        {
                            for (float y = 0.05f; y < zone.CeilingHeight; y += 0.25f)
                            {
                                bool covered = segments.Any(seg =>
                                    seg.InConstantXWall == inConstantXWall &&
                                    Math.Abs(seg.Plane - plane) < 0.01f &&
                                    u >= seg.From - 0.001f && u <= seg.To + 0.001f &&
                                    y >= seg.BottomY - 0.001f && y <= seg.TopY + 0.001f);
                                bool hatch = hall.Openings.Any(o =>
                                    o.InConstantXWall == inConstantXWall &&
                                    Math.Abs(o.Plane - plane) < 0.01f &&
                                    u >= o.From - 0.001f && u <= o.To + 0.001f &&
                                    y <= o.Height + 0.001f);
                                AssertEqual(true, covered || hatch,
                                    $"{zone.Id} face at {plane} is sealed or open at ({u:0.##}, {y:0.##})");
                            }
                        }
                    }
                }

                AssertEqual(true, segments.All(seg => seg.Span > 0.01f && seg.Height > 0.01f),
                    $"{zone.Id} emits no degenerate panels");
            }
        }

        private static void GalleryStandsKeepTheAisleAndFitTheRoom()
        {
            WarmupHallLayout hall = new WarmupHallLayout(Vector3.zero);
            const float spacing = 3.7f;
            const float standWidth = 1.9f;

            AssertEqual(true, hall.DisplayCapacity(spacing, standWidth) >= 7,
                "gallery stands at least the seven default SCP options");

            IReadOnlyList<GalleryDisplaySlot> slots = hall.BuildDisplaySlots(7, spacing, standWidth);
            AssertEqual(7, slots.Count, "seven stands for seven options");

            foreach (GalleryDisplaySlot slot in slots)
            {
                AssertEqual(true, Math.Abs(slot.StandTopCenter.x) + standWidth / 2f < WarmupHallLayout.HubHalfWidth,
                    "stand stays inside the gallery walls");
                AssertEqual(true, slot.StandTopCenter.z > WarmupHallLayout.GalleryFarZ &&
                    slot.StandTopCenter.z < WarmupHallLayout.ConnectorFarZ,
                    "stand stays inside the gallery");
                AssertEqual(true, slot.CoinPosition.z > slot.StandTopCenter.z,
                    "coin sits in front of its model, toward arriving players");
            }

            // The centre aisle stays clear so the wall logo reads straight down it from the spawn point.
            AssertEqual(true, slots.All(slot => Math.Abs(slot.StandTopCenter.x) > 1f), "no stand blocks the centre aisle");

            // Stands must not collide with each other.
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    float dx = Math.Abs(slots[i].StandTopCenter.x - slots[j].StandTopCenter.x);
                    float dz = Math.Abs(slots[i].StandTopCenter.z - slots[j].StandTopCenter.z);
                    AssertEqual(true, dx > standWidth + 0.2f || dz > slots[i].StandDepth + 0.2f,
                        $"stands {i} and {j} do not overlap");
                }
            }

            // Arrivals land in the gallery, on the deck, looking at the back rank.
            AssertEqual(true, hall.SpawnPosition.z > WarmupHallLayout.BackRankZ, "spawn is in front of the back rank");
            AssertEqual(true, hall.SpawnPosition.z < WarmupHallLayout.ConnectorFarZ, "spawn is inside the gallery");
            AssertEqual(180f, hall.SpawnYaw, "arrivals face the SCP stands");
            AssertEqual(true, hall.LogoAnchor.z < WarmupHallLayout.BackRankZ, "logo is on the wall behind the back rank");
        }

        private static void AimBayHostsThePersistentCounterArmoury()
        {
            Vector3 origin = new Vector3(100f, 200f, 300f);
            WarmupHallLayout hall = new WarmupHallLayout(origin);
            AimRangeLayout layout = new AimRangeLayout(hall);

            AssertEqual(19f, layout.ShellWidth, "aim bay spans the full width of the east arm");
            AssertEqual(18.4f, layout.ShootingCounterWidth, "shooting counter spans the bay inside its side walls");
            AssertEqual(14.5f, layout.ShootingLineX, "shooting line sits just inside the bay hatch");
            AssertEqual(36f, layout.BackstopX, "backstop is the far bulkhead of the arm");

            AssertEqual(6, layout.ShelfAnchors.Count, "six counter armoury pickups");
            AssertSequence(Enumerable.Range(0, 6).ToArray(), layout.ShelfAnchors.Select(anchor => anchor.SlotId).ToArray(),
                "armoury slot ids remain deterministic");
            AssertEqual(true, layout.ShelfAnchors.All(anchor =>
                    Math.Abs(anchor.LocalPosition.y - (origin.y + AimRangeLayout.ShootingCounterHeight + 0.28f)) < 0.001f),
                "counter guns sit visibly above the counter top");
            AssertEqual(true, layout.ShelfAnchors.All(anchor =>
                    Math.Abs(anchor.LocalPosition.x - (origin.x + layout.ShootingLineX)) < 0.001f),
                "counter guns share the shooting line");
            AssertEqual(3, layout.ShelfAnchors.Count(anchor => anchor.LocalPosition.z < origin.z), "three counter guns left of centre");
            AssertEqual(3, layout.ShelfAnchors.Count(anchor => anchor.LocalPosition.z > origin.z), "three counter guns right of centre");

            AssertEqual(2, layout.AttachmentWorkstationAnchors.Count, "two native attachment workstations");
            AimWorkstationAnchor near = layout.AttachmentWorkstationAnchors[0];
            AimWorkstationAnchor far = layout.AttachmentWorkstationAnchors[1];
            AssertEqual(true, Math.Abs((near.Position.z - origin.z) + (far.Position.z - origin.z)) < 0.001f,
                "attachment workstations are symmetric across the bay");
            AssertEqual(true, Math.Abs(Math.Abs(near.Position.z - origin.z) - (19f / 2f - AimRangeLayout.AttachmentWorkstationWallInset)) < 0.001f,
                "attachment workstations hug the side walls");

            // Ownership deliberately reaches back through the hatch so a player in the doorway is still
            // governed by the range, while the HUD only swaps inside the bay proper.
            Vector3 hubSide = hall.World(layout.EntranceX - 1.5f, 0.5f, 0f);
            Vector3 baySide = hall.World(layout.EntranceX + 1.5f, 0.5f, 0f);
            AssertEqual(false, layout.ContainsAimUi(hubSide), "hub side keeps the original SCP draft panel");
            AssertEqual(true, layout.ContainsAimUi(baySide), "bay side shows the Aim HUD");
            AssertEqual(true, layout.ContainsVerified(hubSide), "range still owns a shooter standing in its hatch");
            AssertEqual(true, layout.RequiredRetaliationDistance > 27f,
                "bot retaliation covers the full downrange diagonal of the bay");
        }

        private static void AimBayDefinesThreeClearLanesAlongTheArm()
        {
            Vector3 origin = new Vector3(25f, 50f, 75f);
            WarmupHallLayout hall = new WarmupHallLayout(origin);
            AimRangeLayout layout = new AimRangeLayout(hall);

            AssertEqual(19f, AimRangeLayout.Width, "authored lane width across the bay");
            AssertEqual(21.5f, AimRangeLayout.Depth, "authored downrange length");
            AssertEqual(3, layout.SlidingTargetTracks.Count, "three persistent sliding tracks");
            AssertEqual(1.70f * 1.35f, layout.SlidingTargetTracks[2].MinimumSpeed,
                "deepest sliding target minimum speed is 35 percent faster");
            AssertEqual(2.15f * 1.35f, layout.SlidingTargetTracks[2].MaximumSpeed,
                "deepest sliding target maximum speed is 35 percent faster");
            AssertEqual(2, layout.BotPaths.Count, "two authored bot patrol paths");
            AssertEqual(6, layout.BotCovers.Count, "three authored covers per bot slot");
            AssertEqual(true, layout.SphereBayWidth >= SphereTargetLayout.RequiredClearWidth,
                "sphere lane keeps the required clear width");
            AssertEqual(true, layout.BackstopX - layout.ShootingLineX >= SphereTargetLayout.RequiredClearDepth,
                "sphere lane keeps the required clear depth");

            // Lane 1 is everything outboard of the first divider; every bot fixture must live there so its
            // fire never crosses into the sliding or sphere lanes.
            foreach (AimBotPath path in layout.BotPaths)
            {
                AssertEqual(true, path.Segments.All(segment => segment.From.Z < layout.DividerOneZ && segment.To.Z < layout.DividerOneZ),
                    $"bot path {path.SlotId} stays in lane 1");
                AssertEqual(true, path.Segments.All(segment => segment.From.Z > layout.BayMinZ && segment.To.Z > layout.BayMinZ),
                    $"bot path {path.SlotId} stays inside the bay wall");
                AssertEqual(true, path.Segments.All(segment =>
                        Math.Abs(segment.From.X - segment.To.X) > 0.0001f || Math.Abs(segment.From.Z - segment.To.Z) > 0.0001f),
                    $"bot path {path.SlotId} has no zero-distance dwell segment");
            }

            foreach (int slotId in layout.BotPaths.Select(path => path.SlotId))
            {
                AimBotCover[] covers = layout.BotCovers.Where(cover => cover.SlotId == slotId).ToArray();
                AssertEqual(3, covers.Length, $"bot slot {slotId} cover count");
                AssertEqual(2, covers.Count(cover => cover.FullHeight), $"bot slot {slotId} full-height cover count");
                AssertEqual(true, covers.Where(cover => cover.FullHeight).All(cover => Math.Abs(cover.Size.y - 2.2f) < 0.001f),
                    $"bot slot {slotId} full-height covers are 2.2m tall");
                AssertEqual(true, covers.All(cover =>
                        cover.Center.z - origin.z < layout.DividerOneZ && cover.ReloadPoint.z - origin.z < layout.DividerOneZ),
                    $"bot slot {slotId} covers and reload points stay in lane 1");
                AssertEqual(true, covers.All(cover => Math.Abs(cover.ReloadPoint.y - origin.y) < 0.001f),
                    $"bot slot {slotId} reload points sit on the deck");
                AssertEqual(true, covers.All(cover => cover.ReloadPoint.x > cover.Center.x),
                    $"bot slot {slotId} reload points sit behind cover, away from the firing line");
            }

            foreach (SlidingTargetTrackDefinition track in layout.SlidingTargetTracks)
            {
                AssertEqual(true, track.EndpointA.z - origin.z > layout.DividerOneZ && track.EndpointA.z - origin.z < layout.DividerTwoZ,
                    $"sliding track {track.SlotId} endpoint A stays in lane 2");
                AssertEqual(true, track.EndpointB.z - origin.z > layout.DividerOneZ && track.EndpointB.z - origin.z < layout.DividerTwoZ,
                    $"sliding track {track.SlotId} endpoint B stays in lane 2");
                AssertEqual(true, track.EndpointA.x > origin.x + layout.ShootingLineX && track.EndpointA.x < origin.x + layout.BackstopX,
                    $"sliding track {track.SlotId} sits downrange of the counter");
            }

            AssertEqual(true, layout.SphereLaneOrigin.z - origin.z > layout.DividerTwoZ, "sphere cloud starts in lane 3");
        }

        private static void SlidingTargetMotionIsDeterministicAndAbsolute()
        {
            AimRangeLayout layout = new AimRangeLayout(new WarmupHallLayout(Vector3.zero));
            SlidingTargetTrackDefinition track = layout.SlidingTargetTracks[1];
            AssertEqual(true, SlidingTargetLogic.TryBuildMotion(track, 1234, 7, 1, 17031, out SlidingTargetMotion first), "first motion builds");
            AssertEqual(true, SlidingTargetLogic.TryBuildMotion(track, 1234, 7, 1, 17031, out SlidingTargetMotion repeat), "repeat motion builds");
            AssertEqual(first.Speed, repeat.Speed, "same seed keeps speed");
            AssertEqual(first.SpeedVariation, repeat.SpeedVariation, "same seed keeps speed variation depth");
            AssertEqual(first.SpeedVariationPeriod, repeat.SpeedVariationPeriod, "same seed keeps speed variation period");
            AssertEqual(first.Phase, repeat.Phase, "same seed keeps phase");
            AssertEqual(first.Reversed, repeat.Reversed, "same seed keeps direction");
            AssertVectorNear(first.Evaluate(3.75d), repeat.Evaluate(3.75d), 0.0001f, "absolute evaluation is deterministic");
            AssertVectorNear(first.Evaluate(3.75d), first.Evaluate(1d + 2.75d), 0.0001f, "tick partition cannot affect position");
            float minimumSpeed = Enumerable.Range(0, 80).Min(i => first.EvaluateSpeed(i * 0.1d));
            float maximumSpeed = Enumerable.Range(0, 80).Max(i => first.EvaluateSpeed(i * 0.1d));
            AssertEqual(true, maximumSpeed - minimumSpeed > first.Speed * 0.3f,
                "sliding target visibly accelerates and decelerates over time");
        }

        private static void SphereTargetStateCreditsOnceAndRelocates()
        {
            SphereTargetState state = new SphereTargetState();
            state.Start(9, 2, 0d);
            AssertEqual(true, state.TryBeginSpawn(9, 0, 0d, out SphereTargetSpawnTicket first), "sphere spawn begins");
            AssertEqual(true, state.CommitSpawn(9, first, 4), "sphere spawn commits");
            AssertEqual(true, state.TryCommitRelocation(9, 0, first.Generation, 5, out int relocatedGeneration), "live sphere commits immediate relocation");
            AssertEqual(false, state.TryCommitRelocation(9, 0, first.Generation, 6, out _), "duplicate old-generation hit rejected");
            AssertEqual(true, state.IsCurrentLive(9, 0, relocatedGeneration), "relocated generation remains live without an intermediate phase");
            state.Stop();
            AssertEqual(false, state.IsCurrentLive(9, 0, relocatedGeneration), "stop invalidates relocated generation");
        }

        private static void SphereTargetLayoutFitsThirdLane()
        {
            SphereTargetLayout layout = SphereTargetLayout.CreateWidenedThirdLane(Vector3.zero, Quaternion.identity);
            AssertEqual(50, layout.Points.Count, "fixed sphere deck provides thirty relocation gaps for 20 live targets");
            AssertEqual(20, new AimRangeActivityConfig().SphereActiveCount, "twenty simultaneous spheres are the activity default");
            AssertEqual(20, new SphereTargetSettings().ActiveCount, "isolated sphere settings share the twenty-target default");
            float minX = layout.Points.Min(point => point.x);
            float maxX = layout.Points.Max(point => point.x);
            float minY = layout.Points.Min(point => point.y);
            float maxY = layout.Points.Max(point => point.y);
            float minZ = layout.Points.Min(point => point.z);
            float maxZ = layout.Points.Max(point => point.z);
            AssertEqual(true, maxX - minX <= SphereTargetLayout.RequiredClearWidth, "sphere cloud fits lane width");
            AssertEqual(true, maxZ - minZ <= SphereTargetLayout.RequiredClearDepth, "sphere cloud fits lane depth");
            AssertEqual(true, maxY - minY > 3f, "sphere cloud fills the usable vertical volume");
            AssertEqual(true, maxZ - minZ > 10.5f, "sphere cloud fills the usable downrange volume");
            AssertEqual(true, layout.Points.Select(point => point.z).Distinct().Count() >= 45,
                "sphere cloud does not collapse onto repeated depth planes");
            AssertEqual(layout.Points.Count, layout.Points.Distinct().Count(), "sphere points are distinct");

            int[] firstPass = Enumerable.Range(0, 50)
                .Select(ordinal => SphereTargetLayout.GetSequencePointIndex(ordinal, layout.Points.Count))
                .ToArray();
            int[] secondPass = Enumerable.Range(50, 50)
                .Select(ordinal => SphereTargetLayout.GetSequencePointIndex(ordinal, layout.Points.Count))
                .ToArray();
            AssertEqual(50, firstPass.Distinct().Count(), "the sequence visits every fixed deck point before wrapping");
            AssertSequence(firstPass, secondPass, "the fixed 50-point sequence wraps without rerolling");

            SphereTargetLayout widened = SphereTargetLayout.CreateWidenedThirdLane(Vector3.zero, Quaternion.identity, 15.3f);
            float widenedMinX = widened.Points.Min(point => point.x);
            float widenedMaxX = widened.Points.Max(point => point.x);
            AssertEqual(true, widenedMaxX - widenedMinX > 14.5f,
                "widened sphere cloud fills the formerly empty horizontal wing");
            AssertEqual(true, widenedMaxX - widenedMinX <= 15.3f,
                "widened sphere cloud stays inside the available bay");
        }

        private static IReadOnlyDictionary<string, MerWorldTransform> BuildRackMarkers(Vector3 origin, Quaternion rotation)
        {
            MerWorldTransform root = new MerWorldTransform(origin, rotation, new Vector3(1f, 1f, 1f));
            return new Dictionary<string, MerWorldTransform>(StringComparer.Ordinal)
            {
                ["marker_shelf_0"] = MerWorldTransformComposer.Compose(root, new Vector3(-0.8f, 1.38f, 0.05f), new Quaternion(0f, 0f, 0f, 1f), new Vector3(0.01f, 0.01f, 0.01f)),
                ["marker_shelf_1"] = MerWorldTransformComposer.Compose(root, new Vector3(-0.8f, 2.10f, 0.05f), new Quaternion(0f, 0f, 0f, 1f), new Vector3(0.01f, 0.01f, 0.01f)),
                ["marker_shelf_2"] = MerWorldTransformComposer.Compose(root, new Vector3(0.8f, 1.38f, 0.05f), new Quaternion(0f, 0f, 0f, 1f), new Vector3(0.01f, 0.01f, 0.01f)),
                ["marker_shelf_3"] = MerWorldTransformComposer.Compose(root, new Vector3(0.8f, 2.10f, 0.05f), new Quaternion(0f, 0f, 0f, 1f), new Vector3(0.01f, 0.01f, 0.01f)),
            };
        }

        // ---- Aim Range HSM UI (Task #9) ------------------------------------------------------------------

        private static void CollapsedStatusStripIsOneLineOneLanguageAndSafe()
        {
            // EN: single line, English words, pick + count + countdown, no CJK.
            string en = WarmupText.BuildCollapsedStatusStrip(12, RoleTypeId.Scp096, 3, false, false);
            AssertEqual(false, en.Contains("\n"), "collapsed strip is one line (en)");
            AssertContains(en, "PICK", "collapsed pick label en");
            AssertContains(en, "SCP-096", "collapsed selected name");
            AssertContains(en, "·3", "collapsed pick count");
            AssertContains(en, "<size=88%>", "collapsed strip wraps in one outer size span (fits the narrow lane)");
            AssertContains(en, ">12<", "collapsed countdown seconds");
            AssertContains(en, ">s<", "collapsed countdown unit en");
            AssertOmits(en, "已选", "collapsed strip renders exactly one language (no CN in en)");
            AssertNoCjk(en, "collapsed strip en has no CJK");
            AssertMarkupSafe(en, "collapsed strip en markup");
            AssertNoNestedSize(en, "collapsed strip has no nested size (en)");

            // CN: single line, Chinese words, no English label words.
            string cn = WarmupText.BuildCollapsedStatusStrip(12, RoleTypeId.Scp096, 3, true, false);
            AssertEqual(false, cn.Contains("\n"), "collapsed strip is one line (cn)");
            AssertContains(cn, "已选", "collapsed pick label cn");
            AssertContains(cn, "SCP-096", "collapsed selected name cn");
            AssertContains(cn, "·3", "collapsed pick count cn");
            AssertContains(cn, ">12<", "collapsed countdown seconds cn");
            AssertContains(cn, ">秒<", "collapsed countdown unit cn");
            AssertOmits(cn, "PICK", "collapsed strip renders exactly one language (no en in cn)");
            AssertOmits(cn, "Starts", "collapsed strip renders exactly one language (no en countdown in cn)");
            AssertMarkupSafe(cn, "collapsed strip cn markup");
            AssertNoNestedSize(cn, "collapsed strip has no nested size (cn)");

            // Waiting / starting states and the empty-selection form.
            AssertContains(WarmupText.BuildCollapsedStatusStrip(-2, null, 0, true, false), "等待", "collapsed waiting cn");
            AssertContains(WarmupText.BuildCollapsedStatusStrip(0, null, 0, false, false), "Start", "collapsed starting en");
            string none = WarmupText.BuildCollapsedStatusStrip(30, null, 0, false, false);
            AssertContains(none, "none yet", "collapsed empty selection en");
            AssertOmits(none, "·", "collapsed strip omits the count badge with no selection");
        }

        private static void AimRangeHeroRendersStateInOneLanguage()
        {
            // No weapon -> empty state rail with the teal "you" marker + a TAKE A GUN call, over a dim eyebrow.
            AimRangeViewState empty = new AimRangeViewState(false, "", AimTargetKind.None, AimBotPhase.None, 0, 0, 0, 0);
            string emptyEn = AimRangeText.BuildHero(empty, false, false);
            AssertContains(emptyEn, "TAKE A GUN", "no-weapon hero en");
            AssertContains(emptyEn, "No gun", "no-weapon eyebrow en");
            AssertContains(emptyEn, "No target", "no-target eyebrow en");
            AssertContains(emptyEn, "◆", "no-weapon rail shows the you marker");
            AssertContains(emptyEn, "━", "no-weapon rail shows the track");
            AssertNoCjk(emptyEn, "hero en has no CJK");
            AssertMarkupSafe(emptyEn, "empty hero markup en");
            AssertNoNestedSize(emptyEn, "empty hero no nested size en");
            AssertEqual(2, CountOccurrences(emptyEn, "\n"), "hero is a three-line read (eyebrow + rail + stat)");

            // Held gun -> the state rail is the hero read: accuracy fill + marker, then session hits + accuracy.
            AimRangeViewState armed = new AimRangeViewState(true, "fsp9", AimTargetKind.Static, AimBotPhase.Passive, 30, 8, 4, 0);
            string armedEn = AimRangeText.BuildHero(armed, false, false);
            AssertContains(armedEn, "FSP-9", "eyebrow weapon label en");
            AssertContains(armedEn, "Static", "eyebrow target en");
            AssertContains(armedEn, "Bots idle", "eyebrow bot state en");
            AssertContains(armedEn, "◆", "hero rail shows the you marker");
            AssertContains(armedEn, ">12<", "hero session hits (8+4) en");
            AssertContains(armedEn, "HITS", "hero hits label en");
            AssertContains(armedEn, ">40%<", "hero accuracy (12/30) en");
            AssertContains(armedEn, "ACC", "hero accuracy label en");
            AssertOmits(armedEn, "Shots", "compact rail hero drops the verbose stat strip");
            AssertOmits(armedEn, "Taken", "compact rail hero drops the incoming stat");
            AssertMarkupSafe(armedEn, "armed hero markup en");
            AssertNoNestedSize(armedEn, "armed hero no nested size en");
            AssertEqual(2, CountOccurrences(armedEn, "\n"), "hero is a three-line read (eyebrow + rail + stat)");

            // CN renders the same state in Chinese only.
            string armedCn = AimRangeText.BuildHero(armed, true, false);
            AssertContains(armedCn, "命中", "hero hits label cn");
            AssertContains(armedCn, "命中率", "hero accuracy label cn");
            AssertContains(armedCn, "静态靶", "eyebrow target cn");
            AssertContains(armedCn, "机器人待机", "eyebrow bot state cn");
            AssertOmits(armedCn, "HITS", "hero renders exactly one language (no en in cn)");
            AssertOmits(armedCn, "ACC", "hero renders exactly one language (no en acc label in cn)");
            AssertOmits(armedCn, "Static", "eyebrow renders exactly one language (no en in cn)");
            AssertMarkupSafe(armedCn, "armed hero markup cn");
            AssertNoNestedSize(armedCn, "armed hero no nested size cn");

            // Before any shot the rail sits empty and no accuracy is claimed (honest — nothing to divide by).
            AimRangeViewState fresh = new AimRangeViewState(true, "com15", AimTargetKind.Static, AimBotPhase.Passive, 0, 0, 0, 0);
            string freshEn = AimRangeText.BuildHero(fresh, false, false);
            AssertContains(freshEn, "HITS", "fresh hero still shows the hit count");
            AssertOmits(freshEn, "ACC", "fresh hero omits the accuracy readout until a shot is on record");

            // Retaliating bots color the eyebrow bot state urgently.
            AimRangeViewState hot = new AimRangeViewState(true, "ak", AimTargetKind.Moving, AimBotPhase.Retaliating, 40, 2, 6, 3);
            string hotEn = AimRangeText.BuildHero(hot, false, false);
            AssertContains(hotEn, "Moving", "eyebrow moving target en");
            AssertContains(hotEn, "Bots firing", "eyebrow retaliating en");
        }

        private static void AimRangeFooterIsStateSpecificAndActiveVoice()
        {
            AimRangeViewState noGun = new AimRangeViewState(false, "", AimTargetKind.None, AimBotPhase.None, 0, 0, 0, 0);
            AssertContains(AimRangeText.BuildFooter(noGun, false, false), "Grab a gun off the rack", "footer no-gun en");
            AssertContains(AimRangeText.BuildFooter(noGun, true, false), "去武器架取一把枪", "footer no-gun cn");

            AimRangeViewState target = new AimRangeViewState(true, "com15", AimTargetKind.Static, AimBotPhase.Passive, 5, 1, 0, 0);
            AssertContains(AimRangeText.BuildFooter(target, false, false), "Hit the lit target", "footer target en");
            AssertContains(AimRangeText.BuildFooter(target, true, false), "击中亮起的靶子", "footer target cn");

            AimRangeViewState hot = new AimRangeViewState(true, "com15", AimTargetKind.None, AimBotPhase.Retaliating, 5, 1, 1, 2);
            AssertContains(AimRangeText.BuildFooter(hot, false, false), "Break line of sight", "footer retaliating en");
            AssertContains(AimRangeText.BuildFooter(hot, true, false), "切断视线", "footer retaliating cn");

            AimRangeViewState idle = new AimRangeViewState(true, "com15", AimTargetKind.None, AimBotPhase.Passive, 5, 1, 0, 0);
            AssertContains(AimRangeText.BuildFooter(idle, false, false), "Shoot a bot", "footer provoke en");
            AssertContains(AimRangeText.BuildFooter(idle, true, false), "激怒它", "footer provoke cn");

            AssertNoNestedSize(AimRangeText.BuildFooter(target, false, false), "footer no nested size");
        }

        private static void AimRangeFlashRendersEveryEventInOneLanguage()
        {
            AssertEqual(string.Empty, AimRangeText.BuildFlash(AimFlashKind.None, false, false), "None flash is empty");

            (AimFlashKind kind, string en, string cn)[] cases =
            {
                (AimFlashKind.TargetHit, "HIT!", "命中"),
                (AimFlashKind.BotProvoked, "BOT ENGAGED", "激怒"),
                (AimFlashKind.IncomingHit, "INCOMING!", "你被击中"),
                (AimFlashKind.BotDown, "BOT DOWN", "机器人倒下"),
                (AimFlashKind.BotRespawn, "BOT BACK", "机器人重生"),
                (AimFlashKind.Reset, "RANGE RESET", "靶场重置"),
                (AimFlashKind.Handoff, "ROUND START", "回合开始"),
            };

            foreach ((AimFlashKind kind, string en, string cn) in cases)
            {
                string flashEn = AimRangeText.BuildFlash(kind, false, false);
                AssertContains(flashEn, en, kind + " flash en");
                AssertNoCjk(flashEn, kind + " flash en has no CJK");
                AssertEqual(false, flashEn.Contains("\n"), kind + " flash en is one line");
                AssertMarkupSafe(flashEn, kind + " flash markup en");
                AssertNoNestedSize(flashEn, kind + " flash no nested size en");

                string flashCn = AimRangeText.BuildFlash(kind, true, false);
                AssertContains(flashCn, cn, kind + " flash cn");
                AssertOmits(flashCn, en, kind + " flash renders one language (no en in cn)");
                AssertMarkupSafe(flashCn, kind + " flash markup cn");
            }
        }

        private static void AimRangeTextMarkupIsBalancedAndUnnested()
        {
            // Weapon-label prettify is markup-safe even for a hostile config id (angle brackets stripped).
            AssertEqual("COM-15", AimRangeText.WeaponLabel("com15"), "known preset prettified");
            AssertEqual("FSP-9", AimRangeText.WeaponLabel("bot-fsp9"), "bot- prefix stripped");
            string hostile = AimRangeText.WeaponLabel("<script>alert(1)</script>");
            AssertOmits(hostile, "<", "hostile preset id strips open brackets");
            AssertOmits(hostile, ">", "hostile preset id strips close brackets");

            AimRangeViewState injected = new AimRangeViewState(true, "<b>x</b>", AimTargetKind.Moving, AimBotPhase.Respawning, 3, 1, 1, 1);
            string hero = AimRangeText.BuildHero(injected, false, false);
            AssertOmits(hero, "<script", "no script in hero output");
            AssertMarkupSafe(hero, "injected hero markup balanced");
            AssertNoNestedSize(hero, "injected hero no nested size");
            AssertContains(hero, "Bot respawning", "respawning eyebrow en");

            // ASCII glyph fallback path stays balanced/safe (no signature glyphs used, so it is a no-op here).
            AssertMarkupSafe(AimRangeText.BuildHero(injected, true, true), "ascii-fallback hero markup balanced");
        }

        private static void HintChangeCacheSkipsUnchangedButResendsOnChange()
        {
            HintChangeCache cache = new HintChangeCache();
            string key = "u1|warmupscp.aim.hero";
            string sig = HintChangeCache.Signature(0f, 700f, "HELLO");

            AssertEqual(false, cache.Matches(key, sig), "empty cache never matches (first push sent)");
            cache.Set(key, sig);
            AssertEqual(true, cache.Matches(key, sig), "identical resubmission is skipped");

            AssertEqual(false, cache.Matches(key, HintChangeCache.Signature(0f, 700f, "WORLD")), "changed text re-sends");
            AssertEqual(false, cache.Matches(key, HintChangeCache.Signature(0f, 701f, "HELLO")), "moved Y re-sends");
            AssertEqual(false, cache.Matches(key, HintChangeCache.Signature(-1077f, 700f, "HELLO")), "moved X re-sends (status collapsing into the Aim lane)");
            AssertEqual(false,
                HintChangeCache.Signature(0f, 700f, "HELLO") == HintChangeCache.Signature(0f, 701f, "HELLO"),
                "Y is part of the signature");
            AssertEqual(false,
                HintChangeCache.Signature(0f, 700f, "HELLO") == HintChangeCache.Signature(-1077f, 700f, "HELLO"),
                "X is part of the signature");
            AssertEqual(false,
                HintChangeCache.Signature(0f, 700f, "HELLO") == HintChangeCache.Signature(0f, 700f, "WORLD"),
                "text is part of the signature");

            cache.Remove(key);
            AssertEqual(false, cache.Matches(key, sig), "removed key re-sends (Remove clears the entry)");

            cache.Set(key, sig);
            cache.Clear();
            AssertEqual(false, cache.Matches(key, sig), "cleared cache re-sends (Disable clears all)");
            AssertEqual(0, cache.Count, "cleared cache is empty");
        }

        private static void ParkourDefaultsToExplicitOptIn()
        {
            ParkourActivityConfig config = new ActivityConfig().Parkour;
            AssertEqual(false, config.Enabled, "parkour default off");
            AssertEqual(20f, config.SchedulerRateHz, "parkour shared tick default");
            AssertEqual(0.6f, config.StartHoldSeconds, "parkour start hold");
            AssertEqual(3f, config.CountdownSeconds, "parkour countdown");
        }

        private static void ParkourRunRequiresOrderedGatesAndFreezesFinishTime()
        {
            ParkourRunState run = new ParkourRunState("runner", 41);
            run.BeginArming(10d);
            AssertEqual(false, run.TryBeginCountdown(10.5d, 0.6d, 3d), "start hold not complete");
            AssertEqual(true, run.TryBeginCountdown(10.61d, 0.6d, 3d), "start hold arms countdown");
            AssertEqual(false, run.TryStartRun(13.60d), "countdown remains authoritative");
            AssertEqual(true, run.TryStartRun(13.61d), "run starts on GO timestamp");
            AssertEqual(false, run.TryAdvanceGate(1), "future gate rejected");
            AssertEqual(true, run.TryAdvanceGate(0), "next ordered gate accepted");
            AssertEqual(false, run.TryFinish(20d, 2), "finish rejected before all gates");
            AssertEqual(true, run.TryAdvanceGate(1), "second ordered gate accepted");
            AssertEqual(true, run.TryFinish(20d, 2), "finish accepted after every gate");
            AssertEqual(6.39d, Math.Round(run.Elapsed(99d), 3), "finish freezes exact elapsed time");
            AssertEqual(true, run.RequireStartExit, "finish cannot auto-rearm under runner");
        }

        private static void ParkourRouteIsClearableButNotTrivial()
        {
            WarmupHallLayout hall = new WarmupHallLayout(Vector3.zero);
            // The live Tutorial role's measured constants (see the pulse-line playtest transcript), not
            // the fallbacks: this is the course players actually get.
            ParkourJumpModel model = new ParkourJumpModel(4.9f, 3.9f, 5.4f);
            ParkourLayout layout = new ParkourLayout(hall, model);

            AssertEqual(true, layout.Platforms.Count >= 10, $"route has a real number of landings (got {layout.Platforms.Count})");
            AssertEqual(layout.Platforms.Count + 1, layout.Hops.Count, "every landing plus the finish is a measured hop");
            AssertEqual(true, ContainsPoint(layout.ShaftBounds, layout.StartPlate.Center), "start inside the shaft");
            AssertEqual(true, ContainsPoint(layout.ShaftBounds, layout.FinishPlate.Center), "finish inside the shaft");

            // Possible: no hop may exceed the reachable distance at the reference speed, with margin.
            foreach (ParkourHop hop in layout.Hops)
            {
                AssertEqual(true, hop.Difficulty <= ParkourLayout.MaximumDifficulty,
                    $"hop {hop.Index} is clearable (difficulty {hop.Difficulty:0.###})");
                AssertEqual(true, hop.Rise < model.MaxRise,
                    $"hop {hop.Index} rise stays under the jump apex");
            }

            // Not trivial: the closing hops must genuinely demand a sprint, not a walk.
            AssertEqual(true, layout.PeakDifficulty >= ParkourLayout.MinimumPeakDifficulty,
                $"route peaks at a committed difficulty (got {layout.PeakDifficulty:0.###})");
            // The closing stretch must be out of a walker's reach by a real margin, not by 2%: an earlier
            // calibration missed that and a walking dummy completed the whole course in game.
            int walkOnly = layout.Hops.Count(hop => hop.Gap <= model.MaxGap(hop.Rise, model.WalkSpeed));
            AssertEqual(true, walkOnly <= layout.Hops.Count * 3 / 4,
                $"the closing quarter of the route must demand a sprint (got {walkOnly}/{layout.Hops.Count} walkable)");
            AssertEqual(true, walkOnly >= layout.Hops.Count / 2,
                "the opening half of the route is still clearable at walking pace");
            ParkourHop hardest = layout.Hops.OrderByDescending(hop => hop.Difficulty).First();
            AssertEqual(true, hardest.Gap > model.MaxGap(hardest.Rise, model.WalkSpeed) * 1.1f,
                $"the hardest hop clears a walker's reach by a real margin ({hardest.Gap:0.##}m vs {model.MaxGap(hardest.Rise, model.WalkSpeed):0.##}m)");

            foreach (ParkourPlatform platform in layout.Platforms)
            {
                AssertEqual(true, ContainsPoint(layout.ShaftBounds, platform.Center), "landing inside the shaft");
                AssertEqual(true, platform.SurfaceY + 1.9f + model.MaxRise < WarmupHallLayout.ShaftCeilingHeight,
                    "landing keeps jump headroom under the shaft overhead");
                AssertEqual(true,
                    Math.Abs(platform.Center.x) + platform.Size.x / 2f < WarmupHallLayout.ShaftHalfWidth,
                    "landing stays clear of the shaft walls");
            }

            // A miss is caught while still falling, well before the deck and well before fall damage.
            ParkourPlatform third = layout.Platforms[2];
            AssertEqual(false, layout.HasFallenOffRoute(third.RecoveryPosition, 2), "standing on the route is not a fall");
            AssertEqual(true, layout.HasFallenOffRoute(third.RecoveryPosition - new Vector3(0f, 2.5f, 0f), 2),
                "dropping clear of the last landing is caught early");
        }

        private static void ParkourRouteAdaptsToMovementConstants()
        {
            WarmupHallLayout hall = new WarmupHallLayout(Vector3.zero);

            // The route is generated from the jump model rather than hand-placed, so a balance patch that
            // changes movement cannot silently turn it into a walk-over or an impossibility.
            // The low end is the one that matters: SCP:SL's Tutorial role really jumps at 4.9 with a
            // 0.61 m apex, far below what a hand-authored course would assume.
            foreach (float[] constants in new[]
            {
                new[] { 4.0f, 3.2f, 4.4f },
                new[] { 4.5f, 3.6f, 5.0f },
                new[] { 4.9f, 3.9f, 5.4f },
                new[] { 5.5f, 4.2f, 6.0f },
                new[] { 6.0f, 4.0f, 5.5f },
                new[] { 7.0f, 4.5f, 6.5f },
                new[] { 8.0f, 5.5f, 8.0f },
            })
            {
                ParkourJumpModel model = new ParkourJumpModel(constants[0], constants[1], constants[2]);
                ParkourLayout layout = new ParkourLayout(hall, model);
                AssertEqual(true, layout.PeakDifficulty <= ParkourLayout.MaximumDifficulty,
                    $"jump {constants[0]} route stays clearable (peak {layout.PeakDifficulty:0.###})");
                AssertEqual(true, layout.PeakDifficulty >= ParkourLayout.MinimumPeakDifficulty,
                    $"jump {constants[0]} route stays non-trivial (peak {layout.PeakDifficulty:0.###})");
                AssertEqual(true, layout.Platforms.Count >= 8, $"jump {constants[0]} route keeps enough landings");
                foreach (ParkourHop hop in layout.Hops)
                {
                    AssertEqual(true, hop.Rise <= model.MaxRise * 0.56f,
                        $"jump {constants[0]} hop {hop.Index} respects the step-rise cap ({hop.Rise:0.###}m)");
                }
            }
        }

        private static void ParkourJumpModelMatchesNativeProjectileMotion()
        {
            ParkourJumpModel model = new ParkourJumpModel(7f, 4.5f, 6.5f);

            // y(t) = J*t - g*t^2/2, caught on the descending branch. Apex is J^2 / 2g.
            AssertEqual(1.25d, Math.Round(model.MaxRise, 3), "jump apex from native gravity and jump speed");
            AssertEqual(0.714d, Math.Round(model.AirTime(0f), 3), "flat air time is the full arc");
            AssertEqual(0f, model.AirTime(model.MaxRise + 0.05f), "a landing above the apex is unreachable");

            // Landing higher costs reach; landing lower buys it.
            AssertEqual(true, model.MaxGap(0.6f) < model.MaxGap(0f), "climbing shortens the reachable gap");
            AssertEqual(true, model.MaxGap(-1f) > model.MaxGap(0f), "dropping lengthens the reachable gap");
            AssertEqual(true, model.MaxGap(0f, model.SprintSpeed) > model.MaxGap(0f, model.WalkSpeed),
                "sprinting reaches further than walking");
            AssertEqual(1d, Math.Round(model.Difficulty(model.MaxGap(0.4f), 0.4f), 3),
                "a hop at exactly the reachable distance is difficulty 1");
        }

        private static void ParkourSweptGateDetectionCatchesFastCrossings()
        {
            Bounds gate = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0.2f));
            AssertEqual(true,
                ParkourLayout.SegmentIntersectsBounds(new Vector3(0f, 1f, -2f), new Vector3(0f, 1f, 2f), gate),
                "fast segment crossing thin gate is credited");
            AssertEqual(false,
                ParkourLayout.SegmentIntersectsBounds(new Vector3(2f, 1f, -2f), new Vector3(2f, 1f, 2f), gate),
                "parallel miss is rejected");
        }

        private static void ParkourTextIsBilingualAndMarkupSafe()
        {
            ParkourViewState active = new ParkourViewState(ParkourPhase.Active, 2, 5, 17, 12.64d, 0d, 31.904d);
            string english = ParkourText.BuildHero(active, false);
            string chinese = ParkourText.BuildHero(active, true);
            AssertContains(english, "SECTOR 2 / 4", "parkour English sector");
            AssertContains(english, ">12.6<", "parkour tenths timer");
            AssertNoCjk(english, "parkour English has one language");
            AssertContains(chinese, "第 2 / 4 段", "parkour Chinese sector");
            AssertEqual(true, ContainsCjk(chinese), "parkour Chinese has CJK");
            foreach (string text in new[]
            {
                english,
                chinese,
                ParkourText.BuildFooter(ParkourPhase.Active, false),
                ParkourText.BuildFooter(ParkourPhase.Finished, true),
                ParkourText.BuildFlash("best", false),
                ParkourText.BuildFlash("recover", true),
            })
            {
                AssertMarkupSafe(text, "parkour markup");
                AssertNoNestedSize(text, "parkour no nested size");
            }
        }

        private static void ScpReplacementDefaultsAllowLivingAndSpectatorVolunteers()
        {
            ScpReplacementConfig config = new ScpReplacementConfig();
            AssertEqual(true, config.IsEnabled, "replacement enabled by default");
            AssertEqual(true, config.AllowAliveVolunteers, "living volunteers enabled by default");
            AssertEqual(true,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.Spectator, false, config.AllowAliveVolunteers),
                "spectator may volunteer");
            AssertEqual(true,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.ClassD, true, config.AllowAliveVolunteers),
                "living ClassD may volunteer");
            AssertEqual(true,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.NtfPrivate, true, config.AllowAliveVolunteers),
                "living MTF may volunteer");
            AssertEqual(false,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.Scp939, true, config.AllowAliveVolunteers),
                "SCP may not volunteer");
            AssertEqual(false,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.ClassD, false, config.AllowAliveVolunteers),
                "dead non-spectator state may not volunteer");
            AssertEqual(false,
                ScpReplacementPolicy.CanVolunteer(RoleTypeId.ClassD, true, false),
                "spectator-only config rejects living human");
        }

        private static void ScpReplacementDeparturePolicyHonoursCutoffHealthAndIgnoredRoles()
        {
            List<RoleTypeId> ignored = new List<RoleTypeId> { RoleTypeId.Scp079 };
            AssertEqual(true,
                ScpReplacementPolicy.CanOpenDeparture(RoleTypeId.Scp096, 950f, 1000f, 60d, 60f, 95f, ignored),
                "departure at exact cutoff and health threshold");
            AssertEqual(false,
                ScpReplacementPolicy.CanOpenDeparture(RoleTypeId.Scp096, 949f, 1000f, 60d, 60f, 95f, ignored),
                "low-health departure rejected");
            AssertEqual(false,
                ScpReplacementPolicy.CanOpenDeparture(RoleTypeId.Scp096, 1000f, 1000f, 60.01d, 60f, 95f, ignored),
                "late departure rejected");
            AssertEqual(false,
                ScpReplacementPolicy.CanOpenDeparture(RoleTypeId.Scp079, 100f, 100f, 1d, 60f, 95f, ignored),
                "configured ignored role rejected");
            AssertEqual(false,
                ScpReplacementPolicy.CanOpenDeparture(RoleTypeId.Scp0492, 100f, 100f, 1d, 60f, 95f, ignored),
                "zombie is never a main SCP slot");
        }

        private static void ScpReplacementStateRejectsDuplicateStableUserIds()
        {
            ScpReplacementState state = new ScpReplacementState();
            state.BeginRound();
            AssertEqual(true, state.TryOpen(RoleTypeId.Scp079, 0, out PendingScpReplacement? entry), "open SCP-079");
            AssertEqual(true,
                state.TryVolunteer(RoleTypeId.Scp079, "user@steam", out _, out bool start, out int token),
                "first stable UserId enters");
            AssertEqual(true, start, "first volunteer starts lottery");
            AssertEqual(1, token, "first lottery token");
            AssertEqual(false,
                state.TryVolunteer(RoleTypeId.Scp079, "user@steam", out _, out _, out _),
                "duplicate stable UserId rejected");
            AssertEqual(1, entry!.Volunteers.Count, "duplicate did not grow pool");
        }

        private static void ScpReplacementDisconnectRemovalDropsVolunteerFromEverySlot()
        {
            ScpReplacementState state = new ScpReplacementState();
            state.BeginRound();
            state.TryOpen(RoleTypeId.Scp079, 0, out _);
            state.TryOpen(RoleTypeId.Scp096, 0, out _);
            state.TryVolunteer(RoleTypeId.Scp079, "leaver", out _, out _, out _);
            state.TryVolunteer(RoleTypeId.Scp096, "leaver", out _, out _, out _);
            state.RemoveVolunteer("leaver");
            state.TryFind(RoleTypeId.Scp079, out PendingScpReplacement? first);
            state.TryFind(RoleTypeId.Scp096, out PendingScpReplacement? second);
            AssertEqual(0, first!.Volunteers.Count, "leaver removed from first lottery");
            AssertEqual(0, second!.Volunteers.Count, "leaver removed from second lottery");
        }

        private static void ScpReplacementRoundGenerationRejectsStaleLottery()
        {
            ScpReplacementState state = new ScpReplacementState();
            state.BeginRound();
            state.TryOpen(RoleTypeId.Scp173, 0, out PendingScpReplacement? entry);
            state.TryVolunteer(RoleTypeId.Scp173, "candidate", out _, out _, out int token);
            int staleGeneration = entry!.RoundGeneration;
            state.EndRound();
            state.BeginRound();
            AssertEqual(false,
                state.TryTakeLottery(RoleTypeId.Scp173, staleGeneration, token, out _),
                "stale callback cannot touch new round");
            AssertEqual(0, state.PendingCount, "stale callback leaves new round clean");
        }

        private static void ScpReplacementCapacityCountsPendingReservations()
        {
            ScpReplacementState state = new ScpReplacementState();
            state.BeginRound();
            AssertEqual(true, state.TryOpen(RoleTypeId.Scp079, 1, out PendingScpReplacement? entry), "first slot reserves cap");
            AssertEqual(false, state.TryOpen(RoleTypeId.Scp096, 1, out _), "second pending slot cannot exceed cap");
            state.TryVolunteer(RoleTypeId.Scp079, "candidate", out _, out _, out int token);
            AssertEqual(true,
                state.TryTakeLottery(RoleTypeId.Scp079, entry!.RoundGeneration, token, out _),
                "lottery consumes pending reservation");
            state.MarkReplacementSucceeded();
            AssertEqual(false, state.TryOpen(RoleTypeId.Scp096, 1, out _), "successful replacement keeps cap occupied");
        }

        private static void ScpReplacementParserAcceptsFriendlyScpNumbers()
        {
            AssertEqual(true, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "079"), "canonical number");
            AssertEqual(true, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "79"), "number without leading zero");
            AssertEqual(true, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "SCP-079"), "prefixed number");
            AssertEqual(true, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp3114, "scp3114"), "four-digit number");
            AssertEqual(false, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "096"), "different role rejected");
            AssertEqual(false, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "SCP"), "missing digits rejected");
            AssertEqual(false, ScpReplacementPolicy.MatchesScpArgument(RoleTypeId.Scp079, "not079"), "unrecognized prefix rejected");
        }

        private static void ScpReplacementWeightedHumanRolesRejectInvalidEntries()
        {
            Dictionary<RoleTypeId, int> weights = new Dictionary<RoleTypeId, int>
            {
                [RoleTypeId.Scp096] = 1000,
                [RoleTypeId.Spectator] = 1000,
                [RoleTypeId.ClassD] = 1,
                [RoleTypeId.Scientist] = 1,
            };
            AssertEqual(RoleTypeId.ClassD, ScpReplacementPolicy.PickWeightedHumanRole(weights, 0), "first valid weighted role");
            AssertEqual(RoleTypeId.Scientist, ScpReplacementPolicy.PickWeightedHumanRole(weights, 1), "second valid weighted role");
            AssertEqual(RoleTypeId.ClassD,
                ScpReplacementPolicy.PickWeightedHumanRole(new Dictionary<RoleTypeId, int> { [RoleTypeId.Scp939] = 5 }, 0),
                "invalid-only weights fail closed to ClassD");
        }

        private static void ScpReplacementCooldownIsSharedAndResettable()
        {
            ScpReplacementState state = new ScpReplacementState();
            state.BeginRound();
            AssertEqual(true, state.TryConsumeCooldown("user", 10d, 3d, out _), "first command accepted");
            AssertEqual(false, state.TryConsumeCooldown("user", 11d, 3d, out double remaining), "second command throttled");
            AssertEqual(2d, remaining, "cooldown remaining");
            state.RemoveVolunteer("user");
            AssertEqual(true, state.TryConsumeCooldown("user", 11d, 3d, out _), "disconnect cleanup removes cooldown");
            state.EndRound();
            state.BeginRound();
            AssertEqual(true, state.TryConsumeCooldown("user", 11d, 3d, out _), "round reset clears cooldown");
        }

        private static void ScpReplacementTextIsBilingualAndMarkupSafe()
        {
            string english = ScpReplacementText.Departure(RoleTypeId.Scp079, false);
            string chinese = ScpReplacementText.Departure(RoleTypeId.Scp079, true);
            AssertContains(english, ".volunteer 079", "English departure command");
            AssertNoCjk(english, "English replacement text has one language");
            AssertContains(chinese, ".volunteer 079", "Chinese departure command");
            AssertEqual(true, ContainsCjk(chinese), "Chinese replacement text has CJK");
            foreach (string text in new[]
            {
                english,
                chinese,
                ScpReplacementText.Winner(RoleTypeId.Scp096, true, false),
                ScpReplacementText.Winner(RoleTypeId.Scp096, false, true),
                ScpReplacementText.NoWinner(RoleTypeId.Scp173, true, false),
                ScpReplacementText.HumanHint(true),
            })
            {
                AssertMarkupSafe(text, "replacement markup");
            }
        }

        private static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char ch in text)
            {
                if (ch >= 0x4E00 && ch <= 0x9FFF)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPoint(Bounds bounds, Vector3 point)
        {
            Vector3 center = bounds.center;
            Vector3 size = bounds.size;
            return Math.Abs(point.x - center.x) <= size.x / 2f &&
                Math.Abs(point.y - center.y) <= size.y / 2f &&
                Math.Abs(point.z - center.z) <= size.z / 2f;
        }

        private static void AssertNoCjk(string text, string label)
        {
            if (ContainsCjk(text))
            {
                throw new InvalidOperationException($"{label}: expected no CJK, got \"{text}\"");
            }
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        // Balanced tag pairs, no stray angle brackets, and no injected script. TMP rich text the game (and the
        // .tests/UI harness parser) accepts requires open/close counts to match for the tags we emit.
        private static void AssertMarkupSafe(string text, string label)
        {
            if (text.IndexOf("<script", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException($"{label}: contains <script.");
            }

            AssertEqual(CountOccurrences(text, "<color="), CountOccurrences(text, "</color>"), label + " color tags balanced");
            AssertEqual(CountOccurrences(text, "<size="), CountOccurrences(text, "</size>"), label + " size tags balanced");
            AssertEqual(CountOccurrences(text, "<b>"), CountOccurrences(text, "</b>"), label + " bold tags balanced");
            AssertEqual(CountOccurrences(text, "<i>"), CountOccurrences(text, "</i>"), label + " italic tags balanced");
            AssertEqual(CountOccurrences(text, "<align"), CountOccurrences(text, "</align>"), label + " align tags balanced");

            if (CountOccurrences(text, "<") != CountOccurrences(text, ">"))
            {
                throw new InvalidOperationException($"{label}: unbalanced angle brackets in \"{text}\"");
            }
        }

        // No <size> span is ever opened while another is still open (a TMP nested-scale trap the UI must avoid).
        private static void AssertNoNestedSize(string text, string label)
        {
            int depth = 0;
            int index = 0;
            while (index < text.Length)
            {
                if (text.IndexOf("<size=", index, StringComparison.Ordinal) == index)
                {
                    depth++;
                    if (depth > 1)
                    {
                        throw new InvalidOperationException($"{label}: nested <size> span in \"{text}\"");
                    }

                    index += "<size=".Length;
                }
                else if (text.IndexOf("</size>", index, StringComparison.Ordinal) == index)
                {
                    depth--;
                    index += "</size>".Length;
                }
                else
                {
                    index++;
                }
            }
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

        private static void AssertVectorNear(Vector3 expected, Vector3 actual, float tolerance, string label)
        {
            if (MerWorldTransformComposer.Distance(expected, actual) > tolerance)
            {
                throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
            }
        }

        private static void AssertContains(string actual, string expectedSubstring, string label)
        {
            if (actual == null || actual.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException($"{label}: expected to contain \"{expectedSubstring}\", got \"{actual}\"");
            }
        }

        private static void AssertOmits(string actual, string unexpectedSubstring, string label)
        {
            if (actual != null && actual.IndexOf(unexpectedSubstring, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException($"{label}: expected to omit \"{unexpectedSubstring}\", got \"{actual}\"");
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
