using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Agents
{
    /// <summary>Builds deterministic AI loadouts through the same slot and budget admission as players.</summary>
    public static class AiTalentLoadoutFactory
    {
        private static readonly string[][] ArchetypePriorities =
        {
            new[]
            {
                "sheathed_edge", "head_start", "midas_touch", "dragon_ascent",
                "interception", "composure", "peek", "starting_capital", "draw_reward"
            },
            new[]
            {
                "interception", "composure", "sheathed_edge", "head_start",
                "dragon_ascent", "midas_touch", "starting_capital", "draw_reward", "peek"
            },
            new[]
            {
                "peek", "starting_capital", "draw_reward", "midas_touch", "head_start",
                "dragon_ascent", "interception", "composure", "sheathed_edge"
            }
        };

        public static PlayerLoadoutMessage Create(AlienationPreset preset, int seatIndex, int seed)
        {
            int archetypeIndex = PositiveModulo(seed + seatIndex, ArchetypePriorities.Length);
            return CreateInternal(preset, seatIndex, DeckConfig.CreateStandard(), ArchetypePriorities[archetypeIndex]);
        }

        public static PlayerLoadoutMessage Create(AlienationPreset preset, AiLoadoutTemplate template,
            int seatIndex, int seed)
        {
            if (template == AiLoadoutTemplate.Custom)
                throw new ArgumentException("Custom AI loadouts must provide an explicit full loadout.", nameof(template));
            int archetypeIndex = template == AiLoadoutTemplate.Aggressive ? 0
                : template == AiLoadoutTemplate.Stable ? 1 : 2;
            return CreateInternal(preset, seatIndex, CreateTemplateDeck(template), ArchetypePriorities[archetypeIndex]);
        }

        private static PlayerLoadoutMessage CreateInternal(AlienationPreset preset, int seatIndex,
            DeckConfig deck, string[] priorities)
        {
            if (!AlienationBudgetPolicy.IsDefined(preset))
                throw new ArgumentOutOfRangeException(nameof(preset));
            if (seatIndex < 0 || seatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(seatIndex));

            TalentRegistry registry = TalentRegistry.Instance;
            var config = new TalentSlotConfig();
            var carried = new HashSet<string>(StringComparer.Ordinal);

            foreach (string talentId in priorities)
            {
                if (!registry.HasTalent(talentId) || carried.Contains(talentId)) continue;
                if (!TryFindMainSlot(config, registry.GetTier(talentId), out int slotIndex)) continue;

                config.SlotTalentIds[slotIndex] = talentId;
                if (IsAdmitted(deck, config, preset))
                {
                    carried.Add(talentId);
                }
                else
                {
                    config.SlotTalentIds[slotIndex] = null;
                }
            }

            foreach (string talentId in priorities)
            {
                if (!registry.HasTalent(talentId)
                    || carried.Contains(talentId)
                    || registry.GetMetadata(talentId).SideboardPolicy != TalentSideboardPolicy.Flexible
                    || !TryFindReserveSlot(config, registry.GetTier(talentId), out int slotIndex))
                {
                    continue;
                }

                config.ReserveTalentIds[slotIndex] = talentId;
                if (IsAdmitted(deck, config, preset))
                {
                    carried.Add(talentId);
                }
                else
                {
                    config.ReserveTalentIds[slotIndex] = null;
                }
            }

            return PlayerLoadoutCodec.CreateMessage(deck, config, preset);
        }

        private static DeckConfig CreateTemplateDeck(AiLoadoutTemplate template)
        {
            DeckConfig deck = DeckConfig.CreateStandard();
            if (template == AiLoadoutTemplate.Aggressive)
            {
                deck.SetCardCount(Suit.Man, 1, 0);
                deck.SetCardCount(Suit.Man, 9, 0);
                deck.SetCardCount(Suit.Man, 4, 2);
                deck.SetCardCount(Suit.Man, 6, 2);
            }
            else if (template == AiLoadoutTemplate.TalentSynergy)
            {
                deck.SetCardCount(Suit.Wind, 4, 0);
                deck.SetCardCount(Suit.Wind, 1, 2);
                deck.SetCardCount(Suit.Dragon, 3, 0);
                deck.SetCardCount(Suit.Dragon, 1, 2);
            }
            deck.CalculateAlienationScore();
            return deck;
        }

        private static bool IsAdmitted(DeckConfig deck, TalentSlotConfig config, AlienationPreset preset)
        {
            PlayerLoadoutMessage candidate = PlayerLoadoutCodec.CreateMessage(
                deck, config, preset);
            return PlayerLoadoutCodec.TryDecode(candidate, preset, out _, out _);
        }

        private static bool TryFindMainSlot(TalentSlotConfig config, TalentTier tier, out int slotIndex)
        {
            return TryFindSlot(
                config.SlotTalentIds,
                TalentSlotConfig.MainSlotCount,
                index => config.CanEquip(index, tier),
                out slotIndex);
        }

        private static bool TryFindReserveSlot(TalentSlotConfig config, TalentTier tier, out int slotIndex)
        {
            return TryFindSlot(
                config.ReserveTalentIds,
                TalentSlotConfig.ReserveSlotCount,
                index => config.CanEquipReserve(index, tier),
                out slotIndex);
        }

        private static bool TryFindSlot(
            IReadOnlyList<string> slots,
            int slotCount,
            Func<int, bool> accepts,
            out int slotIndex)
        {
            for (int index = slotCount - 1; index >= 0; index--)
            {
                if (string.IsNullOrEmpty(slots[index]) && accepts(index))
                {
                    slotIndex = index;
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }
    }
}
