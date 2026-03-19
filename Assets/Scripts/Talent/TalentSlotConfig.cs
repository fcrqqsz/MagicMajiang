using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    [Serializable]
    public class TalentSlotConfig
    {
        // 6 槽: index 0=大, 1-2=中, 3-5=小
        public string[] SlotTalentIds = new string[6];

        public List<string> GetAllEquippedIds()
        {
            return SlotTalentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
        }

        public bool CanEquip(int slotIndex, TalentTier talentTier)
        {
            TalentTier slotTier = GetSlotTier(slotIndex);
            return talentTier <= slotTier; // 向下兼容
        }

        public static TalentTier GetSlotTier(int index) => index switch
        {
            0 => TalentTier.Large,
            1 or 2 => TalentTier.Medium,
            _ => TalentTier.Small
        };
    }
}
