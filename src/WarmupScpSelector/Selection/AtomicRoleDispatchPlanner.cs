using System.Collections.Generic;
using PlayerRoles;

namespace WarmupScpSelector.Selection
{
    /// <summary>
    /// Splits a completed atomic role plan around the player whose cancellable ChangingRole callback is
    /// currently executing. Re-entering ServerSetRole for that same player can corrupt the native role
    /// transition; their final role must instead be written back to the current event arguments.
    /// </summary>
    public static class AtomicRoleDispatchPlanner
    {
        public static AtomicRoleDispatchPlan<T> Build<T>(
            IReadOnlyDictionary<T, RoleTypeId> finalScpRoles,
            T callbackPlayer)
        {
            List<KeyValuePair<T, RoleTypeId>> immediate = new List<KeyValuePair<T, RoleTypeId>>();
            bool callbackReceivesScp = false;
            RoleTypeId callbackRole = RoleTypeId.None;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;

            foreach (KeyValuePair<T, RoleTypeId> assignment in finalScpRoles)
            {
                if (comparer.Equals(assignment.Key, callbackPlayer))
                {
                    callbackReceivesScp = true;
                    callbackRole = assignment.Value;
                }
                else
                {
                    immediate.Add(assignment);
                }
            }

            return new AtomicRoleDispatchPlan<T>(immediate, callbackReceivesScp, callbackRole);
        }
    }

    public sealed class AtomicRoleDispatchPlan<T>
    {
        public AtomicRoleDispatchPlan(
            IReadOnlyList<KeyValuePair<T, RoleTypeId>> immediateAssignments,
            bool callbackReceivesScp,
            RoleTypeId callbackRole)
        {
            ImmediateAssignments = immediateAssignments;
            CallbackReceivesScp = callbackReceivesScp;
            CallbackRole = callbackRole;
        }

        public IReadOnlyList<KeyValuePair<T, RoleTypeId>> ImmediateAssignments { get; }

        public bool CallbackReceivesScp { get; }

        public RoleTypeId CallbackRole { get; }
    }
}
