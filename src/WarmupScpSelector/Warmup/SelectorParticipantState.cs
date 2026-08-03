using System;
using System.Collections.Generic;
using PlayerRoles;

namespace WarmupScpSelector.Warmup
{
    /// <summary>
    /// Pure selector-owned human state. Every mutating admission path rechecks the canonical identity so
    /// dummy/NPC/host hubs cannot enter moved-in, selection, or vanilla-role state even if a caller regresses.
    /// </summary>
    public sealed class SelectorParticipantState
    {
        private readonly HashSet<string> _movedIn = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, RoleTypeId> _selections = new Dictionary<string, RoleTypeId>(StringComparer.Ordinal);
        private readonly Dictionary<string, RoleTypeId> _vanillaRoles = new Dictionary<string, RoleTypeId>(StringComparer.Ordinal);

        public int MovedInCount => _movedIn.Count;
        public int SelectionCount => _selections.Count;
        public int VanillaRoleCount => _vanillaRoles.Count;
        public IEnumerable<RoleTypeId> SelectedRoles => _selections.Values;

        public bool TrackMovedIn(ParticipantIdentity identity)
        {
            return identity.IsCanonicalHuman && _movedIn.Add(identity.StableKey);
        }

        public bool IsMovedIn(string stableKey)
        {
            return !string.IsNullOrEmpty(stableKey) && _movedIn.Contains(stableKey);
        }

        public bool RecordSelection(ParticipantIdentity identity, RoleTypeId role)
        {
            if (!identity.IsCanonicalHuman || !_movedIn.Contains(identity.StableKey))
            {
                return false;
            }

            _selections[identity.StableKey] = role;
            return true;
        }

        public bool TryGetSelection(string stableKey, out RoleTypeId role)
        {
            if (!string.IsNullOrEmpty(stableKey) && _selections.TryGetValue(stableKey, out role))
            {
                return true;
            }

            role = RoleTypeId.None;
            return false;
        }

        public bool SnapshotVanillaRole(ParticipantIdentity identity, RoleTypeId role)
        {
            if (!identity.IsCanonicalHuman || !_movedIn.Contains(identity.StableKey))
            {
                return false;
            }

            _vanillaRoles[identity.StableKey] = role;
            return true;
        }

        public bool TryGetVanillaRole(string stableKey, out RoleTypeId role)
        {
            if (!string.IsNullOrEmpty(stableKey) && _vanillaRoles.TryGetValue(stableKey, out role))
            {
                return true;
            }

            role = RoleTypeId.None;
            return false;
        }

        public void Remove(string stableKey)
        {
            if (string.IsNullOrEmpty(stableKey))
            {
                return;
            }

            _movedIn.Remove(stableKey);
            _selections.Remove(stableKey);
            _vanillaRoles.Remove(stableKey);
        }

        public void ClearVanillaRoles() => _vanillaRoles.Clear();

        public void Clear()
        {
            _movedIn.Clear();
            _selections.Clear();
            _vanillaRoles.Clear();
        }
    }
}
