using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.UI
{
    public sealed class AiLoadoutValidation
    {
        public bool IsValid { get; internal set; }
        public string ErrorCode { get; internal set; }
        public int TotalTiles { get; internal set; }
        public int DeckAlienation { get; internal set; }
        public int TalentAlienation { get; internal set; }
        public int TotalAlienation { get; internal set; }
        public int BudgetLimit { get; internal set; }
    }

    /// <summary>Deep-copied, room-scoped AI loadout draft shared by quick and advanced editors.</summary>
    public sealed class AiLoadoutDraft
    {
        private AiDifficulty _baselineDifficulty;
        private AiLoadoutTemplate _baselineTemplate;
        private PlayerLoadoutMessage _baselineLoadout;
        private PlayerLoadoutMessage _loadout;

        public AiDifficulty Difficulty { get; private set; }
        public AiLoadoutTemplate Template { get; private set; }
        public AlienationPreset RoomPreset { get; }
        public bool IsDirty { get; private set; }

        public AiLoadoutDraft(
            AiDifficulty difficulty,
            AiLoadoutTemplate template,
            PlayerLoadoutMessage loadout,
            AlienationPreset roomPreset)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (!AlienationBudgetPolicy.IsDefined(roomPreset)) throw new ArgumentOutOfRangeException(nameof(roomPreset));
            RoomPreset = roomPreset;
            Difficulty = difficulty;
            Template = template;
            _loadout = CloneMessage(loadout);
            MarkSaved();
        }

        public PlayerLoadoutMessage ToMessage() => CloneMessage(_loadout);

        public AiLoadoutDraft Clone()
        {
            var clone = new AiLoadoutDraft(Difficulty, Template, _loadout, RoomPreset);
            if (IsDirty) clone.IsDirty = true;
            return clone;
        }

        public void SetDifficulty(AiDifficulty difficulty)
        {
            if (Difficulty == difficulty) return;
            Difficulty = difficulty;
            IsDirty = true;
        }

        public void SetTemplate(AiLoadoutTemplate template)
        {
            if (Template == template) return;
            Template = template;
            IsDirty = true;
        }

        public void ReplaceLoadout(AiLoadoutTemplate template, PlayerLoadoutMessage loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            Template = template;
            _loadout = CloneMessage(loadout);
            IsDirty = true;
        }

        public bool SetTileCount(int suit, int value, int count)
        {
            DeckTileCountMessage entry = (_loadout.deckEntries ?? Array.Empty<DeckTileCountMessage>())
                .FirstOrDefault(item => item != null && item.suit == suit && item.value == value);
            if (entry == null) return false;
            int next = Math.Max(0, Math.Min(34, count));
            if (entry.count == next) return true;
            entry.count = next;
            Template = AiLoadoutTemplate.Custom;
            IsDirty = true;
            return true;
        }

        public bool SetMainTalent(int slotIndex, string talentId)
        {
            if (slotIndex < 0 || slotIndex >= (_loadout.mainTalentSlotIds?.Length ?? 0)) return false;
            if (string.Equals(_loadout.mainTalentSlotIds[slotIndex], talentId, StringComparison.Ordinal)) return true;
            _loadout.mainTalentSlotIds[slotIndex] = talentId;
            Template = AiLoadoutTemplate.Custom;
            IsDirty = true;
            return true;
        }

        public bool SetReserveTalent(int slotIndex, string talentId)
        {
            if (slotIndex < 0 || slotIndex >= (_loadout.reserveTalentSlotIds?.Length ?? 0)) return false;
            if (string.Equals(_loadout.reserveTalentSlotIds[slotIndex], talentId, StringComparison.Ordinal)) return true;
            _loadout.reserveTalentSlotIds[slotIndex] = talentId;
            Template = AiLoadoutTemplate.Custom;
            IsDirty = true;
            return true;
        }

        public AiLoadoutValidation Validate()
        {
            int totalTiles = (_loadout.deckEntries ?? Array.Empty<DeckTileCountMessage>())
                .Where(entry => entry != null)
                .Sum(entry => entry.count);
            int limit = AlienationBudgetPolicy.GetLimit(RoomPreset);
            var result = new AiLoadoutValidation
            {
                TotalTiles = totalTiles,
                BudgetLimit = limit,
                IsValid = false
            };

            if (totalTiles != 34)
            {
                result.ErrorCode = PlayerLoadoutErrorCodes.InvalidDeck;
                return result;
            }

            PlayerLoadoutMessage candidate = CloneMessage(_loadout);
            candidate.alienationPreset = (int)RoomPreset;
            if (!PlayerLoadoutCodec.TryDecode(candidate, out TrustedPlayerLoadout trusted, out string error))
            {
                result.ErrorCode = error;
                return result;
            }

            result.DeckAlienation = trusted.DeckConfig.AlienationScore;
            result.TotalAlienation = trusted.TotalAlienation;
            result.TalentAlienation = Math.Max(0, result.TotalAlienation - result.DeckAlienation);
            if (result.TotalAlienation > limit)
            {
                result.ErrorCode = PlayerLoadoutErrorCodes.AlienationLimitExceeded;
                return result;
            }

            result.IsValid = true;
            return result;
        }

        public void RestoreBaseline()
        {
            Difficulty = _baselineDifficulty;
            Template = _baselineTemplate;
            _loadout = CloneMessage(_baselineLoadout);
            IsDirty = false;
        }

        public void MarkSaved()
        {
            _baselineDifficulty = Difficulty;
            _baselineTemplate = Template;
            _baselineLoadout = CloneMessage(_loadout);
            IsDirty = false;
        }

        private static PlayerLoadoutMessage CloneMessage(PlayerLoadoutMessage source)
        {
            return new PlayerLoadoutMessage
            {
                schemaVersion = source.schemaVersion,
                alienationPreset = source.alienationPreset,
                deckEntries = (source.deckEntries ?? Array.Empty<DeckTileCountMessage>())
                    .Select(entry => entry == null ? null : new DeckTileCountMessage
                    {
                        suit = entry.suit,
                        value = entry.value,
                        count = entry.count
                    }).ToArray(),
                mainTalentSlotIds = source.mainTalentSlotIds?.ToArray() ?? Array.Empty<string>(),
                reserveTalentSlotIds = source.reserveTalentSlotIds?.ToArray() ?? Array.Empty<string>()
            };
        }
    }
}
