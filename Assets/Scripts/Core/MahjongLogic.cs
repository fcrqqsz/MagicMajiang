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

            // 1. 先判定牌型是否成胡 (基本形状)
            if (!IsWin(hand, melds, winTile))
            {
                return false;
            }

            // 2. 创建算番上下文
            // 假设圈风是东(1)，门风是东(1)，实际要从 GameManager 传进来
            var ctx = new Fan.FanContext(hand, melds, winTile, isSelfDraw, Suit.Wind, Suit.Wind); // 临时写死风

            // 3. 计算番数
            totalFan = _calculator.CalculateTotalFan(ctx, out fanDetails);

            // 4. 8番起胡
            if (totalFan < 8)
            {
                Debug.Log($"牌型成立，但番数不足 (当前: {totalFan})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 核心判定：是否胡牌
        /// </summary>
        /// <param name="handTiles">立牌 (手中的牌)</param>
        /// <param name="melds">副露 (吃碰杠的牌)</param>
        /// <param name="winningTile">最后拿到的那张牌 (自摸或点炮)</param>
        /// <returns>是否构成胡牌型</returns>
        public static bool IsWin(List<TileData> handTiles, List<Meld> melds, TileData winningTile)
        {
            // 1. 数据转换：将手牌对象转为 int[34] 频率数组
            int[] tileCounts = ConvertToFrequencyArray(handTiles);
            
            // 把最后一张牌加进去一起算
            int winIndex = GetTileIndex(winningTile);
            tileCounts[winIndex]++;

            // 2. 特殊牌型判定
            if (CheckSevenPairs(tileCounts)) return true; // 七对子 (国标24番)
            if (CheckThirteenOrphans(tileCounts)) return true; // 十三幺 (国标88番)
            // if (CheckKnitted(tileCounts)) return true; // 组合龙/全不靠 (国标特色)

            // 3. 标准牌型判定 (4面子 + 1雀头)
            // 面子包括：顺子(Shunzi) 或 刻子(Kezi)
            // 雀头：对子(Pair)
            
            // 计算当前还需要凑几个面子。
            // 标准胡牌是 4组 + 1对。
            // 已经有的副露(melds)算作完成的面子。
            int setsNeeded = 4 - melds.Count;
            
            return CheckStandardWin(tileCounts, setsNeeded);
        }

        // --- 递归回溯算法 ---

        // 修正后的标准胡牌算法入口
        /// <summary>
        /// 算法入口：检查是否符合 N面子 + 1雀头
        /// </summary>
        /// <param name="tiles">牌的频率数组 (int[34])</param>
        /// <param name="setsNeeded">需要凑齐的面子数量 (4 - 副露数)</param>
        private static bool CheckStandardWin(int[] tiles, int setsNeeded)
        {
            // 1. 穷举雀头 (Pair)
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                // 如果这张牌 >= 2张，尝试把它当作雀头
                if (tiles[i] >= 2)
                {
                    tiles[i] -= 2; // 扣除雀头

                    // 2. 检查剩下的牌能否组成指定数量的面子
                    if (CheckSets(tiles, setsNeeded)) 
                    {
                        // 记得回溯 (虽然只读判定不需要，但保持良好习惯)
                        tiles[i] += 2; 
                        return true;
                    }

                    tiles[i] += 2; // 回溯，试下一张做雀头
                }
            }
            return false;
        }

        // 递归检查面子
        private static bool CheckSets(int[] tiles, int setsNeeded)
        {
            // 递归终点：不需要面子了，检查手里是不是空了
            if (setsNeeded == 0) return true;

            // 找到手里第一张存在的牌
            int first = -1;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] > 0) { first = i; break; }
            }
            
            // 如果没牌了但还需要面子，说明失败
            if (first == -1) return false;

            // 尝试 1: 组成刻子 (AAA)
            if (tiles[first] >= 3)
            {
                tiles[first] -= 3;
                if (CheckSets(tiles, setsNeeded - 1)) 
                {
                    tiles[first] += 3; // 回溯
                    return true;
                }
                tiles[first] += 3;
            }

            // 尝试 2: 组成顺子 (ABC)
            // 前提：是数牌，且不能涉及跨花色(例如9万1筒)，且后面两张牌都有
            if (IsSequencePossible(first) && tiles[first + 1] > 0 && tiles[first + 2] > 0)
            {
                tiles[first]--; 
                tiles[first + 1]--; 
                tiles[first + 2]--;
                
                if (CheckSets(tiles, setsNeeded - 1)) 
                {
                    tiles[first]++; tiles[first + 1]++; tiles[first + 2]++; // 回溯
                    return true;
                }
                
                tiles[first]++; tiles[first + 1]++; tiles[first + 2]++;
            }

            // 既组不了刻子，也组不了顺子，这条路死胡同
            return false;
        }

        // --- 辅助函数 ---

        // 检查剩余的牌是否只剩一对
        private static bool HasPair(int[] tiles)
        {
            int pairCount = 0;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] == 2) pairCount++;
                else if (tiles[i] != 0) return false; // 还有杂牌
            }
            return pairCount == 1;
        }

        // 七对子判定 (不允许副露)
        private static bool CheckSevenPairs(int[] tiles)
        {
            int pairs = 0;
            for (int i = 0; i < MAX_TILE_INDEX; i++)
            {
                if (tiles[i] == 2) pairs++;
                else if (tiles[i] != 0 && tiles[i] != 4) return false; // 七对子不能有单牌 (4张算2对的情况国标通常算暗杠或特殊处理，这里简化)
            }
            return pairs == 7;
        }

        // 十三幺判定 (1,9,字牌 各一张 + 其中一张成对)
        private static bool CheckThirteenOrphans(int[] tiles)
        {
            // ... (逻辑较长，此处略，原理同上) ...
            return false; 
        }

        private static bool IsSequencePossible(int index)
        {
            // 字牌不能顺
            if (index >= 27) return false;
            // 风/箭牌边界逻辑：
            // 万子 0-8: 7,8 不能开头
            // 筒子 9-17: 16,17 不能开头
            // 索子 18-26: 25,26 不能开头
            int rel = index % 9;
            return rel < 7;
        }

        // 转换工具
        public static int[] ConvertToFrequencyArray(List<TileData> hand)
        {
            int[] counts = new int[MAX_TILE_INDEX];
            foreach (var t in hand) counts[GetTileIndex(t)]++;
            return counts;
        }

        public static int GetTileIndex(TileData t)
        {
            // 简单的映射逻辑
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