using System;
using System.Collections.Generic;
using System.Linq;

namespace WarmupScpSelector.Roles;

/// <summary>A slot as the pure planner sees it.</summary>
internal sealed class DraftSlot<TPlayer>
{
    public DraftSlot(string id, int priority, int maxCount, Func<TPlayer, bool> isEligible, Func<TPlayer, float>? weight)
    {
        Id = id;
        Priority = priority;
        MaxCount = maxCount;
        IsEligible = isEligible;
        Weight = weight;
    }

    public string Id { get; }

    public int Priority { get; }

    public int MaxCount { get; }

    public Func<TPlayer, bool> IsEligible { get; }

    public Func<TPlayer, float>? Weight { get; }
}

/// <summary>
/// Pure draft order: slots in ascending priority (ties keep registration order), each taking up to
/// <c>MaxCount</c> weighted-random players from those not already taken by an earlier slot. A slot's
/// eligibility is evaluated only against players still free when that slot drafts.
/// </summary>
internal static class RoleDraftPlanner
{
    public static List<KeyValuePair<string, TPlayer>> Plan<TPlayer>(
        IEnumerable<DraftSlot<TPlayer>> slots,
        IEnumerable<TPlayer> candidates,
        Func<double> random)
    {
        List<TPlayer> free = candidates.ToList();
        List<KeyValuePair<string, TPlayer>> picks = new();
        foreach (DraftSlot<TPlayer> slot in slots.Select((slot, index) => (slot, index))
                     .OrderBy(entry => entry.slot.Priority)
                     .ThenBy(entry => entry.index)
                     .Select(entry => entry.slot))
        {
            for (int taken = 0; taken < Math.Max(0, slot.MaxCount); taken++)
            {
                List<TPlayer> eligible = free.Where(player => SafeEligible(slot, player)).ToList();
                if (eligible.Count == 0)
                {
                    break;
                }

                TPlayer chosen = WeightedChoice(eligible, slot.Weight, random);
                free.Remove(chosen);
                picks.Add(new KeyValuePair<string, TPlayer>(slot.Id, chosen));
            }
        }

        return picks;
    }

    private static bool SafeEligible<TPlayer>(DraftSlot<TPlayer> slot, TPlayer player)
    {
        try
        {
            return slot.IsEligible(player);
        }
        catch
        {
            return false;
        }
    }

    private static TPlayer WeightedChoice<TPlayer>(List<TPlayer> eligible, Func<TPlayer, float>? weight, Func<double> random)
    {
        double[] weights = eligible.Select(player =>
        {
            float value;
            try
            {
                value = weight?.Invoke(player) ?? 1f;
            }
            catch
            {
                value = 1f;
            }

            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1d;
        }).ToArray();

        double roll = Math.Min(Math.Max(random(), 0d), 0.999999999d) * weights.Sum();
        for (int i = 0; i < eligible.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0d)
            {
                return eligible[i];
            }
        }

        return eligible[eligible.Count - 1];
    }
}
