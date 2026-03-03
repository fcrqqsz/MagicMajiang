using MahjongGame.Core;
using MahjongGame.Core.Fan;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace MahjongGame.Core.Fan.Rules
{
    // ============================================================
    //  国标麻将 (MCR) 番种规则 — 32+ 番
    // ============================================================

    #region === 32番 ===

    // === 32番：一色四步高 ===
    [FanRule("four_pure_shifted_chows", 32)]
    public class Fan_FourPureShiftedChows : FanRule
    {
        public override string Name => "一色四步高";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "pure_shifted_chows", "pure_double_sequence" };

        public override int GetMatchCount(FanContext ctx)
        {
            var chows = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            if (chows.Count < 4) return 0;
            var suit = chows[0].FirstTile.TileSuit;
            if (suit == Suit.Wind || suit == Suit.Dragon) return 0;
            foreach (var m in chows) if (m.FirstTile.TileSuit != suit) return 0;

            var vals = chows.Select(c => c.FirstTile.Value).OrderBy(v => v).ToList();
            int d1 = vals[1] - vals[0];
            int d2 = vals[2] - vals[1];
            int d3 = vals[3] - vals[2];
            if (d1 == d2 && d2 == d3 && (d1 == 1 || d1 == 2)) return 1;
            return 0;
        }
    }

    // === 32番：三杠 ===
    [FanRule("three_kongs", 32)]
    public class Fan_ThreeKongs : FanRule
    {
        public override string Name => "三杠";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "two_concealed_kongs", "two_melded_kongs", "melded_kong", "concealed_kong" };

        public override int GetMatchCount(FanContext ctx)
        {
            int kongs = ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Concealed || m.Type == MeldType.Kan_Added);
            return kongs == 3 ? 1 : 0;
        }
    }

    // === 32番：混幺九 ===
    [FanRule("mixed_terminals", 32)]
    public class Fan_MixedTerminals : FanRule
    {
        public override string Name => "混幺九";
        public override int Priority => 80;
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "terminal_honor_pung", "outside_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;
            bool hasWord = false;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type == MeldType.Chi || m.Type == MeldType.Knitted) return 0;
                var t = m.FirstTile;
                if (t.TileType == TileType.Number && (t.Value != 1 && t.Value != 9)) return 0;
                if (t.TileType == TileType.Word) hasWord = true;
            }
            foreach (var t in ctx.Decomposition.Pair)
            {
                if (t.TileType == TileType.Number && (t.Value != 1 && t.Value != 9)) return 0;
                if (t.TileType == TileType.Word) hasWord = true;
            }
            return hasWord ? 1 : 0;
        }
    }

    #endregion

    #region === 48番 ===

    // === 48番：一色四同顺 ===
    [FanRule("quadruple_chow", 48)]
    public class Fan_QuadrupleChow : FanRule
    {
        public override string Name => "一色四同顺";
        public override int Priority => 85;
        public override string[] ExcludedRuleIds => new[] { "pure_triple_chow", "pure_double_sequence", "four_tiles" };

        public override int GetMatchCount(FanContext ctx)
        {
            var chows = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = chows.GroupBy(c => new { c.FirstTile.TileSuit, c.FirstTile.Value });
            foreach (var g in grouped)
            {
                if (g.Count() >= 4) return 1;
            }
            return 0;
        }
    }

    // === 48番：一色四节高 ===
    [FanRule("four_pure_shifted_pungs", 48)]
    public class Fan_FourPureShiftedPungs : FanRule
    {
        public override string Name => "一色四节高";
        public override int Priority => 85;
        public override string[] ExcludedRuleIds => new[] { "three_pure_shifted_pungs", "all_pungs" };

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
                    if (vals[i] == vals[i-1] + 1) { consec++; maxConsec = Mathf.Max(maxConsec, consec); }
                    else consec = 1;
                }
                if (maxConsec >= 4) return 1;
            }
            return 0;
        }
    }

    #endregion

    #region === 64番 ===

    // === 64番：字一色 (由字牌组成的胡牌) ===
    [FanRule("all_honors", 64)]
    public class Fan_AllHonors : FanRule
    {
        public override string Name => "字一色";
        public override string Description => "由字牌组成的胡牌";

        public override int Priority => 90;
        // 字一色计分后，不计碰碰和、幺九刻
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "terminal_honor_pung" };

        public override int GetMatchCount(FanContext ctx)
        {
            foreach (var t in ctx.HandTiles) if (t.TileType != TileType.Word) return 0;
            foreach (var m in ctx.FixedMelds) if (m.FirstTile.TileType != TileType.Word) return 0;
            if (ctx.WinningTile != null && ctx.WinningTile.TileType != TileType.Word) return 0;
            return 1;
        }
    }

    // === 64番：四暗刻 (4个暗刻/暗杠) ===
    [FanRule("four_concealed_pungs", 64)]
    public class Fan_FourConcealedPungs : FanRule
    {
        public override string Name => "四暗刻";
        public override string Description => "4个暗刻(或暗杠)组成的胡牌";

        public override int Priority => 95;
        // 不计碰碰和、三暗刻、双暗刻
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "three_concealed_pungs", "two_concealed_pungs" };

        public override int GetMatchCount(FanContext ctx)
        {
            int concealedPungs = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.IsConcealed) concealedPungs++;
            }
            return (concealedPungs == 4) ? 1 : 0;
        }
    }

    // === 64番：小三元 (2组箭刻+箭牌雀头) ===
    [FanRule("little_three_dragons", 64)]
    public class Fan_LittleThreeDragons : FanRule
    {
        public override string Name => "小三元";
        public override int Priority => 80;
        // 小三元计分后，不计箭刻、双箭刻
        public override string[] ExcludedRuleIds => new[] { "dragon_pung", "two_dragon_pungs" };

        public override int GetMatchCount(FanContext ctx)
        {
            var pungValues = new HashSet<int>();
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.IsPungOrKong) pungValues.Add(m.FirstTile.Value);
            }

            if (ctx.Decomposition.Pair.Count != 1 || ctx.Decomposition.Pair[0].TileSuit != Suit.Dragon) return 0;

            int pairValue = ctx.Decomposition.Pair[0].Value;
            if (pungValues.Count >= 2 && !pungValues.Contains(pairValue)) return 1;
            return 0;
        }
    }

    // === 64番：清幺九 ===
    [FanRule("pure_terminal_pung", 64)]
    public class Fan_PureTerminalPung : FanRule
    {
        public override string Name => "清幺九";
        public override int Priority => 95;
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "terminal_honor_pung", "double_pung", "no_honors", "all_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0; // 必须是标准4面子牌型，排斥十三幺、七对子结构的误判

            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type == MeldType.Chi || m.Type == MeldType.Knitted) return 0;
                if (m.FirstTile.TileType != TileType.Number || (m.FirstTile.Value != 1 && m.FirstTile.Value != 9)) return 0;
            }
            foreach (var t in ctx.Decomposition.Pair)
            {
                if (t.TileType != TileType.Number || (t.Value != 1 && t.Value != 9)) return 0;
            }
            return 1;
        }
    }

    // === 64番：小四喜 ===
    [FanRule("little_four_winds", 64)]
    public class Fan_LittleFourWinds : FanRule
    {
        public override string Name => "小四喜";
        public override int Priority => 95;
        public override string[] ExcludedRuleIds => new[] { "three_wind_pungs" };

        public override int GetMatchCount(FanContext ctx)
        {
            int windPungs = 0;
            bool windPair = false;
            foreach(var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.FirstTile.TileSuit == Suit.Wind) windPungs++;
            }
            foreach(var t in ctx.Decomposition.Pair)
            {
                if (t.TileSuit == Suit.Wind) windPair = true;
            }
            return (windPungs == 3 && windPair) ? 1 : 0;
        }
    }

    // === 64番：一色双龙会 ===
    [FanRule("pure_double_reversible", 64)]
    public class Fan_PureDoubleReversible : FanRule
    {
        public override string Name => "一色双龙会";
        public override int Priority => 95;
        public override string[] ExcludedRuleIds => new[] { "pure_double_sequence", "full_flush", "all_chows", "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Any(m => m.Type != MeldType.Chi)) return 0;
            if (ctx.Decomposition.Pair.Count != 1 || ctx.Decomposition.Pair[0].Value != 5) return 0;

            Suit suit = ctx.Decomposition.Pair[0].TileSuit;
            if (suit == Suit.Wind || suit == Suit.Dragon) return 0;

            int lowCount = 0;
            int highCount = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit != suit) return 0;
                if (m.FirstTile.Value == 1) lowCount++;
                else if (m.FirstTile.Value == 7) highCount++;
                else return 0;
            }
            return (lowCount == 2 && highCount == 2) ? 1 : 0;
        }
    }

    #endregion

    #region === 88番 ===

    // === 88番：大三元 (中、发、白各一组刻子/杠) ===
    [FanRule("big_three_dragons", 88)]
    public class Fan_BigThreeDragons : FanRule
    {
        public override string Name => "大三元";
        public override string Description => "由中、发、白3副刻子组成的胡牌";

        public override int Priority => 100;
        // 大三元计分后，不计箭刻、双箭刻
        public override string[] ExcludedRuleIds => new[] { "dragon_pung", "two_dragon_pungs" };

        public override int GetMatchCount(FanContext ctx)
        {
            bool hasZhong = false, hasFa = false, hasBai = false;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.IsPungOrKong)
                {
                    if (m.FirstTile.Value == 1) hasZhong = true;
                    if (m.FirstTile.Value == 2) hasFa = true;
                    if (m.FirstTile.Value == 3) hasBai = true;
                }
            }
            return (hasZhong && hasFa && hasBai) ? 1 : 0;
        }
    }

    // === 88番：大四喜 ===
    [FanRule("big_four_winds", 88)]
    public class Fan_BigFourWinds : FanRule
    {
        public override string Name => "大四喜";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "seat_wind", "prevalent_wind", "all_pungs", "terminal_honor_pung" };

        public override int GetMatchCount(FanContext ctx)
        {
            int windPungs = 0;
            foreach(var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.FirstTile.TileSuit == Suit.Wind) windPungs++;
            }
            return windPungs == 4 ? 1 : 0;
        }
    }

    // === 88番：绿一色 ===
    [FanRule("green_flush", 88)]
    public class Fan_GreenFlush : FanRule
    {
        public override string Name => "绿一色";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "half_flush" };

        public override int GetMatchCount(FanContext ctx)
        {
            bool hasGreen = true;
            void CheckTile(TileData t)
            {
                if (t.TileSuit == Suit.Dragon && t.Value == 2) return; // 发
                if (t.TileSuit == Suit.Sou && (t.Value == 2 || t.Value == 3 || t.Value == 4 || t.Value == 6 || t.Value == 8)) return;
                hasGreen = false;
            }

            foreach(var t in ctx.HandTiles) CheckTile(t);
            foreach(var m in ctx.FixedMelds) foreach(var t in m.Tiles) CheckTile(t);
            if (ctx.WinningTile != null) CheckTile(ctx.WinningTile);

            return hasGreen ? 1 : 0;
        }
    }

    // === 88番：九莲宝灯 ===
    [FanRule("nine_gates", 88)]
    public class Fan_NineGates : FanRule
    {
        public override string Name => "九莲宝灯";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "full_flush", "fully_concealed", "concealed_hand", "terminal_honor_pung" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.FixedMelds.Count > 0) return 0; // 必须门前清，不允许任何副露

            int[] counts = ctx.HandCounts;

            for (int suit = 0; suit < 3; suit++)
            {
                int baseIdx = suit * 9;
                bool ok = true;
                if (counts[baseIdx] < 3 || counts[baseIdx + 8] < 3) continue;
                for (int v = 1; v < 8; v++)
                {
                    if (counts[baseIdx + v] < 1) { ok = false; break; }
                }

                if (ok)
                {
                    // 确保全是这个花色
                    int suitTotal = 0;
                    for (int v = 0; v < 9; v++) suitTotal += counts[baseIdx + v];
                    if (suitTotal == 14) return 1;
                }
            }
            return 0;
        }
    }

    // === 88番：四杠 ===
    [FanRule("four_kongs", 88)]
    public class Fan_FourKongs : FanRule
    {
        public override string Name => "四杠";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "all_pungs", "single_wait", "two_concealed_kongs", "two_melded_kongs", "melded_kong", "concealed_kong" };

        public override int GetMatchCount(FanContext ctx)
        {
            int kongs = ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Concealed || m.Type == MeldType.Kan_Added);
            return kongs == 4 ? 1 : 0;
        }
    }

    // === 88番：连七对 ===
    [FanRule("seven_shifted_pairs", 88)]
    public class Fan_SevenShiftedPairs : FanRule
    {
        public override string Name => "连七对";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "seven_pairs", "full_flush", "fully_concealed", "single_wait", "concealed_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.FixedMelds.Count > 0) return 0;

            int[] counts = ctx.HandCounts;

            int totalTiles = 0;
            for (int i = 0; i < 34; i++) totalTiles += counts[i];
            if (totalTiles != 14) return 0; // 必须是 14 张牌

            // 检查是否全是一种序数牌
            int suit = -1;
            for (int i = 0; i < 27; i++)
            {
                if (counts[i] > 0)
                {
                    if (suit == -1) suit = i / 9;
                    else if (suit != i / 9) return 0;
                }
            }
            if (suit == -1) return 0;
            for (int i = 27; i < 34; i++) if (counts[i] > 0) return 0;

            // 连七对必须是7种不同的牌，每种正好2张（或者异化牌堆里有更多张但是用来拆别的对子了，这里严格要求至少2张，且7种连续）
            int seqCount = 0;
            int maxSeq = 0;
            int baseIdx = suit * 9;
            for (int v = 0; v < 9; v++)
            {
                if (counts[baseIdx + v] >= 2)
                {
                    seqCount++;
                    maxSeq = Mathf.Max(maxSeq, seqCount);
                }
                else
                {
                    seqCount = 0;
                }
            }

            return maxSeq == 7 ? 1 : 0;
        }
    }

    // === 88番：十三幺 ===
    [FanRule("thirteen_orphans", 88)]
    public class Fan_ThirteenOrphans : FanRule
    {
        public override string Name => "十三幺";
        public override int Priority => 100;
        public override string[] ExcludedRuleIds => new[] { "all_types", "fully_concealed", "concealed_hand", "single_wait", "outside_hand" };

        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.FixedMelds.Count > 0) return 0;
            int[] counts = ctx.HandCounts;

            int[] orphanIndices = { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };
            int typeCount = 0;
            int totalOrphans = 0;
            int total = 0;
            foreach (int i in orphanIndices)
            {
                if (counts[i] > 0) typeCount++;
                totalOrphans += counts[i];
            }
            for (int i = 0; i < 34; i++) total += counts[i];

            return (typeCount == 13 && totalOrphans == 14 && total == 14) ? 1 : 0;
        }
    }

    #endregion
}
