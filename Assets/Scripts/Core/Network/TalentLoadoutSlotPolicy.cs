using System;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    /// <summary>Shared strict reconstruction for the authoritative 6+3 carried talent slots.</summary>
    public static class TalentLoadoutSlotPolicy
    {
        public static bool TryBuild(
            string[] mainTalentSlotIds,
            string[] reserveTalentSlotIds,
            TalentRegistry registry,
            out TalentSlotConfig talentConfig)
        {
            talentConfig = null;
            if (registry == null
                || mainTalentSlotIds == null
                || mainTalentSlotIds.Length != TalentSlotConfig.MainSlotCount
                || reserveTalentSlotIds == null
                || reserveTalentSlotIds.Length != TalentSlotConfig.ReserveSlotCount)
            {
                return false;
            }

            var rebuilt = new TalentSlotConfig
            {
                SlotTalentIds = NormalizeSlotIds(mainTalentSlotIds),
                ReserveTalentIds = NormalizeSlotIds(reserveTalentSlotIds)
            };
            if (rebuilt.HasDuplicateCarriedIds()) return false;

            for (int slotIndex = 0; slotIndex < TalentSlotConfig.MainSlotCount; slotIndex++)
            {
                string talentId = rebuilt.SlotTalentIds[slotIndex];
                if (string.IsNullOrEmpty(talentId)) continue;
                if (!registry.HasTalent(talentId)
                    || !rebuilt.CanEquip(slotIndex, registry.GetTier(talentId))) return false;
            }

            for (int slotIndex = 0; slotIndex < TalentSlotConfig.ReserveSlotCount; slotIndex++)
            {
                string talentId = rebuilt.ReserveTalentIds[slotIndex];
                if (string.IsNullOrEmpty(talentId)) continue;
                if (!registry.HasTalent(talentId)
                    || !rebuilt.CanEquipReserve(slotIndex, registry.GetTier(talentId))
                    || registry.GetMetadata(talentId).SideboardPolicy != TalentSideboardPolicy.Flexible)
                {
                    return false;
                }
            }

            talentConfig = rebuilt;
            return true;
        }

        private static string[] NormalizeSlotIds(string[] source)
        {
            string[] normalized = new string[source.Length];
            for (int index = 0; index < source.Length; index++)
                normalized[index] = string.IsNullOrWhiteSpace(source[index]) ? null : source[index].Trim();
            return normalized;
        }
    }
}
