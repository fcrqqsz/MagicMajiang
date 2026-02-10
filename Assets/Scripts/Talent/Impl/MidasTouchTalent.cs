using UnityEngine;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    // 这一行很关键，让你能在 Project 窗口创建这个资源
    [CreateAssetMenu(menuName = "Mahjong/Talents/Midas Touch")]
    public class MidasTouchTalent : TalentBase
    {
        public override void OnTileDrawn(TileData drawnTile)
        {
            // 判断逻辑：如果是 一万 (Man_1)
            if (drawnTile.TileSuit == Suit.Dragon || drawnTile.TileSuit == Suit.Wind)
            {
                Debug.Log($"<color=yellow>[天赋触发] {talentName}: 将{drawnTile.GetName()}变成了发财！</color>");
                // 修改数据 (由于是引用传递，这会直接影响后续生成的牌)
                drawnTile.TileSuit = Suit.Dragon;
                drawnTile.Value = 2; // 2代表发
                drawnTile.IsModified = true;
                drawnTile.SpecialEffectID = this.id;
            }
        }
    }
}