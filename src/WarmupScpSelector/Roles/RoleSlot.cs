using System;
using LabApi.Features.Wrappers;

namespace WarmupScpSelector.Roles;

/// <summary>
/// One special role filled by the round-start draft. Consumers register slots with
/// <see cref="RoundRoles.Register"/>; the draft fills them in ascending <see cref="Priority"/> from living
/// players nobody has claimed yet, calls <see cref="Apply"/> for each chosen player and claims them as
/// <see cref="Id"/>.
/// </summary>
public sealed class RoleSlot
{
    public RoleSlot(string id, int priority, Func<Player, bool> isEligible, Action<Player> apply)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A role slot needs a stable id.", nameof(id));
        }

        Id = id;
        Priority = priority;
        IsEligible = isEligible ?? throw new ArgumentNullException(nameof(isEligible));
        Apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    /// <summary>Stable claim id, such as <c>rs.facility_manager</c> or <c>scp999</c>.</summary>
    public string Id { get; }

    /// <summary>Lower drafts first. Leave gaps so new roles fit without renumbering.</summary>
    public int Priority { get; }

    /// <summary>The consumer's own role rules, evaluated only for unclaimed living players.</summary>
    public Func<Player, bool> IsEligible { get; }

    /// <summary>
    /// Turns the chosen player into the role. Runs as the claim owner, so role changes inside it keep the
    /// claim. An exception releases the claim.
    /// </summary>
    public Action<Player> Apply { get; }

    /// <summary>How many players this slot takes. Defaults to one.</summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>
    /// Whether the slot runs this round, given the number of connected non-host players (dummies included).
    /// Null means always.
    /// </summary>
    public Func<int, bool>? IsActive { get; set; }

    /// <summary>Optional relative weight for the random choice. Null or non-positive weights count as one.</summary>
    public Func<Player, float>? Weight { get; set; }
}
