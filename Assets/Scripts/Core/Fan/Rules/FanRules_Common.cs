using MahjongGame.Core;
using MahjongGame.Core.Fan;

namespace MahjongGame.Core.Fan.Rules
{
    // === 2番：断幺九 (无1、9、字牌) ===
    [FanRule("all_simples", 2)]
    public class Fan_AllSimples : FanRule
    {
        public override string Name => "断幺九";
        public override string Description => "无一、九牌及字牌";

        public override int GetMatchCount(FanContext ctx)
        {
            // 1. 检查手牌
            foreach (var t in ctx.HandTiles)
            {
                if (IsYaoJiu(t)) return 0;
            }
            
            // 2. 检查副露 (固定面子)
            foreach (var m in ctx.FixedMelds)
            {
                foreach (var t in m.Tiles)
                {
                    if (IsYaoJiu(t)) return 0;
                }
            }

            // 3. 检查胡的牌
            if (ctx.WinningTile != null && IsYaoJiu(ctx.WinningTile)) return 0;

            return 1;
        }

        private bool IsYaoJiu(TileData t)
        {
            if (t.TileSuit == Suit.Wind || t.TileSuit == Suit.Dragon) return true;
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
            // 利用拆解结果，检查 4 个面子是否都是刻子/杠
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
                if (t.TileSuit == Suit.Wind || t.TileSuit == Suit.Dragon) hasWord = true;
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
            // 直接遍历拆解出的所有面子，看有多少箭牌刻子
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
        public override string[] ExcludedRuleIds => new[] { "dragon_pung" };

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
        
        // 杠开通常包含了自摸，如果不允许重复计算，可以在此添加排斥
        public override string[] ExcludedRuleIds => new[] { "self_draw" };
    }

    // === 4番：和绝张 ===
    [FanRule("last_tile_win", 4)]
    public class Fan_LastTileWin : FanRule
    {
        public override string Name => "和绝张";
        public override int GetMatchCount(FanContext ctx) => ctx.IsLastTileWin ? 1 : 0;
    }
}
