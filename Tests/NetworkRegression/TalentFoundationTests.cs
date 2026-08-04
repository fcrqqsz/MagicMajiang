using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network.Data;
using MahjongGame.Talents;

internal static class TalentFoundationTests
{
    public static void Run(RegressionRunner runner)
    {
        LegacySlotsNormalizeWithoutDiscardingMainLoadout(runner);
        CarriedIdsEnumerateMainBeforeReserve(runner);
        DuplicateValidationCoversAllCarriedSlots(runner);
        ReserveSlotsEnforceTheirFixedTiers(runner);
        ProfileNormalizationRepairsLegacyDeckTalentSchema(runner);
        MetadataPreservesDefaultsAndExplicitPolicies(runner);
        ExistingTalentsRemainRegistrable(runner);
    }

    private static void LegacySlotsNormalizeWithoutDiscardingMainLoadout(RegressionRunner runner)
    {
        TalentSlotConfig legacy = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = null
        };

        legacy.Normalize();

        runner.Check(legacy.SlotTalentIds.Length == TalentSlotConfig.MainSlotCount,
            "legacy main slots normalize to six");
        runner.Check(legacy.ReserveTalentIds.Length == TalentSlotConfig.ReserveSlotCount,
            "legacy save without reserve slots normalizes to three empty entries");
        runner.Check(legacy.GetCarriedIds().SequenceEqual(new[] { "midas_touch" }),
            "legacy normalization preserves its main talent as the carried loadout");
    }

    private static void CarriedIdsEnumerateMainBeforeReserve(RegressionRunner runner)
    {
        TalentSlotConfig config = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "main_large", null, "main_medium", null, null, null },
            ReserveTalentIds = new[] { "reserve_medium", null, "reserve_small" }
        };

        runner.Check(config.GetCarriedIds().SequenceEqual(new[]
            { "main_large", "main_medium", "reserve_medium", "reserve_small" }),
            "carried ids enumerate all non-empty main slots before reserve slots");
        runner.Check(config.GetAllEquippedIds().SequenceEqual(new[] { "main_large", "main_medium" }),
            "legacy equipped ids continue to represent only active main slots");
    }

    private static void DuplicateValidationCoversAllCarriedSlots(RegressionRunner runner)
    {
        TalentSlotConfig duplicateAcrossAreas = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "midas_touch", null, null, null, null, null },
            ReserveTalentIds = new[] { "midas_touch", "", null }
        };
        TalentSlotConfig emptyIdsOnly = new TalentSlotConfig
        {
            SlotTalentIds = new[] { "", null, null, null, null, null },
            ReserveTalentIds = new[] { "", null, null }
        };

        runner.Check(duplicateAcrossAreas.HasDuplicateCarriedIds(),
            "duplicate validation rejects one id shared by main and reserve slots");
        runner.Check(!emptyIdsOnly.HasDuplicateCarriedIds(),
            "duplicate validation ignores empty slot values");
    }

    private static void ProfileNormalizationRepairsLegacyDeckTalentSchema(RegressionRunner runner)
    {
        PlayerProfile profile = new PlayerProfile
        {
            Settings = null,
            SavedDecks = new System.Collections.Generic.List<SavedDeck>
            {
                new SavedDeck
                {
                    Talents = new TalentSlotConfig
                    {
                        SlotTalentIds = new[] { "starting_capital", null, null, null, null, null },
                        ReserveTalentIds = null
                    }
                },
                new SavedDeck { Talents = null }
            }
        };

        profile.Normalize();

        runner.Check(profile.Settings != null,
            "profile normalization restores missing settings");
        runner.Check(profile.SavedDecks[0].Talents.ReserveTalentIds.Length == TalentSlotConfig.ReserveSlotCount
                     && profile.SavedDecks[0].Talents.SlotTalentIds[0] == "starting_capital",
            "profile normalization upgrades legacy deck reserves without changing main slots");
        runner.Check(profile.SavedDecks[1].Talents.GetCarriedIds().Count() == 0,
            "profile normalization restores a missing deck talent config");
    }

    private static void ReserveSlotsEnforceTheirFixedTiers(RegressionRunner runner)
    {
        TalentSlotConfig config = new TalentSlotConfig();

        runner.Check(config.CanEquipReserve(0, TalentTier.Medium),
            "the first reserve slot accepts medium talents");
        runner.Check(!config.CanEquipReserve(0, TalentTier.Large),
            "reserve slots never accept large talents");
        runner.Check(config.CanEquipReserve(1, TalentTier.Small)
                     && !config.CanEquipReserve(1, TalentTier.Medium),
            "small reserve slots accept only small talents");
    }

    private static void MetadataPreservesDefaultsAndExplicitPolicies(RegressionRunner runner)
    {
        TalentMetadata startingCapital = TalentRegistry.Instance.GetMetadata("starting_capital");
        TalentMetadata peek = TalentRegistry.Instance.GetMetadata("peek");

        runner.Check(startingCapital.StateScope == TalentStateScope.Match,
            "starting capital metadata persists for the whole match");
        runner.Check(startingCapital.RevealPolicy == TalentRevealPolicy.PublicAtMatchStart,
            "starting capital becomes public when the match starts");
        runner.Check(startingCapital.SideboardPolicy == TalentSideboardPolicy.MainOnlyLocked,
            "starting capital is marked as locked main-only metadata");
        runner.Check(peek.StateScope == TalentStateScope.Round
                     && peek.ActivationWindow == TalentActivationWindow.None
                     && peek.RevealPolicy == TalentRevealPolicy.OwnerOnly
                     && peek.SideboardPolicy == TalentSideboardPolicy.Flexible,
            "peek keeps default state, activation, and sideboard metadata while remaining owner-only");
    }

    private static void ExistingTalentsRemainRegistrable(RegressionRunner runner)
    {
        string[] existingIds =
        {
            "midas_touch", "peek", "dragon_ascent", "draw_reward", "head_start", "starting_capital"
        };

        runner.Check(existingIds.All(TalentRegistry.Instance.HasTalent),
            "all existing talent ids remain available through registry reflection");
        runner.Check(existingIds.All(id => TalentRegistry.Instance.CreateInstance(id, 2)?.Id == id),
            "all existing talent ids still create their matching rule instances");
    }
}
