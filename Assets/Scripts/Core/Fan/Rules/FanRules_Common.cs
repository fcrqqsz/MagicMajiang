using MahjongGame.Core;
using MahjongGame.Core.Fan;
using System.Linq;
using System.Collections.Generic;
using UnityEngine; // 确保引用 Mathf

namespace MahjongGame.Core.Fan.Rules
{
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

    // === 6番：碰碰和 (由4个刻子或杠、1个将头组成) ===
    [FanRule("all_pungs", 6)]
    public class Fan_AllPungs : FanRule
    {
        public override string Name => "碰碰和";
        public override string Description => "由4个刻子(或杠)组成的胡牌";

        public override int GetMatchCount(FanContext ctx)
        {
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

    // === 2番：箭刻 (中、发、白) - 支持多组叠加 ===
    [FanRule("dragon_pung", 2)]
    public class Fan_DragonPung : FanRule
    {
        public override string Name => "箭刻";
        
        public override int GetMatchCount(FanContext ctx)
        {
            int totalSets = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.Type != MeldType.Chi)
                {
                    totalSets++;
                }
            }
            return totalSets;
        }
    }

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
                if (m.FirstTile.TileSuit == Suit.Dragon && m.Type != MeldType.Chi)
                {
                    if (m.FirstTile.Value == 1) hasZhong = true;
                    if (m.FirstTile.Value == 2) hasFa = true;
                    if (m.FirstTile.Value == 3) hasBai = true;
                }
            }
            return (hasZhong && hasFa && hasBai) ? 1 : 0;
        }
    }

    // === 1番：自摸 ===
    [FanRule("self_draw", 1)]
    public class Fan_SelfDraw : FanRule
    {
        public override string Name => "自摸";
        public override int GetMatchCount(FanContext ctx) => ctx.IsSelfDraw ? 1 : 0;
    }

    // === 1番：单钓将 ===
    [FanRule("single_wait", 1)]
    public class Fan_SingleWait : FanRule
    {
        public override string Name => "单钓将";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Single ? 1 : 0;
    }

    // === 8番：杠上开花 ===
    [FanRule("kong_win", 8)]
    public class Fan_KongWin : FanRule
    {
        public override string Name => "杠上开花";
        public override int GetMatchCount(FanContext ctx) => ctx.IsKongWin ? 1 : 0;
        public override string[] ExcludedRuleIds => new[] { "self_draw" };
    }

    // === 4番：和绝张 ===
    [FanRule("last_tile_win", 4)]
    public class Fan_LastTileWin : FanRule
    {
        public override string Name => "和绝张";
        public override int GetMatchCount(FanContext ctx) => ctx.IsLastTileWin ? 1 : 0;
    }

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
                if (m.Type != MeldType.Chi && m.IsConcealed) concealedPungs++;
            }
            return (concealedPungs == 4) ? 1 : 0;
        }
    }

    // === 24番：七对子 (由7个对子组成的胡牌) ===
    [FanRule("seven_pairs", 24)]
    public class Fan_SevenPairs : FanRule
    {
        public override string Name => "七对子";
        public override int GetMatchCount(FanContext ctx)
        {
            if (ctx.Decomposition.Pair.Count == 7) return 1;
            return 0;
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
                if (m.FirstTile.TileSuit == Suit.Dragon && m.Type != MeldType.Chi) pungValues.Add(m.FirstTile.Value);
            }

            if (ctx.Decomposition.Pair.Count != 1 || ctx.Decomposition.Pair[0].TileSuit != Suit.Dragon) return 0;

            int pairValue = ctx.Decomposition.Pair[0].Value;
            if (pungValues.Count >= 2 && !pungValues.Contains(pairValue)) return 1;
            return 0;
        }
    }

    // === 1番：幺九刻 (1、9或风牌的刻子) ===
    [FanRule("terminal_honor_pung", 1)]
    public class Fan_TerminalHonorPung : FanRule
    {
        public override string Name => "幺九刻";
        public override int GetMatchCount(FanContext ctx)
        {
            int total = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type != MeldType.Chi)
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
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            for (int i = 0; i < sequences.Count; i++)
            {
                for (int j = i + 1; j < sequences.Count; j++)
                {
                    var s1 = sequences[i].FirstTile;
                    var s2 = sequences[j].FirstTile;
                    if (s1.TileSuit == s2.TileSuit && s1.Value == s2.Value) matches++;
                }
            }
            return matches;
        }
    }

    // === 1番：喜相逢 (不同花色同序数顺子 x2) ===
    [FanRule("mixed_double_sequence", 1)]
    public class Fan_MixedDoubleSequence : FanRule
    {
        public override string Name => "喜相逢";
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            for (int i = 0; i < sequences.Count; i++)
            {
                for (int j = i + 1; j < sequences.Count; j++)
                {
                    var s1 = sequences[i].FirstTile;
                    var s2 = sequences[j].FirstTile;
                    if (s1.TileSuit != s2.TileSuit && s1.Value == s2.Value) matches++;
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
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            for (int i = 0; i < sequences.Count; i++)
            {
                for (int j = i + 1; j < sequences.Count; j++)
                {
                    var s1 = sequences[i].FirstTile;
                    var s2 = sequences[j].FirstTile;
                    if (s1.TileSuit == s2.TileSuit && Mathf.Abs(s1.Value - s2.Value) == 3) matches++;
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
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var sequences = ctx.Decomposition.AllMelds.Where(m => m.Type == MeldType.Chi).ToList();
            for (int i = 0; i < sequences.Count; i++)
            {
                for (int j = i + 1; j < sequences.Count; j++)
                {
                    var s1 = sequences[i].FirstTile;
                    var s2 = sequences[j].FirstTile;
                    if (s1.TileSuit == s2.TileSuit)
                    {
                        if ((s1.Value == 1 && s2.Value == 7) || (s1.Value == 7 && s2.Value == 1)) matches++;
                    }
                }
            }
            return matches;
        }
    }

    // === 1番：明杠 ===
    [FanRule("melded_kong", 1)]
    public class Fan_MeldedKong : FanRule
    {
        public override string Name => "明杠";
        public override int GetMatchCount(FanContext ctx)
        {
            return ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Added);
        }
    }

    // === 2番：暗杠 ===
    [FanRule("concealed_kong", 2)]
    public class Fan_ConcealedKong : FanRule
    {
        public override string Name => "暗杠";
        public override int GetMatchCount(FanContext ctx)
        {
            return ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Concealed);
        }
    }

    // === 1番：缺一门 (缺少一种花色的序数牌) ===
    [FanRule("one_voided_suit", 1)]
    public class Fan_OneVoidedSuit : FanRule
    {
        public override string Name => "缺一门";
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
        public override int GetMatchCount(FanContext ctx)
        {
            foreach (var t in ctx.HandTiles) if (t.TileType == TileType.Word) return 0;
            foreach (var m in ctx.FixedMelds) foreach (var t in m.Tiles) if (t.TileType == TileType.Word) return 0;
            if (ctx.WinningTile != null && ctx.WinningTile.TileType == TileType.Word) return 0;
            return 1;
        }
    }

    // === 1番：坎张 ===
    [FanRule("closed_wait", 1)]
    public class Fan_ClosedWait : FanRule
    {
        public override string Name => "坎张";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Closed ? 1 : 0;
    }

    // === 1番：边张 ===
    [FanRule("edge_wait", 1)]
    public class Fan_EdgeWait : FanRule
    {
        public override string Name => "边张";
        public override int GetMatchCount(FanContext ctx) => ctx.Wait == WaitType.Edge ? 1 : 0;
    }

    // === 2番：四归一 (四张相同的牌不报杠) ===
    [FanRule("four_tiles", 2)]
    public class Fan_FourTiles : FanRule
    {
        public override string Name => "四归一";
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
        public override int GetMatchCount(FanContext ctx)
        {
            int matches = 0;
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.Type != MeldType.Chi).ToList();
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
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.Decomposition.AllMelds.Count(m => m.Type != MeldType.Chi && m.IsConcealed);
            return (count == 2) ? 1 : 0;
        }
    }

    // === 2番：圈风刻 ===
    [FanRule("prevalent_wind", 2)]
    public class Fan_PrevalentWind : FanRule
    {
        public override string Name => "圈风刻";
        public override int GetMatchCount(FanContext ctx)
        {
            int count = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type != MeldType.Chi && m.FirstTile.TileSuit == Suit.Wind && m.FirstTile.Value == (int)ctx.RoundWind)
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
        public override int GetMatchCount(FanContext ctx)
        {
            int count = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.Type != MeldType.Chi && m.FirstTile.TileSuit == Suit.Wind && m.FirstTile.Value == (int)ctx.SeatWind)
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
        public override int GetMatchCount(FanContext ctx)
        {
            bool isMelded = ctx.FixedMelds.Any(m => m.Type != MeldType.Kan_Exposed || m.Type == MeldType.Kan_Added);
            return (!isMelded && !ctx.IsSelfDraw) ? 1 : 0;
        }
    }

    // === 2番 : 平和 ===
    [FanRule("all_chows", 2)]
    public class Fan_AllChows : FanRule
    {
        public override string Name => "平和";
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

    // === 4番：全带幺 (每副面子和雀头都含有幺九牌) ===
    [FanRule("outside_hand", 4)]
    public class Fan_OutsideHand : FanRule
    {
        public override string Name => "全带幺";
        public override int GetMatchCount(FanContext ctx)
        {
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
        public override int Priority => 10;
        public override string[] ExcludedRuleIds => new[] { "melded_kong" };
        public override int GetMatchCount(FanContext ctx)
        {
            int count = ctx.FixedMelds.Count(m => m.Type == MeldType.Kan_Exposed || m.Type == MeldType.Kan_Added);
            return (count == 2) ? 1 : 0;
        }
    }

    // === 6番：五门齐 (万、筒、索、风、箭) ===
    [FanRule("all_types", 6)]
    public class Fan_AllTypes : FanRule
    {
        public override string Name => "五门齐";
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
                        if (d1 == d2 && (d1 == 1 || d1 == 2)) return 1;
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
        public override int Priority => 70;
        // 双箭刻排斥基础的箭刻计数
        public override string[] ExcludedRuleIds => new[] { "dragon_pung" };

        public override int GetMatchCount(FanContext ctx)
        {
            int dragonPungs = 0;
            foreach (var m in ctx.Decomposition.AllMelds)
            {
                if (m.FirstTile.TileSuit == Suit.Dragon && m.Type != MeldType.Chi) dragonPungs++;
            }
            return (dragonPungs == 2) ? 1 : 0;
        }
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
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.Type != MeldType.Chi).ToList();
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
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.Type != MeldType.Chi).ToList();
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

    // === 12番：大于五 (由6-9的序数牌组成) ===
    [FanRule("greater_five", 12)]
    public class Fan_GreaterFive : FanRule
    {
        public override string Name => "大于五";
        public override int GetMatchCount(FanContext ctx)
        {
            void Check(TileData t, ref bool fail)
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
            void Check(TileData t, ref bool fail)
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
            int count = ctx.Decomposition.AllMelds.Count(m => m.Type != MeldType.Chi && m.IsConcealed);
            return (count == 3) ? 1 : 0;
        }
    }

    // === 24番：一色三同刻 (一种花色序数相同的三个刻子) ===
    [FanRule("pure_triple_pung", 24)]
    public class Fan_PureTriplePung : FanRule
    {
        public override string Name => "一色三同刻";
        public override int GetMatchCount(FanContext ctx)
        {
            var pungs = ctx.Decomposition.AllMelds.Where(m => m.Type != MeldType.Chi).ToList();
            var grouped = pungs.GroupBy(p => new { p.FirstTile.TileSuit, p.FirstTile.Value });
            foreach (var group in grouped)
            {
                if (group.Count() >= 3) return 1;
            }
            return 0;
        }
    }
}
