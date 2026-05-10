using PlayerRoles;

namespace ScpslCustomRoomPlugin
{
    public static class VanillaRoleAssignmentResolver
    {
        public static bool TryResolve(RoleTypeId currentRole, RoleTypeId? capturedRole, out RoleTypeId resolvedRole)
        {
            if (IsRoundRole(currentRole))
            {
                resolvedRole = currentRole;
                return true;
            }

            if (capturedRole.HasValue && IsRoundRole(capturedRole.Value))
            {
                resolvedRole = capturedRole.Value;
                return true;
            }

            resolvedRole = RoleTypeId.None;
            return false;
        }

        private static bool IsRoundRole(RoleTypeId role)
        {
            return role is not RoleTypeId.None and not RoleTypeId.Spectator and not RoleTypeId.Tutorial;
        }
    }
}
