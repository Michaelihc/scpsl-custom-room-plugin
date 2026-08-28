using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace WarmupScpSelector.Replacement;

/// <summary>Pure eligibility, parsing, and weighted-choice rules for SCP replacement.</summary>
internal static class ScpReplacementPolicy
{
    public static bool IsMainScp(RoleTypeId role)
    {
        switch (role)
        {
            case RoleTypeId.Scp049:
            case RoleTypeId.Scp079:
            case RoleTypeId.Scp096:
            case RoleTypeId.Scp106:
            case RoleTypeId.Scp173:
            case RoleTypeId.Scp939:
            case RoleTypeId.Scp3114:
                return true;
            default:
                return false;
        }
    }

    public static bool CanOpenDeparture(
        RoleTypeId role,
        float health,
        float maxHealth,
        double elapsedSeconds,
        float departureCutoffSeconds,
        float requiredHealthPercentage,
        ICollection<RoleTypeId>? ignoredRoles)
    {
        if (!IsMainScp(role) || (ignoredRoles?.Contains(role) ?? false))
        {
            return false;
        }

        if (!IsFinite(elapsedSeconds) || elapsedSeconds < 0d || elapsedSeconds > departureCutoffSeconds)
        {
            return false;
        }

        if (!IsFinite(health) || !IsFinite(maxHealth) || maxHealth <= 0f)
        {
            return false;
        }

        float percentage = Math.Max(0f, Math.Min(100f, requiredHealthPercentage));
        return health >= maxHealth * (percentage / 100f);
    }

    /// <summary>
    /// Spectators are always eligible. When alive volunteers are enabled, any living non-SCP role is also
    /// eligible; dead/None/overwatch states cannot enter merely because the option is enabled.
    /// </summary>
    public static bool CanVolunteer(RoleTypeId role, bool isAlive, bool allowAliveVolunteers)
    {
        if (IsAnyScp(role))
        {
            return false;
        }

        if (role == RoleTypeId.Spectator)
        {
            return true;
        }

        return allowAliveVolunteers && isAlive;
    }

    public static bool IsHumanCommandRole(RoleTypeId role)
    {
        switch (role)
        {
            case RoleTypeId.ClassD:
            case RoleTypeId.Scientist:
            case RoleTypeId.FacilityGuard:
            case RoleTypeId.NtfPrivate:
            case RoleTypeId.NtfSergeant:
            case RoleTypeId.NtfCaptain:
            case RoleTypeId.NtfSpecialist:
            case RoleTypeId.ChaosConscript:
            case RoleTypeId.ChaosRifleman:
            case RoleTypeId.ChaosMarauder:
            case RoleTypeId.ChaosRepressor:
                return true;
            default:
                return false;
        }
    }

    public static RoleTypeId PickWeightedHumanRole(
        IReadOnlyDictionary<RoleTypeId, int>? weights,
        int nonNegativeRoll)
    {
        KeyValuePair<RoleTypeId, int>[] valid = (weights ?? new Dictionary<RoleTypeId, int>())
            .Where(pair => pair.Value > 0 && IsHumanCommandRole(pair.Key))
            .OrderBy(pair => (int)pair.Key)
            .ToArray();

        long total = valid.Sum(pair => (long)pair.Value);
        if (total <= 0)
        {
            return RoleTypeId.ClassD;
        }

        long roll = Math.Abs((long)nonNegativeRoll) % total;
        foreach (KeyValuePair<RoleTypeId, int> pair in valid)
        {
            roll -= pair.Value;
            if (roll < 0)
            {
                return pair.Key;
            }
        }

        return RoleTypeId.ClassD;
    }

    public static string ScpNumber(RoleTypeId role)
    {
        switch (role)
        {
            case RoleTypeId.Scp049: return "049";
            case RoleTypeId.Scp079: return "079";
            case RoleTypeId.Scp096: return "096";
            case RoleTypeId.Scp106: return "106";
            case RoleTypeId.Scp173: return "173";
            case RoleTypeId.Scp939: return "939";
            case RoleTypeId.Scp3114: return "3114";
            case RoleTypeId.Scp0492: return "049-2";
            default: return string.Empty;
        }
    }

    public static bool MatchesScpArgument(RoleTypeId role, string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        string candidate = argument!.Trim();
        if (candidate.Length > 12)
        {
            return false;
        }

        string normalized = candidate.ToLowerInvariant();
        if (normalized.StartsWith("scp", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(3);
        }

        if (normalized.StartsWith("-", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        string expected = DigitsOnly(ScpNumber(role));
        string actual = normalized.Replace("-", string.Empty);
        if (expected.Length == 0 || actual.Length == 0 || actual.Length > 4)
        {
            return false;
        }

        if (actual.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        return string.Equals(TrimLeadingZeroes(expected), TrimLeadingZeroes(actual), StringComparison.Ordinal);
    }

    public static string HumanRoleName(RoleTypeId role, bool chinese)
    {
        if (!chinese)
        {
            return role.ToString();
        }

        switch (role)
        {
            case RoleTypeId.ClassD: return "D级人员";
            case RoleTypeId.Scientist: return "科学家";
            case RoleTypeId.FacilityGuard: return "设施警卫";
            case RoleTypeId.NtfPrivate: return "九尾狐列兵";
            case RoleTypeId.NtfSergeant: return "九尾狐中士";
            case RoleTypeId.NtfCaptain: return "九尾狐指挥官";
            case RoleTypeId.NtfSpecialist: return "九尾狐收容专家";
            case RoleTypeId.ChaosConscript: return "混沌征召兵";
            case RoleTypeId.ChaosRifleman: return "混沌步枪兵";
            case RoleTypeId.ChaosMarauder: return "混沌掠夺者";
            case RoleTypeId.ChaosRepressor: return "混沌压制者";
            default: return role.ToString();
        }
    }

    private static bool IsAnyScp(RoleTypeId role) =>
        role == RoleTypeId.Scp0492 || IsMainScp(role);

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string TrimLeadingZeroes(string value)
    {
        string trimmed = value.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
