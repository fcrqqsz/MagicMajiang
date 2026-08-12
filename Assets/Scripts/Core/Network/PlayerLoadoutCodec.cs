using System;
using System.Collections.Generic;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    /// <summary>Server-owned reconstruction of an accepted player loadout.</summary>
    public sealed class TrustedPlayerLoadout
    {
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion { get; }
        public DeckConfig DeckConfig { get; }
        public TalentSlotConfig TalentConfig { get; }
        public AlienationPreset AlienationPreset { get; }
        public int TotalAlienation { get; }

        internal TrustedPlayerLoadout(
            int schemaVersion,
            DeckConfig deckConfig,
            TalentSlotConfig talentConfig,
            AlienationPreset alienationPreset,
            int totalAlienation)
        {
            SchemaVersion = schemaVersion;
            DeckConfig = deckConfig;
            TalentConfig = talentConfig;
            AlienationPreset = alienationPreset;
            TotalAlienation = totalAlienation;
        }
    }

    /// <summary>Explicit JsonUtility-safe codec and validation boundary for player-owned loadouts.</summary>
    public static class PlayerLoadoutCodec
    {
        private const int DeckEntryCount = 34;

        public static PlayerLoadoutMessage CreateMessage(DeckConfig deckConfig, TalentSlotConfig talentConfig)
        {
            return CreateMessage(deckConfig, talentConfig, AlienationPreset.Standard);
        }

        public static PlayerLoadoutMessage CreateMessage(
            DeckConfig deckConfig,
            TalentSlotConfig talentConfig,
            AlienationPreset alienationPreset)
        {
            var entries = new DeckTileCountMessage[DeckEntryCount];
            int entryIndex = 0;
            foreach (var tileType in EnumerateLegalTileTypes())
            {
                entries[entryIndex++] = new DeckTileCountMessage
                {
                    suit = (int)tileType.Suit,
                    value = tileType.Value,
                    count = deckConfig?.GetCardCount(tileType.Suit, tileType.Value) ?? 0
                };
            }

            return new PlayerLoadoutMessage
            {
                schemaVersion = TrustedPlayerLoadout.CurrentSchemaVersion,
                alienationPreset = (int)alienationPreset,
                deckEntries = entries,
                mainTalentSlotIds = CopySlotIds(talentConfig?.SlotTalentIds, TalentSlotConfig.MainSlotCount),
                reserveTalentSlotIds = CopySlotIds(talentConfig?.ReserveTalentIds, TalentSlotConfig.ReserveSlotCount)
            };
        }

        public static TrustedPlayerLoadout CreateStandardLoadout()
        {
            var deckConfig = DeckConfig.CreateStandard();
            var talentConfig = new TalentSlotConfig();
            return new TrustedPlayerLoadout(
                TrustedPlayerLoadout.CurrentSchemaVersion,
                deckConfig,
                talentConfig,
                AlienationPreset.Standard,
                AlienationBudgetPolicy.Calculate(deckConfig, talentConfig.GetMainIds(), TalentRegistry.Instance));
        }

        public static bool TryCreateMessage(DeckConfig deckConfig, TalentSlotConfig talentConfig, out PlayerLoadoutMessage message, out string errorCode)
        {
            return TryCreateMessage(deckConfig, talentConfig, AlienationPreset.Standard, out message, out errorCode);
        }

        public static bool TryCreateMessage(
            DeckConfig deckConfig,
            TalentSlotConfig talentConfig,
            AlienationPreset alienationPreset,
            out PlayerLoadoutMessage message,
            out string errorCode)
        {
            message = CreateMessage(deckConfig, talentConfig, alienationPreset);
            if (!AlienationBudgetPolicy.IsDefined(alienationPreset))
            {
                message = null;
                errorCode = PlayerLoadoutErrorCodes.InvalidAlienationPreset;
                return false;
            }
            if (!TryBuildDeck(message.deckEntries, out _))
            {
                message = null;
                errorCode = PlayerLoadoutErrorCodes.InvalidDeck;
                return false;
            }
            if (!TryBuildTalents(message.mainTalentSlotIds, message.reserveTalentSlotIds, out _))
            {
                message = null;
                errorCode = PlayerLoadoutErrorCodes.InvalidTalent;
                return false;
            }

            errorCode = null;
            return true;
        }

        // Transitional compatibility for callers that validate a local loadout before room admission.
        public static bool TryDecode(PlayerLoadoutMessage message, out TrustedPlayerLoadout loadout, out string errorCode) =>
            TryDecodeInternal(message, null, out loadout, out errorCode);

        public static bool TryDecode(
            PlayerLoadoutMessage message,
            AlienationPreset preset,
            out TrustedPlayerLoadout loadout,
            out string errorCode)
        {
            return TryDecodeInternal(message, preset, out loadout, out errorCode);
        }

        private static bool TryDecodeInternal(
            PlayerLoadoutMessage message,
            AlienationPreset? preset,
            out TrustedPlayerLoadout loadout,
            out string errorCode)
        {
            loadout = null;
            errorCode = null;
            if (message == null)
            {
                errorCode = PlayerLoadoutErrorCodes.MissingLoadout;
                return false;
            }
            if (message.schemaVersion != TrustedPlayerLoadout.CurrentSchemaVersion)
            {
                errorCode = PlayerLoadoutErrorCodes.UnsupportedLoadoutVersion;
                return false;
            }
            AlienationPreset messagePreset = (AlienationPreset)message.alienationPreset;
            if (!AlienationBudgetPolicy.IsDefined(messagePreset))
            {
                errorCode = PlayerLoadoutErrorCodes.InvalidAlienationPreset;
                return false;
            }
            if (preset.HasValue && !AlienationBudgetPolicy.IsDefined(preset.Value))
            {
                errorCode = PlayerLoadoutErrorCodes.InvalidAlienationPreset;
                return false;
            }
            if (preset.HasValue && messagePreset != preset.Value)
            {
                errorCode = PlayerLoadoutErrorCodes.AlienationPresetMismatch;
                return false;
            }
            if (!TryBuildDeck(message.deckEntries, out DeckConfig deck))
            {
                errorCode = PlayerLoadoutErrorCodes.InvalidDeck;
                return false;
            }
            if (!TryBuildTalents(message.mainTalentSlotIds, message.reserveTalentSlotIds, out TalentSlotConfig talents))
            {
                errorCode = PlayerLoadoutErrorCodes.InvalidTalent;
                return false;
            }

            int total = AlienationBudgetPolicy.Calculate(deck, talents.GetMainIds(), TalentRegistry.Instance);
            if (preset.HasValue && total > AlienationBudgetPolicy.GetLimit(preset.Value))
            {
                errorCode = PlayerLoadoutErrorCodes.AlienationLimitExceeded;
                return false;
            }

            loadout = new TrustedPlayerLoadout(message.schemaVersion, deck, talents, messagePreset, total);
            return true;
        }

        public static TrustedPlayerLoadout CloneTrustedLoadout(TrustedPlayerLoadout loadout)
        {
            if (loadout == null) return null;
            PlayerLoadoutMessage message = CreateMessage(
                loadout.DeckConfig, loadout.TalentConfig, loadout.AlienationPreset);
            return TryBuildDeck(message.deckEntries, out DeckConfig deck)
                   && TryBuildTalents(message.mainTalentSlotIds, message.reserveTalentSlotIds, out TalentSlotConfig talents)
                ? new TrustedPlayerLoadout(
                    TrustedPlayerLoadout.CurrentSchemaVersion,
                    deck,
                    talents,
                    loadout.AlienationPreset,
                    AlienationBudgetPolicy.Calculate(deck, talents.GetMainIds(), TalentRegistry.Instance))
                : null;
        }

        private static bool TryBuildDeck(DeckTileCountMessage[] entries, out DeckConfig deckConfig)
        {
            deckConfig = null;
            if (entries == null || entries.Length != DeckEntryCount) return false;

            var counts = new Dictionary<int, int>(DeckEntryCount);
            long totalCount = 0;
            foreach (DeckTileCountMessage entry in entries)
            {
                if (entry == null || !TryGetLegalTileType(entry.suit, entry.value, out Suit suit) || entry.count < 0)
                    return false;

                int key = GetTileKey(suit, entry.value);
                if (!counts.TryAdd(key, entry.count)) return false;
                totalCount += entry.count;
            }
            if (counts.Count != DeckEntryCount || totalCount != DeckEntryCount) return false;

            var rebuilt = new DeckConfig();
            foreach (var tileType in EnumerateLegalTileTypes())
            {
                int key = GetTileKey(tileType.Suit, tileType.Value);
                if (!counts.TryGetValue(key, out int count)) return false;
                rebuilt.SetCardCount(tileType.Suit, tileType.Value, count);
            }

            rebuilt.CalculateAlienationScore();
            deckConfig = rebuilt;
            return true;
        }

        private static bool TryBuildTalents(
            string[] mainTalentSlotIds,
            string[] reserveTalentSlotIds,
            out TalentSlotConfig talentConfig)
        {
            talentConfig = null;
            if (mainTalentSlotIds == null || mainTalentSlotIds.Length != TalentSlotConfig.MainSlotCount
                || reserveTalentSlotIds == null || reserveTalentSlotIds.Length != TalentSlotConfig.ReserveSlotCount)
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
                if (!TalentRegistry.Instance.HasTalent(talentId)
                    || !rebuilt.CanEquip(slotIndex, TalentRegistry.Instance.GetTier(talentId))) return false;
            }
            for (int slotIndex = 0; slotIndex < TalentSlotConfig.ReserveSlotCount; slotIndex++)
            {
                string talentId = rebuilt.ReserveTalentIds[slotIndex];
                if (string.IsNullOrEmpty(talentId)) continue;
                if (!TalentRegistry.Instance.HasTalent(talentId)
                    || !rebuilt.CanEquipReserve(slotIndex, TalentRegistry.Instance.GetTier(talentId))
                    || TalentRegistry.Instance.GetMetadata(talentId).SideboardPolicy != TalentSideboardPolicy.Flexible)
                {
                    return false;
                }
            }

            talentConfig = rebuilt;
            return true;
        }

        private static string[] CopySlotIds(string[] source, int length)
        {
            string[] copy = new string[length];
            if (source != null) Array.Copy(source, copy, Math.Min(source.Length, length));
            return copy;
        }

        private static string[] NormalizeSlotIds(string[] source)
        {
            string[] normalized = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
                normalized[i] = string.IsNullOrWhiteSpace(source[i]) ? null : source[i].Trim();
            return normalized;
        }

        private static IEnumerable<(Suit Suit, int Value)> EnumerateLegalTileTypes()
        {
            for (int value = 1; value <= 9; value++) yield return (Suit.Man, value);
            for (int value = 1; value <= 9; value++) yield return (Suit.Pin, value);
            for (int value = 1; value <= 9; value++) yield return (Suit.Sou, value);
            for (int value = 1; value <= 4; value++) yield return (Suit.Wind, value);
            for (int value = 1; value <= 3; value++) yield return (Suit.Dragon, value);
        }

        private static bool TryGetLegalTileType(int suitValue, int value, out Suit suit)
        {
            suit = (Suit)suitValue;
            int maxValue = suit switch
            {
                Suit.Man or Suit.Pin or Suit.Sou => 9,
                Suit.Wind => 4,
                Suit.Dragon => 3,
                _ => 0
            };
            return maxValue > 0 && value >= 1 && value <= maxValue;
        }

        private static int GetTileKey(Suit suit, int value) => ((int)suit * 10) + value;
    }
}
