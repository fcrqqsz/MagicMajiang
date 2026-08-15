using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Talents;

internal static class ActionValidationTests
{
    public static void Run(RegressionRunner runner)
    {
        DoesNotTreatHonorsAsContinuationOfNineSou(runner);
        DoesNotTreatAdjacentFrequencySlotsAsCrossSuitChi(runner);
        PreservesAllLegalChiDirections(runner);
        KongReplacementDrawSuppliesEligibilityAndExcludesSelfDraw(runner);
        SheathedEdgeDoesNotGrantWinEligibilityButAddsAfterLegalWin(runner);
        PostLegalPenaltiesClampPerEffectAndInTotal(runner);
    }

    private static void KongReplacementDrawSuppliesEligibilityAndExcludesSelfDraw(
        RegressionRunner runner)
    {
        List<Meld> melds = BuildThreeFixedPungs();
        List<TileData> hand = new List<TileData>
        {
            Tile(Suit.Man, 5), Tile(Suit.Man, 5),
            Tile(Suit.Wind, 1), Tile(Suit.Wind, 1)
        };
        TileData winningTile = Tile(Suit.Man, 5);

        bool ordinary = MahjongLogic.CheckWinWithFan(
            hand,
            melds,
            winningTile,
            isSelfDraw: true,
            out _,
            out _,
            isRobKongWin: false,
            isKongWin: false);
        bool afterKong = MahjongLogic.CheckWinWithFan(
            hand,
            melds,
            winningTile,
            isSelfDraw: true,
            out int fan,
            out List<string> details,
            isRobKongWin: false,
            isKongWin: true);
        AllowedActions ordinaryActions = ActionValidator.CheckSelfActions(
            hand, melds, winningTile, isKongWin: false);
        AllowedActions replacementActions = ActionValidator.CheckSelfActions(
            hand, melds, winningTile, isKongWin: true);

        runner.Check(!ordinary
                     && afterKong
                     && !ordinaryActions.CanHu
                     && replacementActions.CanHu
                     && fan == 14
                     && details.Any(detail => detail.StartsWith("杠上开花("))
                     && details.All(detail => !detail.StartsWith("自摸(")),
            $"kong replacement self-draw supplies the eight-fan threshold and excludes self-draw " +
            $"(ordinary={ordinary}, afterKong={afterKong}, ordinaryCanHu={ordinaryActions.CanHu}, " +
            $"replacementCanHu={replacementActions.CanHu}, fan={fan}, " +
            $"details={string.Join("|", details ?? new List<string>())})");
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

    private static void SheathedEdgeDoesNotGrantWinEligibilityButAddsAfterLegalWin(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateArmedScoringRuntime(
            new[] { "sheathed_edge" },
            out GameSession session);
        List<Meld> sixFanMelds = BuildThreeFixedPungs();
        List<TileData> sixFanHand = new List<TileData>
        {
            Tile(Suit.Man, 5), Tile(Suit.Man, 5),
            Tile(Suit.Wind, 1), Tile(Suit.Wind, 1)
        };
        TileData sixFanWinTile = Tile(Suit.Man, 5);

        bool sixFanIsLegal = MahjongLogic.CheckWinWithFan(
            sixFanHand, sixFanMelds, sixFanWinTile, false, out _, out _);
        bool sixPlusEligibilityBonusIsLegal = MahjongLogic.CheckWinWithFan(
            sixFanHand,
            sixFanMelds,
            sixFanWinTile,
            false,
            out int boostedToEight,
            out List<string> sixFanDetails,
            options: new ScoringOptions { BonusFan = 2 });

        List<Meld> eightFanMelds = BuildThreeFixedPungs();
        List<TileData> eightFanHand = new List<TileData>
        {
            Tile(Suit.Dragon, 1), Tile(Suit.Dragon, 1),
            Tile(Suit.Man, 5), Tile(Suit.Man, 5)
        };
        bool eightFanIsLegal = MahjongLogic.CheckWinWithFan(
            eightFanHand,
            eightFanMelds,
            Tile(Suit.Dragon, 1),
            false,
            out int baseEight,
            out List<string> eightFanDetails);
        TalentFanResolution resolved = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0), baseEight);

        runner.Check(!sixFanIsLegal && sixPlusEligibilityBonusIsLegal && boostedToEight == 8,
            $"a six-fan hand remains ineligible because sheathed edge is not an eligibility bonus " +
            $"(legal={sixFanIsLegal}, boostedLegal={sixPlusEligibilityBonusIsLegal}, boostedFan={boostedToEight}, " +
            $"details={string.Join("|", sixFanDetails ?? new List<string>())})");
        runner.Check(eightFanIsLegal && baseEight == 8
                     && resolved.PostLegalBonusFan == 16
                     && resolved.FinalFan == 24,
            $"an eligible eight-fan hand resolves to twenty-four after sheathed edge " +
            $"(legal={eightFanIsLegal}, base={baseEight}, bonus={resolved.PostLegalBonusFan}, final={resolved.FinalFan}, " +
            $"details={string.Join("|", eightFanDetails ?? new List<string>())})");
    }

    private static void PostLegalPenaltiesClampPerEffectAndInTotal(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateArmedScoringRuntime(
            new[] { "network_test_penalty_ten", "network_test_penalty_five" },
            out GameSession session,
            armSheathedEdge: false);

        TalentFanResolution resolved = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0), eligibilityFan: 8);

        runner.Check(TalentFanModifierPolicy.ClampPenalty(-10) == -4
                     && TalentFanModifierPolicy.ClampPenalty(-5) == -4,
            "each post-legal penalty is clamped to minus four");
        runner.Check(resolved.EligibilityFan == 8
                     && resolved.NegativeFan == -8
                     && resolved.FinalFan == 0,
            "post-legal penalties clamp to minus eight total without revoking base win eligibility");
    }

    private static TalentMatchRuntime CreateArmedScoringRuntime(
        IReadOnlyList<string> talentIds,
        out GameSession session,
        bool armSheathedEdge = true)
    {
        var config = new TalentSlotConfig();
        for (int index = 0; index < talentIds.Count; index++)
            config.SlotTalentIds[index] = talentIds[index];
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        if (!armSheathedEdge) return runtime;
        for (int round = 0; round < 3; round++)
        {
            runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = round + 1 }, session);
            BeginReadyRound(runtime, session);
        }
        runtime.OpenMainDecision(0, 501);
        TalentActionResult result = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 501 },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId: 501));
        if (!result.Accepted) throw new InvalidOperationException("Could not arm sheathed edge scoring fixture.");
        return runtime;
    }

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
    }

    private static List<Meld> BuildThreeFixedPungs()
    {
        return new List<Meld>
        {
            Pung(Suit.Man, 2),
            Pung(Suit.Pin, 4),
            Pung(Suit.Sou, 7)
        };
    }

    private static Meld Pung(Suit suit, int value) => new Meld(
        MeldType.Pon,
        new List<TileData> { Tile(suit, value), Tile(suit, value), Tile(suit, value) },
        sourceId: 1);

    private static TileData Tile(Suit suit, int value) => new TileData(suit, value, ownerID: 0);
}

[TalentRule("network_test_penalty_ten", "Penalty Ten", "test", TalentTier.Small, 0,
    TalentPhase.Scoring)]
internal sealed class PenaltyTenTalent : TalentRule
{
    public override int GetPostLegalFanPenalty(TalentWinContext context) => -10;
}

[TalentRule("network_test_penalty_five", "Penalty Five", "test", TalentTier.Small, 0,
    TalentPhase.Scoring)]
internal sealed class PenaltyFiveTalent : TalentRule
{
    public override int GetPostLegalFanPenalty(TalentWinContext context) => -5;
}
