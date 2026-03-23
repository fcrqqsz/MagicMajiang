using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MahjongGame.Core
{
    /// <summary>
    /// 纯静态类，负责纯粹的数学/规则计算
    /// 不依赖 Unity 的 GameObject
    /// </summary>
    public static class MahjongLogic
    {
        // 牌索引映射常量
        // 0-8: 万, 9-17: 筒, 18-26: 索, 27-30: 东南西北, 31-33: 中发白
        public const int MAX_TILE_INDEX = 34;

        // 静态实例，避免重复创建
        private static Fan.FanCalculator _calculator = new Fan.FanCalculator();

        /// <summary>
        /// 完整胡牌判定 (含8番起胡校验)
        /// </summary>
        public static bool CheckWinWithFan(List<TileData> hand, List<Meld> melds, TileData winTile, bool isSelfDraw, out int totalFan, out List<string> fanDetails, WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East, ScoringOptions options = null)
        {
            totalFan = 0;
            fanDetails = null;

            // 1. 获取所有胡牌拆解方案
            var decompositions = GetAllDecompositions(hand, melds, winTile, isSelfDraw);
            if (decompositions.Count == 0) return false;

            int bonusFan = options?.BonusFan ?? 0;
            int maxFan = -1;
            List<string> bestDetails = null;

            // 2. 遍历所有方案，选番数最高的
            foreach (var decomp in decompositions)
            {
                var ctx = new Fan.FanContext(hand, melds, winTile, isSelfDraw, roundWind, seatWind, decomp);
                ctx.Wait = decomp.Wait;

                int currentFan = _calculator.CalculateTotalFan(ctx, out List<string> currentDetails);

                // 宽松清龙检查：如果标准清龙未命中，尝试宽松版
                if (options?.RelaxedPureStraight == true && !HasStandardPureStraight(decomp))
                {
                    int relaxedFan = CheckRelaxedPureStraight(decomp);
                    if (relaxedFan > 0)
                    {
                        currentFan += relaxedFan;
                        currentDetails.Add($"清龙·如龙({relaxedFan})");
                    }
                }

                if (currentFan > maxFan)
                {
                    maxFan = currentFan;
                    bestDetails = currentDetails;
                }
            }

            // 3. 8番起胡校验 (含天赋加番)
            if (maxFan + bonusFan < 8) return false;

            totalFan = maxFan + bonusFan;
            fanDetails = bestDetails;
            if (bonusFan > 0)
                fanDetails.Add($"快人一步(+{bonusFan})");
            return true;
        }

        /// <summary>
        /// 检查拆解方案中是否已包含标准清龙 (同花色 1-4-7 三副顺子)
        /// </summary>
        private static bool HasStandardPureStraight(InternalDecomposition decomp)
        {
            var sequences = decomp.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var groupedBySuit = sequences.GroupBy(s => s.FirstTile.TileSuit);
            foreach (var group in groupedBySuit)
            {
                var vals = group.Select(s => s.FirstTile.Value).ToList();
                if (vals.Contains(1) && vals.Contains(4) && vals.Contains(7)) return true;
            }
            return false;
        }

        /// <summary>
        /// 宽松清龙检查：允许三个同花色顺子中有一个的起始值与目标 {1,4,7} 差 1
        /// </summary>
        private static int CheckRelaxedPureStraight(InternalDecomposition decomp)
        {
            var sequences = decomp.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var groupedBySuit = sequences.GroupBy(s => s.FirstTile.TileSuit);

            int[] targets = { 1, 4, 7 };

            foreach (var group in groupedBySuit)
            {
                var vals = group.Select(s => s.FirstTile.Value).ToList();
                if (vals.Count < 3) continue;

                // 尝试从该花色的所有顺子中选出 3 个，其中恰好 2 个命中 {1,4,7}，第 3 个与缺失目标差值为 1
                for (int i = 0; i < vals.Count; i++)
                {
                    for (int j = i + 1; j < vals.Count; j++)
                    {
                        for (int k = j + 1; k < vals.Count; k++)
                        {
                            int[] triple = { vals[i], vals[j], vals[k] };
                            // 检查是否恰好2个匹配 {1,4,7}，第3个与缺失目标差1
                            var matched = new List<int>();
                            var unmatched = new List<int>();
                            foreach (var v in triple)
                            {
                                if (v == 1 || v == 4 || v == 7) matched.Add(v);
                                else unmatched.Add(v);
                            }

                            if (matched.Count == 2 && unmatched.Count == 1)
                            {
                                // 找到缺失的目标
                                int missing = -1;
                                foreach (var t in targets)
                                {
                                    if (!matched.Contains(t)) { missing = t; break; }
                                }

                                if (missing != -1 && System.Math.Abs(unmatched[0] - missing) == 1)
                                {
                                    return 16; // 清龙 16 番
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }

        // --- 核心逻辑：获取所有拆解方案 ---

        public class InternalDecomposition : Fan.HandDecomposition
        {
            public Fan.WaitType Wait;
        }

        private static List<InternalDecomposition> GetAllDecompositions(List<TileData> hand, List<Meld> melds, TileData winTile, bool isSelfDraw)
        {
            List<InternalDecomposition> results = new List<InternalDecomposition>();
            
            // 准备所有牌的池子
            List<TileData> pool = new List<TileData>(hand);
            
            // 如果手牌数量是 3n+1 (1,4,7,10,13)，说明 winTile 还没被包含，需要加进去
            if (pool.Count % 3 == 1)
            {
                pool.Add(winTile);
            }

            int[] tileCounts = ConvertToFrequencyArray(pool);

            // --- 1. 特殊形：七对子 (不计副露) ---
            if (melds.Count == 0 && CheckSevenPairs(tileCounts))
            {
                var decomp = new InternalDecomposition();
                // 七对子拆解：允许同种牌拆成多个对子
                for(int i = 0; i < MAX_TILE_INDEX; i++)
                {
                    int pairCount = tileCounts[i] / 2;
                    for (int p = 0; p < pairCount; p++)
                    {
                        decomp.Pair.Add(CreateTileFromIndex(i));
                    }
                }
                decomp.Wait = Fan.WaitType.Single; // 七对子必然是单钓
                results.Add(decomp);
            }

            // --- 2. 特殊形：十三幺 (不计副露) ---
            if (melds.Count == 0 && CheckThirteenOrphans(tileCounts))
            {
                var decomp = new InternalDecomposition();
                decomp.Wait = Fan.WaitType.Single; // 十三幺单钓
                results.Add(decomp);
            }

            // --- 3. 特殊形：全不靠/七星不靠 (不计副露) ---
            if (melds.Count == 0 && CheckQuanBuKao(tileCounts))
            {
                var decomp = new InternalDecomposition();
                decomp.Wait = Fan.WaitType.Single; // 全不靠单钓
                results.Add(decomp);
            }

            // --- 4. 标准形：4面子 + 1雀头 ---
            int setsNeeded = 4 - melds.Count;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tileCounts[i] >= 2)
                {
                    tileCounts[i] -= 2;
                    var currentPath = new List<Meld>();
                    FindDecompositionsWithKnitted(tileCounts, setsNeeded, currentPath, (completedSets) =>
                    {
                        var decomp = new InternalDecomposition();
                        decomp.AllMelds.AddRange(melds);
                        foreach(var ms in completedSets) decomp.AllMelds.Add(ms);
                        
                        decomp.Pair.Add(CreateTileFromIndex(i));
                        decomp.Wait = AnalyzeWaitType(decomp, winTile, i);

                        results.Add(decomp);
                    });
                    tileCounts[i] += 2;
                }
            }
            return results;
        }

        private static bool HasKnittedSuit(int[] tiles, int suitIndex, int offset)
        {
            int baseIdx = suitIndex * 9;
            return tiles[baseIdx + offset] > 0 && tiles[baseIdx + offset + 3] > 0 && tiles[baseIdx + offset + 6] > 0;
        }

        private static void ModKnittedSuit(int[] tiles, int suitIndex, int offset, int delta)
        {
            int baseIdx = suitIndex * 9;
            tiles[baseIdx + offset] += delta;
            tiles[baseIdx + offset + 3] += delta;
            tiles[baseIdx + offset + 6] += delta;
        }

        private static Meld CreateKnittedMeld(int suitIndex, int offset)
        {
            int baseIdx = suitIndex * 9;
            return new Meld(MeldType.Knitted, new List<TileData> { 
                CreateTileFromIndex(baseIdx + offset), 
                CreateTileFromIndex(baseIdx + offset + 3), 
                CreateTileFromIndex(baseIdx + offset + 6) 
            }, -1, true);
        }

        private static void FindDecompositionsWithKnitted(int[] tiles, int setsNeeded, List<Meld> currentPath, System.Action<List<Meld>> onFound)
        {
            FindAllSets(tiles, setsNeeded, currentPath, onFound);

            if (setsNeeded >= 3)
            {
                int[][] permutations = new int[][]
                {
                    new int[] { 0, 1, 2 }, new int[] { 0, 2, 1 },
                    new int[] { 1, 0, 2 }, new int[] { 1, 2, 0 },
                    new int[] { 2, 0, 1 }, new int[] { 2, 1, 0 }
                };

                foreach (var p in permutations)
                {
                    int suit147 = p[0];
                    int suit258 = p[1];
                    int suit369 = p[2];

                    if (HasKnittedSuit(tiles, suit147, 0) && HasKnittedSuit(tiles, suit258, 1) && HasKnittedSuit(tiles, suit369, 2))
                    {
                        ModKnittedSuit(tiles, suit147, 0, -1);
                        ModKnittedSuit(tiles, suit258, 1, -1);
                        ModKnittedSuit(tiles, suit369, 2, -1);

                        currentPath.Add(CreateKnittedMeld(suit147, 0));
                        currentPath.Add(CreateKnittedMeld(suit258, 1));
                        currentPath.Add(CreateKnittedMeld(suit369, 2));

                        FindAllSets(tiles, setsNeeded - 3, currentPath, onFound);

                        currentPath.RemoveAt(currentPath.Count - 1);
                        currentPath.RemoveAt(currentPath.Count - 1);
                        currentPath.RemoveAt(currentPath.Count - 1);

                        ModKnittedSuit(tiles, suit147, 0, 1);
                        ModKnittedSuit(tiles, suit258, 1, 1);
                        ModKnittedSuit(tiles, suit369, 2, 1);
                    }
                }
            }
        }

        private static Fan.WaitType AnalyzeWaitType(InternalDecomposition decomp, TileData winTile, int pairIndex)
        {
            if (GetTileIndex(winTile) == pairIndex) return Fan.WaitType.Single;

            foreach (var meld in decomp.AllMelds)
            {
                if (!meld.IsConcealed) continue;

                bool containsWinTile = false;
                foreach(var t in meld.Tiles) if(t.TileSuit == winTile.TileSuit && t.Value == winTile.Value) containsWinTile = true;
                if (!containsWinTile) continue;

                if (meld.Type != MeldType.Chi) continue;

                int v1 = meld.Tiles[0].Value;
                int v2 = meld.Tiles[1].Value;
                int v3 = meld.Tiles[2].Value;
                int wv = winTile.Value;

                if (wv == v2) return Fan.WaitType.Closed; 
                if (wv == 3 && v1 == 1 && v3 == 3) return Fan.WaitType.Edge;
                if (wv == 7 && v1 == 7 && v3 == 9) return Fan.WaitType.Edge;
            }

            return Fan.WaitType.Unknown;
        }

        // 深度优先搜索所有面子组合
        private static void FindAllSets(int[] tiles, int setsNeeded, List<Meld> currentPath, System.Action<List<Meld>> onFound)
        {
            if (setsNeeded == 0)
            {
                for(int i=0; i<34; i++) if(tiles[i] > 0) return;
                onFound(new List<Meld>(currentPath));
                return;
            }

            int first = -1;
            for(int i=0; i<34; i++) if(tiles[i] > 0) { first = i; break; }
            if (first == -1) return;

            if (tiles[first] >= 3)
            {
                tiles[first] -= 3;
                TileData t = CreateTileFromIndex(first);
                var pung = new Meld(MeldType.Pon, new List<TileData> { t, t, t }, -1, true);
                currentPath.Add(pung);
                FindAllSets(tiles, setsNeeded - 1, currentPath, onFound);
                currentPath.RemoveAt(currentPath.Count - 1);
                tiles[first] += 3;
            }

            if (IsSequencePossible(first) && tiles[first+1] > 0 && tiles[first+2] > 0)
            {
                tiles[first]--; tiles[first+1]--; tiles[first+2]--;
                var chi = new Meld(MeldType.Chi, new List<TileData> { CreateTileFromIndex(first), CreateTileFromIndex(first+1), CreateTileFromIndex(first+2) }, -1, true);
                currentPath.Add(chi);
                FindAllSets(tiles, setsNeeded - 1, currentPath, onFound);
                currentPath.RemoveAt(currentPath.Count - 1);
                tiles[first]++; tiles[first+1]++; tiles[first+2]++;
            }
        }

        private static TileData CreateTileFromIndex(int index)
        {
            Suit s = Suit.Man;
            int v = 0;
            if (index < 9) { s = Suit.Man; v = index + 1; }
            else if (index < 18) { s = Suit.Pin; v = index - 9 + 1; }
            else if (index < 27) { s = Suit.Sou; v = index - 18 + 1; }
            else if (index < 31) { s = Suit.Wind; v = index - 27 + 1; }
            else { s = Suit.Dragon; v = index - 31 + 1; }
            return new TileData(s, v, -1);
        }

        /// <summary>
        /// 核心判定：是否胡牌
        /// </summary>
        public static bool IsWin(List<TileData> handTiles, List<Meld> melds, TileData winningTile)
        {
            int[] tileCounts = ConvertToFrequencyArray(handTiles);
            
            // 如果手牌数量是 3n+1 (1,4,7,10,13)，说明 winningTile 还没被包含，需要加 1
            if (handTiles.Count % 3 == 1)
            {
                tileCounts[GetTileIndex(winningTile)]++;
            }
            
            if (melds.Count == 0)
            {
                if (CheckSevenPairs(tileCounts)) return true;
                if (CheckThirteenOrphans(tileCounts)) return true;
                if (CheckQuanBuKao(tileCounts)) return true;
            }

            int setsNeeded = 4 - melds.Count;
            return CheckStandardWin(tileCounts, setsNeeded);
        }

        private static bool CheckStandardWin(int[] tiles, int setsNeeded)
        {
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] >= 2)
                {
                    tiles[i] -= 2;
                    if (CheckSetsWithKnitted(tiles, setsNeeded)) 
                    {
                        tiles[i] += 2; 
                        return true;
                    }
                    tiles[i] += 2;
                }
            }
            return false;
        }

        private static bool CheckSetsWithKnitted(int[] tiles, int setsNeeded)
        {
            if (CheckSets(tiles, setsNeeded)) return true;

            if (setsNeeded >= 3)
            {
                int[][] permutations = new int[][]
                {
                    new int[] { 0, 1, 2 }, new int[] { 0, 2, 1 },
                    new int[] { 1, 0, 2 }, new int[] { 1, 2, 0 },
                    new int[] { 2, 0, 1 }, new int[] { 2, 1, 0 }
                };

                foreach (var p in permutations)
                {
                    int suit147 = p[0];
                    int suit258 = p[1];
                    int suit369 = p[2];

                    if (HasKnittedSuit(tiles, suit147, 0) && HasKnittedSuit(tiles, suit258, 1) && HasKnittedSuit(tiles, suit369, 2))
                    {
                        ModKnittedSuit(tiles, suit147, 0, -1);
                        ModKnittedSuit(tiles, suit258, 1, -1);
                        ModKnittedSuit(tiles, suit369, 2, -1);

                        bool ok = CheckSets(tiles, setsNeeded - 3);

                        ModKnittedSuit(tiles, suit147, 0, 1);
                        ModKnittedSuit(tiles, suit258, 1, 1);
                        ModKnittedSuit(tiles, suit369, 2, 1);

                        if (ok) return true;
                    }
                }
            }

            return false;
        }

        private static bool CheckSets(int[] tiles, int setsNeeded)
        {
            if (setsNeeded == 0) return true;
            int first = -1;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] > 0) { first = i; break; }
            }
            if (first == -1) return false;

            if (tiles[first] >= 3)
            {
                tiles[first] -= 3;
                if (CheckSets(tiles, setsNeeded - 1)) { tiles[first] += 3; return true; }
                tiles[first] += 3;
            }

            if (IsSequencePossible(first) && tiles[first + 1] > 0 && tiles[first + 2] > 0)
            {
                tiles[first]--; tiles[first + 1]--; tiles[first + 2]--;
                if (CheckSets(tiles, setsNeeded - 1)) { tiles[first]++; tiles[first + 1]++; tiles[first + 2]++; return true; }
                tiles[first]++; tiles[first + 1]++; tiles[first + 2]++;
            }
            return false;
        }

        private static bool CheckSevenPairs(int[] tiles)
        {
            int pairs = 0;
            int totalTiles = 0;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                pairs += tiles[i] / 2;
                totalTiles += tiles[i];
            }
            return totalTiles == 14 && pairs == 7;
        }

        private static bool CheckThirteenOrphans(int[] tiles)
        {
            int[] orphanIndices = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int typesCount = 0;
            int totalOrphans = 0;
            int totalTiles = 0;

            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                totalTiles += tiles[i];
            }

            foreach (int i in orphanIndices)
            {
                if (tiles[i] > 0)
                {
                    typesCount++;
                    totalOrphans += tiles[i];
                }
            }

            // 要求：总计14张牌，全都是幺九/字牌，且包含了所有13种类型
            return totalTiles == 14 && totalOrphans == 14 && typesCount == 13;
        }

        private static bool CheckQuanBuKao(int[] tiles)
        {
            int totalTiles = 0;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] > 1) return false; // 全不靠不能有任何对子
                totalTiles += tiles[i];
            }
            if (totalTiles != 14) return false;

            List<int> manVals = new List<int>();
            List<int> pinVals = new List<int>();
            List<int> souVals = new List<int>();
            for (int i = 0; i < 9; i++) if (tiles[i] > 0) manVals.Add(i % 9);
            for (int i = 9; i < 18; i++) if (tiles[i] > 0) pinVals.Add(i % 9);
            for (int i = 18; i < 27; i++) if (tiles[i] > 0) souVals.Add(i % 9);

            if (!IsValidKnittedSuit(manVals) || !IsValidKnittedSuit(pinVals) || !IsValidKnittedSuit(souVals)) return false;

            int[] usedPatterns = new int[3]; 
            if (manVals.Count > 0) usedPatterns[manVals[0] % 3]++;
            if (pinVals.Count > 0) usedPatterns[pinVals[0] % 3]++;
            if (souVals.Count > 0) usedPatterns[souVals[0] % 3]++;

            for (int i = 0; i < 3; i++)
            {
                if (usedPatterns[i] > 1) return false; // 两门花色不能使用同一种147/258/369步进
            }

            return true;
        }

        private static bool IsValidKnittedSuit(List<int> vals)
        {
            if (vals.Count == 0) return true;
            int mod = vals[0] % 3;
            for (int i = 1; i < vals.Count; i++)
            {
                if (vals[i] % 3 != mod) return false;
            }
            return true;
        }

        private static bool IsSequencePossible(int index)
        {
            if (index >= 27) return false;
            int rel = index % 9;
            return rel < 7;
        }

        public static int[] ConvertToFrequencyArray(List<TileData> hand)
        {
            int[] counts = new int[MAX_TILE_INDEX];
            foreach (var t in hand) counts[GetTileIndex(t)]++;
            return counts;
        }

        public static int GetTileIndex(TileData t)
        {
            int baseIdx = 0;
            switch (t.TileSuit)
            {
                case Suit.Man: baseIdx = 0; break;
                case Suit.Pin: baseIdx = 9; break;
                case Suit.Sou: baseIdx = 18; break;
                case Suit.Wind: baseIdx = 27; break; // Wind 1-4 -> 27-30
                case Suit.Dragon: baseIdx = 31; break; // Dragon 1-3 -> 31-33
            }
            return baseIdx + (t.Value - 1);
        }

        public struct WaitDetail
        {
            public TileData WaitTile;
            public int MaxFan;
        }

        /// <summary>
        /// 获取所有打出后能听牌的提示信息
        /// </summary>
        /// <returns>Key: 打出的牌的索引 (GetTileIndex), Value: 听的牌及最大番数列表</returns>
        public static Dictionary<int, List<WaitDetail>> GetWaitHints(List<TileData> hand, List<Meld> melds, WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East, ScoringOptions options = null)
        {
            var hints = new Dictionary<int, List<WaitDetail>>();

            var uniqueHandTiles = new HashSet<int>();
            foreach (var t in hand)
            {
                uniqueHandTiles.Add(GetTileIndex(t));
            }

            foreach (var discardIndex in uniqueHandTiles)
            {
                var handAfterDiscard = new List<TileData>(hand);

                for (int i = 0; i < handAfterDiscard.Count; i++)
                {
                    if (GetTileIndex(handAfterDiscard[i]) == discardIndex)
                    {
                        handAfterDiscard.RemoveAt(i);
                        break;
                    }
                }

                var waitDetails = new List<WaitDetail>();

                for (int i = 0; i < MAX_TILE_INDEX; i++)
                {
                    var waitTile = CreateTileFromIndex(i);
                    if (IsWin(handAfterDiscard, melds, waitTile))
                    {
                        if (CheckWinWithFan(handAfterDiscard, melds, waitTile, false, out int totalFan, out _, roundWind, seatWind, options))
                        {
                            waitDetails.Add(new WaitDetail { WaitTile = waitTile, MaxFan = totalFan });
                        }
                    }
                }

                if (waitDetails.Count > 0)
                {
                    hints[discardIndex] = waitDetails;
                }
            }

            return hints;
        }
    }
}