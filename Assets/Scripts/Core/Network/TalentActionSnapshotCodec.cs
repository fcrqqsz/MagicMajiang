using System;
using System.Linq;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public static class TalentActionSnapshotCodec
    {
        public static SnapshotTalentActionOption ToSnapshot(TalentActionOption option)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.TalentId)) return null;
            return new SnapshotTalentActionOption
            {
                talentId = option.TalentId,
                targetSeatIndex = option.TargetSeatIndex,
                targetTalentId = option.TargetTalentId,
                targetPublicCharge = option.TargetPublicCharge,
                choice = ToSnapshot(option.Choice)
            };
        }

        public static TalentActionOption FromSnapshot(SnapshotTalentActionOption option)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.talentId)) return null;
            TalentChoiceSet choice = null;
            if (option.choice != null)
            {
                try
                {
                    TalentChoiceOption[] choices = (option.choice.options
                                                     ?? Array.Empty<SnapshotTalentChoiceOption>())
                        .Where(candidate => candidate != null)
                        .Select(candidate => new TalentChoiceOption(
                            candidate.choiceId,
                            candidate.displayKey,
                            candidate.value,
                            candidate.tile?.isValid == true
                                ? TalentTileFacts.FromTile(candidate.tile.ToTileData())
                                : null))
                        .ToArray();
                    choice = new TalentChoiceSet(
                        (TalentChoiceKind)option.choice.kind,
                        option.choice.promptKey,
                        option.choice.defaultChoiceId,
                        choices);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            return new TalentActionOption
            {
                TalentId = option.talentId,
                TargetSeatIndex = option.targetSeatIndex,
                TargetTalentId = option.targetTalentId,
                TargetPublicCharge = option.targetPublicCharge,
                Choice = choice
            };
        }

        public static SnapshotTalentActionOption CloneSnapshot(
            SnapshotTalentActionOption option)
        {
            if (option == null) return null;
            return new SnapshotTalentActionOption
            {
                talentId = option.talentId,
                targetSeatIndex = option.targetSeatIndex,
                targetTalentId = option.targetTalentId,
                targetPublicCharge = option.targetPublicCharge,
                choice = option.choice == null
                    ? null
                    : new SnapshotTalentChoiceSet
                    {
                        kind = option.choice.kind,
                        promptKey = option.choice.promptKey,
                        defaultChoiceId = option.choice.defaultChoiceId,
                        options = (option.choice.options ?? Array.Empty<SnapshotTalentChoiceOption>())
                            .Select(candidate => candidate == null
                                ? null
                                : new SnapshotTalentChoiceOption
                                {
                                    choiceId = candidate.choiceId,
                                    displayKey = candidate.displayKey,
                                    value = candidate.value,
                                    tile = CloneTile(candidate.tile)
                                })
                            .ToArray()
                    }
            };
        }

        private static SnapshotTalentChoiceSet ToSnapshot(TalentChoiceSet choice)
        {
            if (choice == null) return null;
            return new SnapshotTalentChoiceSet
            {
                kind = (int)choice.Kind,
                promptKey = choice.PromptKey,
                defaultChoiceId = choice.DefaultChoiceId,
                options = choice.Options.Select(option => new SnapshotTalentChoiceOption
                {
                    choiceId = option.ChoiceId,
                    displayKey = option.DisplayKey,
                    value = option.Value,
                    tile = option.Tile == null
                        ? null
                        : new SimpleTileData
                        {
                            suit = (int)option.Tile.Suit,
                            value = option.Tile.Value,
                            ownerId = option.Tile.OriginalOwnerId,
                            isValid = true
                        }
                }).ToArray()
            };
        }

        private static SimpleTileData CloneTile(SimpleTileData tile) => tile == null
            ? null
            : new SimpleTileData
            {
                suit = tile.suit,
                value = tile.value,
                ownerId = tile.ownerId,
                isValid = tile.isValid
            };
    }
}
