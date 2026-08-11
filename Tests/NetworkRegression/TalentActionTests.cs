using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Talents;

internal static class TalentActionTests
{
    public static void Run(RegressionRunner runner)
    {
        SupplementalActionValidationDoesNotConsumeMainDecision(runner);
        SupplementalActionValidationRejectsInvalidDecisionContexts(runner);
        SupplementalTalentAdmissionRejectsResponseWindowsBeforeRuntime(runner);
        CarriedTalentActionExecutesPolymorphically(runner);
        ComposureBlocksOnlyTheFirstNegativeEffectPerRound(runner);
        NegativeEffectChecksTargetDefensesByPriority(runner);
        NegativeEffectDescriptionDoesNotExposeAnApplyDelegate(runner);
        NonTargetAndReserveDefensesDoNotBlockPublicChargeReduction(runner);
        NegativeEffectRejectsMissingPublicChargeTarget(runner);
        NegativeEffectRejectsUnknownTypesWithoutApplying(runner);
        NegativeEffectRejectsIneligiblePublicChargeBeforeDefenses(runner);
        SheathedEdgeChargesCapsAndExposesPublicTargets(runner);
        SheathedEdgeDoesNotChargeOnOwnerWinOrAbortedRound(runner);
        SheathedEdgeArmsOnlyOnTheFirstMainDecision(runner);
        SheathedEdgeReadOnlyResolutionConsumesOnlyAfterAcceptedWin(runner);
        SheathedEdgeUnusedArmExpiresWithoutRefund(runner);
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

        TalentNegativeEffect effect = BuildLayerReduction(1, 0);
        IReadOnlyList<TalentRuntimeEvent> beforeBlock = runtime.DrainEventsForSeat(1);
        TalentNegativeEffectResult blocked = runtime.ApplyNegativeEffect(effect);
        int reductionsAfterFirstEffect = NetworkTestPublicChargeTalent.ReductionCount;
        TalentNegativeEffectResult second = runtime.ApplyNegativeEffect(effect);
        IReadOnlyList<TalentRuntimeEvent> afterBlock = runtime.DrainEventsForSeat(1);

        runner.Check(beforeBlock.All(runtimeEvent => runtimeEvent.TalentId != "composure"),
            "composure remains unrevealed before it blocks a negative effect");
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
                                                   && runtimeEvent.Value == 1),
            "composure becomes public and records its consumed round state when it blocks");

        runtime.EndRound(new TalentRoundOutcome { IsAborted = true }, session);
        BeginReadyRound(runtime, session);
        NetworkTestPublicChargeTalent.SetCharge(1);
        TalentNegativeEffectResult refreshed = runtime.ApplyNegativeEffect(effect);

        runner.Check(refreshed.WasBlocked && !refreshed.WasApplied,
            "composure refreshes at the next round boundary");
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

    private static void SheathedEdgeArmsOnlyOnTheFirstMainDecision(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateChargedSheathedEdgeRuntime(out GameSession session);
        runtime.OpenMainDecision(ownerSeatIndex: 0, decisionId: 91);

        var firstContext = new TalentActionQueryContext(
            session, 0, TalentActivationWindow.MainTurn, decisionId: 91);
        int availableOnFirstDecision = runtime.GetAvailableActions(0, firstContext).Count;
        TalentActionResult armed = runtime.TryActivate(
            0,
            new TalentActionRequest { TalentId = "sheathed_edge", DecisionId = 91 },
            new TalentActivationContext(
                session, 0, TalentActivationWindow.MainTurn, decisionId: 91));

        runner.Check(availableOnFirstDecision == 1,
            "three layers can arm on the first main decision of the round");
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
            new TalentWinContext(session, 0), eligibilityFan: 8);
        TalentFanResolution second = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0), eligibilityFan: 8);
        bool candidatesStayedQuiet = runtime.DrainEventsForSeat(0)
            .All(runtimeEvent => runtimeEvent.EventType != "armed_consumed");

        runtime.ConfirmAcceptedWin(new TalentWinContext(session, 0));
        runtime.ConfirmAcceptedWin(new TalentWinContext(session, 0));
        int consumedEvents = runtime.DrainEventsForSeat(0)
            .Count(runtimeEvent => runtimeEvent.EventType == "armed_consumed");

        runner.Check(first.EligibilityFan == 8
                     && first.PostLegalBonusFan == 16
                     && first.NegativeFan == 0
                     && first.FinalFan == 24
                     && second.FinalFan == 24,
            "post-legal fan resolution is read-only and repeatable for candidate and final scoring");
        runner.Check(candidatesStayedQuiet && consumedEvents == 1,
            "sheathed edge emits its consumed event only after an accepted win is confirmed");
    }

    private static void SheathedEdgeUnusedArmExpiresWithoutRefund(RegressionRunner runner)
    {
        TalentMatchRuntime runtime = CreateArmedSheathedEdgeRuntime(out GameSession session);
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);
        BeginReadyRound(runtime, session);

        TalentFanResolution nextRound = runtime.ResolvePostLegalFan(
            new TalentWinContext(session, 0), eligibilityFan: 8);

        runner.Check(nextRound.PostLegalBonusFan == 0 && nextRound.FinalFan == 8,
            "an unused sheathed edge arm expires at the next round boundary");
        runner.Check(runtime.GetPublicCounter(0, "sheathed_edge", "edge") == 1,
            "an expired arm earns only the normal non-winning layer and does not refund three spent layers");
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

    private static TalentMatchRuntime CreateChargedSheathedEdgeRuntime(out GameSession session)
    {
        TalentMatchRuntime runtime = CreateSheathedEdgeRuntime(out session);
        for (int index = 0; index < 3; index++)
            EndNonWinningRound(runtime, session, winnerSeatIndex: index + 1);
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

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
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
        return TalentActionResult.Success();
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
