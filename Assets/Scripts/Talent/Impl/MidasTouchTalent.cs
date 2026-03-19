using UnityEngine;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("midas_touch", "点金手", "摸牌时，将风牌和箭牌转化为发财",
        TalentTier.Medium, 15, TalentPhase.OnDraw)]
    public class MidasTouchTalent : TalentRule
    {
        public override TalentScope Scope => TalentScope.Self;

        public override TileData OnDraw(TalentContext ctx, TileData tile)
        {
            if (!ctx.IsOwnersTurn) return tile;

            if (tile.TileSuit == Suit.Dragon || tile.TileSuit == Suit.Wind)
            {
                Debug.Log($"<color=yellow>[天赋触发] 点金手: 将{tile.GetName()}变成了发财！</color>");
                tile.TileSuit = Suit.Dragon;
                tile.Value = 2; // 发财
                tile.IsModified = true;
                tile.SpecialEffectID = Id;
            }
            return tile;
        }
    }
}
