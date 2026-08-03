using System;
using System.Collections.Generic;

namespace WarmupScpSelector.Activities.AimRange
{
    public readonly struct RangePoint
    {
        public RangePoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static RangePoint Lerp(RangePoint a, RangePoint b, float t) => new RangePoint(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
    }

    public readonly struct RangePathSegment
    {
        public RangePathSegment(RangePoint from, RangePoint to, double durationSeconds)
        {
            From = from;
            To = to;
            DurationSeconds = durationSeconds > 0d ? durationSeconds : 0.001d;
        }

        public RangePoint From { get; }
        public RangePoint To { get; }
        public double DurationSeconds { get; }
    }

    public enum TargetCardKind
    {
        Static,
        Moving,
    }

    public readonly struct TargetCard
    {
        public TargetCard(TargetCardKind kind, int anchorIndex)
        {
            Kind = kind;
            AnchorIndex = anchorIndex;
        }

        public TargetCardKind Kind { get; }
        public int AnchorIndex { get; }
    }

    public static class AimRangeDeterminism
    {
        public static int CombineSeed(int mapSeed, int rangeGeneration, int slotId, int ordinal, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, mapSeed);
                Mix(ref hash, rangeGeneration);
                Mix(ref hash, slotId);
                Mix(ref hash, ordinal);
                Mix(ref hash, salt);
                return (int)(hash & 0x7fffffff);
            }
        }

        public static IReadOnlyList<TargetCard> BuildTargetDeck(int staticCount, int movingCount, int sequenceSize, int seed)
        {
            List<TargetCard> source = new List<TargetCard>();
            for (int i = 0; i < Math.Max(0, staticCount); i++)
            {
                source.Add(new TargetCard(TargetCardKind.Static, i));
            }

            for (int i = 0; i < Math.Max(0, movingCount); i++)
            {
                source.Add(new TargetCard(TargetCardKind.Moving, i));
            }

            if (source.Count == 0 || sequenceSize <= 0)
            {
                return Array.Empty<TargetCard>();
            }

            Random random = new Random(seed);
            List<TargetCard> deck = new List<TargetCard>(sequenceSize);
            List<TargetCard> bag = new List<TargetCard>(source);
            while (deck.Count < sequenceSize)
            {
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    TargetCard tmp = bag[i];
                    bag[i] = bag[j];
                    bag[j] = tmp;
                }

                for (int i = 0; i < bag.Count && deck.Count < sequenceSize; i++)
                {
                    deck.Add(bag[i]);
                }
            }

            return deck;
        }

        public static int SelectIndex(int count, int seed) => count <= 0 ? -1 : new Random(seed).Next(count);

        public static int SelectBotPresetIndex(
            int presetCount,
            int mapSeed,
            int rangeGeneration,
            int slotId,
            int spawnOrdinal,
            int salt) => SelectIndex(
                presetCount,
                CombineSeed(mapSeed, rangeGeneration, slotId, spawnOrdinal, salt));

        /// <summary>Looped piecewise path evaluated from absolute elapsed time, independent of tick partition.</summary>
        public static RangePoint EvaluatePath(IReadOnlyList<RangePathSegment> segments, double elapsedSeconds)
        {
            if (segments == null || segments.Count == 0)
            {
                return default;
            }

            double total = 0d;
            for (int i = 0; i < segments.Count; i++)
            {
                total += Math.Max(0.001d, segments[i].DurationSeconds);
            }

            double time = elapsedSeconds % total;
            if (time < 0d)
            {
                time += total;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                RangePathSegment segment = segments[i];
                if (time <= segment.DurationSeconds || i == segments.Count - 1)
                {
                    return RangePoint.Lerp(segment.From, segment.To, (float)(time / segment.DurationSeconds));
                }

                time -= segment.DurationSeconds;
            }

            return segments[segments.Count - 1].To;
        }

        private static void Mix(ref uint hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 16777619u;
                hash ^= (uint)(value >> 16);
                hash *= 16777619u;
            }
        }
    }
}
