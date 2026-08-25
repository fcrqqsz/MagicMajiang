using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class TalentActionTests
{
    public static void Run(RegressionRunner runner)
    {
        SupplementalActionValidationDoesNotConsumeMainDecision(runner);
        SupplementalActionValidationRejectsInvalidDecisionContexts(runner);
        SupplementalTalentAdmissionRejectsResponseWindowsBeforeRuntime(runner);
        CarriedTalentActionExecutesPolymorphically(runner);
        ActiveTalentFeedbackDistinguishesAppliedEffects(runner);
        PublicCounterEventsUseStablePresentationCategory(runner);
        InterceptionConsumesUsageBeforeTargetDefense(runner);
        InterceptionLimitsUsageAndEnumeratesOnlyEligibleTargets(runner);
        InterceptionRevalidatesEveryTargetEligibilityOnTheServer(runner);
        InactiveCarriedTalentKeepsOwnerPrivateCounter(runner);
        InactiveRevealedInterceptionKeepsStickyPublicCounter(runner);
        ComposureBlocksOnlyTheFirstNegativeEffectPerRound(runner);
        NegativeEffectChecksTargetDefensesByPriority(runner);
        NegativeEffectDescriptionDoesNotExposeAnApplyDelegate(runner);
        NonTargetAndReserveDefensesDoNotBlockPublicChargeReduction(runner);
        NegativeEffectRejectsMissingPublicChargeTarget(runner);
        NegativeEffectRejectsUnknownTypesWithoutApplying(runner);
        NegativeEffectRejectsIneligiblePublicChargeBeforeDefenses(runner);
        SheathedEdgeChargesCapsAndExposesPublicTargets(runner);
        SheathedEdgeDoesNotChargeOnOwnerWinOrAbortedRound(runner);
        SheathedEdgeConsumesAnyPositiveChargeForScaledBonus(runner);
        SheathedEdgeArmsOnlyOnTheFirstMainDecision(runner);
        SheathedEdgeReadOnlyResolutionConsumesOnlyAfterAcceptedWin(runner);
        SheathedEdgeConsumedChargeDoesNotCarryAcrossRounds(runner);
        SuitConvergenceChoosesAndTransformsExactlyTwoOffSuitDraws(runner);
        GatherMomentumChargesOnCommittedMeldsAndCapsAtThree(runner);
        GatherMomentumArmsAndSpendsAllLayersInMainTurn(runner);
        GatherMomentumSupportsPublicChargeControl(runner);
        FadingColorChargesOnFirstModifiedDiscardPerRoundAndCapsAtTwo(runner);
        FadingColorFullInkExhaustsRoundOpportunityEvenIfInkIsSpentLater(runner);
        FadingColorSpendsInkToReduceTargetChargeAndReturnsRemainingInk(runner);
        FadingColorImplementsPublicChargeTalentAndCanBeControlled(runner);
        FadingColorInvalidTargetDoesNotSpendInk(runner);
        FadingColorBlockedByComposureOrRedirectForceSpendsInkWithoutRefund(runner);
        FadingColorActivePublicEventAndSnapshotFinalValueIsOne(runner);
        RedirectForceBlocksPublicChargeReductionAndArmsBonus(runner);
        RedirectForceAndComposureDefenseOrder(runner);
        RoomManagerRoutesTalentActionsWithExactlyOneSeatResolution(runner);
        EncirclementTriggersOnlyOnDistinctOpponentSourcesAndAwardsBonus(runner);
        LastStandFormationTriggersOnSecondMeldRaisesGateAndAwardsBonus(runner);
        CallTheMarkActionAndAttributionLifecycle(runner);
        FollowTheTrailTracksOpponentDiscardsAndAwardsBonusOnMatchingSuitRon(runner);
        MultipleNewTalentsStackAndAttributeWithGatherMomentum(runner);
        PiercingInsightUniversalRevealAndNetworkTests(runner);
    }

    private static void SuitConvergenceChoosesAndTransformsExactlyTwoOffSuitDraws(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "suit_convergence";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        var gameState = new ServerGameState(4);
        gameState.InitHand(0, new List<TileData>
        {
            new TileData(Suit.Man, 1, 0) { ID = "default-man" },
            new TileData(Suit.Pin, 2, 0) { ID = "default-pin-1" },
            new TileData(Suit.Pin, 3, 0) { ID = "default-pin-2" },
            new TileData(Suit.Sou, 4, 0) { ID = "default-sou-1" },
            new TileData(Suit.Sou, 5, 0) { ID = "default-sou-2" },
            new TileData(Suit.Wind, 1, 0) { ID = "default-east" }
        });
        for (int seatIndex = 1; seatIndex < 4; seatIndex++)
            gameState.InitHand(seatIndex, new List<TileData>());
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, gameState));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));

        const long DecisionId = 9201;
        runtime.OpenMainDecision(0, DecisionId);
        TalentActionOption option = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, DecisionId)).Single();
        TalentActionOption aiChoice = MahjongGame.Core.Agents.AiTalentDecisionPolicy
            .ChooseActiveAction(new[] { option });
        runner.Check(option.Choice.Kind == TalentChoiceKind.Suit
                     && option.Choice.DefaultChoiceId == "pin"
                     && option.Choice.Options.Select(choice => choice.ChoiceId)
                         .SequenceEqual(new[] { "man", "pin", "sou" })
                     && option.AiPriority == 300
                     && aiChoice.SelectedChoiceId == "pin",
            "归色 offers a stable suit choice and defaults to the most common starting suit with Man-Pin-Sou tie order");

        TalentActionResult activated = runtime.TryActivate(
            0,
            new TalentActionRequest
            {
                TalentId = "suit_convergence",
                DecisionId = DecisionId,
                ChoiceId = "sou"
            },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, DecisionId));
        TalentSnapshotEntry selectedSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.OwnerSeatIndex == 0 && entry.TalentId == "suit_convergence");

        TileData targetSuit = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Sou, 7, 0) { ID = "target-suit" });
        TileData honor = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Dragon, 2, 0) { ID = "honor" });
        TileData first = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Man, 4, 0) { ID = "first-off-suit" });
        TileData second = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Pin, 8, 0) { ID = "second-off-suit" });
        TileData exhausted = runtime.ApplyDraw(
            new TalentDrawContext(session, 0),
            new TileData(Suit.Man, 5, 0) { ID = "exhausted" });
        TalentSnapshotEntry exhaustedSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.OwnerSeatIndex == 0 && entry.TalentId == "suit_convergence");

        runner.Check(activated.Accepted
                     && selectedSnapshot.IsRevealed
                     && selectedSnapshot.PrivateStatusKey == "sou"
                     && selectedSnapshot.LastPublicEventType == "suit_convergence_sou"
                     && selectedSnapshot.LastPublicValue == 2,
            "归色 acceptance publicly records its target suit and two remaining transformations");
        runner.Check(targetSuit.TileSuit == Suit.Sou
                     && !targetSuit.IsModified
                     && honor.TileSuit == Suit.Dragon
                     && !honor.IsModified
                     && first.TileSuit == Suit.Sou
                     && first.Value == 4
                     && first.IsModified
                     && first.SpecialEffectID == "suit_convergence"
                     && second.TileSuit == Suit.Sou
                     && second.Value == 8
                     && second.IsModified,
            "归色 ignores target-suit draws and honors, then preserves values while changing the next two off-suit draws");
        runner.Check(exhausted.TileSuit == Suit.Man
                     && !exhausted.IsModified
                     && exhaustedSnapshot.LastPublicEventType == "suit_convergence_sou"
                     && exhaustedSnapshot.LastPublicValue == 0,
            "归色 stops after two transformations and publicly reaches zero remaining");

        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);
        runtime.OpenMainDecision(0, DecisionId + 1);
        runner.Check(runtime.GetAvailableActions(
                0,
                new TalentActionQueryContext(
                    session, 0, TalentActivationWindow.MainTurn, DecisionId + 1)).Count == 1,
            "归色 clears its choice and transformation allowance at the next small round");

        var reserveConfig = new TalentSlotConfig();
        reserveConfig.ReserveTalentIds[1] = "suit_convergence";
        var reserveRuntime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = reserveConfig },
            TalentRegistry.Instance);
        var reserveSession = new GameSession(GameMode.Single);
        reserveRuntime.BeginMatch(reserveSession);
        BeginReadyRound(reserveRuntime, reserveSession);
        reserveRuntime.OpenMainDecision(0, 9301);
        TileData reserveDraw = reserveRuntime.ApplyDraw(
            new TalentDrawContext(reserveSession, 0),
            new TileData(Suit.Man, 3, 0) { ID = "reserve-draw" });
        runner.Check(reserveRuntime.GetAvailableActions(
                         0,
                         new TalentActionQueryContext(
                             reserveSession, 0, TalentActivationWindow.MainTurn, 9301)).Count == 0
                     && reserveDraw.TileSuit == Suit.Man
                     && !reserveDraw.IsModified,
            "an inactive reserve copy of 归色 neither offers a choice nor transforms draws");
    }

    private static void RoomManagerRoutesTalentActionsWithExactlyOneSeatResolution(RegressionRunner runner)
    {
        VerifyManagedTalentAction(
            runner,
            "talent-route-accepted",
            room => room.GameServer.NextTalentActionResult = TalentActionResult.Success(effectApplied: true),
            new TalentActionMessage
            {
                decisionId = 501,
                talentId = "sheathed_edge",
                targetSeatIndex = -1
            },
            expectedAccepted: true,
            expectedEffectApplied: true,
            expectedErrorCode: null,
            expectedServerSubmissions: 1,
            "RoomManager routes an authenticated TalentAction through its bound seat and emits one ordered acceptance");

        VerifyManagedTalentAction(
            runner,
            "talent-route-server-rejected",
            room => room.GameServer.NextTalentActionResult = TalentActionResult.Reject(NetworkErrorCodes.WrongPhase),
            new TalentActionMessage { decisionId = 502, talentId = "sheathed_edge" },
            expectedAccepted: false,
            expectedEffectApplied: false,
            expectedErrorCode: NetworkErrorCodes.WrongPhase,
            expectedServerSubmissions: 1,
            "a GameServer talent rejection emits one ordered seat resolution without a RoomError");

        VerifyManagedTalentAction(
            runner,
            "talent-route-wrong-controller",
            room => room.Seats[0].Controller.HumanSubmissionAllowed = false,
            new TalentActionMessage { decisionId = 503, talentId = "sheathed_edge" },
            expectedAccepted: false,
            expectedEffectApplied: false,
            expectedErrorCode: NetworkErrorCodes.WrongController,
            expectedServerSubmissions: 0,
            "a Room controller rejection emits one ordered seat resolution without reaching GameServer");

        VerifyManagedTalentAction(
            runner,
            "talent-route-empty",
            _ => { },
            null,
            expectedAccepted: false,
            expectedEffectApplied: false,
            expectedErrorCode: NetworkErrorCodes.InvalidAction,
            expectedServerSubmissions: 0,
            "an empty bound-seat TalentAction emits one ordered InvalidAction resolution");

        using (var manager = new RoomManager(1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            Room room = CreateManagedTalentRoom(manager, "talent-route-wrong-phase", out GameEndpoint endpoint);
            room.GameServer.CompleteDrawRound();
            int beforeCount = endpoint.SentMessages.Count;

            endpoint.Receive(
                "talent-route-wrong-phase",
                1,
                MessageSerializer.Serialize("TalentAction", 0,
                    new TalentActionMessage { decisionId = 504, talentId = "sheathed_edge" }));

            NetworkMessageEnvelope[] newMessages = endpoint.SentMessages
                .Skip(beforeCount)
                .Select(MessageSerializer.DeserializeEnvelope)
                .Where(envelope => envelope != null)
                .ToArray();
            TalentActionResolvedMessage resolved = newMessages.Length == 1
                && newMessages[0].type == "TalentActionResolved"
                    ? MessageSerializer.DeserializePayload<TalentActionResolvedMessage>(newMessages[0].data)
                    : null;
            runner.Check(
                newMessages.Length == 1
                && newMessages[0].seq > 0
                && resolved?.accepted == false
                && resolved.errorCode == NetworkErrorCodes.NoActiveDecision,
                "a bound-seat TalentAction in the wrong room phase emits exactly one ordered NoActiveDecision resolution");
        }

        using (var manager = new RoomManager(1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            var endpoint = new GameEndpoint();
            endpoint.Connect("talent-route-unbound", 1);
            endpoint.Receive("talent-route-unbound", 1, MessageSerializer.Serialize("Hello", 0, new HelloMessage
            {
                protocolVersion = NetworkProtocol.Version,
                username = "talent-route-unbound"
            }));
            int beforeCount = endpoint.SentMessages.Count;

            endpoint.Receive(
                "talent-route-unbound",
                1,
                MessageSerializer.Serialize("TalentAction", 0,
                    new TalentActionMessage { decisionId = 505, talentId = "sheathed_edge" }));

            NetworkMessageEnvelope[] newMessages = endpoint.SentMessages
                .Skip(beforeCount)
                .Select(MessageSerializer.DeserializeEnvelope)
                .Where(envelope => envelope != null)
                .ToArray();
            RoomErrorMessage error = newMessages.Length == 1 && newMessages[0].type == "RoomError"
                ? MessageSerializer.DeserializePayload<RoomErrorMessage>(newMessages[0].data)
                : null;
            runner.Check(
                newMessages.Length == 1
                && error?.code == "NotInRoom",
                "an authenticated connection without a bound room seat keeps the existing RoomError contract");
        }
    }

    private static void VerifyManagedTalentAction(
        RegressionRunner runner,
        string connectionId,
        Action<Room> configure,
        TalentActionMessage request,
        bool expectedAccepted,
        bool expectedEffectApplied,
        string expectedErrorCode,
        int expectedServerSubmissions,
        string description)
    {
        using var manager = new RoomManager(1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64);
        Room room = CreateManagedTalentRoom(manager, connectionId, out GameEndpoint endpoint);
        configure(room);
        int beforeCount = endpoint.SentMessages.Count;
        int beforeSequence = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Where(envelope => envelope?.seq > 0)
            .Select(envelope => envelope.seq)
            .DefaultIfEmpty(0)
            .Max();

        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("TalentAction", 0, request));

        NetworkMessageEnvelope[] newMessages = endpoint.SentMessages
            .Skip(beforeCount)
            .Select(MessageSerializer.DeserializeEnvelope)
            .Where(envelope => envelope != null)
            .ToArray();
        TalentActionResolvedMessage resolved = newMessages.Length == 1
            && newMessages[0].type == "TalentActionResolved"
                ? MessageSerializer.DeserializePayload<TalentActionResolvedMessage>(newMessages[0].data)
                : null;
        runner.Check(
            newMessages.Length == 1
            && newMessages[0].seq == beforeSequence + 1
            && resolved != null
            && resolved.decisionId == (request?.decisionId ?? 0)
            && resolved.ownerSeatIndex == 0
            && resolved.talentId == request?.talentId
            && resolved.accepted == expectedAccepted
            && resolved.effectApplied == expectedEffectApplied
            && resolved.errorCode == expectedErrorCode
            && room.GameServer.TalentActionSubmissionCount == expectedServerSubmissions
            && (expectedServerSubmissions == 0
                || (room.GameServer.LastTalentActionSeatIndex == 0
                    && room.GameServer.LastTalentActionMessage?.decisionId == request.decisionId
                    && room.GameServer.LastTalentActionMessage.talentId == request.talentId
                    && room.GameServer.LastTalentActionMessage.targetSeatIndex == request.targetSeatIndex
                    && room.GameServer.LastTalentActionMessage.targetTalentId == request.targetTalentId)),
            description);
    }

    private static Room CreateManagedTalentRoom(
        RoomManager manager,
        string connectionId,
        out GameEndpoint endpoint)
    {
        endpoint = new GameEndpoint();
        endpoint.Connect(connectionId, 1);
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Hello", 0, new HelloMessage
        {
            protocolVersion = NetworkProtocol.Version,
            username = connectionId
        }));
        TrustedPlayerLoadout loadout = PlayerLoadoutCodec.CreateStandardLoadout();
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
        {
            gameMode = (int)GameMode.Single,
            alienationPreset = (int)AlienationPreset.Standard,
            loadout = PlayerLoadoutCodec.CreateMessage(
                loadout.DeckConfig, loadout.TalentConfig, AlienationPreset.Standard)
        }));

        var roomsField = typeof(RoomManager).GetField(
            "_rooms",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var rooms = roomsField?.GetValue(manager) as Dictionary<string, Room>;
        Room room = rooms?.Values.SingleOrDefault();
        if (room == null) throw new InvalidOperationException("RoomManager did not create the talent route test room.");

        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Ready", 0,
            new ReadyMessage { phase = (int)ReadyPhase.MatchStart }));
        endpoint.Receive(connectionId, 1, MessageSerializer.Serialize("Ready", 0,
            new ReadyMessage { phase = (int)ReadyPhase.GameSceneLoaded }));
        if (room.State != RoomState.InRound || room.GameServer == null)
            throw new InvalidOperationException("RoomManager did not start the talent route test room.");
        return room;
    }

    private static void SupplementalActionValidationDoesNotConsumeMainDecision(RegressionRunner runner)
    {
        var tracker = new NetworkDecisionTracker();
        NetworkDecisionContext decision = tracker.OpenMainTurn(2, FutureDeadline());

        bool accepted = tracker.TryValidateSupplementalAction(
            decision.DecisionId,
            seatIndex: 2,
            requiredPhase: NetworkDecisionPhase.MainTurn,
            out string errorCode);

        runner.Check(accepted && errorCode == null,
            "supplemental action validates against the active main decision");
        runner.Check(tracker.Active.SubmittedSeats.Length == 0,
            "supplemental validation does not consume the base action slot");
        runner.Check(tracker.TrySubmitNetworkAction(
                decision.DecisionId, 2, ClientActionType.Discard, out _),
            "ordinary discard remains legal after a supplemental action");
    }

    private static void SupplementalActionValidationRejectsInvalidDecisionContexts(RegressionRunner runner)
    {
        var wrongSeatTracker = new NetworkDecisionTracker();
        NetworkDecisionContext main = wrongSeatTracker.OpenMainTurn(2, FutureDeadline());
        bool wrongSeatAccepted = wrongSeatTracker.TryValidateSupplementalAction(
            main.DecisionId, 1, NetworkDecisionPhase.MainTurn, out string wrongSeatError);

        var staleTracker = new NetworkDecisionTracker();
        NetworkDecisionContext stale = staleTracker.OpenMainTurn(2, FutureDeadline());
        bool staleAccepted = staleTracker.TryValidateSupplementalAction(
            stale.DecisionId - 1, 2, NetworkDecisionPhase.MainTurn, out string staleError);

        var expiredTracker = new NetworkDecisionTracker();
        NetworkDecisionContext expired = expiredTracker.OpenMainTurn(
            2, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        bool expiredAccepted = expiredTracker.TryValidateSupplementalAction(
            expired.DecisionId, 2, NetworkDecisionPhase.MainTurn, out string expiredError);

        var responseTracker = new NetworkDecisionTracker();
        NetworkDecisionContext response = responseTracker.OpenResponse(
            0, new TileData(Suit.Man, 3, 0), new[] { 1 }, FutureDeadline());
        bool wrongPhaseAccepted = responseTracker.TryValidateSupplementalAction(
            response.DecisionId, 1, NetworkDecisionPhase.MainTurn, out string wrongPhaseError);

        runner.Check(!wrongSeatAccepted && wrongSeatError == NetworkErrorCodes.WrongController,
            "supplemental action rejects a non-controller during a main decision");
        runner.Check(!staleAccepted && staleError == NetworkErrorCodes.StaleDecision,
            "supplemental action rejects an old decision id");
        runner.Check(!expiredAccepted && expiredError == NetworkErrorCodes.DecisionExpired,
            "supplemental action rejects an expired decision");
        runner.Check(!wrongPhaseAccepted && wrongPhaseError == NetworkErrorCodes.WrongPhase,
            "supplemental action rejects a main-turn request during a response decision");
    }

    private static long FutureDeadline() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;

    private static void SupplementalTalentAdmissionRejectsResponseWindowsBeforeRuntime(RegressionRunner runner)
    {
        var responseTracker = new NetworkDecisionTracker();
        NetworkDecisionContext response = responseTracker.OpenResponse(
            0, new TileData(Suit.Man, 3, 0), new[] { 1 }, FutureDeadline());
        bool responseRuntimeExecuted = false;
        if (TalentActionAdmissionPolicy.TryValidateMainTurn(
                responseTracker, response.DecisionId, 1, out string responseError))
        {
            responseRuntimeExecuted = true;
        }

        var robKongTracker = new NetworkDecisionTracker();
        NetworkDecisionContext robKong = robKongTracker.OpenRobKong(
            0, new TileData(Suit.Man, 3, 0), new[] { 1 }, FutureDeadline());
        bool robKongRuntimeExecuted = false;
        if (TalentActionAdmissionPolicy.TryValidateMainTurn(
                robKongTracker, robKong.DecisionId, 1, out string robKongError))
        {
            robKongRuntimeExecuted = true;
        }

        runner.Check(!responseRuntimeExecuted && responseError == NetworkErrorCodes.WrongPhase,
            "formal talent admission rejects a response-window request before runtime execution");
        runner.Check(!robKongRuntimeExecuted && robKongError == NetworkErrorCodes.WrongPhase,
            "formal talent admission rejects a rob-kong request before runtime execution");
    }

    private static void CarriedTalentActionExecutesPolymorphically(RegressionRunner runner)
    {
        ActionTalentTestRule.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "network_test_talent_action";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, new ServerGameState(4)));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));

        TalentActionResult accepted = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "network_test_talent_action" },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn));
        TalentActionResult unavailable = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "not_carried" },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn));
        TalentActionResult wrongWindow = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "network_test_talent_action" },
            new TalentActivationContext(session, 0, TalentActivationWindow.Response));

        runner.Check(accepted.Accepted && ActionTalentTestRule.ActivationCount == 1,
            "a carried talent action is located by id and executed through its rule override");
        runner.Check(!unavailable.Accepted
                     && unavailable.ErrorCode == TalentActionErrorCodes.NotCarriedOrInactive,
            "a talent action rejects an id that is not actively carried by its owner");
        runner.Check(!wrongWindow.Accepted && wrongWindow.ErrorCode == TalentActionErrorCodes.NotAvailable,
            "a talent action rejects activation outside its declared window");
        runner.Check(runtime.DrainEventsForSeat(0).Any(runtimeEvent => runtimeEvent.EventType == "test_action"),
            "a polymorphic talent action can emit an owner-filtered runtime event");
    }

    private static void ActiveTalentFeedbackDistinguishesAppliedEffects(RegressionRunner runner)
    {
        TalentMatchRuntime sheathedRuntime = CreateChargedSheathedEdgeRuntime(out GameSession sheathedSession);
        sheathedRuntime.DrainEventsForSeat(0);
        sheathedRuntime.OpenMainDecision(ownerSeatIndex: 0, decisionId: 3000000101L);
        TalentActionResult sheathed = sheathedRuntime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 3000000101L },
            new TalentActivationContext(
                sheathedSession, 0, TalentActivationWindow.MainTurn, decisionId: 3000000101L));
        TalentActionResult rejected = sheathedRuntime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "not_carried", DecisionId = 3000000101L },
            new TalentActivationContext(
                sheathedSession, 0, TalentActivationWindow.MainTurn, decisionId: 3000000101L));
        IReadOnlyList<TalentRuntimeEvent> sheathedEvents = sheathedRuntime.DrainEventsForSeat(0);

        TalentMatchRuntime blockedRuntime = CreateInterceptionRuntime(
            includeTargetComposure: true,
            out GameSession blockedSession);
        ChargeSheathedEdge(blockedRuntime, blockedSession);
        blockedRuntime.DrainEventsForSeat(0);
        blockedRuntime.OpenMainDecision(ownerSeatIndex: 1, decisionId: 3000000102L);
        TalentActionResult interceptionBlocked = TryInterceptionAt(
            blockedRuntime, blockedSession, 3000000102L, 0, "sheathed_edge");
        TalentActionResult duplicate = TryInterceptionAt(
            blockedRuntime, blockedSession, 3000000102L, 0, "sheathed_edge");

        TalentMatchRuntime appliedRuntime = CreateInterceptionRuntime(
            includeTargetComposure: false,
            out GameSession appliedSession);
        ChargeSheathedEdge(appliedRuntime, appliedSession);
        appliedRuntime.DrainEventsForSeat(0);
        appliedRuntime.OpenMainDecision(ownerSeatIndex: 1, decisionId: 3000000103L);
        TalentActionResult interceptionApplied = TryInterceptionAt(
            appliedRuntime, appliedSession, 3000000103L, 0, "sheathed_edge");
        TalentActionResult stale = appliedRuntime.TryActivate(
            1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = 3000000102L,
                TargetSeatIndex = 0,
                TargetTalentId = "sheathed_edge"
            },
            new TalentActivationContext(
                appliedSession, 1, TalentActivationWindow.MainTurn, decisionId: 3000000103L));
        IReadOnlyList<TalentRuntimeEvent> events = appliedRuntime.DrainEventsForSeat(0);

        runner.Check(sheathed.Accepted && sheathed.EffectApplied,
            "arming sheathed edge is an applied active effect");
        runner.Check(interceptionBlocked.Accepted && !interceptionBlocked.EffectApplied,
            "a blocked interception still spends its use but is not a strong success");
        runner.Check(interceptionApplied.Accepted && interceptionApplied.EffectApplied,
            "an unblocked charge reduction is a strong success");
        runner.Check(sheathedEvents.Count(runtimeEvent => runtimeEvent.EventType == "active_talent_applied"
                                                   && runtimeEvent.TalentId == "sheathed_edge"
                                                   && runtimeEvent.Visibility == TalentEventVisibility.Public) == 1
                     && events.Count(runtimeEvent => runtimeEvent.EventType == "active_talent_applied"
                                                   && runtimeEvent.TalentId == "interception"
                                                   && runtimeEvent.Visibility == TalentEventVisibility.Public) == 1,
            "an applied request emits one standardized public feedback event owned by its source talent");
        runner.Check(!rejected.Accepted && !rejected.EffectApplied
                     && sheathedEvents.Count(runtimeEvent => runtimeEvent.EventType == "active_talent_applied") == 1,
            "a rejected request emits no standardized feedback event");
        runner.Check(!duplicate.Accepted && !duplicate.EffectApplied
                     && !stale.Accepted && !stale.EffectApplied,
            "duplicate and stale requests are not applied active effects");
        runner.Check(blockedRuntime.DrainEventsForSeat(0)
                         .Count(runtimeEvent => runtimeEvent.EventType == "active_talent_applied") == 0,
            "blocked, duplicate, and stale requests emit zero standardized feedback events");
    }

    private static void PublicCounterEventsUseStablePresentationCategory(RegressionRunner runner)
    {
        TalentMatchRuntime sheathedRuntime = CreateSheathedEdgeRuntime(out GameSession sheathedSession);
        sheathedRuntime.DrainEventsForSeat(0);
        EndNonWinningRound(sheathedRuntime, sheathedSession, winnerSeatIndex: 1);
        TalentRuntimeEvent edge = sheathedRuntime.DrainEventsForSeat(0)
            .Single(runtimeEvent => runtimeEvent.TalentId == "sheathed_edge");

        TalentMatchRuntime interceptionRuntime = CreateInterceptionRuntime(
            includeTargetComposure: false,
            out GameSession interceptionSession);
        ChargeSheathedEdge(interceptionRuntime, interceptionSession);
        interceptionRuntime.DrainEventsForSeat(0);
        interceptionRuntime.OpenMainDecision(ownerSeatIndex: 1, decisionId: 3000000110L);
        TryInterceptionAt(interceptionRuntime, interceptionSession, 3000000110L, 0, "sheathed_edge");
        TalentRuntimeEvent uses = interceptionRuntime.DrainEventsForSeat(0)
            .Single(runtimeEvent => runtimeEvent.TalentId == "interception"
                                    && runtimeEvent.EventType == "public_counter_changed");

        TalentFeedbackView edgeFeedback = TalentEventPresentationPolicy.Build(ToMessage(edge), false);
        TalentFeedbackView usesFeedback = TalentEventPresentationPolicy.Build(ToMessage(uses), false);
        runner.Check(edge.EventType == "public_counter_changed"
            && edgeFeedback.Level == TalentFeedbackLevel.Medium
            && edgeFeedback.AppendFeed && edgeFeedback.PulseChip
            && !edgeFeedback.ShowToast && !edgeFeedback.PlayAudio,
            "sheathed edge public charge changes use the stable medium presentation category");
        runner.Check(usesFeedback.Level == TalentFeedbackLevel.Medium
            && usesFeedback.AppendFeed && usesFeedback.PulseChip
            && !usesFeedback.ShowToast && !usesFeedback.PlayAudio,
            "interception public use changes use the same stable medium presentation category");
    }

    private static TalentRuntimeEventMessage ToMessage(TalentRuntimeEvent runtimeEvent) => new TalentRuntimeEventMessage
    {
        eventId = runtimeEvent.EventId,
        ownerSeatIndex = runtimeEvent.OwnerSeatIndex,
        talentId = runtimeEvent.TalentId,
        eventType = runtimeEvent.EventType,
        visibility = (int)runtimeEvent.Visibility,
        value = runtimeEvent.Value,
        isScoreDelta = runtimeEvent.IsScoreDelta
    };

    private static void InterceptionConsumesUsageBeforeTargetDefense(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateInterceptionRuntime(
            includeTargetComposure: true,
            out GameSession session);
        ChargeSheathedEdge(runtime, session);
        runtime.DrainEventsForSeat(1);
        runtime.OpenMainDecision(ownerSeatIndex: 1, decisionId: 3000000001L);

        TalentActionResult blocked = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = 3000000001L,
                TargetSeatIndex = 0,
                TargetTalentId = "sheathed_edge"
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId: 3000000001L));
        TalentActionResult repeated = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = 3000000001L,
                TargetSeatIndex = 0,
                TargetTalentId = "sheathed_edge"
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId: 3000000001L));
        IReadOnlyList<TalentRuntimeEvent> events = runtime.DrainEventsForSeat(0);

        runner.Check(blocked.Accepted,
            "a defended interception is still an accepted use");
        runner.Check(runtime.GetPrivateCounter(1, "interception", "uses_remaining") == 2,
            "composure does not refund interception usage");
        runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 3,
            "a blocked interception leaves the target charge unchanged");
        runner.Check(!repeated.Accepted
                     && repeated.ErrorCode == TalentActionErrorCodes.AlreadyUsedThisTurn,
            "interception cannot be used twice for the same long main-decision token");
        runner.Check(events.Any(runtimeEvent => runtimeEvent.TalentId == "interception"
                                               && runtimeEvent.EventType == "talent_revealed"
                                               && runtimeEvent.Visibility == TalentEventVisibility.Public)
                     && events.Any(runtimeEvent => runtimeEvent.TalentId == "interception"
                                                    && runtimeEvent.EventType == "public_counter_changed"
                                                    && runtimeEvent.Value == 2)
                     && events.All(runtimeEvent => runtimeEvent.EventType != "uses_remaining"),
            "the first interception use emits a stable public counter category without leaking its counter key");
    }

    private static void InterceptionLimitsUsageAndEnumeratesOnlyEligibleTargets(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateInterceptionRuntime(
            includeTargetComposure: false,
            out GameSession session);
        ChargeSheathedEdge(runtime, session);
        const long FirstDecision = 3000000010L;
        runtime.OpenMainDecision(ownerSeatIndex: 1, decisionId: FirstDecision);

        IReadOnlyList<TalentActionOption> options = runtime.GetAvailableActions(
            1,
            new TalentActionQueryContext(
                session, 1, TalentActivationWindow.MainTurn, FirstDecision));
        TalentActionResult invalidSelfTarget = runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = FirstDecision,
                TargetSeatIndex = 1,
                TargetTalentId = "interception"
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, FirstDecision));
        int usesAfterInvalidTarget = runtime.GetPrivateCounter(
            1, "interception", "uses_remaining");
        TalentActionResult firstUse = ActivateInterception(runtime, session, FirstDecision);

        AdvanceInterceptionRound(runtime, session);
        const long SecondDecision = 3000000011L;
        TalentActionResult secondUse = ActivateInterception(runtime, session, SecondDecision);
        AdvanceInterceptionRound(runtime, session);
        const long ThirdDecision = 3000000012L;
        TalentActionResult thirdUse = ActivateInterception(runtime, session, ThirdDecision);
        int chargeAfterThirdUse = runtime.GetPublicCounter(0, "sheathed_edge", "edge");
        AdvanceInterceptionRound(runtime, session);
        const long FourthDecision = 3000000013L;
        TalentActionResult exhausted = ActivateInterception(runtime, session, FourthDecision);

        runner.Check(options.Count == 1
                     && options[0].TalentId == "interception"
                     && options[0].AiPriority == 100
                     && options[0].TargetSeatIndex == 0
                     && options[0].TargetTalentId == "sheathed_edge",
            "interception enumerates only the active revealed opposing charge target");
        runner.Check(!invalidSelfTarget.Accepted
                     && usesAfterInvalidTarget == 3,
            "an invalid self target does not consume interception use before a valid target is resolved");
        runner.Check(firstUse.Accepted && secondUse.Accepted && thirdUse.Accepted
                     && chargeAfterThirdUse == 2,
            "each valid interception reduces the public target charge by exactly one");
        runner.Check(!exhausted.Accepted
                     && exhausted.ErrorCode == TalentActionErrorCodes.InsufficientResource,
            "interception has exactly three uses across the match");
        runner.Check(runtime.DrainEventsForSeat(0)
                         .Where(runtimeEvent => runtimeEvent.TalentId == "interception"
                                                && runtimeEvent.EventType == "public_counter_changed")
                         .Select(runtimeEvent => runtimeEvent.Value)
                         .SequenceEqual(new[] { 2, 1, 0 }),
            "later interceptions publish stable counter updates with their visible values");
    }

    private static void InterceptionRevalidatesEveryTargetEligibilityOnTheServer(
        RegressionRunner runner)
    {
        var publicTargetConfig = new TalentSlotConfig();
        publicTargetConfig.SlotTalentIds[0] = "sheathed_edge";
        var interceptorConfig = new TalentSlotConfig();
        interceptorConfig.SlotTalentIds[0] = "interception";
        var hiddenTargetConfig = new TalentSlotConfig();
        hiddenTargetConfig.SlotTalentIds[0] = "network_test_hidden_public_charge";
        var inactiveTargetConfig = new TalentSlotConfig();
        inactiveTargetConfig.ReserveTalentIds[0] = "sheathed_edge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = publicTargetConfig,
                [1] = interceptorConfig,
                [2] = hiddenTargetConfig,
                [3] = inactiveTargetConfig
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        ChargeSheathedEdge(runtime, session);

        const long DecisionId = 3000000020L;
        runtime.OpenMainDecision(ownerSeatIndex: 1, decisionId: DecisionId);
        IReadOnlyList<TalentActionOption> options = runtime.GetAvailableActions(
            1,
            new TalentActionQueryContext(
                session, 1, TalentActivationWindow.MainTurn, DecisionId));
        TalentActionResult self = TryInterceptionAt(runtime, session, DecisionId, 1, "interception");
        TalentActionResult hidden = TryInterceptionAt(
            runtime, session, DecisionId, 2, "network_test_hidden_public_charge");
        TalentActionResult inactive = TryInterceptionAt(runtime, session, DecisionId, 3, "sheathed_edge");
        for (int index = 0; index < 3; index++)
        {
            runtime.ApplyNegativeEffect(new TalentNegativeEffect(
                2,
                "test_source",
                0,
                "sheathed_edge",
                TalentNegativeEffectTypes.ReducePublicChargeLayer));
        }
        TalentActionResult emptyCharge = TryInterceptionAt(
            runtime, session, DecisionId, 0, "sheathed_edge");

        runner.Check(options.Count == 1
                     && options[0].TargetSeatIndex == 0
                     && options[0].TargetTalentId == "sheathed_edge",
            "interception candidates exclude self hidden and inactive public-charge entries");
        runner.Check(!self.Accepted && !hidden.Accepted && !inactive.Accepted && !emptyCharge.Accepted
                     && self.ErrorCode == TalentActionErrorCodes.InvalidTarget
                     && hidden.ErrorCode == TalentActionErrorCodes.InvalidTarget
                     && inactive.ErrorCode == TalentActionErrorCodes.InvalidTarget
                     && emptyCharge.ErrorCode == TalentActionErrorCodes.InvalidTarget
                     && runtime.GetPrivateCounter(1, "interception", "uses_remaining") == 3,
            "server target revalidation rejects self hidden inactive and empty charges without spending uses");
    }

    private static void InactiveCarriedTalentKeepsOwnerPrivateCounter(RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.ReserveTalentIds[0] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [1] = config },
            TalentRegistry.Instance);
        runtime.BeginMatch(new GameSession(GameMode.Single));

        runner.Check(runtime.GetPrivateCounter(1, "interception", "uses_remaining") == 3,
            "an inactive carried interception retains its owner-visible match counter");
    }

    private static void InactiveRevealedInterceptionKeepsStickyPublicCounter(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateInterceptionRuntime(
            includeTargetComposure: false,
            out GameSession session);
        ChargeSheathedEdge(runtime, session);
        const long DecisionId = 3000000030L;
        TalentActionResult used = ActivateInterception(runtime, session, DecisionId);
        runtime.DrainEventsForSeat(0);

        runtime.ReplaceActiveSet(1, new string[0]);
        IReadOnlyList<TalentRuntimeEvent> afterDeactivation = runtime.DrainEventsForSeat(0);
        int privateWhileInactive = runtime.GetPrivateCounter(1, "interception", "uses_remaining");
        int publicWhileInactive = runtime.GetPublicCounter(1, "interception", "uses_remaining");
        TalentActionResult inactiveAction = TryInterceptionAt(
            runtime, session, DecisionId + 1, 0, "sheathed_edge");

        runtime.ReplaceActiveSet(1, new[] { "interception" });
        IReadOnlyList<TalentRuntimeEvent> afterReactivation = runtime.DrainEventsForSeat(0);

        runner.Check(used.Accepted && privateWhileInactive == 2 && publicWhileInactive == 2,
            "an inactive revealed interception preserves owner and public counter projections");
        runner.Check(!inactiveAction.Accepted
                     && inactiveAction.ErrorCode == TalentActionErrorCodes.NotCarriedOrInactive,
            "deactivated carried talents remain unavailable to actions");
        runner.Check(afterDeactivation.Count == 0 && afterReactivation.Count == 0
                     && runtime.GetPublicCounter(1, "interception", "uses_remaining") == 2,
            "activation-set changes do not emit inactive or automatic reactivation updates");
    }

    private static void ComposureBlocksOnlyTheFirstNegativeEffectPerRound(RegressionRunner runner)
    {
        NetworkTestPublicChargeTalent.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "composure";
        config.SlotTalentIds[1] = "network_test_public_charge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        int availableLayersAtRoundStart = runtime.GetSnapshotEntries()
            .Single(entry => entry.OwnerSeatIndex == 0 && entry.TalentId == "composure")
            .PrivateValue;
        TalentNegativeEffect effect = BuildLayerReduction(1, 0);
        IReadOnlyList<TalentRuntimeEvent> beforeBlock = runtime.DrainEventsForSeat(1);
        TalentNegativeEffectResult blocked = runtime.ApplyNegativeEffect(effect);
        int availableLayersAfterBlock = runtime.GetSnapshotEntries()
            .Single(entry => entry.OwnerSeatIndex == 0 && entry.TalentId == "composure")
            .PrivateValue;
        int reductionsAfterFirstEffect = NetworkTestPublicChargeTalent.ReductionCount;
        TalentNegativeEffectResult second = runtime.ApplyNegativeEffect(effect);
        IReadOnlyList<TalentRuntimeEvent> afterBlock = runtime.DrainEventsForSeat(1);

        runner.Check(availableLayersAtRoundStart == 1
                     && beforeBlock.All(runtimeEvent => runtimeEvent.TalentId != "composure"),
            "composure privately projects one available layer at round start while remaining hidden from opponents");
        runner.Check(blocked.WasBlocked && !blocked.WasApplied
                     && blocked.BlockingTalentId == "composure"
                     && reductionsAfterFirstEffect == 0,
            "composure blocks the first negative talent effect each round");
        runner.Check(!second.WasBlocked && second.WasApplied
                     && NetworkTestPublicChargeTalent.ReductionCount == 1,
            "the second negative effect in the same round is not blocked");
        runner.Check(afterBlock.Any(runtimeEvent => runtimeEvent.TalentId == "composure"
                                                   && runtimeEvent.EventType == "blocked_negative_effect"
                                                   && runtimeEvent.Visibility == TalentEventVisibility.Public
                                                   && runtimeEvent.Value == 0)
                     && availableLayersAfterBlock == 0,
            "composure becomes public with zero remaining layers after it blocks");

        runtime.EndRound(new TalentRoundOutcome { IsAborted = true }, session);
        BeginReadyRound(runtime, session);
        int refreshedLayers = runtime.GetSnapshotEntries()
            .Single(entry => entry.OwnerSeatIndex == 0 && entry.TalentId == "composure")
            .PrivateValue;
        NetworkTestPublicChargeTalent.SetCharge(1);
        TalentNegativeEffectResult refreshed = runtime.ApplyNegativeEffect(effect);

        runner.Check(refreshedLayers == 1 && refreshed.WasBlocked && !refreshed.WasApplied,
            "composure refreshes its private layer at the next round boundary");
    }

    private static void NegativeEffectRejectsUnknownTypesWithoutApplying(RegressionRunner runner)
    {
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = new TalentSlotConfig() },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(new TalentNegativeEffect(
            1,
            "test_source",
            0,
            "network_test_public_charge",
            "ChangeConcealedHand"));

        runner.Check(!result.WasBlocked && !result.WasApplied,
            "negative effects reject unknown types before runtime resolves an execution capability");
    }

    private static void NegativeEffectChecksTargetDefensesByPriority(RegressionRunner runner)
    {
        PriorityDefenseTalent.Reset();
        NetworkTestPublicChargeTalent.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "composure";
        config.SlotTalentIds[1] = "network_test_priority_defense";
        config.SlotTalentIds[2] = "network_test_public_charge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(BuildLayerReduction(1, 0));

        runner.Check(PriorityDefenseTalent.BlockAttempts == 1
                     && result.WasBlocked
                     && result.BlockingTalentId == "composure"
                     && NetworkTestPublicChargeTalent.ReductionCount == 0,
            "negative effects check target defenses in priority order before the first block stops application");
    }

    private static void NegativeEffectDescriptionDoesNotExposeAnApplyDelegate(RegressionRunner runner)
    {
        bool exposesDelegate = typeof(TalentNegativeEffect)
            .GetProperties()
            .Any(property => typeof(Delegate).IsAssignableFrom(property.PropertyType));

        runner.Check(!exposesDelegate,
            "defenses receive a read-only negative-effect description with no executable delegate");
    }

    private static void NonTargetAndReserveDefensesDoNotBlockPublicChargeReduction(RegressionRunner runner)
    {
        NetworkTestPublicChargeTalent.Reset();
        var targetConfig = new TalentSlotConfig();
        targetConfig.SlotTalentIds[0] = "network_test_public_charge";
        targetConfig.ReserveTalentIds[0] = "composure";
        var otherSeatConfig = new TalentSlotConfig();
        otherSeatConfig.SlotTalentIds[0] = "composure";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = targetConfig,
                [1] = otherSeatConfig
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(BuildLayerReduction(1, 0));

        runner.Check(!result.WasBlocked && result.WasApplied
                     && NetworkTestPublicChargeTalent.ReductionCount == 1,
            "only active defenses owned by the target seat can block a negative effect");
    }

    private static void NegativeEffectRejectsMissingPublicChargeTarget(RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "composure";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(new TalentNegativeEffect(
            1,
            "test_source",
            0,
            "composure",
            TalentNegativeEffectTypes.ReducePublicChargeLayer));

        runner.Check(!result.WasBlocked && !result.WasApplied,
            "a negative effect rejects an active target without the public-charge capability");
    }

    private static void NegativeEffectRejectsIneligiblePublicChargeBeforeDefenses(
        RegressionRunner runner)
    {
        NetworkTestPublicChargeTalent.Reset();
        var publicConfig = new TalentSlotConfig();
        publicConfig.SlotTalentIds[0] = "composure";
        publicConfig.SlotTalentIds[1] = "network_test_public_charge";
        var hiddenConfig = new TalentSlotConfig();
        hiddenConfig.SlotTalentIds[0] = "network_test_hidden_public_charge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = publicConfig,
                [2] = hiddenConfig
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        NetworkTestPublicChargeTalent.SetCharge(0);
        TalentNegativeEffectResult zeroCharge = runtime.ApplyNegativeEffect(
            BuildLayerReduction(sourceSeatIndex: 1, targetSeatIndex: 0));
        NetworkTestPublicChargeTalent.SetCharge(1);
        TalentNegativeEffectResult defenseStillAvailable = runtime.ApplyNegativeEffect(
            BuildLayerReduction(sourceSeatIndex: 1, targetSeatIndex: 0));
        TalentNegativeEffectResult selfTarget = runtime.ApplyNegativeEffect(
            BuildLayerReduction(sourceSeatIndex: 0, targetSeatIndex: 0));
        TalentNegativeEffectResult hiddenTarget = runtime.ApplyNegativeEffect(
            new TalentNegativeEffect(
                1,
                "test_source",
                2,
                "network_test_hidden_public_charge",
                TalentNegativeEffectTypes.ReducePublicChargeLayer));

        runner.Check(!zeroCharge.WasBlocked && !zeroCharge.WasApplied
                     && defenseStillAvailable.WasBlocked,
            "a zero-charge target is rejected before it can consume composure");
        runner.Check(!selfTarget.WasBlocked && !selfTarget.WasApplied,
            "a public charge effect cannot target its source seat");
        runner.Check(!hiddenTarget.WasBlocked && !hiddenTarget.WasApplied,
            "an unrevealed charge talent cannot be targeted by a negative effect");
    }

    private static void SheathedEdgeChargesCapsAndExposesPublicTargets(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateSheathedEdgeRuntime(out GameSession session);

        EndNonWinningRound(runtime, session, winnerSeatIndex: 1);
        EndNonWinningRound(runtime, session, winnerSeatIndex: null);
        EndNonWinningRound(runtime, session, winnerSeatIndex: 2);
        EndNonWinningRound(runtime, session, winnerSeatIndex: 3);

        IReadOnlyList<PublicChargeTarget> opponentView = runtime.GetPublicChargeTargets(1);
        runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 3,
            "sheathed edge gains one layer on non-winning rounds and caps at three");
        runner.Check(opponentView.Count == 1
                     && opponentView[0].OwnerSeatIndex == 0
                     && opponentView[0].TalentId == "sheathed_edge"
                     && opponentView[0].CurrentCharge == 3,
            "revealed active public charge is exposed to opponents as a read-only target");
        runner.Check(runtime.GetPublicChargeTargets(0).Count == 0,
            "public charge targeting excludes talents owned by the requesting seat");
    }

    private static void SheathedEdgeDoesNotChargeOnOwnerWinOrAbortedRound(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateSheathedEdgeRuntime(out GameSession session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0 }, session);
        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome { IsAborted = true }, session);

        runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 0,
            "sheathed edge does not gain charge when its owner wins or the round aborts");
    }

    private static void SheathedEdgeConsumesAnyPositiveChargeForScaledBonus(
        RegressionRunner runner)
    {
        TalentMatchRuntime emptyRuntime = CreateSheathedEdgeRuntime(out GameSession emptySession);
        emptyRuntime.OpenMainDecision(ownerSeatIndex: 0, decisionId: 80);
        var emptyQuery = new TalentActionQueryContext(
            emptySession, 0, TalentActivationWindow.MainTurn, decisionId: 80);
        TalentActionResult emptyResult = emptyRuntime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 80 },
            new TalentActivationContext(
                emptySession, 0, TalentActivationWindow.MainTurn, decisionId: 80));

        runner.Check(emptyRuntime.GetAvailableActions(0, emptyQuery).Count == 0
                     && !emptyResult.Accepted
                     && emptyResult.ErrorCode == TalentActionErrorCodes.InsufficientResource,
            "zero sheathed-edge layers expose no action and reject direct activation");

        (int Layers, int ExpectedBonus)[] cases =
        {
            (1, 12),
            (2, 24),
            (3, 36)
        };
        foreach ((int layers, int expectedBonus) in cases)
        {
            TalentMatchRuntime runtime = CreateChargedSheathedEdgeRuntime(
                layers, out GameSession session);
            long decisionId = 80 + layers;
            runtime.OpenMainDecision(ownerSeatIndex: 0, decisionId: decisionId);
            var query = new TalentActionQueryContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId);
            int availableActions = runtime.GetAvailableActions(0, query).Count;
            TalentActionResult result = runtime.TryActivate(
                0,
                new TalentActionRequest
                {
                    TalentId = "sheathed_edge",
                    DecisionId = decisionId
                },
                new TalentActivationContext(
                    session, 0, TalentActivationWindow.MainTurn, decisionId));
            TalentFanResolution first = runtime.ResolvePostLegalFan(
                new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)), eligibilityFan: 8);
            TalentFanResolution second = runtime.ResolvePostLegalFan(
                new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)), eligibilityFan: 8);

            runner.Check(availableActions == 1 && result.Accepted && result.EffectApplied,
                $"{layers} sheathed-edge layers expose and accept the active action");
            runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 0,
                $"activating with {layers} sheathed-edge layers immediately consumes them all");
            runner.Check(first.PostLegalBonusFan == expectedBonus
                         && first.FinalFan == 8 + expectedBonus
                         && second.FinalFan == first.FinalFan,
                $"{layers} consumed sheathed-edge layers grant a stable {expectedBonus}-fan bonus");
        }
    }

    private static void SheathedEdgeArmsOnlyOnTheFirstMainDecision(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateChargedSheathedEdgeRuntime(out GameSession session);
        runtime.OpenMainDecision(ownerSeatIndex: 0, decisionId: 91);

        var firstContext = new TalentActionQueryContext(
            session, 0, TalentActivationWindow.MainTurn, decisionId: 91);
        IReadOnlyList<TalentActionOption> firstOptions = runtime.GetAvailableActions(0, firstContext);
        TalentActionResult armed = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 91 },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId: 91));

        runner.Check(firstOptions.Count == 1 && firstOptions[0].AiPriority == 200,
            "three layers advertise a rule-authored finisher priority on the first main decision");
        runner.Check(armed.Accepted
                     && runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 0,
            "arming spends all three layers immediately");

        TalentMatchRuntime missedRuntime = CreateChargedSheathedEdgeRuntime(out GameSession missedSession);
        missedRuntime.OpenMainDecision(0, 101);
        missedRuntime.OpenMainDecision(0, 102);
        var laterContext = new TalentActionQueryContext(
            missedSession, 0, TalentActivationWindow.MainTurn, decisionId: 102);
        TalentActionResult tooLate = missedRuntime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 102 },
            new TalentActivationContext(
                missedSession, 0, TalentActivationWindow.MainTurn, decisionId: 102));

        runner.Check(missedRuntime.GetAvailableActions(0, laterContext).Count == 0
                     && !tooLate.Accepted,
            "sheathed edge cannot arm after the owner's first main decision has passed");
    }

    private static void SheathedEdgeReadOnlyResolutionConsumesOnlyAfterAcceptedWin(
        RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateArmedSheathedEdgeRuntime(out GameSession session);
        runtime.DrainEventsForSeat(0);

        TalentFanResolution first = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)), eligibilityFan: 8);
        TalentFanResolution second = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)), eligibilityFan: 8);
        bool candidatesStayedQuiet = runtime.DrainEventsForSeat(0)
            .All(runtimeEvent => runtimeEvent.EventType != "armed_consumed");

        runtime.ConfirmAcceptedWin(new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)));
        runtime.ConfirmAcceptedWin(new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)));
        int consumedEvents = runtime.DrainEventsForSeat(0)
            .Count(runtimeEvent => runtimeEvent.EventType == "armed_consumed");

        runner.Check(first.EligibilityFan == 8
                     && first.PostLegalBonusFan == 36
                     && first.NegativeFan == 0
                     && first.FinalFan == 44
                     && second.FinalFan == 44,
            "post-legal fan resolution is read-only and repeatable for candidate and final scoring");
        runner.Check(candidatesStayedQuiet && consumedEvents == 1,
            "sheathed edge emits its consumed event only after an accepted win is confirmed");
    }

    private static void SheathedEdgeConsumedChargeDoesNotCarryAcrossRounds(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateArmedSheathedEdgeRuntime(out GameSession session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);

        TalentFanResolution nextRound = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, TalentTestFacts.Win(session, 0)), eligibilityFan: 8);

        runner.Check(nextRound.PostLegalBonusFan == 0 && nextRound.FinalFan == 8,
            "consumed sheathed-edge charge does not carry its armed bonus across rounds");
        runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 1,
            "the next round contains only the newly earned sheathed-edge layer");
    }

    private static TalentMatchRuntime CreateSheathedEdgeRuntime(out GameSession session)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "sheathed_edge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        return runtime;
    }

    private static TalentMatchRuntime CreateInterceptionRuntime(
        bool includeTargetComposure,
        out GameSession session)
    {
        var targetConfig = new TalentSlotConfig();
        targetConfig.SlotTalentIds[0] = "sheathed_edge";
        if (includeTargetComposure)
            targetConfig.SlotTalentIds[1] = "composure";
        var interceptorConfig = new TalentSlotConfig();
        interceptorConfig.SlotTalentIds[0] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = targetConfig,
                [1] = interceptorConfig
            },
            TalentRegistry.Instance);
        session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        return runtime;
    }

    private static void ChargeSheathedEdge(TalentMatchRuntime runtime, GameSession session)
    {
        for (int index = 0; index < 3; index++)
            EndNonWinningRound(runtime, session, winnerSeatIndex: 2);
    }

    private static TalentActionResult ActivateInterception(
        TalentMatchRuntime runtime,
        GameSession session,
        long decisionId)
    {
        runtime.OpenMainDecision(ownerSeatIndex: 1, decisionId: decisionId);
        return TryInterceptionAt(runtime, session, decisionId, 0, "sheathed_edge");
    }

    private static TalentActionResult TryInterceptionAt(
        TalentMatchRuntime runtime,
        GameSession session,
        long decisionId,
        int targetSeatIndex,
        string targetTalentId)
    {
        return runtime.TryActivate(
            1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = decisionId,
                TargetSeatIndex = targetSeatIndex,
                TargetTalentId = targetTalentId
            },
            new TalentActivationContext(
                session, 1, TalentActivationWindow.MainTurn, decisionId));
    }

    private static void AdvanceInterceptionRound(TalentMatchRuntime runtime, GameSession session)
    {
        EndNonWinningRound(runtime, session, winnerSeatIndex: 2);
    }

    private static TalentMatchRuntime CreateChargedSheathedEdgeRuntime(out GameSession session)
    {
        return CreateChargedSheathedEdgeRuntime(3, out session);
    }

    private static TalentMatchRuntime CreateChargedSheathedEdgeRuntime(
        int layers,
        out GameSession session)
    {
        TalentMatchRuntime runtime = CreateSheathedEdgeRuntime(out session);
        for (int index = 0; index < layers; index++)
            EndNonWinningRound(runtime, session, winnerSeatIndex: 1);
        return runtime;
    }

    private static TalentMatchRuntime CreateArmedSheathedEdgeRuntime(out GameSession session)
    {
        TalentMatchRuntime runtime = CreateChargedSheathedEdgeRuntime(out session);
        runtime.OpenMainDecision(0, 91);
        TalentActionResult result = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 91 },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId: 91));
        if (!result.Accepted) throw new InvalidOperationException("Could not arm sheathed edge test fixture.");
        return runtime;
    }

    private static void EndNonWinningRound(
        TalentMatchRuntime runtime,
        GameSession session,
        int? winnerSeatIndex)
    {
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = winnerSeatIndex }, session);
        BeginReadyRound(runtime, session);
    }

    private static TalentNegativeEffect BuildLayerReduction(
        int sourceSeatIndex,
        int targetSeatIndex)
    {
        return new TalentNegativeEffect(
            sourceSeatIndex,
            "test_source",
            targetSeatIndex,
            "network_test_public_charge",
            TalentNegativeEffectTypes.ReducePublicChargeLayer);
    }

    private static void GatherMomentumChargesOnCommittedMeldsAndCapsAtThree(RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. Chi commits -> +1
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        int afterChi = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // 2. Discard does not charge
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Man, 9, 0), null, false, null));
        int afterDiscard = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // 3. Other player's meld does not charge seat 0
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 103, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 5, 2), null, false, null));
        int afterOther = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // 4. Duplicate decision does not charge
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        int afterDuplicate = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // 5. Pon + JiaGang (added kong) on same tile counts as 2 actions
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 104, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Pon,
            new TileData(Suit.Sou, 3, 1), null, false, null));
        int afterPon = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 105, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.JiaGang,
            new TileData(Suit.Sou, 3, 0), null, false, null));
        int afterJiaGang = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // 6. 4th meld caps at 3
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 106, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.AnGan,
            new TileData(Suit.Wind, 1, 0), null, false, null));
        int afterAnGan = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        runner.Check(afterChi == 1
                     && afterDiscard == 1
                     && afterOther == 1
                     && afterDuplicate == 1
                     && afterPon == 2
                     && afterJiaGang == 3
                     && afterAnGan == 3,
            "gather momentum charges on chi, pon, jiagang, angan, caps at 3, and ignores non-meld, duplicate and other-seat actions");
    }

    private static void GatherMomentumArmsAndSpendsAllLayersInMainTurn(RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // Cannot arm with 0 layers
        runtime.OpenMainDecision(0, decisionId: 201);
        var zeroContext = new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 201);
        var zeroOptions = runtime.GetAvailableActions(0, zeroContext);
        TalentActionResult zeroArmed = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "gather_momentum", DecisionId = 201 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 201));

        // Charge 2 layers via 2 melds
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 202, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 203, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.MingGan,
            new TileData(Suit.Pin, 4, 2), null, false, null));

        runtime.OpenMainDecision(0, decisionId: 204);
        var queryContext = new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 204);
        var options = runtime.GetAvailableActions(0, queryContext);
        TalentActionResult armed = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "gather_momentum", DecisionId = 204 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 204));
        int momentumAfterArm = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // Try arming second time in same round
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 205, actorSeatIndex: 0, sourceSeatIndex: 3, ClientActionType.Chi,
            new TileData(Suit.Sou, 5, 3), new[] { 4, 6 }, false, null));
        runtime.OpenMainDecision(0, decisionId: 206);
        var secondQueryContext = new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 206);
        var secondOptions = runtime.GetAvailableActions(0, secondQueryContext);
        TalentActionResult secondArmed = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "gather_momentum", DecisionId = 206 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 206));

        runner.Check(zeroOptions.Count == 0 && !zeroArmed.Accepted,
            "gather momentum cannot arm with 0 momentum layers");
        runner.Check(options.Count == 1 && options[0].AiPriority == 200,
            "gather momentum advertises main-turn option with priority 200");
        runner.Check(armed.Accepted && momentumAfterArm == 0,
            "gather momentum spends all layers immediately upon arming");
        runner.Check(secondOptions.Count == 0 && !secondArmed.Accepted,
            "gather momentum cannot arm more than once per round even if recharged");
    }

    private static void GatherMomentumSupportsPublicChargeControl(RegressionRunner runner)
    {
        var targetConfig = new TalentSlotConfig();
        targetConfig.SlotTalentIds[0] = "gather_momentum";
        var controlConfig = new TalentSlotConfig();
        controlConfig.SlotTalentIds[0] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = targetConfig,
                [1] = controlConfig
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // Charge 2 momentum on seat 0
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 301, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 302, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 3, 2), null, false, null));

        int initial = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // Seat 1 uses Interception on seat 0 gather_momentum
        runtime.OpenMainDecision(1, decisionId: 303);
        TalentActionResult firstIntercept = runtime.TryActivate(1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = 303,
                TargetSeatIndex = 0,
                TargetTalentId = "gather_momentum"
            },
            new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 303));
        int afterFirst = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // Second interception
        runtime.OpenMainDecision(1, decisionId: 304);
        TalentActionResult secondIntercept = runtime.TryActivate(1,
            new TalentActionRequest
            {
                TalentId = "interception",
                DecisionId = 304,
                TargetSeatIndex = 0,
                TargetTalentId = "gather_momentum"
            },
            new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 304));
        int afterSecond = runtime.GetPublicCounter(0, "gather_momentum", "momentum");

        // Third interception when momentum is 0 -> target should not be eligible
        runtime.OpenMainDecision(1, decisionId: 305);
        var query = new TalentActionQueryContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 305);
        var options = runtime.GetAvailableActions(1, query);

        runner.Check(initial == 2
                     && firstIntercept.Accepted && afterFirst == 1
                     && secondIntercept.Accepted && afterSecond == 0,
            "gather momentum implements IPublicChargeTalent and reduces by 1 per control effect down to 0");
        runner.Check(options.Count == 0,
            "gather momentum with 0 charge is no longer an eligible public charge target");
    }

    private static void FadingColorChargesOnFirstModifiedDiscardPerRoundAndCapsAtTwo(RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "fading_color";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1
        BeginReadyRound(runtime, session);

        // Non-modified discard -> 0 ink
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = false }, null, false, null));
        int inkAfterNormal = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // First modified discard -> 1 ink
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));
        int inkAfterFirstModified = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Second modified discard in same round -> still 1 ink
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 103, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 3, 0) { IsModified = true }, null, false, null));
        int inkAfterSecondModified = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Round 2
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);

        // First modified discard in round 2 -> 2 ink
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 4, 0) { IsModified = true }, null, false, null));
        int inkRound2 = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Round 3
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);

        // First modified discard in round 3 -> capped at 2
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 301, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 5, 0) { IsModified = true }, null, false, null));
        int inkRound3 = runtime.GetPrivateCounter(0, "fading_color", "ink");

        runner.Check(inkAfterNormal == 0
                     && inkAfterFirstModified == 1
                     && inkAfterSecondModified == 1
                     && inkRound2 == 2
                     && inkRound3 == 2,
            "fading color charges 1 ink on first modified discard per round, cross-round up to 2");
    }

    private static void FadingColorFullInkExhaustsRoundOpportunityEvenIfInkIsSpentLater(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "sheathed_edge";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1 -> gain 1 ink for seat 0, seat 1 gains 1 edge
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 2 -> gain 2nd ink for seat 0, seat 1 gains 2nd edge
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 3 -> starts with 2 ink (full)
        BeginReadyRound(runtime, session);
        int startingInk = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Turn 1: Commit modified discard while full (2 ink) -> should consume round opportunity even though ink is full
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 301, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 3, 0) { IsModified = true }, null, false, null));
        int inkAfterDiscardFull = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Turn 2: Spend 1 ink via active ability -> ink drops to 1 and reveals fading_color
        runtime.OpenMainDecision(0, decisionId: 302);
        var result = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 302,
            TargetSeatIndex = 1,
            TargetTalentId = "sheathed_edge"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 302));
        int inkAfterSpend = runtime.GetPublicCounter(0, "fading_color", "ink");

        // Turn 3: Commit another modified discard in same round -> MUST NOT re-charge ink (must stay 1)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 303, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 4, 0) { IsModified = true }, null, false, null));
        int inkAfterSecondDiscard = runtime.GetPublicCounter(0, "fading_color", "ink");

        runner.Check(startingInk == 2
                     && inkAfterDiscardFull == 2
                     && result.Accepted && result.EffectApplied && inkAfterSpend == 1
                     && inkAfterSecondDiscard == 1,
            "fading color exhausts round charge opportunity on first modified discard even when full");
    }

    private static void FadingColorSpendsInkToReduceTargetChargeAndReturnsRemainingInk(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Charge seat 0 to 2 ink across 2 rounds
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        // Seat 1 gets 2 momentum in round 1
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 2, 2), null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 103, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Chi,
            new TileData(Suit.Pin, 3, 2), new[] { 2, 4 }, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));

        // Seat 0 now has 2 ink (private), Seat 1 has 2 momentum (public)
        int seat0Ink = runtime.GetPrivateCounter(0, "fading_color", "ink");
        int seat1Momentum = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        // Seat 0 activates fading color targeting seat 1
        runtime.OpenMainDecision(0, decisionId: 202);
        var query = new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202);
        var options = runtime.GetAvailableActions(0, query);
        var targetOption = options.FirstOrDefault(o => o.TalentId == "fading_color" && o.TargetSeatIndex == 1);

        var firstActivation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));

        int seat0InkAfter1 = runtime.GetPublicCounter(0, "fading_color", "ink");
        int seat1MomentumAfter1 = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        // A second activation in the same main decision is rejected without another spend.
        var repeatedSameTurn = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));
        int seat0InkAfterRepeated = runtime.GetPublicCounter(0, "fading_color", "ink");
        int seat1MomentumAfterRepeated = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        // Second activation spends last ink (1 -> 0)
        runtime.OpenMainDecision(0, decisionId: 203);
        var secondActivation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 203,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 203));

        int seat0InkAfter2 = runtime.GetPublicCounter(0, "fading_color", "ink");
        int seat1MomentumAfter2 = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        // Third activation with 0 ink fails
        runtime.OpenMainDecision(0, decisionId: 204);
        var thirdActivation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 204,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 204));

        runner.Check(seat0Ink == 2 && seat1Momentum == 2
                     && targetOption != null && targetOption.TargetPublicCharge == 2,
            "fading color exposes positive public charge targets");
        runner.Check(firstActivation.Accepted && firstActivation.EffectApplied
                     && seat0InkAfter1 == 1 && seat1MomentumAfter1 == 1,
            "fading color spends 1 ink to reduce target charge by 1 and updates public counters");
        runner.Check(!repeatedSameTurn.Accepted
                     && repeatedSameTurn.ErrorCode == TalentActionErrorCodes.AlreadyUsedThisTurn
                     && seat0InkAfterRepeated == 1 && seat1MomentumAfterRepeated == 1,
            "fading color can activate only once in the same main-turn decision without extra spending");
        runner.Check(secondActivation.Accepted && secondActivation.EffectApplied
                     && seat0InkAfter2 == 0 && seat1MomentumAfter2 == 0,
            "fading color spends final ink down to 0 and reduces target charge to 0");
        runner.Check(!thirdActivation.Accepted,
            "fading color rejects activation when ink is 0");
    }

    private static void FadingColorImplementsPublicChargeTalentAndCanBeControlled(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "gather_momentum";
        config1.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Charge seat 0 to 2 ink across 2 rounds
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        // Seat 1 gets 1 momentum in round 1
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 2, 2), null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));

        // Seat 0 activates fading color once (2 ink -> 1 ink), revealing fading_color!
        runtime.OpenMainDecision(0, decisionId: 202);
        var selfActivation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));

        int inkAfterSelf = runtime.GetPublicCounter(0, "fading_color", "ink");

        // Now seat 1 intercepts seat 0's fading_color (1 ink -> 0 ink)
        runtime.OpenMainDecision(1, decisionId: 203);
        var intercept = runtime.TryActivate(1, new TalentActionRequest
        {
            TalentId = "interception",
            DecisionId = 203,
            TargetSeatIndex = 0,
            TargetTalentId = "fading_color"
        }, new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 203));
        int inkAfterIntercept = runtime.GetPublicCounter(0, "fading_color", "ink");

        runner.Check(selfActivation.Accepted && selfActivation.EffectApplied && inkAfterSelf == 1
                     && intercept.Accepted && intercept.EffectApplied && inkAfterIntercept == 0,
            "fading color is revealed after activation and can be controlled by interception down to 0");
    }

    private static void RedirectForceBlocksPublicChargeReductionAndArmsBonus(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[0] = "sheathed_edge";
        config0.SlotTalentIds[1] = "redirect_force";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1 -> seat 0 gains 1 edge
        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 2
        BeginReadyRound(runtime, session);
        int edgeBefore = runtime.GetPublicCounter(0, "sheathed_edge", "edge");

        // Seat 1 tries to intercept seat 0's edge -> blocked by redirect_force
        runtime.OpenMainDecision(1, decisionId: 201);
        var intercept = runtime.TryActivate(1, new TalentActionRequest
        {
            TalentId = "interception",
            DecisionId = 201,
            TargetSeatIndex = 0,
            TargetTalentId = "sheathed_edge"
        }, new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 201));

        int edgeAfter = runtime.GetPublicCounter(0, "sheathed_edge", "edge");
        var snapshotEntries = runtime.GetSnapshotEntries().Where(e => e.OwnerSeatIndex == 0).ToArray();
        var redirectEntry = snapshotEntries.FirstOrDefault(e => e.TalentId == "redirect_force");

        runner.Check(edgeBefore == 1
                     && intercept.Accepted && !intercept.EffectApplied
                     && edgeAfter == 1
                     && redirectEntry != null && redirectEntry.PrivateValue == 0,
            "redirect force blocks first charge reduction targeting owner and consumes its defense");
    }

    private static void RedirectForceAndComposureDefenseOrder(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[0] = "sheathed_edge";
        config0.SlotTalentIds[1] = "redirect_force";
        config0.SlotTalentIds[3] = "composure";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[3] = "interception";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Build 2 edge over 2 rounds
        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);
        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 3 -> 2 edge
        BeginReadyRound(runtime, session);
        int initialEdge = runtime.GetPublicCounter(0, "sheathed_edge", "edge");

        // 1st Interception: Should be blocked by redirect_force (Priority 10 > 0)
        runtime.OpenMainDecision(1, decisionId: 301);
        var first = runtime.TryActivate(1, new TalentActionRequest
        {
            TalentId = "interception",
            DecisionId = 301,
            TargetSeatIndex = 0,
            TargetTalentId = "sheathed_edge"
        }, new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 301));
        int edgeAfterFirst = runtime.GetPublicCounter(0, "sheathed_edge", "edge");

        var snap1 = runtime.GetSnapshotEntries().Where(e => e.OwnerSeatIndex == 0).ToDictionary(e => e.TalentId);
        int redirectVal1 = snap1["redirect_force"].PrivateValue;
        int composureVal1 = snap1["composure"].PrivateValue;

        // 2nd Interception: Should be blocked by composure (Priority 0)
        runtime.OpenMainDecision(1, decisionId: 302);
        var second = runtime.TryActivate(1, new TalentActionRequest
        {
            TalentId = "interception",
            DecisionId = 302,
            TargetSeatIndex = 0,
            TargetTalentId = "sheathed_edge"
        }, new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 302));
        int edgeAfterSecond = runtime.GetPublicCounter(0, "sheathed_edge", "edge");

        var snap2 = runtime.GetSnapshotEntries().Where(e => e.OwnerSeatIndex == 0).ToDictionary(e => e.TalentId);
        int redirectVal2 = snap2["redirect_force"].PrivateValue;
        int composureVal2 = snap2["composure"].PrivateValue;

        // 3rd Interception: Both defenses consumed -> successfully applies! Edge reduces 2 -> 1
        runtime.OpenMainDecision(1, decisionId: 303);
        var third = runtime.TryActivate(1, new TalentActionRequest
        {
            TalentId = "interception",
            DecisionId = 303,
            TargetSeatIndex = 0,
            TargetTalentId = "sheathed_edge"
        }, new TalentActivationContext(session, 1, TalentActivationWindow.MainTurn, decisionId: 303));
        int edgeAfterThird = runtime.GetPublicCounter(0, "sheathed_edge", "edge");

        runner.Check(initialEdge == 2
                     && first.Accepted && !first.EffectApplied && edgeAfterFirst == 2
                     && redirectVal1 == 0 && composureVal1 == 1,
            "1st defense: redirect force with Priority 10 blocks first, leaving composure ready");
        runner.Check(second.Accepted && !second.EffectApplied && edgeAfterSecond == 2
                     && redirectVal2 == 0 && composureVal2 == 0,
            "2nd defense: composure with Priority 0 blocks second when redirect force is consumed");
        runner.Check(third.Accepted && third.EffectApplied && edgeAfterThird == 1,
            "3rd attempt: all defenses consumed, charge reduction is applied");
    }

    private static void FadingColorInvalidTargetDoesNotSpendInk(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "midas_touch";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1: gain 1 ink
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 2: gain 2nd ink -> ink is 2
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));

        int inkBefore = runtime.GetPrivateCounter(0, "fading_color", "ink");

        // Target seat 1 midas_touch which is NOT an IPublicChargeTalent / not exposed target
        runtime.OpenMainDecision(0, decisionId: 202);
        var activation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "midas_touch"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));

        int inkAfter = runtime.GetPrivateCounter(0, "fading_color", "ink");

        runner.Check(!activation.Accepted && activation.ErrorCode == TalentActionErrorCodes.InvalidTarget
                     && inkBefore == 2 && inkAfter == 2,
            "fading color rejects invalid target without spending ink");
    }

    private static void FadingColorBlockedByComposureOrRedirectForceSpendsInkWithoutRefund(RegressionRunner runner)
    {
        CheckFadingColorBlockedByDefenseSpendsInkWithoutRefund(
            runner,
            "redirect_force",
            defenseSlotIndex: 1);
        CheckFadingColorBlockedByDefenseSpendsInkWithoutRefund(
            runner,
            "composure",
            defenseSlotIndex: 3);
    }

    private static void CheckFadingColorBlockedByDefenseSpendsInkWithoutRefund(
        RegressionRunner runner,
        string defenseTalentId,
        int defenseSlotIndex)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "gather_momentum";
        config1.SlotTalentIds[defenseSlotIndex] = defenseTalentId;
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1: seat 0 charges 1 ink, seat 1 charges 1 momentum
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 1, 2), null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 2: seat 0 charges 2nd ink -> ink = 2
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));

        int seat0InkBefore = runtime.GetPrivateCounter(0, "fading_color", "ink");
        int seat1MomentumBefore = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        // Seat 0 targets seat 1's momentum -> blocked by redirect_force
        runtime.OpenMainDecision(0, decisionId: 202);
        var activation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));

        int seat0InkAfter = runtime.GetPublicCounter(0, "fading_color", "ink");
        int seat1MomentumAfter = runtime.GetPublicCounter(1, "gather_momentum", "momentum");

        runner.Check(seat0InkBefore == 2 && seat1MomentumBefore == 1
                     && activation.Accepted && !activation.EffectApplied
                     && seat0InkAfter == 1 && seat1MomentumAfter == 1,
            $"fading color blocked by {defenseTalentId} still consumes ink without refund and leaves target charge intact");
    }

    private static void FadingColorActivePublicEventAndSnapshotFinalValueIsOne(RegressionRunner runner)
    {
        var config0 = new TalentSlotConfig();
        config0.SlotTalentIds[3] = "fading_color";
        var config1 = new TalentSlotConfig();
        config1.SlotTalentIds[0] = "gather_momentum";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config0, [1] = config1 },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);

        // Round 1: seat 0 gets 1 ink, seat 1 gets 1 momentum
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 101, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 1, 0) { IsModified = true }, null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 102, actorSeatIndex: 1, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 1, 2), null, false, null));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 2 }, session);

        // Round 2: seat 0 gets 2nd ink
        BeginReadyRound(runtime, session);
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 0, ClientActionType.Discard,
            new TileData(Suit.Man, 2, 0) { IsModified = true }, null, false, null));

        // Seat 0 activates fading_color from 2 ink -> 1 ink
        runtime.OpenMainDecision(0, decisionId: 202);
        var activation = runtime.TryActivate(0, new TalentActionRequest
        {
            TalentId = "fading_color",
            DecisionId = 202,
            TargetSeatIndex = 1,
            TargetTalentId = "gather_momentum"
        }, new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, decisionId: 202));

        var snapshotEntries = runtime.GetSnapshotEntries().Where(e => e.OwnerSeatIndex == 0).ToArray();
        var fadingEntry = snapshotEntries.FirstOrDefault(e => e.TalentId == "fading_color");

        runner.Check(activation.Accepted && activation.EffectApplied
                     && activation.PublicStateEventType == "ink_changed"
                     && activation.PublicStateValue == 1
                     && fadingEntry != null && fadingEntry.LastPublicValue == 1 && fadingEntry.PrivateValue == 1,
            "fading color activation emits ink_changed event with value 1 and snapshot final public/private value is 1");
    }

    private static void EncirclementTriggersOnlyOnDistinctOpponentSourcesAndAwardsBonus(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "encirclement";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. Initial: not revealed, 0 bonus
        TalentWinFacts winFacts = TalentTestFacts.Win(session, 0);
        TalentFanResolution initialRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry initialSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "encirclement");

        // 2. Chi from Seat 1 -> 1 source, not revealed, 0 bonus
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 201, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        TalentFanResolution afterChi1 = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry afterChiSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "encirclement");

        // 3. Pon from same Seat 1 -> still 1 distinct source, not revealed, 0 bonus
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 202, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Pon,
            new TileData(Suit.Pin, 5, 1), null, false, null));
        TalentFanResolution afterPon1 = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry afterPonSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "encirclement");

        // 4. AnGan (source null) -> ignored
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 203, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.AnGan,
            new TileData(Suit.Sou, 8, 0), null, false, null));

        // 5. JiaGang (source null) -> ignored
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 204, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.JiaGang,
            new TileData(Suit.Pin, 5, 0), null, false, null));

        TalentFanResolution afterGangs = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);

        // 6. MingGan from Seat 2 -> 2nd distinct opponent source -> triggers!
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 205, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.MingGan,
            new TileData(Suit.Wind, 1, 2), null, false, null));
        TalentFanResolution afterMingGan2 = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry afterTriggerSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "encirclement");
        var events = runtime.DrainEventsForSeat(0);
        bool hasTriggerEvent = events.Any(e => e.EventType == "encirclement_triggered" && e.Visibility == TalentEventVisibility.Public);

        runner.Check(initialRes.PostLegalBonusFan == 0 && !initialSnap.IsRevealed,
            "合围 initial state is hidden with 0 bonus");
        runner.Check(afterChi1.PostLegalBonusFan == 0 && !afterChiSnap.IsRevealed,
            "合围 with 1 source remains hidden with 0 bonus");
        runner.Check(afterPon1.PostLegalBonusFan == 0 && !afterPonSnap.IsRevealed,
            "合围 duplicate source does not count twice and remains hidden");
        runner.Check(afterGangs.PostLegalBonusFan == 0,
            "合围 ignores AnGan and JiaGang");
        runner.Check(afterMingGan2.PostLegalBonusFan == 4
                     && afterMingGan2.FinalFan == 12
                     && afterTriggerSnap.IsRevealed
                     && hasTriggerEvent,
            "合围 triggers on 2nd distinct opponent source, reveals publicly, and awards +4 post-legal fan");

        // 7. Check round reset
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 12 }, session);
        BeginReadyRound(runtime, session);
        TalentFanResolution nextRoundRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        runner.Check(nextRoundRes.PostLegalBonusFan == 0,
            "合围 resets round state on next round start");
    }

    private static void LastStandFormationTriggersOnSecondMeldRaisesGateAndAwardsBonus(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[1] = "last_stand_formation";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. Initial state: not revealed, MinimumFan = 8, post-legal bonus = 0
        ScoringOptions initialOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentWinFacts winFacts = TalentTestFacts.Win(session, 0);
        TalentFanResolution initialRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry initialSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "last_stand_formation");

        // 2. 1st meld: Chi -> meld count 1, not triggered
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 301, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));
        ScoringOptions afterChiOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentFanResolution afterChiRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry afterChiSnap = runtime.GetSnapshotEntries()
            .Single(e => e.TalentId == "last_stand_formation");

        // 3. AnGan -> ignored (does not count as public meld)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 302, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.AnGan,
            new TileData(Suit.Sou, 8, 0), null, false, null));

        // 4. JiaGang -> ignored (does not count as new public meld)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 303, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.JiaGang,
            new TileData(Suit.Man, 2, 0), null, false, null));

        ScoringOptions afterGangsOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentFanResolution afterGangsRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);

        // 5. 2nd public meld: Pon from Seat 2 -> triggers!
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 304, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 5, 2), null, false, null));
        ScoringOptions afterPonOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentFanResolution afterPonRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 10);
        TalentSnapshotEntry afterPonSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "last_stand_formation");
        var events = runtime.DrainEventsForSeat(0);
        bool hasTriggerEvent = events.Any(e => e.EventType == "last_stand_formation_triggered" && e.Visibility == TalentEventVisibility.Public);

        // 6. Test threshold check with MahjongLogic
        // Exactly 8 fan hand: 3 melds (2m, 4p, 7s pungs) + 1 dragon pung (Red Dragon) in hand + 5m pair = All Pungs (6) + Dragon Pung (2) = 8 fan
        var eightFanMelds = new List<Meld>
        {
            new Meld(MeldType.Pon, new List<TileData> { new TileData(Suit.Man, 2, 0), new TileData(Suit.Man, 2, 0), new TileData(Suit.Man, 2, 0) }, 1),
            new Meld(MeldType.Pon, new List<TileData> { new TileData(Suit.Pin, 4, 0), new TileData(Suit.Pin, 4, 0), new TileData(Suit.Pin, 4, 0) }, 2),
            new Meld(MeldType.Pon, new List<TileData> { new TileData(Suit.Sou, 7, 0), new TileData(Suit.Sou, 7, 0), new TileData(Suit.Sou, 7, 0) }, 3)
        };
        var eightFanHand = new List<TileData>
        {
            new TileData(Suit.Dragon, 1, 0), new TileData(Suit.Dragon, 1, 0),
            new TileData(Suit.Man, 5, 0), new TileData(Suit.Man, 5, 0)
        };
        // Without last_stand (or before trigger): 8 fan meets 8 fan gate -> Legal
        bool canWin8Before = MahjongLogic.CheckWinWithFan(
            eightFanHand, eightFanMelds, new TileData(Suit.Dragon, 1, 0), false,
            out int fanBefore, out _, options: afterChiOpts);
        // After trigger: the independent minimum threshold rises from 8 to 10.
        bool canWin8After = MahjongLogic.CheckWinWithFan(
            eightFanHand, eightFanMelds, new TileData(Suit.Dragon, 1, 0), false,
            out int fanAfter, out _, options: afterPonOpts);
        bool canWinWithHeadStart = MahjongLogic.CheckWinWithFan(
            eightFanHand, eightFanMelds, new TileData(Suit.Dragon, 1, 0), false,
            out int fanWithHeadStart, out _,
            options: new ScoringOptions { BonusFan = 2, MinimumFan = afterPonOpts.MinimumFan });

        // 7. 3rd meld: MingGan from Seat 3 -> still triggered, no double increase
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 305, actorSeatIndex: 0, sourceSeatIndex: 3, ClientActionType.MingGan,
            new TileData(Suit.Sou, 4, 3), null, false, null));
        ScoringOptions after3rdOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentFanResolution after3rdRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 10);

        runner.Check(initialOpts.BonusFan == 0 && initialOpts.MinimumFan == 8
                     && initialRes.PostLegalBonusFan == 0 && !initialSnap.IsRevealed,
            "背水阵 initial state has MinimumFan=8, no eligibility adjustment, 0 bonus, hidden");
        runner.Check(afterChiOpts.BonusFan == 0 && afterChiOpts.MinimumFan == 8
                     && afterChiRes.PostLegalBonusFan == 0
                     && afterChiSnap.PrivateValue == 1,
            "背水阵 after 1st meld remains unrevealed with MinimumFan=8 and 0 bonus");
        runner.Check(afterGangsOpts.BonusFan == 0 && afterGangsOpts.MinimumFan == 8
                     && afterGangsRes.PostLegalBonusFan == 0,
            "背水阵 ignores AnGan and JiaGang");
        runner.Check(afterPonOpts.BonusFan == 0
                     && afterPonOpts.MinimumFan == 10
                     && afterPonRes.PostLegalBonusFan == 12
                     && afterPonRes.FinalFan == 22
                     && afterPonSnap.IsRevealed
                     && hasTriggerEvent,
            "背水阵 triggers on 2nd meld: MinimumFan=10 without changing fan, reveals publicly, awards +12 post-legal fan");
        runner.Check(canWin8Before && fanBefore == 8 && !canWin8After && fanAfter == 0
                     && canWinWithHeadStart && fanWithHeadStart == 10,
            "背水阵 raises the independent Hu threshold while 快人一步 can still meet the ten-fan gate");
        runner.Check(after3rdOpts.BonusFan == 0 && after3rdOpts.MinimumFan == 10
                     && after3rdRes.PostLegalBonusFan == 12,
            "背水阵 3rd meld does not increase threshold again or change bonus");

        // 8. Round reset
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 22 }, session);
        BeginReadyRound(runtime, session);
        ScoringOptions nextRoundOpts = runtime.BuildScoringOptions(new TalentScoringContext(session, 0));
        TalentFanResolution nextRoundRes = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        runner.Check(nextRoundOpts.BonusFan == 0 && nextRoundOpts.MinimumFan == 8
                     && nextRoundRes.PostLegalBonusFan == 0,
            "背水阵 resets round state on next round start");
    }

    private static void CallTheMarkActionAndAttributionLifecycle(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[1] = "call_the_mark";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. Check reveal policy: PublicAtMatchStart -> IsRevealed is true from match start
        TalentSnapshotEntry startSnap = runtime.GetSnapshotEntries().Single(e => e.TalentId == "call_the_mark");
        runner.Check(startSnap.IsRevealed, "点将 is revealed publicly at match start");

        // 2. Open decision 401: Available actions enumerates 3 opponent seats (1, 2, 3)
        // Kamicha of seat 0 is seat 3 ((0+3)%4 = 3), so seat 3 has highest AiPriority
        const long DecisionId1 = 401;
        runtime.OpenMainDecision(0, DecisionId1);
        var actions = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));
        var callActions = actions.Where(a => a.TalentId == "call_the_mark").ToList();
        var aiSelected = MahjongGame.Core.Agents.AiTalentDecisionPolicy.ChooseActiveAction(callActions);

        runner.Check(callActions.Count == 3
                     && callActions.Any(a => a.TargetSeatIndex == 1)
                     && callActions.Any(a => a.TargetSeatIndex == 2)
                     && callActions.Any(a => a.TargetSeatIndex == 3),
            "点将 offers 3 target options for opponents 1, 2, 3");
        runner.Check(aiSelected != null && aiSelected.TargetSeatIndex == 3,
            "点将 AI prioritizes kamicha (seat 3) over other seats");

        // 3. Rejections do not consume usage:
        // Self target:
        TalentActionResult selfResult = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId1, TargetSeatIndex = 0 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));
        // Invalid seat index:
        TalentActionResult outOfBoundsResult = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId1, TargetSeatIndex = 5 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));
        // Wrong decisionId:
        TalentActionResult wrongDecisionResult = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = 9999, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));

        runner.Check(!selfResult.Accepted && !outOfBoundsResult.Accepted && !wrongDecisionResult.Accepted,
            "点将 rejects self target, out-of-bounds target, and mismatched decisionId");

        // 4. Activate targeting seat 1:
        TalentActionResult validResult = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId1, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));
        TalentSnapshotEntry markedSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.TalentId == "call_the_mark");
        runner.Check(validResult.Accepted
                     && validResult.EffectApplied
                     && validResult.PublicStateValue == 2
                     && markedSnapshot.LastPublicValue == 2
                     && markedSnapshot.PrivateValue == 2
                     && markedSnapshot.PrivateStatusKey == "pending",
            "点将 snapshots its authoritative target and pending presentation state");

        // 5. Duplicate activation in same round is rejected (consumed once per round):
        TalentActionResult duplicateResult = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId1, TargetSeatIndex = 2 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId1));
        runner.Check(!duplicateResult.Accepted,
            "点将 cannot be activated more than once per round");

        // 6. AnGan and JiaGang are ignored (do not resolve mark):
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 402, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.AnGan,
            new TileData(Suit.Sou, 8, 0), null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 403, actorSeatIndex: 0, sourceSeatIndex: null, ClientActionType.JiaGang,
            new TileData(Suit.Pin, 2, 0), null, false, null));

        TalentWinFacts winFacts = TalentTestFacts.Win(session, 0);
        TalentFanResolution resBeforeMeld = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        runner.Check(resBeforeMeld.PostLegalBonusFan == 0,
            "点将 ignores AnGan and JiaGang and bonus is not yet awarded");

        // 7. Commit Chi from target seat 1 -> SUCCESS!
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 404, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));

        TalentFanResolution resSuccess = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry successSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.TalentId == "call_the_mark");
        runner.Check(resSuccess.PostLegalBonusFan == 6
                     && resSuccess.FinalFan == 14
                     && successSnapshot.PrivateStatusKey == "success",
            "点将 awards +6 post-legal fan and snapshots a readable success state");

        // 8. Now test failure case in next round:
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 0, FinalFan = 14 }, session);
        BeginReadyRound(runtime, session);

        const long DecisionId2 = 410;
        runtime.OpenMainDecision(0, DecisionId2);
        runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId2, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId2));

        // Melded from Seat 2 instead of marked Seat 1 -> FAIL!
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 411, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 5, 2), null, false, null));

        TalentFanResolution resFailed = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);

        // Subsequent meld from Seat 1 later in the round does NOT restore bonus:
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 412, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 6, 1), new[] { 5, 7 }, false, null));

        TalentFanResolution resStillFailed = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 8);
        TalentSnapshotEntry failedSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.TalentId == "call_the_mark");

        runner.Check(resFailed.PostLegalBonusFan == 0
                     && resStillFailed.PostLegalBonusFan == 0
                     && failedSnapshot.PrivateStatusKey == "failed",
            "点将 permanently invalidates bonus and snapshots a readable failure state");

        // 9. Expired without meld:
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 3 }, session);
        BeginReadyRound(runtime, session);
        // Round 3 begins: usage is restored
        const long DecisionId3 = 420;
        runtime.OpenMainDecision(0, DecisionId3);
        var round3Actions = runtime.GetAvailableActions(
            0,
            new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, DecisionId3));
        runner.Check(round3Actions.Any(a => a.TalentId == "call_the_mark"),
            "点将 restores usage in subsequent round even if expired in previous round");
    }

    private static void FollowTheTrailTracksOpponentDiscardsAndAwardsBonusOnMatchingSuitRon(
        RegressionRunner runner)
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "follow_the_trail";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. First discard from Seat 1: Man 3
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 501, actorSeatIndex: 1, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Man, 3, 1), null, false, null));

        // Ron immediately on Seat 1's first discard (no previous discard exists):
        TalentWinFacts winFacts1st = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 1,
            new[] { new TileData(Suit.Man, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Man, 3, 1),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution res1st = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts1st), eligibilityFan: 8);
        runner.Check(res1st.PostLegalBonusFan == 0,
            "循迹 awards 0 bonus when discarder has no previous discard");

        // 2. Second discard from Seat 1 (automatic fallback): Man 7 -> prev is Man 3 (Man), curr is Man 7 (Man)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 502, actorSeatIndex: 1, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Man, 7, 1), null, wasAutomatic: true, null));

        // Ron on Seat 1's Man 7 discard: winning tile is Man 7, previous discard was Man 3 -> Both Man -> +4 bonus!
        TalentWinFacts winFactsMatching = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 1,
            new[] { new TileData(Suit.Man, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Man, 7, 1),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution resMatching = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFactsMatching), eligibilityFan: 8);
        runner.Check(resMatching.PostLegalBonusFan == 4 && resMatching.FinalFan == 12,
            "循迹 awards +4 post-legal fan on Ron when discarder previous discard suit matches winning tile suit (both Man)");

        runtime.ResolveAcceptedWinVisibility(new TalentAcceptedWinContext(
            session,
            0,
            winFactsMatching,
            new TalentWinEvaluation(isLegal: true, finalFan: resMatching.FinalFan),
            withoutEntryOptions =>
            {
                TalentFanResolution counterfactual = runtime.ResolvePostLegalFan(
                    new TalentWinContext(session, 0, winFactsMatching),
                    eligibilityFan: 8,
                    withoutEntryOptions);
                return new TalentWinEvaluation(isLegal: true, finalFan: counterfactual.FinalFan);
            }));
        TalentSnapshotEntry revealedSnapshot = runtime.GetSnapshotEntries()
            .Single(entry => entry.TalentId == "follow_the_trail");
        runner.Check(revealedSnapshot.IsRevealed,
            "循迹 becomes public through accepted-win counterfactual visibility");

        TalentWinFacts opponentWinFacts = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 2,
            discarderSeatIndex: 1,
            new[] { new TileData(Suit.Man, 1, 2) },
            new List<Meld>(),
            new TileData(Suit.Man, 7, 1),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution opponentResolution = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 2, opponentWinFacts), eligibilityFan: 8);
        runner.Check(opponentResolution.PostLegalBonusFan == 0,
            "循迹 global discard observation never grants its bonus to another winner");

        // 3. Different suit Ron:
        // Seat 2 discards Pin 2 (1st), then Sou 5 (2nd):
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 503, actorSeatIndex: 2, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Pin, 2, 2), null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 504, actorSeatIndex: 2, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Sou, 5, 2), null, false, null));

        TalentWinFacts winFactsDiffSuit = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 2,
            new[] { new TileData(Suit.Sou, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Sou, 5, 2),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution resDiffSuit = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFactsDiffSuit), eligibilityFan: 8);
        runner.Check(resDiffSuit.PostLegalBonusFan == 0,
            "循迹 awards 0 bonus when previous discard suit (Pin) differs from winning tile suit (Sou)");

        TalentWinFacts robKongFacts = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 2,
            new[] { new TileData(Suit.Sou, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Sou, 5, 2),
            isSelfDraw: false,
            isRobKong: true,
            isKongReplacement: false);
        TalentFanResolution robKongResolution = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, robKongFacts), eligibilityFan: 8);
        runner.Check(robKongResolution.PostLegalBonusFan == 4,
            "循迹 compares a robbed-kong tile with the discarder current history because the JiaGang is not a discard commit");

        // 4. Self Draw (Tsumo):
        TalentWinFacts winFactsTsumo = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: null,
            new[] { new TileData(Suit.Man, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Man, 7, 0),
            isSelfDraw: true,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution resTsumo = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFactsTsumo), eligibilityFan: 8);
        runner.Check(resTsumo.PostLegalBonusFan == 0,
            "循迹 awards 0 bonus on self-draw (Tsumo)");

        // 5. Honor tile (字牌):
        // Seat 3 discards East, then East:
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 505, actorSeatIndex: 3, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Wind, 1, 3), null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 506, actorSeatIndex: 3, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Wind, 1, 3), null, false, null));

        TalentWinFacts winFactsHonor = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 3,
            new[] { new TileData(Suit.Wind, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Wind, 1, 3),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);
        TalentFanResolution resHonor = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFactsHonor), eligibilityFan: 8);
        runner.Check(resHonor.PostLegalBonusFan == 0,
            "循迹 awards 0 bonus when tiles are Honor tiles (Wind/Dragon)");
    }

    private static void MultipleNewTalentsStackAndAttributeWithGatherMomentum(
        RegressionRunner runner)
    {
        // Seat 0 has:
        // Slot 0 (Large): gather_momentum
        // Slot 1 (Medium): last_stand_formation
        // Slot 2 (Medium): call_the_mark
        // Slot 3 (Small): encirclement
        // Slot 4 (Small): follow_the_trail
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "gather_momentum";
        config.SlotTalentIds[1] = "last_stand_formation";
        config.SlotTalentIds[2] = "call_the_mark";
        config.SlotTalentIds[3] = "encirclement";
        config.SlotTalentIds[4] = "follow_the_trail";

        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        // 1. Arm call_the_mark targeting Seat 1:
        const long DecisionId = 601;
        runtime.OpenMainDecision(0, DecisionId);
        runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "call_the_mark", DecisionId = DecisionId, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId));

        // 2. Commit Chi from Seat 1 (target):
        // -> gather_momentum: momentum = 1
        // -> call_the_mark: success (+6)
        // -> encirclement: source 1 recorded (1/2)
        // -> last_stand_formation: meld count = 1 (1/2)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 602, actorSeatIndex: 0, sourceSeatIndex: 1, ClientActionType.Chi,
            new TileData(Suit.Man, 2, 1), new[] { 1, 3 }, false, null));

        // 3. Commit Pon from Seat 2:
        // -> gather_momentum: momentum = 2
        // -> encirclement: source 2 recorded (2/2) -> triggers! (+4)
        // -> last_stand_formation: meld count = 2 (2/2) -> triggers! (MinimumFan = 10, +12)
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 603, actorSeatIndex: 0, sourceSeatIndex: 2, ClientActionType.Pon,
            new TileData(Suit.Pin, 5, 2), null, false, null));

        // 4. Activate gather_momentum (armed with 2 layers -> +16):
        const long DecisionId2 = 604;
        runtime.OpenMainDecision(0, DecisionId2);
        runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "gather_momentum", DecisionId = DecisionId2 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, DecisionId2));

        // 5. Opponent 3 discards Sou 2, then Sou 8:
        // -> follow_the_trail: prev = Sou, curr = Sou
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 605, actorSeatIndex: 3, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Sou, 2, 3), null, false, null));
        runtime.CommitAction(TalentActionCommittedFacts.Create(
            decisionId: 606, actorSeatIndex: 3, sourceSeatIndex: null, ClientActionType.Discard,
            new TileData(Suit.Sou, 8, 3), null, false, null));

        // 6. Ron from Seat 3 with Sou 8:
        // -> follow_the_trail: matching suit Sou -> +4
        TalentWinFacts winFacts = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 3,
            new[] { new TileData(Suit.Sou, 1, 0) },
            new List<Meld>(),
            new TileData(Suit.Sou, 8, 3),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);

        // Eligibility base: 10 fan hand meets last_stand_formation MinimumFan = 10.
        // Post-legal bonuses:
        // - gather_momentum: +16
        // - last_stand_formation: +12
        // - call_the_mark: +6
        // - encirclement: +4
        // - follow_the_trail: +4
        // Total post-legal bonus = 16 + 12 + 6 + 4 + 4 = 42
        // Final fan = 10 + 42 = 52.
        TalentFanResolution resolution = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0, winFacts), eligibilityFan: 10);

        runner.Check(resolution.EligibilityFan == 10
                     && resolution.PostLegalBonusFan == 42
                     && resolution.FinalFan == 52,
            "all five talents stack post-legal bonuses correctly (16 + 12 + 6 + 4 + 4 = 42)");

        // 7. Test AcceptedWin fan attribution:
        TalentFanResolution attribution = runtime.ResolveAcceptedWinFan(
            new TalentAcceptedWinAttributionContext(
                session,
                winnerSeatIndex: 0,
                alreadyAcceptedFinalFan: 52,
                facts: winFacts,
                evaluateOptions: scoringOptions => new FanEvaluation
                {
                    HasWinningShape = true,
                    Fan = 10 + scoringOptions.BonusFan,
                    FanDetails = new List<string>()
                }));

        runner.Check(attribution.IsAttributionComplete
                     && attribution.BaseFan == 10
                     && attribution.FinalFan == 52,
            "accepted win attribution reconciles base 10 fan to final 52 fan");

        var gatherRow = attribution.Contributions.FirstOrDefault(c => c.TalentId == "gather_momentum");
        var lastStandRow = attribution.Contributions.FirstOrDefault(c => c.TalentId == "last_stand_formation");
        var callMarkRow = attribution.Contributions.FirstOrDefault(c => c.TalentId == "call_the_mark");
        var encirclementRow = attribution.Contributions.FirstOrDefault(c => c.TalentId == "encirclement");
        var followTrailRow = attribution.Contributions.FirstOrDefault(c => c.TalentId == "follow_the_trail");

        runner.Check(gatherRow != null && gatherRow.FanDelta == 16, "gather_momentum attribution row is +16");
        runner.Check(lastStandRow != null && lastStandRow.FanDelta == 12, "last_stand_formation attribution row is +12 without treating the gate as a fan penalty");
        runner.Check(callMarkRow != null && callMarkRow.FanDelta == 6, "call_the_mark attribution row is +6");
        runner.Check(encirclementRow != null && encirclementRow.FanDelta == 4, "encirclement attribution row is +4");
        runner.Check(followTrailRow != null && followTrailRow.FanDelta == 4, "follow_the_trail attribution row is +4");
    }

    private static void PiercingInsightUniversalRevealAndNetworkTests(RegressionRunner runner)
    {
        // 1. Universal Private Reveal model & sanitization
        var dirtyTiles = new List<TileData>
        {
            new TileData(Suit.Man, 5, 2) { ID = "tile-secret-123", IsModified = true, SpecialEffectID = "midas_touch" },
            new TileData(Suit.Pin, 1, 3) { ID = "tile-secret-456", IsModified = false, SpecialEffectID = null }
        };
        var reveal = new TalentPrivateTileReveal("piercing_insight", 0, 1, 1, dirtyTiles);
        runner.Check(reveal.TalentId == "piercing_insight"
            && reveal.ViewerSeatIndex == 0
            && reveal.TargetSeatIndex == 1
            && reveal.RoundNumber == 1
            && reveal.Tiles.Count == 2
            && reveal.Tiles[0].TileSuit == Suit.Man
            && reveal.Tiles[0].Value == 5
            && reveal.Tiles[0].IsModified
            && string.IsNullOrEmpty(reveal.Tiles[0].ID)
            && string.IsNullOrEmpty(reveal.Tiles[0].SpecialEffectID)
            && reveal.Tiles[0].OriginalOwnerID == 0
            && reveal.Tiles[1].TileSuit == Suit.Pin
            && reveal.Tiles[1].Value == 1
            && !reveal.Tiles[1].IsModified,
            "TalentPrivateTileReveal sanitizes Tile ID, owner, and internal specialEffectId while keeping suit, value, and isModified");

        // 2. Runtime lifecycle and deep copy checks
        var loadouts = Enumerable.Range(0, 4).ToDictionary(index => index, _ => new TalentSlotConfig());
        loadouts[0].SlotTalentIds[0] = "piercing_insight";
        var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        runtime.RecordPrivateTileReveal(0, 1, "piercing_insight", dirtyTiles, 1);
        TalentPrivateTileReveal runtimeReveal = runtime.GetPrivateTileReveal(0);
        runner.Check(runtimeReveal != null && runtimeReveal.Tiles.Count == 2,
            "runtime stores private tile reveal for viewer seat 0");
        runner.Check(runtime.GetPrivateTileReveal(1) == null
            && runtime.GetPrivateTileReveal(2) == null
            && runtime.GetPrivateTileReveal(3) == null,
            "runtime returns null private tile reveal for other seats");

        // Modifying returned tile data must not affect runtime state.
        runtimeReveal.Tiles[0].Value = 9;
        runtimeReveal.Tiles[0].IsModified = false;
        TalentPrivateTileReveal runtimeRevealSecond = runtime.GetPrivateTileReveal(0);
        runner.Check(runtimeRevealSecond != null
            && runtimeRevealSecond.Tiles.Count == 2
            && runtimeRevealSecond.Tiles[0].Value == 5
            && runtimeRevealSecond.Tiles[0].IsModified,
            "mutating returned reveal tiles does not modify runtime state");

        // Ending the round clears private tile reveal before the next round begins.
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        runner.Check(runtime.GetPrivateTileReveal(0) == null,
            "round end clears private tile reveals immediately");
        BeginReadyRound(runtime, session);
        runner.Check(runtime.GetPrivateTileReveal(0) == null,
            "new round clears private tile reveals");

        // 3. PiercingInsight rule tests
        TalentMetadata metadata = TalentRegistry.Instance.GetMetadata("piercing_insight");
        runner.Check(TalentRegistry.Instance.GetTier("piercing_insight") == TalentTier.Large
            && TalentRegistry.Instance.GetCost("piercing_insight") == 26
            && metadata != null
            && metadata.StateScope == TalentStateScope.Round
            && metadata.ActivationWindow == TalentActivationWindow.MainTurn
            && metadata.RevealPolicy == TalentRevealPolicy.HiddenUntilPublicEffect
            && metadata.SideboardPolicy == TalentSideboardPolicy.Flexible,
            "piercing_insight has correct Large tier, 26 cost, MainTurn window, HiddenUntilPublicEffect, Flexible sideboard metadata");

        var options = runtime.GetAvailableActions(0, new TalentActionQueryContext(session, 0, TalentActivationWindow.MainTurn, 1001));
        var piercingOptions = options.Where(o => o.TalentId == "piercing_insight").ToList();
        runner.Check(piercingOptions.Count == 3
            && piercingOptions.Select(o => o.TargetSeatIndex).OrderBy(s => s).SequenceEqual(new[] { 1, 2, 3 }),
            "piercing_insight generates 3 legal opponent candidates and excludes self");

        // Target hands mock
        var targetHands = new Dictionary<int, List<TileData>>
        {
            [0] = new List<TileData> { new(Suit.Man, 1, 0) },
            [1] = new List<TileData>
            {
                new(Suit.Man, 1, 1),
                new(Suit.Man, 9, 1) { IsModified = true },
                new(Suit.Pin, 3, 1),
                new(Suit.Pin, 3, 1), // duplicate preserved
                new(Suit.Sou, 7, 1),
                new(Suit.Wind, 1, 1), // honor filtered out
                new(Suit.Dragon, 2, 1) // honor filtered out
            },
            [2] = new List<TileData>
            {
                new(Suit.Wind, 1, 2),
                new(Suit.Dragon, 3, 2)
            } // no number tiles
        };

        Func<int, IReadOnlyList<TileData>> handProvider = seat => targetHands.TryGetValue(seat, out var h) ? h : Array.Empty<TileData>();

        // Rejection: authoritative concealed-hand provider is unavailable.
        TalentActionResult missingProviderReject = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1001, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 1001));
        runner.Check(!missingProviderReject.Accepted
            && missingProviderReject.ErrorCode == TalentActionErrorCodes.NotAvailable
            && runtime.GetPrivateTileReveal(0) == null,
            "piercing_insight rejects without an authoritative hand provider and does not consume its use");

        // Rejection: Invalid Target (self)
        TalentActionResult selfReject = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1001, TargetSeatIndex = 0 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 1001, handProvider));
        runner.Check(!selfReject.Accepted && selfReject.ErrorCode == TalentActionErrorCodes.InvalidTarget,
            "piercing_insight rejects targeting self");

        // Rejection: Out of range target
        TalentActionResult rangeReject = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1001, TargetSeatIndex = 5 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 1001, handProvider));
        runner.Check(!rangeReject.Accepted && rangeReject.ErrorCode == TalentActionErrorCodes.InvalidTarget,
            "piercing_insight rejects out-of-range target");

        // Rejection: Wrong activation window
        TalentActionResult windowReject = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1001, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.Response, 1001, handProvider));
        runner.Check(!windowReject.Accepted,
            "piercing_insight rejects activation outside MainTurn window");

        // Legal activation targeting Seat 1
        TalentActionResult successResult = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1001, TargetSeatIndex = 1 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 1001, handProvider));
        runner.Check(successResult.Accepted
            && successResult.EffectApplied
            && successResult.PublicStateEventType == "piercing_insight_target"
            && successResult.PublicStateValue == 2,
            "piercing_insight activates successfully with public target value = 2 (seat 1 + 1)");

        TalentPrivateTileReveal revealedResult = runtime.GetPrivateTileReveal(0);
        runner.Check(revealedResult != null
            && revealedResult.TargetSeatIndex == 1
            && revealedResult.Tiles.Count == 5
            && revealedResult.Tiles.All(t => t.TileSuit is Suit.Man or Suit.Pin or Suit.Sou)
            && revealedResult.Tiles.Count(t => t.TileSuit == Suit.Pin && t.Value == 3) == 2
            && revealedResult.Tiles.Single(t => t.TileSuit == Suit.Man && t.Value == 9).IsModified,
            "piercing_insight reveals only number tiles, preserves duplicates and IsModified, and excludes honor tiles");

        // Public event verification: public events must not contain reveal tiles
        var seat1Events = runtime.DrainEventsForSeat(1);
        var targetEvent = seat1Events.FirstOrDefault(e => e.EventType == "piercing_insight_target");
        runner.Check(targetEvent != null && targetEvent.Value == 2,
            "public event exposes target seat index + 1 without exposing tile contents");

        // Snapshot entry private value
        TalentSnapshotEntry snapEntry = runtime.GetSnapshotEntries().Single(e => e.TalentId == "piercing_insight");
        runner.Check(snapEntry.IsRevealed && snapEntry.PrivateValue == 0,
            "snapshot entry is revealed and has private value 0 (0 uses remaining)");

        // Rejection: second activation in same round
        TalentActionResult secondUseReject = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 1002, TargetSeatIndex = 2 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 1002, handProvider));
        runner.Check(!secondUseReject.Accepted && secondUseReject.ErrorCode == TalentActionErrorCodes.AlreadyUsedThisTurn,
            "piercing_insight cannot be activated twice in the same round");

        // Legal activation on seat 2 (empty number tiles) in next round
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);
        TalentActionResult emptyTargetSuccess = runtime.TryActivate(0,
            new TalentActionRequest { TalentId = "piercing_insight", DecisionId = 2001, TargetSeatIndex = 2 },
            new TalentActivationContext(session, 0, TalentActivationWindow.MainTurn, 2001, handProvider));
        runner.Check(emptyTargetSuccess.Accepted && emptyTargetSuccess.EffectApplied,
            "piercing_insight targeting player with no number tiles succeeds and consumes use");
        TalentPrivateTileReveal emptyReveal = runtime.GetPrivateTileReveal(0);
        runner.Check(emptyReveal != null && emptyReveal.Tiles.Count == 0,
            "piercing_insight on player without number tiles yields empty tile list");

        // 4. Network and Snapshot integration
        using (var manager = new RoomManager(1, true, new ConnectionRegistry(int.MaxValue), messageCacheSize: 64))
        {
            var endpoint = new GameEndpoint();
            endpoint.Connect("insight-test-user", 1);
            endpoint.Receive("insight-test-user", 1, MessageSerializer.Serialize("Hello", 0, new HelloMessage
            {
                protocolVersion = NetworkProtocol.Version,
                username = "insight-test-user"
            }));
            TrustedPlayerLoadout standard = PlayerLoadoutCodec.CreateStandardLoadout();
            var talentConfig = new TalentSlotConfig();
            talentConfig.SlotTalentIds[0] = "piercing_insight";
            PlayerLoadoutMessage loadoutMsg = PlayerLoadoutCodec.CreateMessage(
                standard.DeckConfig, talentConfig, AlienationPreset.Standard);

            endpoint.Receive("insight-test-user", 1, MessageSerializer.Serialize("CreateRoom", 0, new CreateRoomMessage
            {
                gameMode = (int)GameMode.Single,
                alienationPreset = (int)AlienationPreset.Standard,
                loadout = loadoutMsg
            }));
            var roomsField = typeof(RoomManager).GetField(
                "_rooms",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var rooms = roomsField?.GetValue(manager) as Dictionary<string, Room>;
            Room room = rooms?.Values.SingleOrDefault();
            endpoint.Receive("insight-test-user", 1, MessageSerializer.Serialize("Ready", 0,
                new ReadyMessage { phase = (int)ReadyPhase.MatchStart }));
            endpoint.Receive("insight-test-user", 1, MessageSerializer.Serialize("Ready", 0,
                new ReadyMessage { phase = (int)ReadyPhase.GameSceneLoaded }));

            runner.Check(room != null && room.State == RoomState.InRound, "room started into round with piercing_insight");
            room.GameServer.SetAiTalentDecisionForTests(
                1001,
                0,
                new[]
                {
                    new TalentActionOption
                    {
                        TalentId = "piercing_insight",
                        TargetSeatIndex = 1
                    }
                },
                TalentActionResult.Success(effectApplied: true, publicStateEventType: "piercing_insight_target", publicStateValue: 2));

            long decisionId = room.GameServer.ActiveDecision?.DecisionId ?? 0;
            runner.Check(decisionId == 1001, "active decision opened for seat 0");

            room.GameServer.TalentRuntime.RecordPrivateTileReveal(0, 1, "piercing_insight", new TileData[] { new(Suit.Man, 1, 0), new(Suit.Pin, 2, 0) }, 1);

            int beforeCount = endpoint.SentMessages.Count;
            endpoint.Receive("insight-test-user", 1, MessageSerializer.Serialize("TalentAction", 0, new TalentActionMessage
            {
                decisionId = decisionId,
                talentId = "piercing_insight",
                targetSeatIndex = 1
            }));

            NetworkMessageEnvelope[] newMessages = endpoint.SentMessages
                .Skip(beforeCount)
                .Select(MessageSerializer.DeserializeEnvelope)
                .Where(envelope => envelope != null)
                .ToArray();

            TalentActionResolvedMessage resolvedMsg = newMessages.Select(e => e.type == "TalentActionResolved"
                ? MessageSerializer.DeserializePayload<TalentActionResolvedMessage>(e.data)
                : null).FirstOrDefault(m => m != null);

            runner.Check(resolvedMsg != null
                && resolvedMsg.accepted
                && resolvedMsg.talentId == "piercing_insight"
                && resolvedMsg.ownerSeatIndex == 0,
                "Seat 0 received TalentActionResolved message");

            // Snapshot test
            RoomGameSnapshot snapshot0 = room.BuildSnapshot(0);
            RoomGameSnapshot snapshot1 = room.BuildSnapshot(1);
            runner.Check(snapshot0.privateSeat.privateTileReveal != null
                && snapshot0.privateSeat.privateTileReveal.talentId == "piercing_insight"
                && snapshot0.privateSeat.privateTileReveal.targetSeatIndex == 1
                && snapshot0.privateSeat.privateTileReveal.tiles.Length == 2,
                "Snapshot for Seat 0 contains privateTileReveal");
            runner.Check(snapshot1.privateSeat.privateTileReveal == null,
                "Snapshot for Seat 1 contains null privateTileReveal");

            // ClientGameState application
            var clientState0 = new ClientGameState();
            clientState0.ApplySnapshot(snapshot0, 10);
            runner.Check(clientState0.Snapshot.privateSeat.privateTileReveal != null
                && clientState0.Snapshot.privateSeat.privateTileReveal.talentId == "piercing_insight",
                "ClientGameState atomically applies snapshot with privateTileReveal");

            var clientState1 = new ClientGameState();
            clientState1.ApplySnapshot(snapshot1, 10);
            runner.Check(clientState1.Snapshot.privateSeat.privateTileReveal == null,
                "ClientGameState for Seat 1 has no privateTileReveal in privateSeat");

            var mismatchedRevealSnapshot = RoomGameSnapshotBuilder.Build(new RoomGameSnapshotSource
            {
                RoomId = "mismatched-private-reveal",
                RoomState = RoomState.InRound,
                GameMode = GameMode.Single,
                Session = new GameSession(GameMode.Single),
                PrivateTileReveal = new TalentPrivateTileReveal(
                    "piercing_insight", 0, 1, 1, new[] { new TileData(Suit.Man, 5, 0) })
            }, 1);
            runner.Check(mismatchedRevealSnapshot.privateSeat.privateTileReveal == null,
                "snapshot builder refuses a private reveal owned by another requesting seat");
        }
    }

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, new ServerGameState(4)));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));

    }
}

[TalentRule("network_test_talent_action", "Action Test", "test", TalentTier.Small, 0,
    ActivationWindow = TalentActivationWindow.MainTurn)]
internal sealed class ActionTalentTestRule : TalentRule
{
    public static int ActivationCount { get; private set; }

    public static void Reset() => ActivationCount = 0;

    public override TalentActionResult TryActivate(TalentActivationContext context, TalentActionRequest request)
    {
        ActivationCount++;
        context.Emit(new TalentRuntimeEvent
        {
            EventType = "test_action",
            Visibility = TalentEventVisibility.OwnerOnly
        });
        return TalentActionResult.Success(effectApplied: false);
    }
}

[TalentRule("network_test_priority_defense", "Priority Defense", "test", TalentTier.Small, 0)]
internal sealed class PriorityDefenseTalent : TalentRule
{
    public static int BlockAttempts { get; private set; }

    public override int Priority => 10;

    public static void Reset() => BlockAttempts = 0;

    public override bool TryBlockNegativeEffect(
        TalentNegativeEffectContext context,
        TalentNegativeEffect effect)
    {
        BlockAttempts++;
        return false;
    }
}

[TalentRule("network_test_public_charge", "Public Charge", "test", TalentTier.Small, 0,
    RevealPolicy = TalentRevealPolicy.PublicAtMatchStart)]
internal sealed class NetworkTestPublicChargeTalent : TalentRule, IPublicChargeTalent
{
    public static int ReductionCount { get; private set; }
    private static int CurrentCharge { get; set; }

    public static void Reset()
    {
        ReductionCount = 0;
        CurrentCharge = 1;
    }

    public static void SetCharge(int charge) => CurrentCharge = charge;

    public int GetCurrentCharge(TalentRuntimeState state) => CurrentCharge;

    public bool TryReduceCharge(TalentRuntimeState state, int amount)
    {
        if (amount <= 0) return false;
        ReductionCount++;
        CurrentCharge = Math.Max(0, CurrentCharge - amount);
        return true;
    }
}

[TalentRule("network_test_hidden_public_charge", "Hidden Public Charge", "test", TalentTier.Small, 0)]
internal sealed class NetworkTestHiddenPublicChargeTalent : TalentRule, IPublicChargeTalent
{
    public int GetCurrentCharge(TalentRuntimeState state) => 1;

    public bool TryReduceCharge(TalentRuntimeState state, int amount) => amount > 0;
}
