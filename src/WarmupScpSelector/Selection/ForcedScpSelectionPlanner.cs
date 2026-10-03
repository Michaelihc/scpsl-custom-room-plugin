using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace WarmupScpSelector.Selection;

internal static class ForcedScpSelectionPlanner
{
    // Reserve every forced player before looking for donors, including players whose request is processed
    // later. A force command may change the native role multiset; ordinary coin picks cannot.
    internal static Dictionary<T, RoleTypeId> Build<T>(
        IReadOnlyDictionary<T, RoleTypeId> originalRoles,
        IReadOnlyDictionary<T, RoleTypeId> forcedRoles) where T : notnull
    {
        Dictionary<T, RoleTypeId> roles = originalRoles.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (KeyValuePair<T, RoleTypeId> forced in forcedRoles)
        {
            if (!roles.TryGetValue(forced.Key, out RoleTypeId previous) || previous == forced.Value)
                continue;

            List<T> donors = roles.Where(pair => !forcedRoles.ContainsKey(pair.Key) &&
                pair.Value == forced.Value).Select(pair => pair.Key).ToList();
            if (donors.Count == 0 && !ScpOption.IsScpRole(previous))
                donors = roles.Where(pair => !forcedRoles.ContainsKey(pair.Key) &&
                    ScpOption.IsScpRole(pair.Value)).Select(pair => pair.Key).ToList();

            if (donors.Count > 0)
                roles[donors[0]] = previous;
            roles[forced.Key] = forced.Value;
        }
        return roles;
    }
}
