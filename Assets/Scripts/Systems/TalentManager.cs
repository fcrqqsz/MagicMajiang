using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Core;
using MahjongGame.Talents;

namespace MahjongGame.Systems
{
    public class TalentManager : MonoBehaviour
    {
        public static TalentManager Instance;

        // 玩家当前已激活的天赋列表
        // 类似于 std::vector<TalentBase*>
        [SerializeField] private List<TalentBase> _activeTalents = new List<TalentBase>();

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // --- 天赋管理 ---
        public void AddTalent(TalentBase talent)
        {
            if (!_activeTalents.Contains(talent))
            {
                _activeTalents.Add(talent);
                Debug.Log($"获得天赋: {talent.talentName}");
            }
        }

        // --- 事件触发器 (Invokers) ---
        
        // 1. 触发游戏开始
        public void TriggerGameStart()
        {
            foreach (var talent in _activeTalents) talent.OnGameStart();
        }

        // 2. 触发摸牌
        public void TriggerOnDraw(TileData tile)
        {
            foreach (var talent in _activeTalents) talent.OnTileDrawn(tile);
        }

        // 3. 触发打牌
        public void TriggerOnDiscard(TileData tile)
        {
            foreach (var talent in _activeTalents) talent.OnTileDiscarded(tile);
        }
    }
}