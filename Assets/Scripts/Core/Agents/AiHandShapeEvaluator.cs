using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Agents
{
    /// <summary>Per-decision cache keyed only by concealed tile counts and open-meld count.</summary>
    public sealed class AiHandShapeEvaluationCache
    {
        private readonly Dictionary<string, int> _shantenByShape = new Dictionary<string, int>(StringComparer.Ordinal);

        public int EntryCount => _shantenByShape.Count;

        public int CalculateShanten(IEnumerable<TileData> hand, IEnumerable<Meld> melds)
        {
            int[] counts = AiHandShapeEvaluator.CreateCounts(hand);
            int openMeldCount = (melds ?? Array.Empty<Meld>()).Count(meld => meld != null);
            string key = openMeldCount + ":" + string.Join(",", counts);
            if (_shantenByShape.TryGetValue(key, out int cached)) return cached;
            int result = AiHandShapeEvaluator.CalculateShanten(counts, openMeldCount);
            _shantenByShape[key] = result;
            return result;
        }
    }

    public static class AiHandShapeEvaluator
    {
        public static int CalculateShanten(IEnumerable<TileData> hand, IEnumerable<Meld> melds)
        {
            int[] counts = CreateCounts(hand);
            int openMeldCount = (melds ?? Array.Empty<Meld>()).Count(meld => meld != null);
            return CalculateShanten(counts, openMeldCount);
        }

        internal static int[] CreateCounts(IEnumerable<TileData> hand)
        {
            int[] counts = new int[MahjongLogic.MAX_TILE_INDEX];
            foreach (TileData tile in hand ?? Array.Empty<TileData>())
            {
                if (tile == null) continue;
                int index = MahjongLogic.GetTileIndex(tile);
                if (index >= 0 && index < counts.Length) counts[index]++;
            }
            return counts;
        }

        internal static int CalculateShanten(int[] counts, int openMeldCount)
        {
            int minimum = CalculateStandardShanten(counts, openMeldCount);
            if (openMeldCount == 0)
            {
                minimum = Math.Min(minimum, CalculateSevenPairsShanten(counts));
                minimum = Math.Min(minimum, CalculateThirteenOrphansShanten(counts));
            }
            return minimum;
        }

        private static int CalculateStandardShanten(int[] counts, int openMeldCount)
        {
            int minimum = 8;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Search(counts, 0, openMeldCount, 0, 0, ref minimum, visited);
            return minimum;
        }

        private static void Search(int[] counts, int index, int melds, int partials, int pair,
            ref int minimum, HashSet<string> visited)
        {
            while (index < counts.Length && counts[index] == 0) index++;
            if (index >= counts.Length)
            {
                int cappedPartials = Math.Min(partials, Math.Max(0, 4 - melds));
                minimum = Math.Min(minimum, 8 - melds * 2 - cappedPartials - pair);
                return;
            }

            string key = index + ":" + melds + ":" + partials + ":" + pair + ":" + string.Join(",", counts);
            if (!visited.Add(key)) return;

            if (counts[index] >= 3)
            {
                counts[index] -= 3;
                Search(counts, index, melds + 1, partials, pair, ref minimum, visited);
                counts[index] += 3;
            }
            if (CanSequence(index) && counts[index + 1] > 0 && counts[index + 2] > 0)
            {
                counts[index]--; counts[index + 1]--; counts[index + 2]--;
                Search(counts, index, melds + 1, partials, pair, ref minimum, visited);
                counts[index]++; counts[index + 1]++; counts[index + 2]++;
            }
            if (pair == 0 && counts[index] >= 2)
            {
                counts[index] -= 2;
                Search(counts, index, melds, partials, 1, ref minimum, visited);
                counts[index] += 2;
            }
            if (counts[index] >= 2)
            {
                counts[index] -= 2;
                Search(counts, index, melds, partials + 1, pair, ref minimum, visited);
                counts[index] += 2;
            }
            if (index < 27 && index % 9 < 8 && counts[index + 1] > 0)
            {
                counts[index]--; counts[index + 1]--;
                Search(counts, index, melds, partials + 1, pair, ref minimum, visited);
                counts[index]++; counts[index + 1]++;
            }
            if (index < 27 && index % 9 < 7 && counts[index + 2] > 0)
            {
                counts[index]--; counts[index + 2]--;
                Search(counts, index, melds, partials + 1, pair, ref minimum, visited);
                counts[index]++; counts[index + 2]++;
            }

            counts[index]--;
            Search(counts, index, melds, partials, pair, ref minimum, visited);
            counts[index]++;
        }

        private static int CalculateSevenPairsShanten(int[] counts)
        {
            int pairs = counts.Count(count => count >= 2);
            int distinct = counts.Count(count => count > 0);
            return 6 - pairs + Math.Max(0, 7 - distinct);
        }

        private static int CalculateThirteenOrphansShanten(int[] counts)
        {
            int[] required = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int distinct = required.Count(index => counts[index] > 0);
            bool pair = required.Any(index => counts[index] > 1);
            return 13 - distinct - (pair ? 1 : 0);
        }

        private static bool CanSequence(int index) => index < 27 && index % 9 < 7;
    }
}
