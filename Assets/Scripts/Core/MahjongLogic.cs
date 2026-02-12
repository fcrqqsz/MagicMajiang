using System.Collections.Generic;
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
        public static bool CheckWinWithFan(List<TileData> hand, List<Meld> melds, TileData winTile, bool isSelfDraw, out int totalFan, out List<string> fanDetails)
        {
            totalFan = 0;
            fanDetails = null;

            // 1. 获取所有胡牌拆解方案
            var decompositions = GetAllDecompositions(hand, melds, winTile, isSelfDraw);
            if (decompositions.Count == 0) return false;

            int maxFan = -1;
            List<string> bestDetails = null;

            // 2. 遍历所有方案，选番数最高的
            foreach (var decomp in decompositions)
            {
                var ctx = new Fan.FanContext(hand, melds, winTile, isSelfDraw, Suit.Wind, Suit.Wind, decomp);
                ctx.Wait = decomp.Wait;

                int currentFan = _calculator.CalculateTotalFan(ctx, out List<string> currentDetails);
                if (currentFan > maxFan)
                {
                    maxFan = currentFan;
                    bestDetails = currentDetails;
                }
            }

            // 3. 8番起胡校验
            if (maxFan < 8) return false;

            totalFan = maxFan;
            fanDetails = bestDetails;
            return true;
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
            pool.Add(winTile);

            int[] tileCounts = ConvertToFrequencyArray(pool);

            // --- 1. 特殊形：七对子 (不计副露) ---
            if (melds.Count == 0 && CheckSevenPairs(tileCounts))
            {
                var decomp = new InternalDecomposition();
                // 七对子的拆解不符合“面子”定义，我们将其存入单独的特殊结构或用对子填充 AllMelds
                for(int i=0; i<34; i++)
                {
                    if (tileCounts[i] >= 2)
                    {
                        TileData t = CreateTileFromIndex(i);
                        // 为了兼容现有规则，我们暂时不把七对子放入 AllMelds，
                        // 或者放入特殊的 7 个 Pair。
                        // 这里我们仅标记其 WaitType
                        decomp.Pair.Add(t); // 简单填充
                    }
                }
                decomp.Wait = Fan.WaitType.Single; // 七对子必然是单钓
                results.Add(decomp);
            }

            // --- 2. 特殊形：十三幺 (不计副露) ---
            if (melds.Count == 0 && CheckThirteenOrphans(tileCounts))
            {
                var decomp = new InternalDecomposition();
                decomp.Wait = Fan.WaitType.Single; // 十三幺必然是单钓 (听十三面或其中一张)
                results.Add(decomp);
            }

            // --- 3. 标准形：4面子 + 1雀头 ---
            int setsNeeded = 4 - melds.Count;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tileCounts[i] >= 2)
                {
                    tileCounts[i] -= 2;
                    var currentPath = new List<Meld>();
                    FindAllSets(tileCounts, setsNeeded, currentPath, (completedSets) =>
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
            tileCounts[GetTileIndex(winningTile)]++;
            
            if (CheckSevenPairs(tileCounts)) return true;
            if (CheckThirteenOrphans(tileCounts)) return true;

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
                    if (CheckSets(tiles, setsNeeded)) 
                    {
                        tiles[i] += 2; 
                        return true;
                    }
                    tiles[i] += 2;
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
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] == 2) pairs++;
                else if (tiles[i] != 0 && tiles[i] != 4) return false;
            }
            return pairs == 7;
        }

        private static bool CheckThirteenOrphans(int[] tiles)
        {
            return false; 
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
    }
}