using System;
using System.Collections.Generic;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    /// <summary>Server-owned reconstruction of an accepted player loadout.</summary>
    public sealed class TrustedPlayerLoadout
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; }
        public DeckConfig DeckConfig { get; }
        public TalentSlotConfig TalentConfig { get; }
        public int TotalAlienation { get; }

        internal TrustedPlayerLoadout(int schemaVersion, DeckConfig deckConfig, TalentSlotConfig talentConfig, int totalAlienation)
        {
            SchemaVersion = schemaVersion;
            DeckConfig = deckConfig;
            TalentConfig = talentConfig;
            TotalAlienation = totalAlienation;
        }
    }

    /// <summary>Explicit JsonUtility-safe codec and validation boundary for player-owned loadouts.</summary>
    public static class PlayerLoadoutCodec
    {
        private const int DeckEntryCount = 34;
        private const int TalentSlotCount = 6;

        public static PlayerLoadoutMessage CreateMessage(DeckConfig deckConfig, TalentSlotConfig talentConfig)
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

            var slotIds = new string[TalentSlotCount];
            if (talentConfig?.SlotTalentIds != null)
            {
                int count = Math.Min(talentConfig.SlotTalentIds.Length, TalentSlotCount);
                Array.Copy(talentConfig.SlotTalentIds, slotIds, count);
            }

            return new PlayerLoadoutMessage
            {
                schemaVersion = TrustedPlayerLoadout.CurrentSchemaVersion,
                deckEntries = entries,
                talentSlotIds = slotIds
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
                DeckConfig.CalculateTotalAlienation(deckConfig, talentConfig));
        }

        public static bool TryCreateMessage(DeckConfig deckConfig, TalentSlotConfig talentConfig, out PlayerLoadoutMessage message, out string errorCode)
        {
            if (deckConfig == null)
            {
                message = null;
                errorCode = "InvalidDeck";
                return false;
            }

            if (talentConfig != null && (talentConfig.SlotTalentIds == null || talentConfig.SlotTalentIds.Length != TalentSlotCount))
            {
                message = null;
                errorCode = "InvalidTalent";
                return false;
            }

            message = CreateMessage(deckConfig, talentConfig);
            return TryDecode(message, out _, out errorCode);
        }

        public static bool TryDecode(PlayerLoadoutMessage message, out TrustedPlayerLoadout loadout, out string errorCode)
        {
            loadout = null;
            errorCode = null;

            if (message == null)
            {
                errorCode = "MissingLoadout";
                return false;
            }

            if (message.schemaVersion != TrustedPlayerLoadout.CurrentSchemaVersion)
            {
                errorCode = "UnsupportedLoadoutVersion";
                return false;
            }

            if (!TryBuildDeck(message.deckEntries, out var deckConfig))
            {
                errorCode = "InvalidDeck";
                return false;
            }

            if (!TryBuildTalents(message.talentSlotIds, out var talentConfig))
            {
                errorCode = "InvalidTalent";
                return false;
            }

            loadout = new TrustedPlayerLoadout(
                message.schemaVersion,
                deckConfig,
                talentConfig,
                DeckConfig.CalculateTotalAlienation(deckConfig, talentConfig));
            return true;
        }

        public static TrustedPlayerLoadout CloneTrustedLoadout(TrustedPlayerLoadout loadout)
        {
            if (loadout == null) return null;
            var message = CreateMessage(loadout.DeckConfig, loadout.TalentConfig);
            return TryDecode(message, out var clone, out _) ? clone : null;
        }

        private static bool TryBuildDeck(DeckTileCountMessage[] entries, out DeckConfig deckConfig)
        {
            deckConfig = null;
            if (entries == null || entries.Length != DeckEntryCount) return false;

            var counts = new Dictionary<int, int>(DeckEntryCount);
            long totalCount = 0;
            foreach (var entry in entries)
            {
                if (entry == null || !TryGetLegalTileType(entry.suit, entry.value, out var suit) || entry.count < 0)
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

        private static bool TryBuildTalents(string[] slotIds, out TalentSlotConfig talentConfig)
        {
            talentConfig = null;
            if (slotIds == null || slotIds.Length != TalentSlotCount) return false;

            var rebuilt = new TalentSlotConfig();
            var seenTalentIds = new HashSet<string>(StringComparer.Ordinal);
            for (int slotIndex = 0; slotIndex < TalentSlotCount; slotIndex++)
            {
                string talentId = string.IsNullOrWhiteSpace(slotIds[slotIndex]) ? null : slotIds[slotIndex].Trim();
                if (string.IsNullOrEmpty(talentId)) continue;

                if (!TalentRegistry.Instance.HasTalent(talentId)
                    || !seenTalentIds.Add(talentId)
                    || !rebuilt.CanEquip(slotIndex, TalentRegistry.Instance.GetTier(talentId)))
                {
                    return false;
                }

                rebuilt.SlotTalentIds[slotIndex] = talentId;
            }

            talentConfig = rebuilt;
            return true;
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
