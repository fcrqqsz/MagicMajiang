using MahjongGame.Core;

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
            
            // 2. 检查副露
            foreach (var m in ctx.Melds)
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
            // 字牌
            if (t.TileSuit == Suit.Wind || t.TileSuit == Suit.Dragon) return true;
            // 数牌 1 或 9
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
            else if (ctx.Melds.Count > 0) baseSuit = ctx.Melds[0].FirstTile.TileSuit;
            else return 0;

            if (baseSuit == Suit.Wind || baseSuit == Suit.Dragon) return 0;

            foreach (var t in ctx.HandTiles) if (t.TileSuit != baseSuit) return 0;
            foreach (var m in ctx.Melds) if (m.FirstTile.TileSuit != baseSuit) return 0;
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
            foreach (var meld in ctx.Melds)
            {
                if (meld.Type == MeldType.Chi) return 0;
            }

            int totalPairs = 0;
            for (int i = 0; i < 34; i++)
            {
                int count = ctx.HandCounts[i];
                if (count == 0) continue;

                int rem = count % 3;
                if (rem == 0) { }
                else if (rem == 2) totalPairs++;
                else return 0;
            }

            return totalPairs == 1 ? 1 : 0;
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
            foreach(var m in ctx.Melds) foreach(var t in m.Tiles) CheckTile(t);
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

            // 遍历三种箭牌 (ID: 31=中, 32=发, 33=白)
            for (int dragonIdx = 31; dragonIdx <= 33; dragonIdx++)
            {
                // 1. 检查副露 (Melds)
                foreach (var m in ctx.Melds)
                {
                    // 注意：这里要转回 TileSuit.Dragon 和 Value (1,2,3)
                    // 31->Value=1, 32->Value=2, 33->Value=3
                    if (m.FirstTile.TileSuit == Suit.Dragon && m.FirstTile.Value == (dragonIdx - 30))
                    {
                        if (m.Type != MeldType.Chi) totalSets++;
                    }
                }

                // 2. 检查手牌 (HandCounts)
                // 在 Roguelike 模式下，可能有 3张, 6张, 9张...
                // 每3张算一组刻子
                int countInHand = ctx.HandCounts[dragonIdx];
                totalSets += countInHand / 3;
            }

            return totalSets;
        }
    }
}