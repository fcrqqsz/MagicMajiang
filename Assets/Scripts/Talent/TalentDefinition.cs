using UnityEngine;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    [CreateAssetMenu(menuName = "Mahjong/TalentDefinition")]
    public class TalentDefinition : ScriptableObject
    {
        public string talentId;      // 与 TalentRuleAttribute.Id 对应
        public string displayName;
        [TextArea] public string description;
        public TalentTier tier;
        public int alienationCost;
        public Sprite icon;
    }
}
