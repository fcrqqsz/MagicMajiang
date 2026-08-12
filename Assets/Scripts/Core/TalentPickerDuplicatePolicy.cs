using System;
using System.Collections.Generic;
using MahjongGame.Talents;

namespace MahjongGame.Core
{
    public static class TalentPickerDuplicatePolicy
    {
        public static bool IsDuplicateOutsideSlot(
            TalentSlotConfig talents,
            string talentId,
            int slotIndex,
            bool isReserve)
        {
            if (talents == null || string.IsNullOrWhiteSpace(talentId)) return false;
            return ContainsOutsideCurrent(
                    talents.SlotTalentIds,
                    talentId,
                    isReserve ? -1 : slotIndex)
                || ContainsOutsideCurrent(
                    talents.ReserveTalentIds,
                    talentId,
                    isReserve ? slotIndex : -1);
        }

        private static bool ContainsOutsideCurrent(
            IReadOnlyList<string> ids,
            string talentId,
            int skippedIndex)
        {
            if (ids == null) return false;
            for (int index = 0; index < ids.Count; index++)
            {
                if (index == skippedIndex) continue;
                if (string.Equals(ids[index], talentId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
