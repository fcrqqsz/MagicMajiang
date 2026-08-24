using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Talents;

internal static class UniversalFillerTalentTests
{
    public static void Run(RegressionRunner runner)
    {
        MetadataMatchesApprovedUniversalTalentDesigns(runner);
        SetTheToneUsesFirstDecisionChoiceAndWinningTileSuit(runner);
        ForetellOutcomeMatchesAcceptedWinMode(runner);
        PruneTheExcessCountsCommittedTerminalAndHonorDiscards(runner);
        BideTheTideCountsSixCommittedDiscards(runner);
        PrepareForRiskRefundsOnlyApprovedOutcomes(runner);
        MisdirectionTransformsExactlyTheNextDiscard(runner);
        MisdirectionUsesTheCompleteHonorCycle(runner);
        UniversalFanTalentsStackAndAttributeWithoutChangingTheGate(runner);
    }

    private static void MetadataMatchesApprovedUniversalTalentDesigns(RegressionRunner runner)
    {
        CheckMetadata(runner, "set_the_tone", TalentTier.Medium, 12,
            TalentActivationWindow.MainTurn, TalentStateScope.Round);
        CheckMetadata(runner, "prepare_for_risk", TalentTier.Medium, 12,
            TalentActivationWindow.MainTurn, TalentStateScope.Round);
        CheckMetadata(runner, "prune_the_excess", TalentTier.Small, 6,
            TalentActivationWindow.None, TalentStateScope.Round);
        CheckMetadata(runner, "bide_the_tide", TalentTier.Small, 4,
            TalentActivationWindow.None, TalentStateScope.Round);
        CheckMetadata(runner, "foretell_outcome", TalentTier.Small, 6,
            TalentActivationWindow.MainTurn, TalentStateScope.Round);
        CheckMetadata(runner, "misdirection", TalentTier.Small, 8,
            TalentActivationWindow.MainTurn, TalentStateScope.Round);
    }

    private static void SetTheToneUsesFirstDecisionChoiceAndWinningTileSuit(RegressionRunner runner)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("set_the_tone", 1);
        const long decisionId = 11001;
        runtime.OpenMainDecision(0, decisionId);

        TalentActionOption option = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId)).Single();
        TalentActionOption aiChoice = AiTalentDecisionPolicy.ChooseActiveAction(new[] { option });
        runner.Check(option.Choice.Kind == TalentChoiceKind.Suit
                     && option.Choice.DefaultChoiceId == "man"
                     && option.Choice.Options.Select(choice => choice.ChoiceId)
                         .SequenceEqual(new[] { "man", "pin", "sou" })
                     && aiChoice.SelectedChoiceId == "man",
            "定调 advertises the three numeric suits and has a deterministic AI default");

        TalentActionResult invalid = ActivateChoice(
            runtime, session, "set_the_tone", decisionId, "wind");
        TalentActionResult accepted = ActivateChoice(
            runtime, session, "set_the_tone", decisionId, "pin");
        TalentSnapshotEntry selected = runtime.GetSnapshotEntries()
            .Single(entry => entry.TalentId == "set_the_tone");
        runner.Check(!invalid.Accepted
                     && invalid.ErrorCode == TalentActionErrorCodes.InvalidChoice
                     && accepted.Accepted
                     && accepted.PublicStateValue == 2
                     && selected.LastPublicEventType == "set_the_tone_suit"
                     && selected.LastPublicValue == 2,
            "定调 rejects unauthorized choices without consumption and snapshots its public suit as Man=1, Pin=2, Sou=3");

        runner.Check(Resolve(runtime, session, WinFacts(session, 0, null, Suit.Pin, 5, true)).PostLegalBonusFan == 4
                     && Resolve(runtime, session, WinFacts(session, 0, 2, Suit.Man, 5, false)).PostLegalBonusFan == 0
                     && Resolve(runtime, session, WinFacts(session, 0, 2, Suit.Wind, 1, false)).PostLegalBonusFan == 0,
            "定调 grants +4 only when the authoritative numeric winning tile matches the chosen suit");

        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 12 }, session);
        BeginReadyRound(runtime, session);
        const long laterDecision = 11003;
        runtime.OpenMainDecision(0, 11002);
        IReadOnlyList<TalentActionOption> later = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, laterDecision));
        runner.Check(later.All(candidate => candidate.TalentId != "set_the_tone"),
            "定调 is available only on the first main decision of each round");
    }

    private static void ForetellOutcomeMatchesAcceptedWinMode(RegressionRunner runner)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("foretell_outcome", 3);
        const long decisionId = 12001;
        runtime.OpenMainDecision(0, decisionId);
        TalentActionOption option = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId)).Single();
        TalentActionOption aiChoice = AiTalentDecisionPolicy.ChooseActiveAction(new[] { option });
        runner.Check(option.Choice.Kind == TalentChoiceKind.Mode
                     && option.Choice.DefaultChoiceId == "self_draw"
                     && option.Choice.Options.Select(choice => choice.ChoiceId)
                         .SequenceEqual(new[] { "self_draw", "ron" })
                     && aiChoice.SelectedChoiceId == "self_draw",
            "预判 advertises self-draw and ron with the approved AI default");

        TalentActionResult accepted = ActivateChoice(
            runtime, session, "foretell_outcome", decisionId, "ron");
        TalentFanResolution selfDraw = Resolve(
            runtime, session, WinFacts(session, 0, null, Suit.Man, 5, true));
        TalentFanResolution ron = Resolve(
            runtime, session, WinFacts(session, 0, 2, Suit.Man, 5, false));
        TalentFanResolution robKong = Resolve(
            runtime, session, WinFacts(session, 0, 2, Suit.Man, 5, false, isRobKong: true));
        runner.Check(accepted.Accepted
                     && selfDraw.PostLegalBonusFan == 0
                     && ron.PostLegalBonusFan == 3
                     && robKong.PostLegalBonusFan == 3
                     && runtime.BuildScoringOptions(
                         new TalentScoringContext(session, 0)).MinimumFan == 8,
            "预判 grants +3 to matching ron including rob-kong without changing the legal gate");
    }

    private static void PruneTheExcessCountsCommittedTerminalAndHonorDiscards(RegressionRunner runner)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("prune_the_excess", 3);
        CommitDiscard(runtime, 13001, new TileData(Suit.Man, 1, 0));
        CommitDiscard(runtime, 13002, new TileData(Suit.Pin, 5, 0));
        CommitDiscard(runtime, 13003, new TileData(Suit.Wind, 2, 0));
        runner.Check(Resolve(runtime, session, WinFacts(session, 0, null, Suit.Sou, 4, true)).PostLegalBonusFan == 0,
            "去芜 does not trigger before three qualifying committed discards");

        var transformedTerminal = new TileData(Suit.Sou, 9, 0)
        {
            IsModified = true,
            SpecialEffectID = "misdirection"
        };
        CommitDiscard(runtime, 13004, transformedTerminal, wasAutomatic: true);
        TalentFanResolution triggered = Resolve(
            runtime, session, WinFacts(session, 0, 3, Suit.Man, 6, false));
        runner.Check(triggered.PostLegalBonusFan == 3 && triggered.FinalFan == 11,
            "去芜 counts post-pipeline terminal/honor commits, including automatic discards, and grants +3 at the third");

        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 11 }, session);
        BeginReadyRound(runtime, session);
        runner.Check(Resolve(runtime, session, WinFacts(session, 0, null, Suit.Man, 2, true)).PostLegalBonusFan == 0,
            "去芜 resets its count and trigger between rounds");
    }

    private static void BideTheTideCountsSixCommittedDiscards(RegressionRunner runner)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("bide_the_tide", 3);
        for (int index = 0; index < 5; index++)
            CommitDiscard(runtime, 14001 + index, new TileData(Suit.Man, index + 1, 0));

        runner.Check(Resolve(runtime, session, WinFacts(session, 0, null, Suit.Man, 6, true)).PostLegalBonusFan == 0,
            "候潮 does not trigger at five committed discards");
        CommitDiscard(runtime, 14006, new TileData(Suit.Dragon, 1, 0), wasAutomatic: true);
        TalentFanResolution selfDraw = Resolve(
            runtime, session, WinFacts(session, 0, null, Suit.Pin, 6, true));
        TalentFanResolution ron = Resolve(
            runtime, session, WinFacts(session, 0, 1, Suit.Pin, 6, false));
        runner.Check(selfDraw.PostLegalBonusFan == 2
                     && ron.PostLegalBonusFan == 2
                     && runtime.BuildScoringOptions(
                         new TalentScoringContext(session, 0)).MinimumFan == 8,
            "候潮 grants +2 from the sixth committed discard for both self-draw and ron without changing the gate");
    }

    private static void PrepareForRiskRefundsOnlyApprovedOutcomes(RegressionRunner runner)
    {
        runner.Check(ResolveInsurance("self_draw", winner: 2, discarder: 1) == 8,
            "未雨绸缪 base insurance refunds an uninvolved owner on another player's ron");
        runner.Check(ResolveInsurance("self_draw", winner: 2, discarder: null) == 8,
            "未雨绸缪 防自摸 refunds exactly the eight-point gate on another player's self-draw");
        runner.Check(ResolveInsurance("ron", winner: 2, discarder: 0) == 8,
            "未雨绸缪 防放铳 refunds exactly eight when the owner deals in");
        runner.Check(ResolveInsurance("ron", winner: 2, discarder: null) == 0,
            "未雨绸缪 防放铳 does not refund another player's self-draw");
        runner.Check(ResolveInsurance("self_draw", winner: 0, discarder: null) == 0,
            "未雨绸缪 never refunds its owner for winning");
        runner.Check(ResolveInsurance("self_draw", winner: null, discarder: null) == 0,
            "未雨绸缪 does not refund a draw");
        runner.Check(ResolveInsurance("self_draw", winner: 2, discarder: 1, isAborted: true) == 0,
            "未雨绸缪 does not refund an aborted round");
    }

    private static void UniversalFanTalentsStackAndAttributeWithoutChangingTheGate(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[1] = "set_the_tone";
        config.SlotTalentIds[2] = "prepare_for_risk";
        config.SlotTalentIds[3] = "prune_the_excess";
        config.SlotTalentIds[4] = "bide_the_tide";
        config.SlotTalentIds[5] = "foretell_outcome";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        const long decisionId = 18001;
        runtime.OpenMainDecision(0, decisionId);
        ActivateChoice(runtime, session, "set_the_tone", decisionId, "pin");
        ActivateChoice(runtime, session, "prepare_for_risk", decisionId, "ron");
        ActivateChoice(runtime, session, "foretell_outcome", decisionId, "self_draw");

        TileData[] discards =
        {
            new(Suit.Man, 1, 0),
            new(Suit.Pin, 2, 0),
            new(Suit.Wind, 1, 0),
            new(Suit.Sou, 5, 0),
            new(Suit.Dragon, 2, 0),
            new(Suit.Man, 6, 0)
        };
        for (int index = 0; index < discards.Length; index++)
            CommitDiscard(runtime, 18002 + index, discards[index]);

        TalentWinFacts facts = WinFacts(session, 0, null, Suit.Pin, 7, isSelfDraw: true);
        TalentFanResolution resolution = Resolve(runtime, session, facts);
        TalentFanResolution attribution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                winnerSeatIndex: 0,
                alreadyAcceptedFinalFan: 20,
                facts,
                evaluateOptions: options => new FanEvaluation
                {
                    HasWinningShape = true,
                    Fan = 8 + options.BonusFan,
                    FanDetails = new List<string>()
                }));

        var deltas = attribution.Contributions.ToDictionary(
            row => row.TalentId,
            row => row.FanDelta);
        runner.Check(resolution.EligibilityFan == 8
                     && resolution.PostLegalBonusFan == 12
                     && resolution.FinalFan == 20
                     && runtime.BuildScoringOptions(
                         new TalentScoringContext(session, 0)).MinimumFan == 8,
            "定调、去芜、候潮与预判 stack to +12 post-legal fan while the legal gate remains eight");
        runner.Check(attribution.IsAttributionComplete
                     && attribution.BaseFan == 8
                     && attribution.FinalFan == 20
                     && deltas.Count == 4
                     && deltas["set_the_tone"] == 4
                     && deltas["prune_the_excess"] == 3
                     && deltas["bide_the_tide"] == 2
                     && deltas["foretell_outcome"] == 3,
            "universal fan talents remain independently attributable through detached counterfactual evaluation");
    }

    private static void MisdirectionTransformsExactlyTheNextDiscard(RegressionRunner runner)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("misdirection", 3);
        const long firstDecision = 15001;
        const long laterDecision = 15002;
        runtime.OpenMainDecision(0, firstDecision);

        IReadOnlyList<TalentActionOption> laterOptions = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, laterDecision));
        runner.Check(laterOptions.Any(option => option.TalentId == "misdirection"),
            "障眼法 may be armed at a later own main-turn decision");

        TalentActionResult armed = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "misdirection", DecisionId = laterDecision },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, laterDecision));
        TalentActionResult duplicate = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "misdirection", DecisionId = laterDecision },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, laterDecision));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            15020,
            actorSeatIndex: 0,
            sourceSeatIndex: null,
            ClientActionType.AnGan,
            new TileData(Suit.Dragon, 1, 0),
            chiCombinations: null,
            wasAutomatic: false,
            winFacts: null));

        var input = new TileData(Suit.Man, 7, 3)
        {
            ID = "physical-tile",
            IsModified = true,
            SpecialEffectID = "earlier_effect"
        };
        TileData transformed = runtime.ApplyDiscard(new TalentDiscardContext(session, 0), input);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            laterDecision, 0, null, ClientActionType.Discard,
            transformed, null, wasAutomatic: true, winFacts: null));
        TileData following = runtime.ApplyDiscard(
            new TalentDiscardContext(session, 0),
            new TileData(Suit.Pin, 4, 0));

        runner.Check(armed.Accepted
                     && !duplicate.Accepted
                     && transformed.TileSuit == Suit.Pin
                     && transformed.Value == 7
                     && transformed.ID == "physical-tile"
                     && transformed.OriginalOwnerID == 3
                     && transformed.IsModified
                     && transformed.SpecialEffectID == "misdirection"
                     && following.TileSuit == Suit.Pin
                     && following.Value == 4,
            "障眼法 survives a committed kong, consumes only on the next actual discard, and preserves physical identity and owner");

        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1, DiscarderSeatIndex = 2 }, session);
        BeginReadyRound(runtime, session);
        runtime.OpenMainDecision(0, 15003);
        runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "misdirection", DecisionId = 15003 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 15003));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1, DiscarderSeatIndex = 2 }, session);
        BeginReadyRound(runtime, session);
        TileData expired = runtime.ApplyDiscard(
            new TalentDiscardContext(session, 0),
            new TileData(Suit.Sou, 4, 0));
        runner.Check(expired.TileSuit == Suit.Sou && expired.Value == 4,
            "障眼法 armed state expires without refund when the round ends before a discard");
    }

    private static void MisdirectionUsesTheCompleteHonorCycle(RegressionRunner runner)
    {
        var cases = new[]
        {
            (Suit.Man, 3, Suit.Pin, 3),
            (Suit.Pin, 3, Suit.Sou, 3),
            (Suit.Sou, 3, Suit.Man, 3),
            (Suit.Wind, 1, Suit.Wind, 2),
            (Suit.Wind, 2, Suit.Wind, 3),
            (Suit.Wind, 3, Suit.Wind, 4),
            (Suit.Wind, 4, Suit.Dragon, 1),
            (Suit.Dragon, 1, Suit.Dragon, 2),
            (Suit.Dragon, 2, Suit.Dragon, 3),
            (Suit.Dragon, 3, Suit.Wind, 1)
        };

        bool allMatched = true;
        long decisionId = 16000;
        foreach ((Suit inputSuit, int inputValue, Suit expectedSuit, int expectedValue) in cases)
        {
            (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("misdirection", 3);
            decisionId++;
            runtime.OpenMainDecision(0, decisionId);
            runtime.TryActivate(
                0,
                new TalentActionRequest { TalentId = "misdirection", DecisionId = decisionId },
                new TalentActivationContext(
                    session, 0, TalentActivationWindow.MainTurn, decisionId));
            TileData transformed = runtime.ApplyDiscard(
                new TalentDiscardContext(session, 0),
                new TileData(inputSuit, inputValue, 0));
            allMatched &= transformed.TileSuit == expectedSuit
                          && transformed.Value == expectedValue;
        }

        runner.Check(allMatched,
            "障眼法 follows Man-Pin-Sou and East-South-West-North-Red-Green-White cycles exactly");
    }

    private static int ResolveInsurance(
        string choiceId,
        int? winner,
        int? discarder,
        bool isAborted = false)
    {
        (TalentMatchRuntime runtime, GameSession session) = CreateRuntime("prepare_for_risk", 1);
        const long decisionId = 17001;
        runtime.OpenMainDecision(0, decisionId);
        TalentActionOption option = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId)).Single();
        if (choiceId == "ron")
        {
            TalentActionOption aiChoice = AiTalentDecisionPolicy.ChooseActiveAction(new[] { option });
            if (aiChoice.SelectedChoiceId != "ron") return int.MinValue;
        }
        TalentActionResult result = ActivateChoice(
            runtime, session, "prepare_for_risk", decisionId, choiceId);
        if (!result.Accepted) return int.MinValue;

        int before = session.Scores[0];
        runtime.EndRound(new TalentRoundOutcome
        {
            WinnerSeatIndex = winner,
            DiscarderSeatIndex = discarder,
            IsAborted = isAborted,
            FinalFan = winner.HasValue ? 8 : 0
        }, session);
        return session.Scores[0] - before;
    }

    private static void CheckMetadata(
        RegressionRunner runner,
        string talentId,
        TalentTier tier,
        int cost,
        TalentActivationWindow window,
        TalentStateScope scope)
    {
        TalentMetadata metadata = TalentRegistry.Instance.GetMetadata(talentId);
        runner.Check(metadata != null
                     && TalentRegistry.Instance.GetTier(talentId) == tier
                     && TalentRegistry.Instance.GetCost(talentId) == cost
                     && metadata.ActivationWindow == window
                     && metadata.StateScope == scope
                     && metadata.SideboardPolicy == TalentSideboardPolicy.Flexible,
            $"{talentId} metadata matches its approved tier, cost, lifecycle, and sideboard policy");
    }

    private static (TalentMatchRuntime Runtime, GameSession Session) CreateRuntime(
        string talentId,
        int slotIndex)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[slotIndex] = talentId;
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        return (runtime, session);
    }

    private static TalentActionResult ActivateChoice(
        TalentMatchRuntime runtime,
        GameSession session,
        string talentId,
        long decisionId,
        string choiceId) => runtime.TryActivate(
            0,
            new TalentActionRequest
            {
                TalentId = talentId,
                DecisionId = decisionId,
                ChoiceId = choiceId
            },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId));

    private static TalentFanResolution Resolve(
        TalentMatchRuntime runtime,
        GameSession session,
        TalentWinFacts facts) => runtime.ResolvePostLegalFan(
            new TalentWinContext(session, facts.WinnerSeatIndex, facts),
            eligibilityFan: 8);

    private static TalentWinFacts WinFacts(
        GameSession session,
        int winner,
        int? discarder,
        Suit suit,
        int value,
        bool isSelfDraw,
        bool isRobKong = false) => TalentWinFacts.Create(
            session,
            winner,
            discarder,
            new[] { new TileData(Suit.Man, 2, winner) },
            new List<Meld>(),
            new TileData(suit, value, discarder ?? winner),
            isSelfDraw,
            isRobKong,
            isKongReplacement: false);

    private static void CommitDiscard(
        TalentMatchRuntime runtime,
        long decisionId,
        TileData tile,
        bool wasAutomatic = false) => runtime.CommitAction(
            TalentActionCommittedFacts.Create(
                decisionId,
                actorSeatIndex: 0,
                sourceSeatIndex: null,
                ClientActionType.Discard,
                tile,
                chiCombinations: null,
                wasAutomatic,
                winFacts: null));

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(
            new TalentInitialHandsContext(session, new ServerGameState(4)));
        runtime.ResolvePostShuffle(
            new TalentPostShuffleContext(session, new List<TileData>()));
    }
}
