using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public static class SideboardLoadoutPolicy
    {
        private const int MaximumSubmittedIds = TalentSlotConfig.MainSlotCount + TalentSlotConfig.ReserveSlotCount;

        public static bool TryValidate(
            TrustedPlayerLoadout loadout,
            string[] activeTalentIds,
            AlienationPreset preset,
            TalentRegistry registry,
            out string[] normalizedActiveTalentIds,
            out int totalAlienation,
            out string errorCode)
        {
            normalizedActiveTalentIds = Array.Empty<string>();
            totalAlienation = 0;
            errorCode = SideboardErrorCodes.InvalidSelection;
            if (loadout?.TalentConfig == null
                || activeTalentIds == null
                || activeTalentIds.Length > MaximumSubmittedIds
                || registry == null
                || !AlienationBudgetPolicy.IsDefined(preset))
            {
                return false;
            }

            string[] carriedIds = GetCarriedIdsInSlotOrder(loadout.TalentConfig);
            var carried = new HashSet<string>(carriedIds, StringComparer.Ordinal);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string submittedId in activeTalentIds)
            {
                if (string.IsNullOrWhiteSpace(submittedId)) continue;
                string normalizedId = submittedId.Trim();
                if (!carried.Contains(normalizedId) || !selected.Add(normalizedId)) return false;
            }

            foreach (string carriedId in carriedIds)
            {
                if (registry.GetMetadata(carriedId).SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked
                    && !selected.Contains(carriedId))
                {
                    errorCode = SideboardErrorCodes.LockedTalentMissing;
                    return false;
                }
            }

            string[] canonical = carriedIds.Where(selected.Contains).ToArray();
            totalAlienation = AlienationBudgetPolicy.Calculate(loadout.DeckConfig, canonical, registry);
            if (totalAlienation > AlienationBudgetPolicy.GetLimit(preset))
            {
                errorCode = SideboardErrorCodes.AlienationLimitExceeded;
                return false;
            }

            normalizedActiveTalentIds = canonical;
            errorCode = null;
            return true;
        }

        public static string[] GetCarriedIdsInSlotOrder(TalentSlotConfig config)
        {
            if (config == null) return Array.Empty<string>();
            return EnumerateSlots(config.SlotTalentIds, TalentSlotConfig.MainSlotCount)
                .Concat(EnumerateSlots(config.ReserveTalentIds, TalentSlotConfig.ReserveSlotCount))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToArray();
        }

        private static IEnumerable<string> EnumerateSlots(string[] ids, int count)
        {
            for (int index = 0; index < count; index++)
                yield return ids != null && index < ids.Length ? ids[index] : null;
        }
    }
}
