using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace WarmupScpSelector.Selection;

internal static class ForcedScpSelectionPlanner
{
    // Select winners against vanilla's capacity before swapping anyone. A winner can initially hold
    // another winner's requested role; the swaps must preserve that slot until its recipient is processed.
    internal static SelectionSwapPlan<T> Build<T>(
        IReadOnlyDictionary<T, RoleTypeId> originalRoles,
        IEnumerable<KeyValuePair<T, RoleTypeId>> forcedRoles) where T : notnull
    {
        Dictionary<T, RoleTypeId> roles = originalRoles.ToDictionary(pair => pair.Key, pair => pair.Value);
        Dictionary<RoleTypeId, int> remaining = originalRoles.Values.GroupBy(role => role)
            .ToDictionary(group => group.Key, group => group.Count());
        List<KeyValuePair<T, RoleTypeId>> winners = new();
        List<SelectionUnresolved<T>> unresolved = new();
        List<RoleTypeId> skipped = new();
        foreach (KeyValuePair<T, RoleTypeId> forced in forcedRoles)
        {
            if (!roles.ContainsKey(forced.Key))
                continue;
            if (!remaining.TryGetValue(forced.Value, out int capacity) || capacity == 0)
            {
                unresolved.Add(new SelectionUnresolved<T>(forced.Key, forced.Value));
                if (!originalRoles.Values.Contains(forced.Value) && !skipped.Contains(forced.Value))
                    skipped.Add(forced.Value);
                continue;
            }
            remaining[forced.Value] = capacity - 1;
            winners.Add(forced);
        }

        List<SelectionNatural<T>> natural = winners.Where(pair => roles[pair.Key] == pair.Value)
            .Select(pair => new SelectionNatural<T>(pair.Key, pair.Value)).ToList();
        HashSet<T> assigned = new(natural.Select(selection => selection.Player));
        List<SelectionSwap<T>> swaps = new();
        foreach (KeyValuePair<T, RoleTypeId> winner in winners)
        {
            if (assigned.Contains(winner.Key))
                continue;
            if (roles[winner.Key] == winner.Value)
            {
                assigned.Add(winner.Key);
                natural.Add(new SelectionNatural<T>(winner.Key, winner.Value));
                continue;
            }
            T holder = roles.First(pair => pair.Value == winner.Value && !assigned.Contains(pair.Key)).Key;
            RoleTypeId previous = roles[winner.Key];
            roles[holder] = previous;
            roles[winner.Key] = winner.Value;
            assigned.Add(winner.Key);
            swaps.Add(new SelectionSwap<T>(winner.Key, holder, winner.Value, previous));
        }
        return new SelectionSwapPlan<T>(roles, skipped, natural, swaps, unresolved);
    }
}
