using UnityEngine;
using MahjongGame.Core; // 引用 TileData 等核心类

namespace MahjongGame.Talents
{
    // 这个 Attribute 让你可以在 Unity 右键菜单创建它
    [CreateAssetMenu(fileName = "NewTalent", menuName = "Mahjong/Talent")]
    public abstract class TalentBase : ScriptableObject
    {
        [Header("Basic Info")]
        public string id;
        public string talentName;
        [TextArea] public string description;

        // --- 核心钩子函数 (虚函数) ---
        // 类似于 C++ virtual void OnDraw(...)
        
        /// <summary>
        /// 当游戏开始时触发 (用于修改牌库)
        /// </summary>
        public virtual void OnGameStart() { }

        /// <summary>
        /// 当玩家摸牌后触发 (用于修改摸到的牌)
        /// </summary>
        /// <param name="drawnTile">摸到的那张牌的数据</param>
        public virtual void OnTileDrawn(TileData drawnTile) { }

        /// <summary>
        /// 当玩家打出一张牌时触发
        /// </summary>
        public virtual void OnTileDiscarded(TileData discardedTile) { }
    }
}