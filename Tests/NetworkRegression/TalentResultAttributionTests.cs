using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;
using MahjongGame.UI;

internal static class TalentResultAttributionTests
{
    public static void Run(RegressionRunner runner)
    {
        BaseEvaluationReturnsActualFanBelowEligibilityGate(runner);
        StableMarginalAttributionExplainsAcceptedFan(runner);
        RelaxedPureStraightContributionUsesCounterfactualEvaluation(runner);
        NegativeClampIsAttributedToItsSourceEntries(runner);
        RuntimeSequenceControlsOrderSensitiveMarginals(runner);
        CandidateEvaluationDoesNotMutateAuthoritativeStateOrEmitEvents(runner);
        LiveWinCarriesAnIndependentBreakdown(runner);
        FourSeatRecoveryPublishesTheSameDeepCopiedBreakdown(runner);
        DuplicateAndGapDoNotDuplicateOrPartiallyApplyBreakdown(runner);
        ReconnectRestoresStoredBreakdownWithoutRuntimeEvaluation(runner);
        UnreconciledAttributionDoesNotCreateWireRows(runner);
        AcceptedFinalRemainsAuthoritativeWhenEvaluationThrows(runner);
        ZeroFanFailureDoesNotCreateAnEmptyBreakdown(runner);
        PostLegalRuleFailurePreservesAcceptedFinal(runner);
        CloneFiltersNullContributionRows(runner);
        LocalPresentationStateReceivesLiveAndRecoveryBreakdowns(runner);
        LocalResultPresentationBridgeCarriesLiveAndRecoveryBreakdowns(runner);
    }

    private static void CloneFiltersNullContributionRows(RegressionRunner runner)
    {
        TalentFanContributionMessage sourceRow = Contribution(
            "head_start", 2, TalentFanContributionCategory.Eligibility, 0);
        TalentFanBreakdownMessage clone = TalentFanBreakdownMessage.Clone(
            new TalentFanBreakdownMessage
            {
                baseFan = 6,
                finalFan = 8,
                contributions = new[] { null, sourceRow }
            });
        sourceRow.fanDelta = 99;

        runner.Check(clone.contributions.Length == 1
                     && clone.contributions[0].fanDelta == 2,
            "wire cloning filters null rows and deep-copies every retained contribution");
    }

    private static void LocalResultPresentationBridgeCarriesLiveAndRecoveryBreakdowns(
        RegressionRunner runner)
    {
        var presentation = new RecordingResultPresentation();
        var bridge = new LocalResultPresentationBridge(presentation);
        TalentFanBreakdownMessage live = Breakdown(
            6,
            8,
            Contribution("head_start", 2, TalentFanContributionCategory.Eligibility, 0));

        bridge.ShowLiveWin(0, 0, 8, new List<string>(), false, null, live);
        live.contributions[0].fanDelta = 99;
        bridge.ShowRecovery(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            result = new RoundResultSnapshot
            {
                winnerId = 1,
                fanCount = 21,
                talentFanBreakdown = Breakdown(
                    5,
                    21,
                    Contribution("dragon_ascent", 16,
                        TalentFanContributionCategory.Eligibility, 0))
            }
        });

        runner.Check(presentation.LiveBreakdown.contributions.Single().fanDelta == 2,
            "LocalPlayerClient live presentation bridge receives an independent breakdown");
        runner.Check(presentation.RecoveryBreakdown.finalFan == 21
                     && presentation.RecoveryBreakdown.contributions.Single().fanDelta == 16,
            "local recovery presentation bridge receives the stored breakdown");
    }

    private static void ZeroFanFailureDoesNotCreateAnEmptyBreakdown(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "head_start" }, out GameSession session);
        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 0,
                _ => throw new InvalidOperationException("injected zero-final failure")));

        runner.Check(TalentFanBreakdownMessage.FromResolution(resolution) == null,
            "a failed zero-fan attribution cannot masquerade as a valid empty breakdown");
    }

    private static void PostLegalRuleFailurePreservesAcceptedFinal(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "network_test_attribution_throwing_rule" }, out GameSession session);
        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 8,
                _ => new FanEvaluation
                {
                    HasWinningShape = true,
                    Fan = 8,
                    FanDetails = new List<string>()
                }));

        runner.Check(resolution.FinalFan == 8
                     && resolution.Contributions.Count == 0
                     && TalentFanBreakdownMessage.FromResolution(resolution) == null,
            "a throwing post-legal rule is diagnostic-only and preserves the accepted final");
    }

    private static void LocalPresentationStateReceivesLiveAndRecoveryBreakdowns(
        RegressionRunner runner)
    {
        var state = new TalentFanPresentationState();
        TalentFanBreakdownMessage live = Breakdown(
            6,
            24,
            Contribution("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
            Contribution("sheathed_edge", 16, TalentFanContributionCategory.PostLegal, 2));
        state.ApplyLive(live);
        live.contributions[0].fanDelta = 99;
        TalentFanBreakdownMessage liveProjection = state.Current;
        liveProjection.contributions[1].fanDelta = 88;
        TalentFanBreakdownMessage liveAfterProjectionMutation = state.Current;

        TalentFanBreakdownMessage recovery = Breakdown(
            5,
            21,
            Contribution("dragon_ascent", 16, TalentFanContributionCategory.Eligibility, 0));
        state.ApplyRecovery(new RoomGameSnapshot
        {
            result = new RoundResultSnapshot { talentFanBreakdown = recovery }
        });
        recovery.contributions[0].fanDelta = 77;

        runner.Check(liveProjection.baseFan == 6
                     && liveProjection.finalFan == 24
                     && liveProjection.contributions.Select(row => row.fanDelta)
                         .SequenceEqual(new[] { 2, 88 }),
            "live result presentation receives an independent breakdown at its stable data boundary");
        runner.Check(liveAfterProjectionMutation.contributions.Select(row => row.fanDelta)
                .SequenceEqual(new[] { 2, 16 }),
            "live presentation getters cannot mutate the stored breakdown");
        runner.Check(state.Current.baseFan == 5
                     && state.Current.finalFan == 21
                     && state.Current.contributions.Single().fanDelta == 16,
            "recovery result presentation receives the exact stored breakdown without shared rows");
    }

    private static void AcceptedFinalRemainsAuthoritativeWhenEvaluationThrows(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "head_start" }, out GameSession session);
        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 8,
                _ => throw new InvalidOperationException("injected attribution failure")));

        runner.Check(resolution.FinalFan == 8
                     && resolution.Contributions.Count == 0
                     && TalentFanBreakdownMessage.FromResolution(resolution) == null,
            "runtime attribution exceptions preserve accepted final and never create UI rows");
    }

    private static void UnreconciledAttributionDoesNotCreateWireRows(RegressionRunner runner)
    {
        TalentFanBreakdownMessage wire = TalentFanBreakdownMessage.FromResolution(
            new TalentFanResolution
            {
                IsAttributionComplete = true,
                BaseFan = 6,
                FinalFan = 24,
                Contributions = new[]
                {
                    new TalentFanContribution
                    {
                        TalentId = "head_start",
                        FanDelta = 2,
                        Category = TalentFanContributionCategory.Eligibility,
                        Sequence = 0
                    }
                }
            });

        runner.Check(wire == null,
            "an unreconciled attribution is diagnostic-only and never creates a fake UI talent row");
    }

    private static void LiveWinCarriesAnIndependentBreakdown(RegressionRunner runner)
    {
        var endpoint = new GameEndpoint();
        var remote = new RemotePlayerClient(0, new SeatMessageStream(endpoint, 16));
        TalentFanBreakdownMessage source = Breakdown(
            6,
            24,
            Contribution("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
            Contribution("sheathed_edge", 16, TalentFanContributionCategory.PostLegal, 2));

        remote.OnPlayerWin(0, 24, new List<string> { "清龙(16)" }, false,
            WinKind.Discard, 1, winningHand: null, source);
        source.contributions[0].fanDelta = 99;

        NetworkMessageEnvelope envelope = MessageSerializer.DeserializeEnvelope(
            endpoint.SentMessages.Single());
        PlayerWinMessage payload = MessageSerializer.DeserializePayload<PlayerWinMessage>(envelope.data);
        runner.Check(payload.talentFanBreakdown.baseFan == 6
                     && payload.talentFanBreakdown.finalFan == 24
                     && payload.talentFanBreakdown.contributions.Select(row => row.fanDelta)
                         .SequenceEqual(new[] { 2, 16 }),
            "live PlayerWin carries an independent authoritative talent breakdown");
    }

    private static void FourSeatRecoveryPublishesTheSameDeepCopiedBreakdown(
        RegressionRunner runner)
    {
        TalentFanBreakdownMessage sourceBreakdown = Breakdown(
            6,
            24,
            Contribution("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
            Contribution("sheathed_edge", 16, TalentFanContributionCategory.PostLegal, 2));
        var source = new RoomGameSnapshotSource
        {
            RoomId = "attribution-room",
            RoomState = RoomState.WaitingForNextRound,
            GameMode = GameMode.EastOnly,
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsOnline = true
            }).ToArray(),
            Hands = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            Melds = Enumerable.Range(0, 4).Select(_ => new List<Meld>()).ToArray(),
            Rivers = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            ScoringOptions = Enumerable.Range(0, 4).Select(_ => new ScoringOptions()).ToArray(),
            PeekWallTiles = Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray(),
            WinnerId = 0,
            WinFan = 24,
            TalentFanBreakdown = sourceBreakdown
        };

        RoomGameSnapshot[] snapshots = Enumerable.Range(0, 4)
            .Select(seat => RoomGameSnapshotBuilder.Build(source, seat))
            .ToArray();
        snapshots[0].result.talentFanBreakdown.contributions[0].fanDelta = 99;
        sourceBreakdown.contributions[1].fanDelta = 88;

        runner.Check(snapshots.Skip(1).All(snapshot =>
                snapshot.result.talentFanBreakdown.baseFan == 6
                && snapshot.result.talentFanBreakdown.finalFan == 24
                && snapshot.result.talentFanBreakdown.contributions.Select(row => row.fanDelta)
                    .SequenceEqual(new[] { 2, 16 })),
            "all four seats receive the same public breakdown through independent snapshot copies");
        runner.Check(snapshots.All(snapshot =>
                snapshot.result.talentFanBreakdown.contributions.All(row =>
                    !string.IsNullOrWhiteSpace(row.talentId)))
            && snapshots.All(snapshot => snapshot.privateSeat.ownTalents.Length == 0),
            "public recovery breakdown carries no hidden carried list or private talent state");
    }

    private static void DuplicateAndGapDoNotDuplicateOrPartiallyApplyBreakdown(
        RegressionRunner runner)
    {
        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot { roomId = "live" }, baselineSequence: 5);
        TalentFanBreakdownMessage sourceBreakdown = Breakdown(
            6,
            24,
            Contribution("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
            Contribution("sheathed_edge", 16, TalentFanContributionCategory.PostLegal, 2));
        NetworkMessageEnvelope live = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
            "PlayerWin",
            6,
            new PlayerWinMessage
            {
                winnerId = 0,
                totalFan = 24,
                talentFanBreakdown = sourceBreakdown,
                scores = new[] { 24, -8, -8, -8 }
            }));

        ClientSequenceDisposition first = state.ApplyEnvelope(live);
        ClientSequenceDisposition duplicate = state.ApplyEnvelope(live);
        RoomGameSnapshot afterDuplicate = state.Snapshot;
        NetworkMessageEnvelope gap = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
            "PlayerWin",
            8,
            new PlayerWinMessage
            {
                winnerId = 1,
                totalFan = 99,
                talentFanBreakdown = Breakdown(99, 99),
                scores = new[] { 0, 99, 0, 0 }
            }));
        ClientSequenceDisposition gapDisposition = state.ApplyEnvelope(gap);
        RoomGameSnapshot afterGap = state.Snapshot;
        sourceBreakdown.contributions[0].fanDelta = 77;

        runner.Check(first == ClientSequenceDisposition.Accepted
                     && duplicate == ClientSequenceDisposition.IgnoredDuplicate
                     && afterDuplicate.result.talentFanBreakdown.contributions.Length == 2,
            "duplicate PlayerWin does not duplicate contribution rows");
        runner.Check(gapDisposition == ClientSequenceDisposition.ResyncRequired
                     && afterGap.result.winnerId == 0
                     && afterGap.result.talentFanBreakdown.finalFan == 24
                     && afterGap.result.talentFanBreakdown.contributions[0].fanDelta == 2,
            "a sequence gap does not partially apply a newer result or share source arrays");
    }

    private static void ReconnectRestoresStoredBreakdownWithoutRuntimeEvaluation(
        RegressionRunner runner)
    {
        int evaluations = 0;
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "head_start" }, out GameSession session);
        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 8,
                options =>
                {
                    evaluations++;
                    return new FanEvaluation
                    {
                        HasWinningShape = true,
                        Fan = 6 + options.BonusFan,
                        FanDetails = new List<string>()
                    };
                }));
        int evaluationsAfterAttribution = evaluations;
        TalentFanBreakdownMessage stored = TalentFanBreakdownMessage.FromResolution(resolution);
        var hostEndpoint = new GameEndpoint();
        using var room = new Room(
            "recover", GameMode.EastOnly, AlienationPreset.Standard, "host", true, 16);
        PlayerLoadoutCodec.TryDecode(
            PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig()),
            AlienationPreset.Standard,
            out TrustedPlayerLoadout loadout,
            out _);
        room.TryAddHuman("host", hostEndpoint, "dev:recover", "Host", loadout, out int hostSeat);
        room.SetReady("host", ReadyPhase.MatchStart, out _);
        room.SetReady("host", ReadyPhase.GameSceneLoaded, out _);
        room.GameServer.SetWinResult(0, 8, stored);
        RoomGameSnapshot reconnect = room.BuildSnapshot(hostSeat);
        var state = new ClientGameState();

        bool applied = state.ApplySnapshot(reconnect, baselineSequence: 12);
        TalentFanBreakdownMessage recovered = state.Snapshot.result.talentFanBreakdown;
        stored.contributions[0].fanDelta = 55;

        runner.Check(applied
                     && evaluationsAfterAttribution > 0
                     && evaluations == evaluationsAfterAttribution
                     && recovered.baseFan == 6
                     && recovered.finalFan == 8
                     && recovered.contributions.Select(row => row.fanDelta)
                         .SequenceEqual(new[] { 2 }),
            "room recovery source restores exact stored breakdown values without runtime reevaluation");
    }

    private static void StableMarginalAttributionExplainsAcceptedFan(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "head_start", "dragon_ascent", "sheathed_edge" },
            out GameSession session,
            armSheathedEdge: true);
        List<Meld> melds = BuildThreeFixedPungs();
        List<TileData> hand = BuildSixFanHand();
        TileData winTile = Tile(Suit.Man, 5);

        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 44,
                options => MahjongLogic.EvaluateBestFan(
                    hand, melds, winTile, isSelfDraw: false,
                    WindDirection.East, WindDirection.East,
                    options, isRobKongWin: false)));

        (string TalentId, int FanDelta, TalentFanContributionCategory Category, int Sequence)[] rows =
            resolution.Contributions
                .Select(row => (row.TalentId, row.FanDelta, row.Category, row.Sequence))
                .ToArray();
        runner.Check(resolution.FinalFan == 44 && resolution.BaseFan == 6,
            $"final and no-talent base fan remain distinct (base={resolution.BaseFan}, final={resolution.FinalFan})");
        runner.Check(rows.SequenceEqual(new[]
            {
                ("head_start", 2, TalentFanContributionCategory.Eligibility, 0),
                ("sheathed_edge", 36, TalentFanContributionCategory.PostLegal, 2)
            }),
            "stable marginal attribution emits non-zero entry rows without talent-id effect branches");
        runner.Check(resolution.BaseFan + resolution.Contributions.Sum(row => row.FanDelta)
                     == resolution.FinalFan,
            "contribution rows reconcile exactly to authoritative final fan");
    }

    private static void RelaxedPureStraightContributionUsesCounterfactualEvaluation(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "dragon_ascent" }, out GameSession session);
        List<TileData> hand = new List<TileData>
        {
            Tile(Suit.Man, 1), Tile(Suit.Man, 2), Tile(Suit.Man, 3),
            Tile(Suit.Man, 4), Tile(Suit.Man, 5), Tile(Suit.Man, 6),
            Tile(Suit.Man, 6), Tile(Suit.Man, 7), Tile(Suit.Man, 8),
            Tile(Suit.Pin, 2), Tile(Suit.Pin, 3), Tile(Suit.Pin, 4),
            Tile(Suit.Dragon, 3)
        };
        TileData winTile = Tile(Suit.Dragon, 3);

        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 21,
                options => MahjongLogic.EvaluateBestFan(
                    hand, new List<Meld>(), winTile, isSelfDraw: false,
                    WindDirection.East, WindDirection.South,
                    options, isRobKongWin: false)));

        runner.Check(resolution.Contributions.Count == 1
                     && resolution.Contributions[0].TalentId == "dragon_ascent"
                     && resolution.Contributions[0].FanDelta == 16
                     && resolution.Contributions[0].Category == TalentFanContributionCategory.Eligibility,
            $"relaxed pure straight is attributed through evaluation counterfactuals " +
            $"(base={resolution.BaseFan}, final={resolution.FinalFan}, " +
            $"delta={resolution.Contributions.FirstOrDefault()?.FanDelta})");
    }

    private static void NegativeClampIsAttributedToItsSourceEntries(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(
            new[] { "network_test_penalty_ten", "network_test_penalty_five" },
            out GameSession session);
        List<Meld> melds = BuildThreeFixedPungs();
        List<TileData> hand = new List<TileData>
        {
            Tile(Suit.Dragon, 1), Tile(Suit.Dragon, 1),
            Tile(Suit.Man, 5), Tile(Suit.Man, 5)
        };

        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 0,
                options => MahjongLogic.EvaluateBestFan(
                    hand, melds, Tile(Suit.Dragon, 1), isSelfDraw: false,
                    WindDirection.East, WindDirection.East,
                    options, isRobKongWin: false)));

        runner.Check(resolution.BaseFan == 8 && resolution.FinalFan == 0,
            "negative attribution preserves the accepted authoritative final fan");
        runner.Check(resolution.Contributions.Select(row =>
                (row.TalentId, row.FanDelta, row.Category, row.Sequence)).SequenceEqual(new[]
            {
                ("network_test_penalty_ten", -4, TalentFanContributionCategory.Negative, 0),
                ("network_test_penalty_five", -4, TalentFanContributionCategory.Negative, 1)
            }),
            "effective negative clamps are attributed to their source entries");
    }

    private static void RuntimeSequenceControlsOrderSensitiveMarginals(RegressionRunner runner)
    {
        TalentFanResolution seedThenDouble = ResolveOrderSensitive(
            new[] { "network_test_attribution_seed", "network_test_attribution_double" });
        TalentFanResolution doubleThenSeed = ResolveOrderSensitive(
            new[] { "network_test_attribution_double", "network_test_attribution_seed" });

        runner.Check(seedThenDouble.FinalFan == 10
                     && seedThenDouble.Contributions.Select(row => row.FanDelta)
                         .SequenceEqual(new[] { 2, 2 }),
            "sequence-ordered cumulative evaluation attributes both non-commutative effects");
        runner.Check(doubleThenSeed.FinalFan == 8
                     && doubleThenSeed.Contributions.Count == 1
                     && doubleThenSeed.Contributions[0].TalentId == "network_test_attribution_seed"
                     && doubleThenSeed.Contributions[0].Sequence == 1,
            "reversing runtime entry sequence changes the accepted marginal path and omits zero rows");
    }

    private static void CandidateEvaluationDoesNotMutateAuthoritativeStateOrEmitEvents(
        RegressionRunner runner)
    {
        ScoringSideEffectTestTalent.Reset();
        HiddenPostLegalBonusTalent.Reset();
        HiddenPostLegalPenaltyTalent.Reset();
        TalentMatchRuntime runtime = CreateReadyRuntime(new[]
        {
            "network_test_scoring_side_effect",
            "network_test_hidden_post_legal_bonus",
            "network_test_hidden_post_legal_penalty"
        }, out GameSession session);

        TalentFanResolution resolution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                0,
                alreadyAcceptedFinalFan: 13,
                options => new FanEvaluation
                {
                    HasWinningShape = true,
                    Fan = 8 + options.BonusFan,
                    FanDetails = new List<string>()
                }));
        bool noEvents = Enumerable.Range(0, 4)
            .All(seatIndex => runtime.DrainEventsForSeat(seatIndex).Count == 0);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0 }, session);

        runner.Check(resolution.FinalFan == 13,
            "detached candidate evaluation still resolves every polymorphic scoring phase");
        runner.Check(ScoringSideEffectTestTalent.AuthoritativeMatchCounterAtRoundEnd == 0
                     && !ScoringSideEffectTestTalent.AuthoritativeRoundFlagAtRoundEnd
                     && HiddenPostLegalBonusTalent.AuthoritativeCallsAtRoundEnd == 0
                     && HiddenPostLegalPenaltyTalent.AuthoritativeCallsAtRoundEnd == 0
                     && noEvents,
            "candidate scoring and post-legal evaluation leave authoritative state and event streams untouched");
    }

    private static TalentFanResolution ResolveOrderSensitive(IReadOnlyList<string> ids)
    {
        TalentMatchRuntime runtime = CreateReadyRuntime(ids, out GameSession session);
        return runtime.ResolveAcceptedWinFan(new TalentAcceptedWinAttributionContext(
            session,
            0,
            alreadyAcceptedFinalFan: ids[0] == "network_test_attribution_seed" ? 10 : 8,
            options => new FanEvaluation
            {
                HasWinningShape = true,
                Fan = 6 + options.BonusFan,
                FanDetails = new List<string>()
            }));
    }

    private static TalentMatchRuntime CreateReadyRuntime(
        IReadOnlyList<string> talentIds,
        out GameSession session,
        bool armSheathedEdge = false)
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
        runtime.OpenMainDecision(0, 701);
        TalentActionResult result = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 701 },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId: 701));
        if (!result.Accepted) throw new InvalidOperationException("Could not arm attribution fixture.");
        return runtime;
    }

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
    }

    private static List<Meld> BuildThreeFixedPungs() => new List<Meld>
    {
        Pung(Suit.Man, 2),
        Pung(Suit.Pin, 4),
        Pung(Suit.Sou, 7)
    };

    private static List<TileData> BuildSixFanHand() => new List<TileData>
    {
        Tile(Suit.Man, 5), Tile(Suit.Man, 5),
        Tile(Suit.Wind, 1), Tile(Suit.Wind, 1)
    };

    private static TalentFanBreakdownMessage Breakdown(
        int baseFan,
        int finalFan,
        params TalentFanContributionMessage[] contributions) =>
        new TalentFanBreakdownMessage
        {
            baseFan = baseFan,
            finalFan = finalFan,
            contributions = contributions ?? Array.Empty<TalentFanContributionMessage>()
        };

    private static TalentFanContributionMessage Contribution(
        string talentId,
        int fanDelta,
        TalentFanContributionCategory category,
        int sequence) =>
        new TalentFanContributionMessage
        {
            talentId = talentId,
            fanDelta = fanDelta,
            category = (int)category,
            sequence = sequence
        };

    private static void BaseEvaluationReturnsActualFanBelowEligibilityGate(RegressionRunner runner)
    {
        List<Meld> melds = new List<Meld>
        {
            Pung(Suit.Man, 2),
            Pung(Suit.Pin, 4),
            Pung(Suit.Sou, 7)
        };
        List<TileData> hand = new List<TileData>
        {
            Tile(Suit.Man, 5), Tile(Suit.Man, 5),
            Tile(Suit.Wind, 1), Tile(Suit.Wind, 1)
        };
        TileData winTile = Tile(Suit.Man, 5);

        FanEvaluation raw = MahjongLogic.EvaluateBestFan(
            hand, melds, winTile, isSelfDraw: false,
            WindDirection.East, WindDirection.East,
            options: null, isRobKongWin: false);
        bool legal = MahjongLogic.CheckWinWithFan(
            hand, melds, winTile, false, out _, out _,
            WindDirection.East, WindDirection.East,
            options: null, isRobKongWin: false);

        runner.Check(raw.HasWinningShape && raw.Fan == 6,
            $"base evaluation returns the actual fan below the eight-fan eligibility gate (fan={raw.Fan})");
        runner.Check(!legal,
            "the public legality method still enforces the eight-fan gate");
    }

    private static Meld Pung(Suit suit, int value) => new Meld(
        MeldType.Pon,
        new List<TileData> { Tile(suit, value), Tile(suit, value), Tile(suit, value) },
        sourceId: 1);

    private static TileData Tile(Suit suit, int value) => new TileData(suit, value, ownerID: 0);
}

[TalentRule("network_test_attribution_seed", "Attribution Seed", "test", TalentTier.Small, 0)]
internal sealed class AttributionSeedTalent : TalentRule
{
    public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options) =>
        options.BonusFan += 2;
}

[TalentRule("network_test_attribution_double", "Attribution Double", "test", TalentTier.Small, 0)]
internal sealed class AttributionDoubleTalent : TalentRule
{
    public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options) =>
        options.BonusFan *= 2;
}

[TalentRule("network_test_attribution_throwing_rule", "Attribution Throw", "test", TalentTier.Small, 0)]
internal sealed class AttributionThrowingRuleTalent : TalentRule
{
    public override int GetPostLegalFanBonus(TalentWinContext context) =>
        throw new InvalidOperationException("injected attribution rule failure");
}

internal sealed class RecordingResultPresentation : ILocalResultPresentation
{
    public TalentFanBreakdownMessage LiveBreakdown { get; private set; }
    public TalentFanBreakdownMessage RecoveryBreakdown { get; private set; }

    public void ShowWin(int totalFan, List<string> fanDetails, bool isSelfDraw,
        WinningHandSnapshot winningHand, TalentFanBreakdownMessage talentFanBreakdown)
    {
        LiveBreakdown = talentFanBreakdown;
    }

    public void ShowLose(int winnerId, int totalFan, List<string> fanDetails,
        WinningHandSnapshot winningHand, TalentFanBreakdownMessage talentFanBreakdown)
    {
        LiveBreakdown = talentFanBreakdown;
    }

    public void ReceiveRecoveryTalentFanBreakdown(
        TalentFanBreakdownMessage talentFanBreakdown)
    {
        RecoveryBreakdown = talentFanBreakdown;
    }
}
