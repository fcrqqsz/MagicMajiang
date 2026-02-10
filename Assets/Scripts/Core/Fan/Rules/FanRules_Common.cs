using MahjongGame.Core;

namespace MahjongGame.Core.Fan.Rules
{
    // === 2番：断幺九 (无1、9、字牌) ===
    [FanRule("all_simples", 8)]
    public class Fan_AllSimples : FanRule
    {
        public override string Name => "断幺九";
        public override string Description => "无一、九牌及字牌";

        public override bool Check(FanContext ctx)
        {
            // 1. 检查手牌
            foreach (var t in ctx.HandTiles)
            {
                if (IsYaoJiu(t)) return false;
            }
            
            // 2. 检查副露
            foreach (var m in ctx.Melds)
            {
                foreach (var t in m.Tiles)
                {
                    if (IsYaoJiu(t)) return false;
                }
            }

            // 3. 检查胡的牌 (如果是点炮，这张牌不在 HandTiles 里)
            // (注：我们在 Context 构造函数里已经处理了 HandCounts，但原始列表没变)
            // 严谨起见，我们应该检查 ctx.WinningTile
            if (ctx.WinningTile != null && IsYaoJiu(ctx.WinningTile)) return false;

            return true;
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

        public override bool Check(FanContext ctx)
        {
            // 获取第一张牌的花色作为基准
            Suit baseSuit;
            
            if (ctx.HandTiles.Count > 0) baseSuit = ctx.HandTiles[0].TileSuit;
            else if (ctx.Melds.Count > 0) baseSuit = ctx.Melds[0].FirstTile.TileSuit;
            else return false; // 没牌？

            // 清一色不能有字牌
            if (baseSuit == Suit.Wind || baseSuit == Suit.Dragon) return false;

            // 检查手牌
            foreach (var t in ctx.HandTiles)
                if (t.TileSuit != baseSuit) return false;

            // 检查副露
            foreach (var m in ctx.Melds)
                if (m.FirstTile.TileSuit != baseSuit) return false;

            // 检查胡牌
            if (ctx.WinningTile != null && ctx.WinningTile.TileSuit != baseSuit) return false;

            return true;
        }
    }
}