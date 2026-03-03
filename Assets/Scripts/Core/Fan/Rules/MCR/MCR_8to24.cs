using MahjongGame.Core;
using MahjongGame.Core.Fan;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace MahjongGame.Core.Fan.Rules
{
    // ============================================================
    //  国标麻将 (MCR) 番种规则 — 8~24 番
    // ============================================================

    #region === 8番 ===

    // === 8番：杠上开花 ===
    [FanRule("kong_win", 8)]
    public class Fan_KongWin : FanRule
    {
        public override string Name => "杠上开花";
        public override int GetMatchCount(FanContext ctx) => ctx.IsKongWin ? 1 : 0;
        public override string[] ExcludedRuleIds => new[] { "self_draw" };
    }

    // === 8番：三色三同顺 (三种花色相同序数的顺子) ===
    [FanRule("triple_chow", 8)]
    public class Fan_TripleChow : FanRule
    {
        public override string Name => "三色三同顺";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = sequences.GroupBy(s => s.FirstTile.Value);
            foreach (var group in grouped)
            {
                var suits = group.Select(s => s.FirstTile.TileSuit).Distinct().ToList();
                if (suits.Contains(Suit.Man) && suits.Contains(Suit.Pin) && suits.Contains(Suit.Sou)) return 1;
            }
            return 0;
        }
    }

    // === 8番：花龙 (三种花色序数相接的顺子 123, 456, 789) ===
    [FanRule("mixed_straight", 8)]
    public class Fan_MixedStraight : FanRule
    {
        public override string Name => "花龙";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            if (sequences.Count < 3) return 0;

            for (int i = 0; i < sequences.Count; i++)
            {
                for (int j = 0; j < sequences.Count; j++)
                {
                    if (i == j) continue;
                    for (int k = 0; k < sequences.Count; k++)
                    {
                        if (i == k || j == k) continue;
                        var s1 = sequences[i].FirstTile;
                        var s2 = sequences[j].FirstTile;
                        var s3 = sequences[k].FirstTile;

                        var vals = new[] { s1.Value, s2.Value, s3.Value };
                        System.Array.Sort(vals);
                        if (vals[0] == 1 && vals[1] == 4 && vals[2] == 7)
                        {
                            var suits = new HashSet<Suit> { s1.TileSuit, s2.TileSuit, s3.TileSuit };
                            if (suits.Count == 3 && !suits.Contains(Suit.Wind) && !suits.Contains(Suit.Dragon)) return 1;
                        }
                    }
                }
            }
            return 0;
        }
    }

    // === 8番：妙手回春 (自摸最后一张牌) ===
    [FanRule("last_tile_draw", 8)]
    public class Fan_LastTileDraw : FanRule
    {
        public override string Name => "妙手回春";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "self_draw" };
        public override int GetMatchCount(FanContext ctx) => ctx.IsLastWallTileWin ? 1 : 0;
    }

    // === 8番：海底捞月 (和最后一张打出的牌) ===
    [FanRule("last_tile_discard", 8)]
    public class Fan_LastTileDiscard : FanRule
    {
        public override string Name => "海底捞月";
        public override int GetMatchCount(FanContext ctx) => ctx.IsLastDiscardWin ? 1 : 0;
    }

    // === 8番：抢杠胡 ===
    [FanRule("rob_kong", 8)]
    public class Fan_RobbingKong : FanRule
    {
        public override string Name => "抢杠胡";
        public override int GetMatchCount(FanContext ctx) => ctx.IsRobKongWin ? 1 : 0;
    }

    // === 8番：推不倒 (全由对称牌组成) ===
    [FanRule("reversible", 8)]
    public class Fan_Reversible : FanRule
    {
        public override string Name => "推不倒";
        public override int GetMatchCount(FanContext ctx)
        {
            var reversibleIndices = new HashSet<int> { 9, 10, 11, 12, 13, 16, 17, 19, 21, 22, 23, 25, 26, 33 };
            for (int i = 0; i < 34; i++)
            {
                if (ctx.HandCounts[i] > 0 && !reversibleIndices.Contains(i)) return 0;
            }
            foreach (var m in ctx.FixedMelds)
            {
                foreach (var t in m.Tiles)
                {
                    if (!reversibleIndices.Contains(MahjongLogic.GetTileIndex(t))) return 0;
                }
            }
            return 1;
        }
    }

    // === 8番：三色三节高 (三种花色相同序数递增的刻子) ===
    [FanRule("mixed_shifted_pungs", 8)]
    public class Fan_MixedShiftedPungs : FanRule
    {
        public override string Name => "三色三节高";
        public override int GetMatchCount(FanContext ctx)
        {
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.IsPungOrKong).ToList();
            if (pungs.Count < 3) return 0;

            for (int i = 0; i < pungs.Count; i++)
            {
                for (int j = 0; j < pungs.Count; j++)
                {
                    if (i == j) continue;
                    for (int k = 0; k < pungs.Count; k++)
                    {
                        if (i == k || j == k) continue;
                        var p1 = pungs[i].FirstTile;
                        var p2 = pungs[j].FirstTile;
                        var p3 = pungs[k].FirstTile;

                        var suits = new HashSet<Suit> { p1.TileSuit, p2.TileSuit, p3.TileSuit };
                        if (suits.Count != 3 || suits.Contains(Suit.Wind) || suits.Contains(Suit.Dragon)) continue;

                        var vals = new List<int> { p1.Value, p2.Value, p3.Value };
                        vals.Sort();
                        if (vals[1] - vals[0] == 1 && vals[2] - vals[1] == 1) return 1;
                    }
                }
            }
            return 0;
        }
    }

    // === 8番：三色三同刻 (三种花色相同序数的刻子) ===
    [FanRule("mixed_triple_pung", 8)]
    public class Fan_MixedTriplePung : FanRule
    {
        public override string Name => "三色三同刻";
        public override int GetMatchCount(FanContext ctx)
        {
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.IsPungOrKong).ToList();
            var grouped = pungs.GroupBy(p => p.FirstTile.Value);
            foreach (var group in grouped)
            {
                if (group.Key < 1 || group.Key > 9) continue;
                var suits = group.Select(p => p.FirstTile.TileSuit).Distinct().ToList();
                if (suits.Contains(Suit.Man) && suits.Contains(Suit.Pin) && suits.Contains(Suit.Sou)) return 1;
            }
            return 0;
        }
    }

    // === 8番：无番和 ===
    [FanRule("no_points_win", 8)]
    public class Fan_NoPointsWin : FanRule
    {
        public override string Name => "无番和";
        public override int GetMatchCount(FanContext ctx)
        {
            return 0;
        }
    }

    #endregion

    #region === 12番 ===

    // === 12番：大于五 (由6-9的序数牌组成) ===
    [FanRule("greater_five", 12)]
    public class Fan_GreaterFive : FanRule
    {
        public override string Name => "大于五";
        public override int GetMatchCount(FanContext ctx)
        {
            static void Check(TileData t, ref bool fail)
            {
                if (t.TileType != TileType.Number || t.Value <= 5) fail = true;
            }
            bool isFail = false;
            foreach (var t in ctx.HandTiles) Check(t, ref isFail);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t, ref isFail);
            if (ctx.WinningTile != null) Check(ctx.WinningTile, ref isFail);
            return isFail ? 0 : 1;
        }
    }

    // === 12番：小于五 (由1-4的序数牌组成) ===
    [FanRule("lesser_five", 12)]
    public class Fan_LesserFive : FanRule
    {
        public override string Name => "小于五";
        public override int GetMatchCount(FanContext ctx)
        {
            static void Check(TileData t, ref bool fail)
            {
                if (t.TileType != TileType.Number || t.Value >= 5) fail = true;
            }
            bool isFail = false;
            foreach (var t in ctx.HandTiles) Check(t, ref isFail);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t, ref isFail);
            if (ctx.WinningTile != null) Check(ctx.WinningTile, ref isFail);
            return isFail ? 0 : 1;
        }
    }

    // === 12番：组合龙 ===
    [FanRule("knitted_straight", 12)]
    public class Fan_KnittedStraight : FanRule
    {
        public override string Name => "组合龙";
        public override int Priority => 70;

        public override int GetMatchCount(FanContext ctx)
        {
            // 简化检测：手牌中是否有齐备的 147、258、369 且跨三门花色。
            int[] counts = new int[34];
            foreach (var t in ctx.HandTiles) counts[MahjongLogic.GetTileIndex(t)]++;
            if (ctx.WinningTile != null) counts[MahjongLogic.GetTileIndex(ctx.WinningTile)]++;

            int manPattern = -1, pinPattern = -1, souPattern = -1;

            bool HasPattern(int baseIdx, int p)
            {
                return counts[baseIdx + p] > 0 && counts[baseIdx + p + 3] > 0 && counts[baseIdx + p + 6] > 0;
            }

            for(int i=0; i<3; i++) if (HasPattern(0, i)) manPattern = i;
            for(int i=0; i<3; i++) if (HasPattern(9, i)) pinPattern = i;
            for(int i=0; i<3; i++) if (HasPattern(18, i)) souPattern = i;

            if (manPattern != -1 && pinPattern != -1 && souPattern != -1)
            {
                if (manPattern != pinPattern && pinPattern != souPattern && manPattern != souPattern)
                {
                    return 1;
                }
            }
            return 0;
        }
    }

    // === 12番：全不靠 ===
    [FanRule("lesser_knitted_hand", 12)]
    public class Fan_LesserKnittedHand : FanRule
    {
        public override string Name => "全不靠";
        public override int Priority => 70;
        public override string[] ExcludedRuleIds => new[] { "all_types", "single_wait", "concealed_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.FixedMelds.Count > 0) return 0;
            if (ctx.Decomposition.AllMelds.Count > 0) return 0;

            int[] counts = new int[34];
            foreach (var t in ctx.HandTiles) counts[MahjongLogic.GetTileIndex(t)]++;
            if (ctx.WinningTile != null) counts[MahjongLogic.GetTileIndex(ctx.WinningTile)]++;

            int totalTiles = 0;
            for (int i = 0; i < 34; i++)
            {
                if (counts[i] > 1) return 0;
                totalTiles += counts[i];
            }
            if (totalTiles != 14) return 0;

            List<int> manVals = new List<int>();
            List<int> pinVals = new List<int>();
            List<int> souVals = new List<int>();
            for (int i = 0; i < 9; i++) if (counts[i] > 0) manVals.Add(i % 9);
            for (int i = 9; i < 18; i++) if (counts[i] > 0) pinVals.Add(i % 9);
            for (int i = 18; i < 27; i++) if (counts[i] > 0) souVals.Add(i % 9);

            if (!IsValidKnittedSuit(manVals) || !IsValidKnittedSuit(pinVals) || !IsValidKnittedSuit(souVals)) return 0;

            int[] usedPatterns = new int[3];
            if (manVals.Count > 0) usedPatterns[manVals[0] % 3]++;
            if (pinVals.Count > 0) usedPatterns[pinVals[0] % 3]++;
            if (souVals.Count > 0) usedPatterns[souVals[0] % 3]++;

            for (int i = 0; i < 3; i++)
            {
                if (usedPatterns[i] > 1) return 0;
            }

            return 1;
        }

        private bool IsValidKnittedSuit(List<int> vals)
        {
            if (vals.Count == 0) return true;
            int mod = vals[0] % 3;
            for (int i = 1; i < vals.Count; i++)
            {
                if (vals[i] % 3 != mod) return false;
            }
            return true;
        }
    }

    // === 12番：三风刻 ===
    [FanRule("three_wind_pungs", 12)]
    public class Fan_ThreeWindPungs : FanRule
    {
        public override string Name => "三风刻";
        public override int Priority => 80;

        public override int GetMatchCount(FanContext ctx)
        {
            int count = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.FirstTile.TileSuit == Suit.Wind) count++;
            }
            return count == 3 ? 1 : 0;
        }
    }

    #endregion

    #region === 16番 ===

    // === 16番：全带五 (每副面子和雀头都含有5的序数牌) ===
    [FanRule("all_fives", 16)]
    public class Fan_AllFives : FanRule
    {
        public override string Name => "全带五";
        public override int Priority => 50;
        public override string[] ExcludedRuleIds => new[] { "all_simples", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0; // 必须是标准4面子牌型

            foreach (var m in ctx.Decomposition.AllMelds)
            {
                bool hasFive = false;
                foreach (var t in m.Tiles)
                {
                    if (t.TileType == TileType.Number && t.Value == 5) hasFive = true;
                }
                if (!hasFive) return 0;
            }

            bool pairHasFive = false;
            foreach (var t in ctx.Decomposition.Pair)
            {
                if (t.TileType == TileType.Number && t.Value == 5) pairHasFive = true;
            }
            if (!pairHasFive) return 0;

            return 1;
        }
    }

    // === 16番：清龙 (一种花色序数相连的 1-9 的三副顺子) ===
    [FanRule("pure_straight", 16)]
    public class Fan_PureStraight : FanRule
    {
        public override string Name => "清龙";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var groupedBySuit = sequences.GroupBy(s => s.FirstTile.TileSuit);
            foreach (var group in groupedBySuit)
            {
                var vals = group.Select(s => s.FirstTile.Value).ToList();
                if (vals.Contains(1) && vals.Contains(4) && vals.Contains(7)) return 1;
            }
            return 0;
        }
    }

    // === 16番：三暗刻 ===
    [FanRule("three_concealed_pungs", 16)]
    public class Fan_ThreeConcealedPungs : FanRule
    {
        public override string Name => "三暗刻";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "two_concealed_pungs" };
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.Decomposition.AllMelds.Count(m => m.IsPungOrKong && m.IsConcealed);
            return (count == 3) ? 1 : 0;
        }
    }

    // === 16番：一色三步高 ===
    [FanRule("pure_shifted_chows", 16)]
    public class Fan_PureShiftedChows : FanRule
    {
        public override string Name => "一色三步高";
        public override int Priority => 75;

        public override int GetMatchCount(FanContext ctx)
        {
            var chows = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            if (chows.Count < 3) return 0;

            var grouped = chows.GroupBy(c => c.FirstTile.TileSuit);
            foreach (var g in grouped)
            {
                if (g.Key == Suit.Wind || g.Key == Suit.Dragon) continue;
                var vals = g.Select(c => c.FirstTile.Value).OrderBy(v => v).ToList();
                for (int i = 0; i < vals.Count; i++)
                {
                    for (int j = i + 1; j < vals.Count; j++)
                    {
                        for (int k = j + 1; k < vals.Count; k++)
                        {
                            int d1 = vals[j] - vals[i];
                            int d2 = vals[k] - vals[j];
                            if (d1 == d2 && (d1 == 1 || d1 == 2)) return 1;
                        }
                    }
                }
            }
            return 0;
        }
    }

    // === 16番：三色双龙会 ===
    [FanRule("mixed_double_reversible", 16)]
    public class Fan_MixedDoubleReversible : FanRule
    {
        public override string Name => "三色双龙会";
        public override int Priority => 75;
        public override string[] ExcludedRuleIds => new[] { "mixed_double_sequence", "all_chows", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            if (ctx.Decomposition.AllMelds.Any(m => m.Type != MeldType.Chi)) return 0;
            if (ctx.Decomposition.Pair.Count != 1 || ctx.Decomposition.Pair[0].Value != 5) return 0;

            var chows = ctx.Decomposition.AllMelds;
            var suit1 = chows[0].FirstTile.TileSuit;
            if (suit1 == Suit.Wind || suit1 == Suit.Dragon) return 0;

            bool ok = false;
            // 简单校验：2个123，2个789。其中一对123和789同花色，另一对123和789同花色，雀头为第三种花色的5。
            // 完整国标：两种花色各一个老少副，第三种花色5作将。
            var suitPairs = chows.GroupBy(c => c.FirstTile.TileSuit).ToList();
            if (suitPairs.Count == 2)
            {
                var s1 = suitPairs[0];
                var s2 = suitPairs[1];
                if (s1.Count() == 2 && s2.Count() == 2)
                {
                    var v1 = s1.Select(c => c.FirstTile.Value).OrderBy(v => v).ToList();
                    var v2 = s2.Select(c => c.FirstTile.Value).OrderBy(v => v).ToList();
                    if (v1[0] == 1 && v1[1] == 7 && v2[0] == 1 && v2[1] == 7)
                    {
                        var pairSuit = ctx.Decomposition.Pair[0].TileSuit;
                        if (pairSuit != s1.Key && pairSuit != s2.Key && pairSuit != Suit.Wind && pairSuit != Suit.Dragon) ok = true;
                    }
                }
            }
            return ok ? 1 : 0;
        }
    }

    #endregion

    #region === 24番 ===

    // === 24番：清一色 (由同一种花色数牌组成) ===
    [FanRule("full_flush", 24)]
    public class Fan_FullFlush : FanRule
    {
        public override string Name => "清一色";
        public override string Description => "由同一种花色数牌组成";

        public override int Priority => 50;
        // 清一色必然无字、缺一门
        public override string[] ExcludedRuleIds => new[] { "no_honors", "one_voided_suit" };

        public override int GetMatchCount(FanContext ctx)
        {
            Suit baseSuit;
            if (ctx.HandTiles.Count > 0) baseSuit = ctx.HandTiles[0].TileSuit;
            else if (ctx.FixedMelds.Count > 0) baseSuit = ctx.FixedMelds[0].FirstTile.TileSuit;
            else return 0;

            if (baseSuit == Suit.Wind || baseSuit == Suit.Dragon) return 0;

            foreach (var t in ctx.HandTiles) if (t.TileSuit != baseSuit) return 0;
            foreach (var m in ctx.FixedMelds) if (m.FirstTile.TileSuit != baseSuit) return 0;
            if (ctx.WinningTile != null && ctx.WinningTile.TileSuit != baseSuit) return 0;

            return 1;
        }
    }

    // === 24番：七对子 (由7个对子组成的胡牌) ===
    [FanRule("seven_pairs", 24)]
    public class Fan_SevenPairs : FanRule
    {
        public override string Name => "七对子";
        public override int Priority => 50;
        public override string[] ExcludedRuleIds => new[] { "single_wait", "fully_concealed", "concealed_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.Pair.Count == 7) return 1;
            return 0;
        }
    }

    // === 24番：一色三节高 (一种花色序数递增的三副刻子) ===
    [FanRule("three_pure_shifted_pungs", 24)]
    public class Fan_ThreePureShiftedPungs : FanRule
    {
        public override string Name => "一色三节高";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "pure_triple_chow" };

        public override int GetMatchCount(FanContext ctx)
        {
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.IsPungOrKong).ToList();
            var grouped = pungs.GroupBy(p => p.FirstTile.TileSuit);
            foreach (var g in grouped)
            {
                if (g.Key == Suit.Wind || g.Key == Suit.Dragon) continue;
                var vals = g.Select(p => p.FirstTile.Value).Distinct().OrderBy(v => v).ToList();
                int consec = 1, maxConsec = 1;
                for (int i = 1; i < vals.Count; i++)
                {
                    if (vals[i] == vals[i - 1] + 1) { consec++; if (consec > maxConsec) maxConsec = consec; }
                    else consec = 1;
                }
                if (maxConsec >= 3) return 1;
            }
            return 0;
        }
    }

    // === 24番：一色三同顺 (一种花色序数相同的三副顺子) ===
    [FanRule("pure_triple_chow", 24)]
    public class Fan_PureTripleChow : FanRule
    {
        public override string Name => "一色三同顺";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "three_pure_shifted_pungs", "pure_double_sequence" };

        public override int GetMatchCount(FanContext ctx)
        {
            var chows = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = chows.GroupBy(c => new { c.FirstTile.TileSuit, c.FirstTile.Value });
            foreach (var group in grouped)
            {
                if (group.Count() >= 3) return 1;
            }
            return 0;
        }
    }

    // === 24番：七星不靠 ===
    [FanRule("seven_star_knitted_hand", 24)]
    public class Fan_SevenStarKnittedHand : FanRule
    {
        public override string Name => "七星不靠";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "knitted_straight", "all_types", "single_wait", "concealed_hand", "lesser_knitted_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.FixedMelds.Count > 0) return 0;
            if (ctx.Decomposition.AllMelds.Count > 0) return 0; // 不是标准胡牌牌型

            int[] counts = new int[34];
            foreach (var t in ctx.HandTiles) counts[MahjongLogic.GetTileIndex(t)]++;
            if (ctx.WinningTile != null) counts[MahjongLogic.GetTileIndex(ctx.WinningTile)]++;

            int totalTiles = 0;
            for (int i = 0; i < 34; i++)
            {
                if (counts[i] > 1) return 0; // 必须全部是单张
                totalTiles += counts[i];
            }
            if (totalTiles != 14) return 0;

            // 检查七星是否齐备
            for (int i = 27; i < 34; i++)
            {
                if (counts[i] != 1) return 0;
            }

            // 检查另外7张散牌是否符合全不靠规则
            List<int> manVals = new List<int>();
            List<int> pinVals = new List<int>();
            List<int> souVals = new List<int>();
            for (int i = 0; i < 9; i++) if (counts[i] > 0) manVals.Add(i % 9);
            for (int i = 9; i < 18; i++) if (counts[i] > 0) pinVals.Add(i % 9);
            for (int i = 18; i < 27; i++) if (counts[i] > 0) souVals.Add(i % 9);

            if (!IsValidKnittedSuit(manVals) || !IsValidKnittedSuit(pinVals) || !IsValidKnittedSuit(souVals)) return 0;

            int[] usedPatterns = new int[3];
            if (manVals.Count > 0) usedPatterns[manVals[0] % 3]++;
            if (pinVals.Count > 0) usedPatterns[pinVals[0] % 3]++;
            if (souVals.Count > 0) usedPatterns[souVals[0] % 3]++;

            for (int i = 0; i < 3; i++)
            {
                if (usedPatterns[i] > 1) return 0; // 两门花色不能使用同一种147/258/369步进
            }

            return 1;
        }

        private bool IsValidKnittedSuit(List<int> vals)
        {
            if (vals.Count == 0) return true;
            int mod = vals[0] % 3;
            for (int i = 1; i < vals.Count; i++)
            {
                if (vals[i] % 3 != mod) return false;
            }
            return true;
        }
    }

    // === 24番：全大 ===
    [FanRule("upper_tiles", 24)]
    public class Fan_UpperTiles : FanRule
    {
        public override string Name => "全大";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "greater_five", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            bool ok = true;
            void Check(TileData t) { if (t.TileType != TileType.Number || t.Value < 7) ok = false; }

            foreach (var t in ctx.HandTiles) Check(t);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t);
            if (ctx.WinningTile != null) Check(ctx.WinningTile);

            return ok ? 1 : 0;
        }
    }

    // === 24番：全中 ===
    [FanRule("middle_tiles", 24)]
    public class Fan_MiddleTiles : FanRule
    {
        public override string Name => "全中";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "all_simples", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            bool ok = true;
            void Check(TileData t) { if (t.TileType != TileType.Number || t.Value < 4 || t.Value > 6) ok = false; }

            foreach (var t in ctx.HandTiles) Check(t);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t);
            if (ctx.WinningTile != null) Check(ctx.WinningTile);

            return ok ? 1 : 0;
        }
    }

    // === 24番：全小 ===
    [FanRule("lower_tiles", 24)]
    public class Fan_LowerTiles : FanRule
    {
        public override string Name => "全小";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "lesser_five", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            bool ok = true;
            void Check(TileData t) { if (t.TileType != TileType.Number || t.Value > 3) ok = false; }

            foreach (var t in ctx.HandTiles) Check(t);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t);
            if (ctx.WinningTile != null) Check(ctx.WinningTile);

            return ok ? 1 : 0;
        }
    }

    // === 24番：全双刻 ===
    [FanRule("all_even_pungs", 24)]
    public class Fan_AllEvenPungs : FanRule
    {
        public override string Name => "全双刻";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "all_simples", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type == MeldType.Chi) return 0;
                if (m.FirstTile.TileType != TileType.Number || m.FirstTile.Value % 2 != 0) return 0;
            }
            foreach (var t in ctx.Decomposition.Pair)
            {
                if (t.TileType != TileType.Number || t.Value % 2 != 0) return 0;
            }
            return 1;
        }
    }

    #endregion
}
