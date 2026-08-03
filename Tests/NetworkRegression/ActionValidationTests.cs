using MahjongGame.Core;

internal static class ActionValidationTests
{
    public static void Run(RegressionRunner runner)
    {
        DoesNotTreatHonorsAsContinuationOfNineSou(runner);
        DoesNotTreatAdjacentFrequencySlotsAsCrossSuitChi(runner);
        PreservesAllLegalChiDirections(runner);
    }

    private static void PreservesAllLegalChiDirections(RegressionRunner runner)
    {
        var cases = new[]
        {
            new { Name = "left", First = 3, Second = 4, CanLeft = true, CanMiddle = false, CanRight = false },
            new { Name = "middle", First = 4, Second = 6, CanLeft = false, CanMiddle = true, CanRight = false },
            new { Name = "right", First = 6, Second = 7, CanLeft = false, CanMiddle = false, CanRight = true }
        };

        foreach (var testCase in cases)
        {
            var hand = new List<TileData>
            {
                Tile(Suit.Sou, testCase.First),
                Tile(Suit.Sou, testCase.Second)
            };
            var actions = ActionValidator.CheckActions(
                hand,
                new List<Meld>(),
                Tile(Suit.Sou, 5),
                isNextPlayer: true);

            runner.Check(
                actions.CanChiLeft == testCase.CanLeft
                && actions.CanChiMiddle == testCase.CanMiddle
                && actions.CanChiRight == testCase.CanRight,
                $"Action validation should set only the {testCase.Name} Chi flag for its legal same-suit combination.");
        }
    }

    private static void DoesNotTreatAdjacentFrequencySlotsAsCrossSuitChi(RegressionRunner runner)
    {
        var cases = new[]
        {
            new { Name = "9-Man followed by 1/2-Pin", Discard = Tile(Suit.Man, 9), First = Tile(Suit.Pin, 1), Second = Tile(Suit.Pin, 2) },
            new { Name = "9-Pin followed by 1/2-Sou", Discard = Tile(Suit.Pin, 9), First = Tile(Suit.Sou, 1), Second = Tile(Suit.Sou, 2) },
            new { Name = "1-Pin preceded by 8/9-Man", Discard = Tile(Suit.Pin, 1), First = Tile(Suit.Man, 8), Second = Tile(Suit.Man, 9) },
            new { Name = "1-Sou preceded by 8/9-Pin", Discard = Tile(Suit.Sou, 1), First = Tile(Suit.Pin, 8), Second = Tile(Suit.Pin, 9) }
        };

        foreach (var testCase in cases)
        {
            var hand = new List<TileData> { testCase.First, testCase.Second };
            var actions = ActionValidator.CheckActions(
                hand,
                new List<Meld>(),
                testCase.Discard,
                isNextPlayer: true);

            runner.Check(!actions.CanChiLeft && !actions.CanChiMiddle && !actions.CanChiRight,
                $"Action validation must not allow cross-suit Chi for {testCase.Name}.");
        }
    }

    private static void DoesNotTreatHonorsAsContinuationOfNineSou(RegressionRunner runner)
    {
        var hand = new List<TileData>
        {
            Tile(Suit.Sou, 9),
            Tile(Suit.Sou, 9),
            Tile(Suit.Wind, 1),
            Tile(Suit.Wind, 2),
            Tile(Suit.Wind, 3),
            Tile(Suit.Wind, 4),
            Tile(Suit.Dragon, 1),
            Tile(Suit.Dragon, 2),
            Tile(Suit.Dragon, 3)
        };
        var discardedTile = Tile(Suit.Sou, 9);

        var actions = ActionValidator.CheckActions(
            hand,
            new List<Meld>(),
            discardedTile,
            isNextPlayer: true);

        runner.Check(actions.CanPon,
            "Action validation should still allow Pon when the hand contains two 9-Sou tiles.");
        runner.Check(!actions.CanChiLeft && !actions.CanChiMiddle && !actions.CanChiRight,
            "Action validation must not use East/South honor tiles as a Chi continuation after 9-Sou.");
        runner.Check(ActionValidator.GetChiCombinations(hand, discardedTile).Count == 0,
            "Chi option generation should agree that the 9-Sou hand has no legal Chi combination.");
    }

    private static TileData Tile(Suit suit, int value) => new TileData(suit, value, ownerID: 0);
}
