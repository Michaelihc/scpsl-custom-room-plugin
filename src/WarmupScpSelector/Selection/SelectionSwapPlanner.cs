using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace WarmupScpSelector.Selection
{
    public sealed class SelectionSwapPlan<TPlayer>
        where TPlayer : notnull
    {
        public SelectionSwapPlan(
            Dictionary<TPlayer, RoleTypeId> finalRoles,
            List<RoleTypeId> skippedUnspawnedRoles,
            List<SelectionNatural<TPlayer>> naturalSelections,
            List<SelectionSwap<TPlayer>> swaps,
            List<SelectionUnresolved<TPlayer>> unresolvedSelections)
        {
            FinalRoles = finalRoles;
            SkippedUnspawnedRoles = skippedUnspawnedRoles;
            NaturalSelections = naturalSelections;
            Swaps = swaps;
            UnresolvedSelections = unresolvedSelections;
        }

        public Dictionary<TPlayer, RoleTypeId> FinalRoles { get; }

        public List<RoleTypeId> SkippedUnspawnedRoles { get; }

        public List<SelectionNatural<TPlayer>> NaturalSelections { get; }

        public List<SelectionSwap<TPlayer>> Swaps { get; }

        public List<SelectionUnresolved<TPlayer>> UnresolvedSelections { get; }
    }

    public sealed class SelectionNatural<TPlayer>
        where TPlayer : notnull
    {
        public SelectionNatural(TPlayer player, RoleTypeId targetRole)
        {
            Player = player;
            TargetRole = targetRole;
        }

        public TPlayer Player { get; }

        public RoleTypeId TargetRole { get; }
    }

    public sealed class SelectionSwap<TPlayer>
        where TPlayer : notnull
    {
        public SelectionSwap(TPlayer selectedPlayer, TPlayer holder, RoleTypeId targetRole, RoleTypeId replacementRole)
        {
            SelectedPlayer = selectedPlayer;
            Holder = holder;
            TargetRole = targetRole;
            ReplacementRole = replacementRole;
        }

        public TPlayer SelectedPlayer { get; }

        public TPlayer Holder { get; }

        public RoleTypeId TargetRole { get; }

        public RoleTypeId ReplacementRole { get; }
    }

    public sealed class SelectionUnresolved<TPlayer>
        where TPlayer : notnull
    {
        public SelectionUnresolved(TPlayer selectedPlayer, RoleTypeId targetRole)
        {
            SelectedPlayer = selectedPlayer;
            TargetRole = targetRole;
        }

        public TPlayer SelectedPlayer { get; }

        public RoleTypeId TargetRole { get; }
    }

    public static class SelectionSwapPlanner
    {
        public static SelectionSwapPlan<TPlayer> BuildPlan<TPlayer>(
            IEnumerable<RoleTypeId> roleOrder,
            IReadOnlyDictionary<TPlayer, RoleTypeId> originalRoles,
            IReadOnlyDictionary<RoleTypeId, IReadOnlyList<TPlayer>> selectedPools,
            Func<IReadOnlyList<TPlayer>, TPlayer> choosePlayer)
            where TPlayer : notnull => BuildPlan(roleOrder, originalRoles, selectedPools, choosePlayer, null);

        public static SelectionSwapPlan<TPlayer> BuildPlan<TPlayer>(
            IEnumerable<RoleTypeId> roleOrder,
            IReadOnlyDictionary<TPlayer, RoleTypeId> originalRoles,
            IReadOnlyDictionary<RoleTypeId, IReadOnlyList<TPlayer>> selectedPools,
            Func<IReadOnlyList<TPlayer>, TPlayer> choosePlayer,
            IEnumerable<TPlayer>? reservedPlayers)
            where TPlayer : notnull
        {
            Dictionary<TPlayer, RoleTypeId> finalRoles = originalRoles.ToDictionary(pair => pair.Key, pair => pair.Value);
            List<RoleTypeId> skippedUnspawnedRoles = new List<RoleTypeId>();
            List<SelectionNatural<TPlayer>> naturalSelections = new List<SelectionNatural<TPlayer>>();
            List<SelectionSwap<TPlayer>> swaps = new List<SelectionSwap<TPlayer>>();
            List<SelectionUnresolved<TPlayer>> unresolvedSelections = new List<SelectionUnresolved<TPlayer>>();
            HashSet<TPlayer> alreadyChosen = reservedPlayers == null
                ? new HashSet<TPlayer>() : new HashSet<TPlayer>(reservedPlayers);

            foreach (RoleTypeId targetRole in roleOrder)
            {
                if (!selectedPools.TryGetValue(targetRole, out IReadOnlyList<TPlayer> pool) || pool.Count == 0)
                {
                    continue;
                }

                List<TPlayer> currentHolders = finalRoles
                    .Where(pair => pair.Value == targetRole)
                    .Select(pair => pair.Key)
                    .ToList();

                if (currentHolders.Count == 0)
                {
                    skippedUnspawnedRoles.Add(targetRole);
                    continue;
                }

                HashSet<TPlayer> holderSet = new HashSet<TPlayer>(currentHolders);

                // Phase 1: pickers who already hold this role keep it (natural). Reserve them FIRST so a
                // different holder's slot can never consume one of them and then displace them later (which
                // would drop a picker off an SCP they both had and picked when vanilla spawned duplicates).
                foreach (TPlayer holder in currentHolders)
                {
                    if (pool.Contains(holder) && alreadyChosen.Add(holder))
                    {
                        naturalSelections.Add(new SelectionNatural<TPlayer>(holder, targetRole));
                    }
                }

                // Phase 2: fill the remaining (non-natural) holder slots from pickers who are NOT current
                // holders of this role. The holder's replacement is the selected player's CURRENT planned
                // role (finalRoles, not originalRoles): a chained earlier pick may already have moved it off
                // their original vanilla role, and reusing the original would duplicate that SCP.
                foreach (TPlayer holder in currentHolders)
                {
                    if (alreadyChosen.Contains(holder))
                    {
                        continue; // kept their own role naturally in phase 1
                    }

                    List<TPlayer> candidates = pool
                        .Where(player => !alreadyChosen.Contains(player) && !holderSet.Contains(player))
                        .ToList();
                    if (candidates.Count == 0)
                    {
                        break;
                    }

                    TPlayer selectedPlayer = choosePlayer(candidates);
                    alreadyChosen.Add(selectedPlayer);

                    if (!finalRoles.TryGetValue(selectedPlayer, out RoleTypeId replacementRole))
                    {
                        unresolvedSelections.Add(new SelectionUnresolved<TPlayer>(selectedPlayer, targetRole));
                        continue;
                    }

                    finalRoles[holder] = replacementRole;
                    finalRoles[selectedPlayer] = targetRole;
                    swaps.Add(new SelectionSwap<TPlayer>(selectedPlayer, holder, targetRole, replacementRole));
                }
            }

            return new SelectionSwapPlan<TPlayer>(finalRoles, skippedUnspawnedRoles, naturalSelections, swaps, unresolvedSelections);
        }
    }
}
