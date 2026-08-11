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
        NegativeEffectRejectsUnknownTypesWithoutApplying(runner);
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
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "composure";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        int applyCount = 0;
        TalentNegativeEffect effect = BuildLayerReduction(1, 0, () => applyCount++);
        IReadOnlyList<TalentRuntimeEvent> beforeBlock = runtime.DrainEventsForSeat(1);
        TalentNegativeEffectResult blocked = runtime.ApplyNegativeEffect(effect);
        TalentNegativeEffectResult second = runtime.ApplyNegativeEffect(effect);
        IReadOnlyList<TalentRuntimeEvent> afterBlock = runtime.DrainEventsForSeat(1);

        runner.Check(beforeBlock.All(runtimeEvent => runtimeEvent.TalentId != "composure"),
            "composure remains unrevealed before it blocks a negative effect");
        runner.Check(blocked.WasBlocked && !blocked.WasApplied
                     && blocked.BlockingTalentId == "composure",
            "composure blocks the first negative talent effect each round");
        runner.Check(!second.WasBlocked && second.WasApplied && applyCount == 1,
            "the second negative effect in the same round is not blocked");
        runner.Check(afterBlock.Any(runtimeEvent => runtimeEvent.TalentId == "composure"
                                                   && runtimeEvent.EventType == "blocked_negative_effect"
                                                   && runtimeEvent.Visibility == TalentEventVisibility.Public
                                                   && runtimeEvent.Value == 1),
            "composure becomes public and records its consumed round state when it blocks");

        runtime.EndRound(new TalentRoundOutcome { IsAborted = true }, session);
        BeginReadyRound(runtime, session);
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

        bool wasApplied = false;
        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(new TalentNegativeEffect
        {
            SourceSeatIndex = 1,
            TargetSeatIndex = 0,
            EffectType = "ChangeConcealedHand",
            Apply = () => wasApplied = true
        });

        runner.Check(!result.WasBlocked && !result.WasApplied && !wasApplied,
            "negative effects reject unknown types before their callback can run");
    }

    private static void NegativeEffectChecksTargetDefensesByPriority(RegressionRunner runner)
    {
        PriorityDefenseTalent.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "composure";
        config.SlotTalentIds[1] = "network_test_priority_defense";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        int applyCount = 0;
        TalentNegativeEffectResult result = runtime.ApplyNegativeEffect(
            BuildLayerReduction(1, 0, () => applyCount++));

        runner.Check(PriorityDefenseTalent.BlockAttempts == 1
                     && result.WasBlocked
                     && result.BlockingTalentId == "composure"
                     && applyCount == 0,
            "negative effects check target defenses in priority order before the first block stops application");
    }

    private static TalentNegativeEffect BuildLayerReduction(
        int sourceSeatIndex,
        int targetSeatIndex,
        Action apply)
    {
        return new TalentNegativeEffect
        {
            SourceSeatIndex = sourceSeatIndex,
            SourceTalentId = "test_source",
            TargetSeatIndex = targetSeatIndex,
            TargetTalentId = "composure",
            EffectType = "ReducePublicChargeLayer",
            Apply = apply
        };
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
