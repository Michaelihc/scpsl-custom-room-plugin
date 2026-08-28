using System;
using System.Collections.Generic;
using System.Linq;
using PlayerRoles;

namespace WarmupScpSelector.Replacement;

/// <summary>One vacant SCP role and the stable UserIds volunteering for it.</summary>
internal sealed class PendingScpReplacement
{
    private readonly HashSet<string> _volunteers = new(StringComparer.Ordinal);

    public PendingScpReplacement(RoleTypeId role, int roundGeneration)
    {
        Role = role;
        RoundGeneration = roundGeneration;
    }

    public RoleTypeId Role { get; }

    public int RoundGeneration { get; }

    public int LotteryToken { get; private set; }

    public bool LotteryRunning { get; private set; }

    public IReadOnlyCollection<string> Volunteers => _volunteers;

    public bool AddVolunteer(string userId) =>
        !string.IsNullOrWhiteSpace(userId) && _volunteers.Add(userId);

    public void RemoveVolunteer(string userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            _volunteers.Remove(userId);
        }
    }

    public int ArmLottery()
    {
        LotteryRunning = true;
        LotteryToken++;
        return LotteryToken;
    }

    public void StopLottery() => LotteryRunning = false;
}

/// <summary>
/// Pure, round-generation-guarded replacement state. Runtime wrappers and coroutines stay in the service.
/// </summary>
internal sealed class ScpReplacementState
{
    private readonly Dictionary<RoleTypeId, PendingScpReplacement> _pending = new();
    private readonly Dictionary<string, double> _lastCommandSeconds = new(StringComparer.Ordinal);

    public int RoundGeneration { get; private set; }

    public bool RoundActive { get; private set; }

    public int ReplacementsThisRound { get; private set; }

    public int PendingCount => _pending.Count;

    public IReadOnlyList<RoleTypeId> PendingRoles => _pending.Keys.OrderBy(role => (int)role).ToArray();

    public void BeginRound()
    {
        RoundGeneration++;
        RoundActive = true;
        ReplacementsThisRound = 0;
        _pending.Clear();
        _lastCommandSeconds.Clear();
    }

    public void EndRound()
    {
        RoundGeneration++;
        RoundActive = false;
        ReplacementsThisRound = 0;
        _pending.Clear();
        _lastCommandSeconds.Clear();
    }

    public bool IsCapacityReserved(int maxReplacementsPerRound) =>
        maxReplacementsPerRound > 0 && ReplacementsThisRound + _pending.Count >= maxReplacementsPerRound;

    public bool TryOpen(RoleTypeId role, int maxReplacementsPerRound, out PendingScpReplacement? entry)
    {
        entry = null;
        if (!RoundActive || _pending.ContainsKey(role) || IsCapacityReserved(maxReplacementsPerRound))
        {
            return false;
        }

        entry = new PendingScpReplacement(role, RoundGeneration);
        _pending.Add(role, entry);
        return true;
    }

    public bool TryFind(RoleTypeId role, out PendingScpReplacement? entry) =>
        _pending.TryGetValue(role, out entry);

    public bool TryFind(string argument, out PendingScpReplacement? entry)
    {
        entry = _pending.Values.FirstOrDefault(candidate =>
            ScpReplacementPolicy.MatchesScpArgument(candidate.Role, argument));
        return entry != null;
    }

    public bool TryVolunteer(
        RoleTypeId role,
        string userId,
        out PendingScpReplacement? entry,
        out bool shouldStartLottery,
        out int lotteryToken)
    {
        shouldStartLottery = false;
        lotteryToken = 0;
        if (!_pending.TryGetValue(role, out entry) || !entry.AddVolunteer(userId))
        {
            return false;
        }

        if (!entry.LotteryRunning)
        {
            shouldStartLottery = true;
            lotteryToken = entry.ArmLottery();
        }

        return true;
    }

    public bool TryTakeLottery(
        RoleTypeId role,
        int roundGeneration,
        int lotteryToken,
        out PendingScpReplacement? entry)
    {
        entry = null;
        if (!RoundActive || RoundGeneration != roundGeneration ||
            !_pending.TryGetValue(role, out PendingScpReplacement current) ||
            current.RoundGeneration != roundGeneration || !current.LotteryRunning ||
            current.LotteryToken != lotteryToken)
        {
            return false;
        }

        _pending.Remove(role);
        current.StopLottery();
        entry = current;
        return true;
    }

    public bool RemovePending(RoleTypeId role, PendingScpReplacement expected)
    {
        if (!_pending.TryGetValue(role, out PendingScpReplacement current) || !ReferenceEquals(current, expected))
        {
            return false;
        }

        current.StopLottery();
        return _pending.Remove(role);
    }

    public void RemoveVolunteer(string userId)
    {
        foreach (PendingScpReplacement entry in _pending.Values)
        {
            entry.RemoveVolunteer(userId);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            _lastCommandSeconds.Remove(userId);
        }
    }

    public void MarkReplacementSucceeded() => ReplacementsThisRound++;

    public bool TryConsumeCooldown(string userId, double nowSeconds, double cooldownSeconds, out double remainingSeconds)
    {
        remainingSeconds = 0d;
        if (string.IsNullOrWhiteSpace(userId) || cooldownSeconds <= 0d)
        {
            return true;
        }

        if (_lastCommandSeconds.TryGetValue(userId, out double last))
        {
            double elapsed = nowSeconds - last;
            if (elapsed >= 0d && elapsed < cooldownSeconds)
            {
                remainingSeconds = cooldownSeconds - elapsed;
                return false;
            }
        }

        _lastCommandSeconds[userId] = nowSeconds;
        return true;
    }
}
