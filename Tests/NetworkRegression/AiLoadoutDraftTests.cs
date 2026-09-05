using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.UI;

internal static class AiLoadoutDraftTests
{
    public static void Run(RegressionRunner runner)
    {
        DeepCopiesWireLoadout(runner);
        TracksDirtyAndRestoresBaseline(runner);
        RejectsInvalidDeckSize(runner);
        RejectsBudgetOverflow(runner);
        ProtectsDirtyDraftBeforeTemplateOverwrite(runner);
        KeepsAdvancedEditorChangesIsolatedUntilAdopted(runner);
    }

    private static void DeepCopiesWireLoadout(RegressionRunner runner)
    {
        PlayerLoadoutMessage source = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Stable, 2, 77);
        AiLoadoutDraft draft = new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Stable, source, AlienationPreset.Standard);

        int before = source.deckEntries[0].count;
        draft.SetTileCount(source.deckEntries[0].suit, source.deckEntries[0].value, before + 1);

        runner.Check(source.deckEntries[0].count == before,
            "AI loadout draft mutations never alter the authoritative wire projection");
        runner.Check(!ReferenceEquals(source.deckEntries, draft.ToMessage().deckEntries),
            "AI loadout draft returns deep-copied deck entries");
    }

    private static void TracksDirtyAndRestoresBaseline(RegressionRunner runner)
    {
        PlayerLoadoutMessage source = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Stable, 1, 12);
        AiLoadoutDraft draft = new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Stable, source, AlienationPreset.Standard);

        runner.Check(!draft.IsDirty, "new AI loadout draft starts clean");
        draft.SetDifficulty(AiDifficulty.Beginner);
        runner.Check(draft.IsDirty, "difficulty changes mark the AI loadout draft dirty");
        draft.RestoreBaseline();
        runner.Check(!draft.IsDirty && draft.Difficulty == AiDifficulty.Standard,
            "restoring the baseline discards unsaved AI loadout edits");
    }

    private static void RejectsInvalidDeckSize(RegressionRunner runner)
    {
        PlayerLoadoutMessage source = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Stable, 1, 15);
        AiLoadoutDraft draft = new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Stable, source, AlienationPreset.Standard);
        DeckTileCountMessage entry = source.deckEntries.First();
        draft.SetTileCount(entry.suit, entry.value, entry.count + 1);

        AiLoadoutValidation validation = draft.Validate();
        runner.Check(!validation.IsValid && validation.TotalTiles == 35,
            "AI loadout draft rejects any deck that is not exactly 34 tiles");
    }

    private static void RejectsBudgetOverflow(RegressionRunner runner)
    {
        PlayerLoadoutMessage source = AiTalentLoadoutFactory.Create(
            AlienationPreset.Low, AiLoadoutTemplate.Stable, 1, 19);
        AiLoadoutDraft draft = new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Custom, source, AlienationPreset.Low);

        DeckTileCountMessage first = source.deckEntries[0];
        DeckTileCountMessage second = source.deckEntries[1];
        draft.SetTileCount(first.suit, first.value, 34);
        draft.SetTileCount(second.suit, second.value, 0);
        foreach (DeckTileCountMessage entry in source.deckEntries.Skip(2))
            draft.SetTileCount(entry.suit, entry.value, 0);

        AiLoadoutValidation validation = draft.Validate();
        runner.Check(!validation.IsValid && validation.TotalTiles == 34
                     && validation.TotalAlienation > validation.BudgetLimit,
            "AI loadout draft reports budget overflow independently from tile-count validity");
    }

    private static void ProtectsDirtyDraftBeforeTemplateOverwrite(RegressionRunner runner)
    {
        PlayerLoadoutMessage stable = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Stable, 1, 29);
        PlayerLoadoutMessage aggressive = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Aggressive, 1, 29);
        var quick = new AiQuickConfigController();
        quick.Select(1, true, new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Stable, stable, AlienationPreset.Standard));
        quick.Draft.SetDifficulty(AiDifficulty.Beginner);

        bool immediatelyApplied = quick.RequestTemplate(AiLoadoutTemplate.Aggressive, aggressive);
        runner.Check(!immediatelyApplied
                     && quick.HasPendingOverwrite
                     && quick.Draft.Template == AiLoadoutTemplate.Stable,
            "switching template never overwrites a modified quick draft before confirmation");
        quick.ConfirmOverwrite();
        runner.Check(!quick.HasPendingOverwrite
                     && quick.Draft.Template == AiLoadoutTemplate.Aggressive,
            "confirming template overwrite replaces only the local room draft");
    }

    private static void KeepsAdvancedEditorChangesIsolatedUntilAdopted(RegressionRunner runner)
    {
        PlayerLoadoutMessage stable = AiTalentLoadoutFactory.Create(
            AlienationPreset.Standard, AiLoadoutTemplate.Stable, 2, 31);
        var quick = new AiQuickConfigController();
        quick.Select(2, false, new AiLoadoutDraft(
            AiDifficulty.Standard, AiLoadoutTemplate.Stable, stable, AlienationPreset.Standard));
        AiLoadoutDraft advanced = quick.Draft.Clone();
        advanced.SetDifficulty(AiDifficulty.Beginner);

        runner.Check(quick.Draft.Difficulty == AiDifficulty.Standard,
            "the shared advanced editor receives an isolated deep copy of the quick draft");
        quick.AdoptAdvancedDraft(advanced);
        runner.Check(quick.Draft.Difficulty == AiDifficulty.Beginner
                     && !ReferenceEquals(quick.Draft, advanced),
            "applying the shared advanced editor adopts another deep copy without profile persistence");
    }
}
