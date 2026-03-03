using MahjongGame.Core;
using MahjongGame.Core.Fan;
using System.Linq;
using System.Collections.Generic;

namespace MahjongGame.Core.Fan.Rules
{
    // ============================================================
    //  国标麻将 (MCR) 番种规则 — 1~6 番
    // ============================================================

    #region === 1番 ===

    // === 1番：自摸 ===
    [FanRule("self_draw", 1)]
    public class Fan_SelfDraw : FanRule
    {
        public override string Name => "自摸";
        public override string Description => "自己摸到和牌的牌";
        public override int GetMatchCount(FanContext ctx) => ctx.IsSelfDraw ? 1 : 0;
    }

    // === 1番：单钓将 ===
    [FanRule("single_wait", 1)]
    public class Fan_SingleWait : FanRule
    {
        public override string Name => "单钓将";
        public override string Description => "听牌时只听一张牌，且该牌为将牌";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Single ? 1 : 0;
    }

    // === 1番：坎张 ===
    [FanRule("closed_wait", 1)]
    public class Fan_ClosedWait : FanRule
    {
        public override string Name => "坎张";
        public override string Description => "听牌时只听顺子中间的那张牌";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Closed ? 1 : 0;
    }

    // === 1番：边张 ===
    [FanRule("edge_wait", 1)]
    public class Fan_EdgeWait : FanRule
    {
        public override string Name => "边张";
        public override string Description => "听牌时只听123的3或789的7";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Edge ? 1 : 0;
    }

    // === 1番：幺九刻 (1、9或风牌的刻子) ===
    [FanRule("terminal_honor_pung", 1)]
    public class Fan_TerminalHonorPung : FanRule
    {
        public override string Name => "幺九刻";
        public override string Description => "由序数牌1、9或字牌组成的刻子";
        public override int GetMatchCount(FanContext ctx)
        {
            int total = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong)
                {
                    TileData first = m.FirstTile;
                    bool isTerminal = (first.TileType == TileType.Number) && (first.Value == 1 || first.Value == 9);
                    bool isWind = first.TileSuit == Suit.Wind;

                    if (isTerminal || isWind)
                    {
                        // 扣除计为圈风/门风的刻子 (因为圈风门风计 2番)
                        bool isPrevalent = isWind && first.Value == (int)ctx.RoundWind;
                        bool isSeat = isWind && first.Value == (int)ctx.SeatWind;

                        if (!isPrevalent && !isSeat) total++;
                    }
                }
            }
            return total;
        }
    }

    // === 1番：一般高 (同花色同序数顺子 x2) ===
    [FanRule("pure_double_sequence", 1)]
    public class Fan_PureDoubleSequence : FanRule
    {
        public override string Name => "一般高";
        public override string Description => "由一种花色2副序数相同的顺子组成的胡牌";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = sequences.GroupBy(s => new { s.FirstTile.TileSuit, s.FirstTile.Value });
            int matches = 0;
            foreach (var g in grouped)
            {
                if (g.Count() >= 2) matches++;
            }
            return matches;
        }
    }

    // === 1番：喜相逢 (不同花色同序数顺子 x2) ===
    [FanRule("mixed_double_sequence", 1)]
    public class Fan_MixedDoubleSequence : FanRule
    {
        public override string Name => "喜相逢";
        public override string Description => "由2种花色2副序数相同的顺子组成的胡牌";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = sequences.GroupBy(s => s.FirstTile.Value);
            int matches = 0;
            foreach (var g in grouped)
            {
                // 按花色统计每种花色有多少副同序数顺子
                var suitCounts = g.GroupBy(s => s.FirstTile.TileSuit).Select(sg => sg.Count()).ToList();
                // 不同花色间两两配对，取组合数
                for (int i = 0; i < suitCounts.Count; i++)
                {
                    for (int j = i + 1; j < suitCounts.Count; j++)
                    {
                        matches += suitCounts[i] * suitCounts[j];
                    }
                }
            }
            return matches;
        }
    }

    // === 1番：连六 (同花色序数相连的六张牌) ===
    [FanRule("short_straight", 1)]
    public class Fan_ShortStraight : FanRule
    {
        public override string Name => "连六";
        public override string Description => "由一种花色序数相连的2副顺子组成的胡牌";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = sequences.GroupBy(s => s.FirstTile.TileSuit);
            int matches = 0;
            foreach (var g in grouped)
            {
                var vals = g.Select(s => s.FirstTile.Value).Distinct().OrderBy(v => v).ToList();
                for (int i = 0; i < vals.Count - 1; i++)
                {
                    if (vals[i + 1] - vals[i] == 3)
                    {
                        matches++;
                        break; // 每个花色最多算一次连六，避免复杂的重叠计算
                    }
                }
            }
            return matches;
        }
    }

    // === 1番：老少副 (同花色的 123 和 789) ===
    [FanRule("two_terminal_chows", 1)]
    public class Fan_TwoTerminalChows : FanRule
    {
        public override string Name => "老少副";
        public override string Description => "由一种花色序数牌123、789的顺子各1副组成的胡牌";
        public override int GetMatchCount(FanContext ctx)
        {
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            var grouped = sequences.GroupBy(s => s.FirstTile.TileSuit);
            int matches = 0;
            foreach (var g in grouped)
            {
                var vals = g.Select(s => s.FirstTile.Value).Distinct().ToList();
                if (vals.Contains(1) && vals.Contains(7)) matches++;
            }
            return matches;
        }
    }

    // === 1番：明杠 ===
    [FanRule("melded_kong", 1)]
    public class Fan_MeldedKong : FanRule
    {
        public override string Name => "明杠";
        public override string Description => "自己有暗刻，碰别人打出的牌；或自己摸到已碰牌的第4张牌";
        public override int GetMatchCount(FanContext ctx)
        {
            return ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Added);
        }
    }

    // === 1番：缺一门 (缺少一种花色的序数牌) ===
    [FanRule("one_voided_suit", 1)]
    public class Fan_OneVoidedSuit : FanRule
    {
        public override string Name => "缺一门";
        public override string Description => "和牌中缺少一种花色的序数牌";
        public override int GetMatchCount(FanContext ctx)
        {
            bool hasMan = false, hasPin = false, hasSou = false;
            void Check(TileData t)
            {
                if (t.TileSuit == Suit.Man) hasMan = true;
                if (t.TileSuit == Suit.Pin) hasPin = true;
                if (t.TileSuit == Suit.Sou) hasSou = true;
            }

            foreach (var t in ctx.HandTiles) Check(t);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t);
            if (ctx.WinningTile != null) Check(ctx.WinningTile);

            int count = (hasMan ? 1 : 0) + (hasPin ? 1 : 0) + (hasSou ? 1 : 0);
            return (count == 2) ? 1 : 0;
        }
    }

    // === 1番：无字 (没有字牌) ===
    [FanRule("no_honors", 1)]
    public class Fan_NoHonors : FanRule
    {
        public override string Name => "无字";
        public override string Description => "和牌中没有字牌";
        public override int GetMatchCount(FanContext ctx)
        {
            foreach (var t in ctx.HandTiles) if (t.TileType == TileType.Word) return 0;
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) if (t.TileType == TileType.Word) return 0;
            if (ctx.WinningTile != null && ctx.WinningTile.TileType == TileType.Word) return 0;
            return 1;
        }
    }

    #endregion

    #region === 2番 ===

    // === 2番：断幺九 (无1、9、字牌) ===
    [FanRule("all_simples", 2)]
    public class Fan_AllSimples : FanRule
    {
        public override string Name => "断幺九";
        public override string Description => "无一、九牌及字牌";

        public override int Priority => 20;
        // 断幺九必然无字
        public override string[] ExcludedRuleIds => new[] { "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            foreach (var t in ctx.HandTiles) if (IsYaoJiu(t)) return 0;
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) if (IsYaoJiu(t)) return 0;
            if (ctx.WinningTile != null && IsYaoJiu(ctx.WinningTile)) return 0;
            return 1;
        }

        private bool IsYaoJiu(TileData t)
        {
            if (t.TileType == TileType.Word) return true;
            if (t.Value == 1 || t.Value == 9) return true;
            return false;
        }
    }

    // === 2番：箭刻 (中、发、白) - 支持多组叠加 ===
    [FanRule("dragon_pung", 2)]
    public class Fan_DragonPung : FanRule
    {
        public override string Name => "箭刻";
        public override string Description => "由中、发、白组成的刻子";

        public override int GetMatchCount(FanContext ctx)
        {
            int totalSets = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.IsPungOrKong)
                {
                    totalSets++;
                }
            }
            return totalSets;
        }
    }

    // === 2番：暗杠 ===
    [FanRule("concealed_kong", 2)]
    public class Fan_ConcealedKong : FanRule
    {
        public override string Name => "暗杠";
        public override string Description => "自己摸到4张相同的牌，并声明暗杠";
        public override int GetMatchCount(FanContext ctx)
        {
            return ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Concealed);
        }
    }

    // === 2番：四归一 (四张相同的牌不报杠) ===
    [FanRule("four_tiles", 2)]
    public class Fan_FourTiles : FanRule
    {
        public override string Name => "四归一";
        public override string Description => "和牌中，有4张相同的牌归于一家的顺子、刻子、对子、将牌中(不包括杠牌)";
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            int[] allCounts = new int[34];
            foreach (var t in ctx.HandTiles) allCounts[MahjongLogic.GetTileIndex(t)]++;
            foreach (var m in ctx.FixedMelds)
            {
                if (m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Concealed || m.Type == MeldType.Kan_Added)
                    continue;
                foreach (var t in m.Tiles) allCounts[MahjongLogic.GetTileIndex(t)]++;
            }
            if (ctx.WinningTile != null) allCounts[MahjongLogic.GetTileIndex(ctx.WinningTile)]++;

            for (int i = 0; i < 34; i++)
            {
                if (allCounts[i] >= 4) matches += allCounts[i] / 4;
            }
            return matches;
        }
    }

    // === 2番：双同刻 (两副同序数同种类的刻子) ===
    [FanRule("double_pung", 2)]
    public class Fan_DoublePung : FanRule
    {
        public override string Name => "双同刻";
        public override string Description => "由2种花色序数相同的2副刻子组成的胡牌";
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.IsPungOrKong).ToList();
            for (int i = 0; i < pungs.Count; i++)
            {
                for (int j = i + 1; j < pungs.Count; j++)
                {
                    var p1 = pungs[i].FirstTile;
                    var p2 = pungs[j].FirstTile;
                    bool p1IsNumber = p1.TileSuit == Suit.Man || p1.TileSuit == Suit.Pin || p1.TileSuit == Suit.Sou;
                    bool p2IsNumber = p2.TileSuit == Suit.Man || p2.TileSuit == Suit.Pin || p2.TileSuit == Suit.Sou;
                    if (p1IsNumber && p2IsNumber && p1.TileSuit != p2.TileSuit && p1.Value == p2.Value)
                    {
                        matches++;
                    }
                }
            }
            return matches;
        }
    }

    // === 2番：双暗刻 ===
    [FanRule("two_concealed_pungs", 2)]
    public class Fan_TwoConcealedPungs : FanRule
    {
        public override string Name => "双暗刻";
        public override string Description => "和牌中包含2副暗刻";
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.Decomposition.AllMelds.Count(m => m.IsPungOrKong && m.IsConcealed);
            return (count == 2) ? 1 : 0;
        }
    }

    // === 2番：圈风刻 ===
    [FanRule("prevalent_wind", 2)]
    public class Fan_PrevalentWind : FanRule
    {
        public override string Name => "圈风刻";
        public override string Description => "由圈风牌组成的刻子";
        public override int GetMatchCount(FanContext ctx)
        {
            int count = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.FirstTile.TileSuit == Suit.Wind && m.FirstTile.Value == (int)ctx.RoundWind)
                    count++;
            }
            return count;
        }
    }

    // === 2番：门风刻 ===
    [FanRule("seat_wind", 2)]
    public class Fan_SeatWind : FanRule
    {
        public override string Name => "门风刻";
        public override string Description => "由门风牌组成的刻子";
        public override int GetMatchCount(FanContext ctx)
        {
            int count = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.IsPungOrKong && m.FirstTile.TileSuit == Suit.Wind && m.FirstTile.Value == (int)ctx.SeatWind)
                    count++;
            }
            return count;
        }
    }

    // === 2番：门前清 ===
    [FanRule("concealed_hand", 2)]
    public class Fan_ConcealedHand : FanRule
    {
        public override string Name => "门前清";
        public override string Description => "没有吃、碰、明杠，和别人打出的牌";
        public override int GetMatchCount(FanContext ctx)
        {
            bool isMelded = ctx.FixedMelds.Any(m => m.Type != MeldType.Kan_Concealed);
            return (!isMelded && !ctx.IsSelfDraw) ? 1 : 0;
        }
    }

    // === 2番：平和 ===
    [FanRule("all_chows", 2)]
    public class Fan_AllChows : FanRule
    {
        public override string Name => "平和";
        public override string Description => "由4副顺子及序数牌作将组成的胡牌";
        public override int Priority => 15;
        // 平和必然无字
        public override string[] ExcludedRuleIds => new[] { "no_honors" };

        public override int GetMatchCount(FanContext ctx)
        {
            bool allChows = ctx.Decomposition.AllMelds.All(m => m.Type == MeldType.Chi);
            bool numberPair = ctx.Decomposition.Pair.Count == 1 && ctx.Decomposition.Pair[0].TileType == TileType.Number;
            return (allChows && numberPair) ? 1 : 0;
        }
    }

    #endregion

    #region === 4番 ===

    // === 4番：和绝张 ===
    [FanRule("last_tile_win", 4)]
    public class Fan_LastTileWin : FanRule
    {
        public override string Name => "和绝张";
        public override string Description => "和牌时，牌墙里或牌池里同张牌已出现3张";
        public override int GetMatchCount(FanContext ctx) => ctx.IsLastTileWin ? 1 : 0;
    }

    // === 4番：全带幺 (每副面子和雀头都含有幺九牌) ===
    [FanRule("outside_hand", 4)]
    public class Fan_OutsideHand : FanRule
    {
        public override string Name => "全带幺";
        public override string Description => "每副顺子、刻子、杠子、将牌均含有幺九牌";
        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.AllMelds.Count != 4) return 0; // 排除特殊牌型
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                bool hasTerminal = false;
                foreach (var t in m.Tiles) if (IsTerminalOrHonor(t)) { hasTerminal = true; break; }
                if (!hasTerminal) return 0;
            }
            foreach (var t in ctx.Decomposition.Pair) if (!IsTerminalOrHonor(t)) return 0;
            return 1;
        }
        private bool IsTerminalOrHonor(TileData t) => t.TileType == TileType.Word || t.Value == 1 || t.Value == 9;
    }

    // === 4番：不求人 (门清自摸) ===
    [FanRule("fully_concealed", 4)]
    public class Fan_FullyConcealed : FanRule
    {
        public override string Name => "不求人";
        public override string Description => "没有吃、碰、明杠，自摸和牌";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "self_draw" };
        public override int GetMatchCount(FanContext ctx)
        {
            bool isMelded = ctx.FixedMelds.Any(m => m.Type != MeldType.Kan_Concealed);
            return (!isMelded && ctx.IsSelfDraw) ? 1 : 0;
        }
    }

    // === 4番：双明杠 ===
    [FanRule("two_melded_kongs", 4)]
    public class Fan_TwoMeldedKongs : FanRule
    {
        public override string Name => "双明杠";
        public override string Description => "和牌中包含2副明杠";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "melded_kong" };
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Added);
            return (count == 2) ? 1 : 0;
        }
    }

    #endregion

    #region === 6番 ===

    // === 6番：碰碰和 (由4个刻子或杠、1个将头组成) ===
    [FanRule("all_pungs", 6)]
    public class Fan_AllPungs : FanRule
    {
        public override string Name => "碰碰和";
        public override string Description => "由4个刻子(或杠)组成的胡牌";

        public override int GetMatchCount(FanContext ctx)
        {
            // 碰碰和必须包含 4 组面子，非标准结构的牌型（如七对子/十三幺，其 AllMelds.Count < 4）直接不成立
            if (ctx.Decomposition.AllMelds.Count != 4) return 0;

            foreach (var meld in ctx.Decomposition.AllMelds)
            {
                if (meld.Type == MeldType.Chi) return 0;
            }
            return 1;
        }
    }

    // === 6番：混一色 (由一种花色数牌和字牌组成) ===
    [FanRule("half_flush", 6)]
    public class Fan_HalfFlush : FanRule
    {
        public override string Name => "混一色";
        public override string Description => "由一种花色数牌及字牌组成的胡牌";

        public override int GetMatchCount(FanContext ctx)
        {
            Suit numberSuit = Suit.Wind;
            bool hasWord = false;

            void CheckTile(TileData t)
            {
                if (t.TileType == TileType.Word) hasWord = true;
                else {
                    if (numberSuit == Suit.Wind) numberSuit = t.TileSuit;
                    else if (numberSuit != t.TileSuit) numberSuit = Suit.Dragon;
                }
            }

            foreach(var t in ctx.HandTiles) CheckTile(t);
            foreach(var m in ctx.FixedMelds) foreach(var t in m.Tiles) CheckTile(t);
            if (ctx.WinningTile != null) CheckTile(ctx.WinningTile);

            if (numberSuit != Suit.Wind && numberSuit != Suit.Dragon && hasWord) return 1;
            return 0;
        }
    }

    // === 6番：五门齐 (万、筒、索、风、箭) ===
    [FanRule("all_types", 6)]
    public class Fan_AllTypes : FanRule
    {
        public override string Name => "五门齐";
        public override string Description => "和牌中万、筒、条、风、箭5种牌齐全";
        public override int GetMatchCount(FanContext ctx)
        {
            bool hasMan = false, hasPin = false, hasSou = false, hasWind = false, hasDragon = false;
            void Check(TileData t)
            {
                if (t.TileSuit == Suit.Man) hasMan = true;
                else if (t.TileSuit == Suit.Pin) hasPin = true;
                else if (t.TileSuit == Suit.Sou) hasSou = true;
                else if (t.TileSuit == Suit.Wind) hasWind = true;
                else if (t.TileSuit == Suit.Dragon) hasDragon = true;
            }
            foreach (var t in ctx.HandTiles) Check(t);
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) Check(t);
            if (ctx.WinningTile != null) Check(ctx.WinningTile);

            return (hasMan && hasPin && hasSou && hasWind && hasDragon) ? 1 : 0;
        }
    }

    // === 6番：全求人 (全副露且点炮胡) ===
    [FanRule("melded_hand", 6)]
    public class Fan_MeldedHand : FanRule
    {
        public override string Name => "全求人";
        public override string Description => "全副露，单钓将，和别人打出的牌";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "single_wait" };
        public override int GetMatchCount(FanContext ctx)
        {
            return (ctx.FixedMelds.Count == 4 && !ctx.IsSelfDraw) ? 1 : 0;
        }
    }

    // === 6番：双暗杠 ===
    [FanRule("two_concealed_kongs", 6)]
    public class Fan_TwoConcealedKongs : FanRule
    {
        public override string Name => "双暗杠";
        public override string Description => "和牌中包含2副暗杠";
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "concealed_kong" };
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Concealed);
            return (count == 2) ? 1 : 0;
        }
    }

    // === 6番：三色三步高 (三种花色序数递增的顺子) ===
    [FanRule("mixed_shifted_chows", 6)]
    public class Fan_MixedShiftedChows : FanRule
    {
        public override string Name => "三色三步高";
        public override string Description => "由3种花色3副序数递增1的顺子组成的胡牌";
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

                        var suits = new HashSet<Suit> { s1.TileSuit, s2.TileSuit, s3.TileSuit };
                        if (suits.Count != 3 || suits.Contains(Suit.Wind) || suits.Contains(Suit.Dragon)) continue;

                        var vals = new List<int> { s1.Value, s2.Value, s3.Value };
                        vals.Sort();
                        int d1 = vals[1] - vals[0];
                        int d2 = vals[2] - vals[1];
                        if (d1 == d2 && d1 == 1) return 1;
                    }
                }
            }
            return 0;
        }
    }

    // === 6番：双箭刻 (两副箭刻) ===
    [FanRule("two_dragon_pungs", 6)]
    public class Fan_TwoDragonPungs : FanRule
    {
        public override string Name => "双箭刻";
        public override string Description => "和牌中包含2副箭刻";
        public override int Priority => 70;
        // 双箭刻排斥基础的箭刻计数
        public override string[] ExcludedRuleIds => new[] { "dragon_pung" };

        public override int GetMatchCount(FanContext ctx)
        {
            int dragonPungs = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.IsPungOrKong) dragonPungs++;
            }
            return (dragonPungs == 2) ? 1 : 0;
        }
    }

    #endregion
}
