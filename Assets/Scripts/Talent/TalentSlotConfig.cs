using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    [Serializable]
    public class TalentSlotConfig
    {
        public const int MainSlotCount = 6;
        public const int ReserveSlotCount = 3;

        // 主槽 6 槽: index 0=大, 1-2=中, 3-5=小
        public string[] SlotTalentIds = new string[MainSlotCount];
        // 备选槽 3 槽: index 0=中, 1-2=小
        public string[] ReserveTalentIds = new string[ReserveSlotCount];

        public void Normalize()
        {
            SlotTalentIds = NormalizeArray(SlotTalentIds, MainSlotCount);
            ReserveTalentIds = NormalizeArray(ReserveTalentIds, ReserveSlotCount);
        }

        public IEnumerable<string> GetMainIds() => GetNonEmpty(SlotTalentIds);
        public IEnumerable<string> GetReserveIds() => GetNonEmpty(ReserveTalentIds);
        public IEnumerable<string> GetCarriedIds() => GetMainIds().Concat(GetReserveIds());

        // 兼容旧调用：在所有调用点迁移完成前，它仍只表示开场激活的六个主槽。
        public IEnumerable<string> GetAllEquippedIds() => GetMainIds();

        public bool HasDuplicateCarriedIds()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in GetCarriedIds())
            {
                if (!seen.Add(id)) return true;
            }
            return false;
        }

        public bool CanEquip(int slotIndex, TalentTier talentTier)
        {
            if (slotIndex < 0 || slotIndex >= MainSlotCount) return false;
            TalentTier slotTier = GetSlotTier(slotIndex);
            return talentTier <= slotTier; // 向下兼容
        }

        public bool CanEquipReserve(int slotIndex, TalentTier talentTier)
        {
            if (slotIndex < 0 || slotIndex >= ReserveSlotCount) return false;
            return talentTier <= GetReserveSlotTier(slotIndex);
        }

        public static TalentTier GetSlotTier(int index) => index switch
        {
            0 => TalentTier.Large,
            1 or 2 => TalentTier.Medium,
            _ => TalentTier.Small
        };

        private static TalentTier GetReserveSlotTier(int index) => index == 0
            ? TalentTier.Medium
            : TalentTier.Small;

        private static string[] NormalizeArray(string[] source, int length)
        {
            string[] normalized = new string[length];
            if (source != null)
                Array.Copy(source, normalized, Math.Min(source.Length, length));
            return normalized;
        }

        private static IEnumerable<string> GetNonEmpty(IEnumerable<string> ids)
        {
            return (ids ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id));
        }
    }
}
