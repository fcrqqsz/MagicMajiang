using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;

internal static class AiDecisionStrategyTests
{
    public static void Run(RegressionRunner runner)
    {
        CalculatesStandardAndSpecialHandShanten(runner);
        BeginnerStrategyIsDeterministicAndPrefersHonors(runner);
        StandardStrategyKeepsPureHandPotential(runner);
        StandardStrategyAlwaysAcceptsLegalHu(runner);
        DecisionContextOwnsImmutableCopies(runner);
        StandardStrategyAcceptsImprovingChi(runner);
    }

    private static void CalculatesStandardAndSpecialHandShanten(RegressionRunner runner)
    {
        var standardReady = Tiles(
            (Suit.Man, 1), (Suit.Man, 2), (Suit.Man, 3),
            (Suit.Pin, 1), (Suit.Pin, 2), (Suit.Pin, 3),
            (Suit.Sou, 1), (Suit.Sou, 2), (Suit.Sou, 3),
            (Suit.Wind, 1), (Suit.Wind, 1), (Suit.Wind, 1),
            (Suit.Dragon, 1));
        var sevenPairsReady = Tiles(
            (Suit.Man, 1), (Suit.Man, 1), (Suit.Man, 2), (Suit.Man, 2),
            (Suit.Pin, 3), (Suit.Pin, 3), (Suit.Pin, 4), (Suit.Pin, 4),
            (Suit.Sou, 5), (Suit.Sou, 5), (Suit.Wind, 1), (Suit.Wind, 1),
            (Suit.Dragon, 1));
        var orphansReady = Tiles(
            (Suit.Man, 1), (Suit.Man, 9), (Suit.Pin, 1), (Suit.Pin, 9),
            (Suit.Sou, 1), (Suit.Sou, 9),
            (Suit.Wind, 1), (Suit.Wind, 2), (Suit.Wind, 3), (Suit.Wind, 4),
            (Suit.Dragon, 1), (Suit.Dragon, 2), (Suit.Dragon, 3));

        runner.Check(AiHandShapeEvaluator.CalculateShanten(standardReady, Array.Empty<Meld>()) == 0
                     && AiHandShapeEvaluator.CalculateShanten(sevenPairsReady, Array.Empty<Meld>()) == 0
                     && AiHandShapeEvaluator.CalculateShanten(orphansReady, Array.Empty<Meld>()) == 0,
            "AI hand evaluator recognizes standard, seven-pairs, and thirteen-orphans ready shapes.");
    }

    private static void BeginnerStrategyIsDeterministicAndPrefersHonors(RegressionRunner runner)
    {
        List<TileData> hand = Tiles(
            (Suit.Man, 1), (Suit.Man, 2), (Suit.Man, 3),
            (Suit.Pin, 1), (Suit.Pin, 2), (Suit.Pin, 3),
            (Suit.Sou, 1), (Suit.Sou, 2), (Suit.Sou, 3),
            (Suit.Man, 4), (Suit.Man, 5), (Suit.Man, 6),
            (Suit.Wind, 1), (Suit.Dragon, 1));
        AiDecisionContext context = AiDecisionContext.ForSelfTurn(41, 2, hand, Array.Empty<Meld>(),
            new AllowedActions(), hand[^1], new ScoringOptions(), 60, 1234);

        AiDecisionResult first = new BeginnerAiDecisionStrategy().Decide(context, CancellationToken.None);
        AiDecisionResult second = new BeginnerAiDecisionStrategy().Decide(context, CancellationToken.None);
        runner.Check(first.ActionType == ClientActionType.Discard
                     && second.ActionType == ClientActionType.Discard
                     && first.TargetTile.ID == second.TargetTile.ID
                     && (first.TargetTile.TileSuit == Suit.Wind || first.TargetTile.TileSuit == Suit.Dragon),
            "Beginner AI preserves honor-first randomness while remaining deterministic for a decision seed.");
    }

    private static void StandardStrategyKeepsPureHandPotential(RegressionRunner runner)
    {
        List<TileData> hand = Tiles(
            (Suit.Man, 1), (Suit.Man, 2), (Suit.Man, 3),
            (Suit.Man, 1), (Suit.Man, 2), (Suit.Man, 3),
            (Suit.Man, 4), (Suit.Man, 5), (Suit.Man, 6),
            (Suit.Man, 7), (Suit.Man, 8), (Suit.Man, 9),
            (Suit.Man, 5), (Suit.Wind, 1));
        AiDecisionContext context = AiDecisionContext.ForSelfTurn(9, 0, hand, Array.Empty<Meld>(),
            new AllowedActions(), hand[^1], new ScoringOptions(), 48, 99);

        AiDecisionResult result = new StandardAiDecisionStrategy().Decide(context, CancellationToken.None);
        runner.Check(result.ActionType == ClientActionType.Discard
                     && result.TargetTile.TileSuit == Suit.Wind,
            $"Standard AI keeps a pure-hand legal wait instead of retaining an unrelated honor " +
            $"(actual={result.TargetTile?.TileSuit}:{result.TargetTile?.Value}).");
    }

    private static void StandardStrategyAlwaysAcceptsLegalHu(RegressionRunner runner)
    {
        List<TileData> hand = Tiles((Suit.Man, 1));
        AiDecisionContext context = AiDecisionContext.ForSelfTurn(7, 1, hand, Array.Empty<Meld>(),
            new AllowedActions { CanHu = true }, hand[0], new ScoringOptions(), 20, 8);
        AiDecisionResult result = new StandardAiDecisionStrategy().Decide(context, CancellationToken.None);
        runner.Check(result.ActionType == ClientActionType.Hu,
            "Standard AI accepts an authoritative legal Hu before evaluating shape.");
    }

    private static void DecisionContextOwnsImmutableCopies(RegressionRunner runner)
    {
        List<TileData> hand = Tiles((Suit.Man, 1), (Suit.Man, 2));
        var meldTiles = Tiles((Suit.Pin, 2), (Suit.Pin, 3), (Suit.Pin, 4));
        var melds = new List<Meld> { new Meld(MeldType.Chi, meldTiles, 1) };
        var options = new ScoringOptions { MinimumFan = 8, BonusFan = 2 };
        AiDecisionContext context = AiDecisionContext.ForSelfTurn(12, 1, hand, melds,
            new AllowedActions(), hand[1], options, 40, 17);

        hand[0].Value = 9;
        meldTiles[0].Value = 9;
        options.MinimumFan = 88;

        runner.Check(context.Hand[0].Value == 1
                     && context.Melds[0].Tiles[0].Value == 2
                     && context.ScoringOptions.MinimumFan == 8,
            "AI decision contexts own deep immutable copies of private hand, meld, and scoring state.");
    }

    private static void StandardStrategyAcceptsImprovingChi(RegressionRunner runner)
    {
        List<TileData> hand = Tiles(
            (Suit.Man, 1), (Suit.Man, 3), (Suit.Man, 5), (Suit.Man, 9),
            (Suit.Pin, 1), (Suit.Pin, 2), (Suit.Pin, 3),
            (Suit.Sou, 1), (Suit.Sou, 2), (Suit.Sou, 3),
            (Suit.Wind, 1), (Suit.Wind, 1), (Suit.Wind, 1));
        var discard = new TileData(Suit.Man, 2, 3) { ID = "chi-trigger" };
        AiDecisionContext context = AiDecisionContext.ForDiscardResponse(22, 0, hand,
            Array.Empty<Meld>(), new AllowedActions { CanChiLeft = true }, discard,
            new[] { new[] { 1, 3 } }, new ScoringOptions(), 40, 123);

        AiDecisionResult result = new StandardAiDecisionStrategy(1).Decide(context, CancellationToken.None);
        runner.Check(result.ActionType == ClientActionType.Chi
                     && result.ChiCombination?.SequenceEqual(new[] { 1, 3 }) == true,
            $"Standard AI accepts a legal chi when the best projected discard strictly improves shape " +
            $"(actual={result.ActionType}).");
    }

    private static List<TileData> Tiles(params (Suit Suit, int Value)[] values)
    {
        return values.Select((value, index) => new TileData(value.Suit, value.Value, 0)
        {
            ID = $"ai-test-{index}"
        }).ToList();
    }
}
