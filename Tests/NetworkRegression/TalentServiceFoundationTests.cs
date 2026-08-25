using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

internal static class TalentServiceFoundationTests
{
    public static void Run(RegressionRunner runner)
    {
        WinFactsOwnDeepImmutablePhysicalSnapshots(runner);
        RuntimeCarriesOneWinFactsInstanceThroughEvaluationAndAcceptance(runner);
        CommittedActionsRouteOnceAndBuildAnImmutableRoundLedger(runner);
        GenericChoicesRejectForgedIdsBeforeRuleMutation(runner);
        GenericChoicesRoundTripThroughPrivateProtocolAndAiDefault(runner);
        UnityDefaultChoiceObjectPreservesDirectTalentAction(runner);
        InitialHandHookOwnsImmutablePrivateFactsAndStrictLifecycle(runner);
        InitialHandMutationsAreStagedAndAtomicallyCommitted(runner);
    }

    private static void InitialHandMutationsAreStagedAndAtomicallyCommitted(
        RegressionRunner runner)
    {
        InitialHandMutationObserverTalent.Reset();
        var stagedConfig = new TalentSlotConfig();
        stagedConfig.SlotTalentIds[3] = "network_test_initial_hand_transformer";
        stagedConfig.SlotTalentIds[4] = "network_test_initial_hand_mutation_observer";
        var stagedRuntime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = stagedConfig },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        var stagedState = new ServerGameState(4);
        stagedState.InitHand(0, new List<TileData>
        {
            Tile(Suit.Man, 1, ownerId: 0, id: "staged-edge"),
            Tile(Suit.Pin, 5, ownerId: 0, id: "staged-inner")
        });
        for (int seatIndex = 1; seatIndex < 4; seatIndex++)
            stagedState.InitHand(seatIndex, new List<TileData>());

        stagedRuntime.BeginMatch(session);
        stagedRuntime.BeginRound(new TalentRoundContext(session));
        stagedRuntime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        stagedRuntime.CompleteInitialHands(new TalentInitialHandsContext(session, stagedState));

        TileData transformed = stagedState.GetHand(0).Single(tile => tile.ID == "staged-edge");
        runner.Check(transformed.TileSuit == Suit.Man
                     && transformed.Value == 2
                     && transformed.OriginalOwnerID == 0
                     && transformed.IsModified
                     && transformed.SpecialEffectID == "network_test_initial_hand_transformer",
            "initial-hand mutation preserves physical identity and ownership while marking its source talent");
        runner.Check(InitialHandMutationObserverTalent.ObservedValue == 2,
            "later initial-hand rules observe the staged result of earlier rules");

        var atomicConfig0 = new TalentSlotConfig();
        atomicConfig0.SlotTalentIds[3] = "network_test_initial_hand_atomic_failure";
        var atomicConfig1 = new TalentSlotConfig();
        atomicConfig1.SlotTalentIds[3] = "network_test_initial_hand_atomic_failure";
        var atomicRuntime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = atomicConfig0, [1] = atomicConfig1 },
            TalentRegistry.Instance);
        var atomicSession = new GameSession(GameMode.Single);
        var atomicState = new ServerGameState(4);
        atomicState.InitHand(0, new List<TileData>
        {
            Tile(Suit.Man, 1, ownerId: 0, id: "atomic-seat-0")
        });
        atomicState.InitHand(1, new List<TileData>
        {
            Tile(Suit.Pin, 9, ownerId: 1, id: "atomic-seat-1")
        });
        atomicState.InitHand(2, new List<TileData>());
        atomicState.InitHand(3, new List<TileData>());
        atomicRuntime.BeginMatch(atomicSession);
        atomicRuntime.BeginRound(new TalentRoundContext(atomicSession));
        atomicRuntime.ApplyWallBuilding(new TalentWallContext(atomicSession, new List<TileData>()));

        bool rejected = ThrowsInvalidOperation(() => atomicRuntime.CompleteInitialHands(
            new TalentInitialHandsContext(atomicSession, atomicState)));
        runner.Check(rejected
                     && atomicState.GetHand(0).Single().Value == 1
                     && !atomicState.GetHand(0).Single().IsModified
                     && atomicState.GetHand(1).Single().Value == 9
                     && !atomicState.GetHand(1).Single().IsModified,
            "one invalid owner-local mutation aborts the complete initial-hand transaction without partial authority writes");
    }

    private static void InitialHandHookOwnsImmutablePrivateFactsAndStrictLifecycle(
        RegressionRunner runner)
    {
        InitialHandObserverTalent.Reset();
        var seat0 = new TalentSlotConfig();
        seat0.SlotTalentIds[3] = "network_test_initial_hand_observer";
        var seat1 = new TalentSlotConfig();
        seat1.SlotTalentIds[3] = "network_test_initial_hand_observer";
        var seat2 = new TalentSlotConfig();
        seat2.ReserveTalentIds[0] = "network_test_initial_hand_observer";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = seat0,
                [1] = seat1,
                [2] = seat2
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        var gameState = new ServerGameState(4);
        for (int seatIndex = 0; seatIndex < 4; seatIndex++)
        {
            gameState.InitHand(seatIndex, new List<TileData>
            {
                new TileData(Suit.Man, seatIndex + 1, seatIndex),
                new TileData(Suit.Pin, seatIndex + 2, seatIndex)
            });
        }

        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(
            session,
            new List<TileData>(),
            gameState,
            new Dictionary<int, DeckConfig>()));
        bool postShuffleBeforeHandsRejected = ThrowsInvalidOperation(() =>
            runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>())));

        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, gameState));
        gameState.RemoveTile(0, gameState.GetHand(0)[0]);
        bool duplicateRejected = ThrowsInvalidOperation(() =>
            runtime.CompleteInitialHands(new TalentInitialHandsContext(session, gameState)));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));

        runner.Check(postShuffleBeforeHandsRejected && duplicateRejected,
            "initial-hand completion is a strict one-shot lifecycle boundary before post-shuffle readiness");
        runner.Check(InitialHandObserverTalent.ByOwner.Count == 2
                     && InitialHandObserverTalent.ByOwner[0].OwnerSeatIndex == 0
                     && InitialHandObserverTalent.ByOwner[0].RoundNumber == 1
                     && InitialHandObserverTalent.ByOwner[0].Tiles.Count == 2
                     && InitialHandObserverTalent.ByOwner[0].Tiles.All(tile => tile.OriginalOwnerId == 0)
                     && InitialHandObserverTalent.ByOwner[0].Tiles[0].Value == 1
                     && InitialHandObserverTalent.ByOwner[1].Tiles.All(tile => tile.OriginalOwnerId == 1)
                     && !InitialHandObserverTalent.ByOwner.ContainsKey(2),
            "each active rule receives only its owner's immutable physical starting hand; inactive reserves receive nothing");
    }

    private static void GenericChoicesRoundTripThroughPrivateProtocolAndAiDefault(
        RegressionRunner runner)
    {
        var serverOption = new TalentActionOption
        {
            TalentId = "network_test_choice_contract",
            Choice = new TalentChoiceSet(
                TalentChoiceKind.Mode,
                "choose_contract",
                "safe",
                new[]
                {
                    new TalentChoiceOption("safe", "contract_safe", 1),
                    new TalentChoiceOption("risk", "contract_risk", 2)
                })
        };
        SnapshotTalentActionOption snapshot = TalentActionSnapshotCodec.ToSnapshot(serverOption);
        TalentActionOption restored = TalentActionSnapshotCodec.FromSnapshot(snapshot);
        TalentActionOption selected = TalentActionPanelPolicy.SelectChoice(restored, "risk");
        TalentActionOption aiChoice = MahjongGame.Core.Agents.AiTalentDecisionPolicy
            .ChooseActiveAction(new[] { serverOption });
        var physicalTile = new TileData(Suit.Dragon, 2, 3)
        {
            ID = "choice-physical-1",
            IsModified = true,
            SpecialEffectID = "future_mutation"
        };
        var tileOption = new TalentActionOption
        {
            TalentId = "network_test_choice_contract",
            Choice = new TalentChoiceSet(
                TalentChoiceKind.Tile,
                "choose_tile",
                "physical-1",
                new[]
                {
                    new TalentChoiceOption(
                        "physical-1",
                        "choose_tile_physical_1",
                        tile: TalentTileFacts.FromTile(physicalTile))
                })
        };
        TalentActionOption restoredTileOption = TalentActionSnapshotCodec.FromSnapshot(
            TalentActionSnapshotCodec.ToSnapshot(tileOption));
        NetworkMessageEnvelope envelope = MessageSerializer.DeserializeEnvelope(
            MessageSerializer.Serialize(
                "TalentAction",
                9,
                new TalentActionMessage
                {
                    decisionId = 77,
                    talentId = selected.TalentId,
                    selectedChoiceId = selected.SelectedChoiceId
                }));
        TalentActionMessage roundTrip = MessageSerializer.DeserializePayload<TalentActionMessage>(envelope.data);

        snapshot.choice.options[0].choiceId = "mutated";
        runner.Check(restored.Choice.Options[0].ChoiceId == "safe"
                     && restored.Choice.DefaultChoiceId == "safe",
            "owner-private snapshot conversion deep-copies generic choice options");
        runner.Check(selected.SelectedChoiceId == "risk"
                     && serverOption.SelectedChoiceId == null
                     && roundTrip.selectedChoiceId == "risk",
            "client selection returns a copied option and the protocol carries only the selected choice id");
        runner.Check(aiChoice.SelectedChoiceId == "safe",
            "AI uses the server-authored default when no talent-specific choice strategy exists");
        runner.Check(restoredTileOption.Choice.Options[0].Tile.Id == "choice-physical-1"
                     && restoredTileOption.Choice.Options[0].Tile.OriginalOwnerId == 3
                     && restoredTileOption.Choice.Options[0].Tile.IsModified
                     && restoredTileOption.Choice.Options[0].Tile.SpecialEffectId == "future_mutation",
            "tile choices preserve complete immutable physical-tile identity across recovery");
    }

    private static void UnityDefaultChoiceObjectPreservesDirectTalentAction(
        RegressionRunner runner)
    {
        TalentActionOption restored = TalentActionSnapshotCodec.FromSnapshot(
            new SnapshotTalentActionOption
            {
                talentId = "piercing_insight",
                targetSeatIndex = 2,
                choice = new SnapshotTalentChoiceSet
                {
                    kind = 0,
                    promptKey = string.Empty,
                    defaultChoiceId = string.Empty,
                    options = Array.Empty<SnapshotTalentChoiceOption>()
                }
            });

        runner.Check(restored != null
                     && restored.TalentId == "piercing_insight"
                     && restored.TargetSeatIndex == 2
                     && restored.Choice == null,
            "Unity's default empty choice object preserves a direct target talent action instead of dropping its button");
    }

    private static void GenericChoicesRejectForgedIdsBeforeRuleMutation(
        RegressionRunner runner)
    {
        ChoiceContractTestTalent.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "network_test_choice_contract";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);
        runtime.OpenMainDecision(0, decisionId: 77);

        TalentActionQueryContext query = new TalentActionQueryContext(
            session, 0, TalentActivationWindow.MainTurn, decisionId: 77);
        TalentActionOption option = runtime.GetAvailableActions(0, query).Single();
        TalentActivationContext activation = new TalentActivationContext(
            session, 0, TalentActivationWindow.MainTurn, decisionId: 77);
        TalentActionResult missing = runtime.TryActivate(
            0,
            new TalentActionRequest
            {
                DecisionId = 77,
                TalentId = "network_test_choice_contract"
            },
            activation);
        TalentActionResult forged = runtime.TryActivate(
            0,
            new TalentActionRequest
            {
                DecisionId = 77,
                TalentId = "network_test_choice_contract",
                ChoiceId = "forged"
            },
            activation);

        runner.Check(option.Choice.Kind == TalentChoiceKind.Mode
                     && option.Choice.PromptKey == "choose_contract"
                     && option.Choice.DefaultChoiceId == "safe"
                     && option.Choice.Options.Select(choice => choice.ChoiceId)
                         .SequenceEqual(new[] { "safe", "risk" }),
            "rules publish a bounded server-authored generic choice set");
        runner.Check(!missing.Accepted
                     && !forged.Accepted
                     && missing.ErrorCode == TalentActionErrorCodes.InvalidChoice
                     && forged.ErrorCode == TalentActionErrorCodes.InvalidChoice
                     && ChoiceContractTestTalent.AuthoritativeCalls == 0,
            "missing and forged choice ids are rejected before the rule can mutate authoritative state");

        TalentActionResult accepted = runtime.TryActivate(
            0,
            new TalentActionRequest
            {
                DecisionId = 77,
                TalentId = "network_test_choice_contract",
                ChoiceId = "risk"
            },
            activation);
        runner.Check(accepted.Accepted
                     && accepted.EffectApplied
                     && ChoiceContractTestTalent.AuthoritativeCalls == 1
                     && ChoiceContractTestTalent.AcceptedChoiceId == "risk",
            "an advertised choice id reaches the rule exactly once through the authoritative activation path");
    }

    private static void CommittedActionsRouteOnceAndBuildAnImmutableRoundLedger(
        RegressionRunner runner)
    {
        ActionFactsGlobalObserverTalent.Reset();
        ActionFactsSelfObserverTalent.Reset();
        var globalConfig = new TalentSlotConfig();
        globalConfig.SlotTalentIds[3] = "network_test_action_global_observer";
        var actorConfig = new TalentSlotConfig();
        actorConfig.SlotTalentIds[3] = "network_test_action_self_observer";
        var otherConfig = new TalentSlotConfig();
        otherConfig.SlotTalentIds[3] = "network_test_action_self_observer";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig>
            {
                [0] = globalConfig,
                [1] = actorConfig,
                [2] = otherConfig
            },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.EastOnly);
        runtime.BeginMatch(session);
        BeginReadyRound(runtime, session);

        TileData target = Tile(Suit.Pin, 7, ownerId: 0, id: "committed-target", modifiedBy: "midas_touch");
        int[] chi = { 6, 8 };
        TalentActionCommittedFacts facts = TalentActionCommittedFacts.Create(
            decisionId: 41,
            actorSeatIndex: 1,
            sourceSeatIndex: 0,
            ClientActionType.Chi,
            target,
            chi,
            wasAutomatic: false,
            winFacts: null);

        bool firstCommit = runtime.CommitAction(facts);
        bool duplicateCommit = runtime.CommitAction(facts);
        target.Value = 9;
        target.ID = "mutated-target";
        chi[0] = 1;

        runner.Check(firstCommit && !duplicateCommit,
            "runtime accepts one authoritative committed action per decision and ignores duplicate delivery");
        runner.Check(ActionFactsGlobalObserverTalent.Calls == 1
                     && ActionFactsSelfObserverTalent.OwnerSeats.SequenceEqual(new[] { 1 }),
            "committed actions route to global rules and only the actor's self-scoped rules");
        runner.Check(ActionFactsGlobalObserverTalent.LedgerAtHook.GetCount(1, ClientActionType.Chi) == 1
                     && ActionFactsGlobalObserverTalent.LedgerAtHook.Actions.Count == 1,
            "round action ledger records the action before polymorphic committed-action hooks run");
        runner.Check(facts.TargetTile.Value == 7
                     && facts.TargetTile.Id == "committed-target"
                     && facts.TargetTile.IsModified
                     && facts.ChiCombinations.SequenceEqual(new[] { 6, 8 }),
            "committed-action facts own immutable tile and chi-combination snapshots");

        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 3 }, session);
        runner.Check(ActionFactsSelfObserverTalent.RoundEndLedger.GetCount(1, ClientActionType.Chi) == 1,
            "round-end hooks receive the final immutable action ledger");

        BeginReadyRound(runtime, session);
        runtime.EndRound(new TalentRoundOutcome(), session);
        runner.Check(ActionFactsSelfObserverTalent.RoundEndLedger.Actions.Count == 0,
            "a new small round starts with an empty action ledger");
    }

    private static void RuntimeCarriesOneWinFactsInstanceThroughEvaluationAndAcceptance(
        RegressionRunner runner)
    {
        WinFactsObserverTalent.Reset();
        var config = new TalentSlotConfig();
        config.SlotTalentIds[3] = "network_test_win_facts_observer";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, new ServerGameState(4)));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
        TalentWinFacts facts = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 1,
            new[] { Tile(Suit.Man, 2, 0, "context-hand") },
            new List<Meld>(),
            Tile(Suit.Man, 2, 1, "context-win"),
            isSelfDraw: false,
            isRobKong: false,
            isKongReplacement: false);

        runtime.ResolvePostLegalFan(new TalentWinContext(session, 0, facts), eligibilityFan: 8);
        runtime.ResolveAcceptedWinFan(new TalentAcceptedWinAttributionContext(
            session,
            winnerSeatIndex: 0,
            alreadyAcceptedFinalFan: 8,
            facts,
            _ => new FanEvaluation
            {
                HasWinningShape = true,
                Fan = 8,
                FanDetails = new List<string>()
            }));
        runtime.ConfirmAcceptedWin(new TalentWinContext(session, 0, facts));

        runner.Check(ReferenceEquals(WinFactsObserverTalent.PostLegalFacts, facts)
                     && ReferenceEquals(WinFactsObserverTalent.AttributionFacts, facts)
                     && ReferenceEquals(WinFactsObserverTalent.AcceptedFacts, facts),
            "runtime carries one immutable win-facts instance through candidate, attribution, and acceptance hooks");
    }

    private static void WinFactsOwnDeepImmutablePhysicalSnapshots(RegressionRunner runner)
    {
        var session = new GameSession(GameMode.EastOnly);
        TileData first = Tile(Suit.Man, 1, ownerId: 3, id: "hand-1", modifiedBy: "midas_touch");
        TileData pair = Tile(Suit.Dragon, 2, ownerId: 0, id: "hand-2");
        TileData meldTile = Tile(Suit.Pin, 4, ownerId: 2, id: "meld-1");
        TileData winningTile = Tile(Suit.Sou, 5, ownerId: 2, id: "win-1", modifiedBy: "future_effect");
        var hand = new List<TileData> { first, pair };
        var melds = new List<Meld>
        {
            new Meld(MeldType.Pon, new List<TileData>
            {
                meldTile,
                Tile(Suit.Pin, 4, 0, "meld-2"),
                Tile(Suit.Pin, 4, 1, "meld-3")
            }, sourceId: 2)
        };

        TalentWinFacts facts = TalentWinFacts.Create(
            session,
            winnerSeatIndex: 0,
            discarderSeatIndex: 2,
            hand,
            melds,
            winningTile,
            isSelfDraw: false,
            isRobKong: true,
            isKongReplacement: false);

        first.Value = 9;
        first.ID = "mutated-hand";
        meldTile.Value = 8;
        melds[0].Tiles.Clear();
        winningTile.Value = 7;
        winningTile.SpecialEffectID = "mutated-effect";
        hand.Clear();

        runner.Check(facts.WinnerSeatIndex == 0
                     && facts.DiscarderSeatIndex == 2
                     && facts.RoundNumber == 1
                     && facts.RoundWind == WindDirection.East
                     && facts.SeatWind == WindDirection.East
                     && !facts.IsSelfDraw
                     && facts.IsRobKong
                     && !facts.IsKongReplacement,
            "win facts preserve the authoritative accepted-win source and round context");
        runner.Check(facts.ConcealedHandTiles.Count == 2
                     && facts.ConcealedHandTiles[0].Value == 1
                     && facts.ConcealedHandTiles[0].Id == "hand-1"
                     && facts.ConcealedHandTiles[0].OriginalOwnerId == 3
                     && facts.ConcealedHandTiles[0].IsModified
                     && facts.ConcealedHandTiles[0].SpecialEffectId == "midas_touch",
            "win facts own immutable concealed physical-tile snapshots");
        runner.Check(facts.Melds.Count == 1
                     && facts.Melds[0].Type == MeldType.Pon
                     && facts.Melds[0].SourceSeatIndex == 2
                     && facts.Melds[0].Tiles.Count == 3
                     && facts.Melds[0].Tiles[0].Value == 4,
            "win facts own immutable meld and meld-tile snapshots");
        runner.Check(facts.WinningTile.Value == 5
                     && facts.WinningTile.Id == "win-1"
                     && facts.WinningTile.SpecialEffectId == "future_effect",
            "win facts own an immutable winning physical tile snapshot");
    }

    private static TileData Tile(
        Suit suit,
        int value,
        int ownerId,
        string id,
        string modifiedBy = null) => new TileData(suit, value, ownerId)
    {
        ID = id,
        IsModified = !string.IsNullOrEmpty(modifiedBy),
        SpecialEffectID = modifiedBy
    };

    private static bool ThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
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

internal static class TalentTestFacts
{
    public static TalentWinFacts Win(GameSession session, int winnerSeatIndex)
    {
        return TalentWinFacts.Create(
            session,
            winnerSeatIndex,
            discarderSeatIndex: null,
            new[] { new TileData(Suit.Man, 1, winnerSeatIndex) },
            new List<Meld>(),
            new TileData(Suit.Man, 1, winnerSeatIndex),
            isSelfDraw: true,
            isRobKong: false,
            isKongReplacement: false);
    }
}

[TalentRule("network_test_win_facts_observer", "Win Facts Observer", "test", TalentTier.Small, 0)]
internal sealed class WinFactsObserverTalent : TalentRule
{
    public static TalentWinFacts PostLegalFacts { get; private set; }
    public static TalentWinFacts AttributionFacts { get; private set; }
    public static TalentWinFacts AcceptedFacts { get; private set; }
    private static int _postLegalCalls;

    public override int GetPostLegalFanBonus(TalentWinContext context)
    {
        _postLegalCalls++;
        if (_postLegalCalls == 1)
            PostLegalFacts = context.Facts;
        else
            AttributionFacts = context.Facts;
        return 0;
    }

    public override void OnAcceptedWin(TalentWinContext context) => AcceptedFacts = context.Facts;

    public static void Reset()
    {
        PostLegalFacts = null;
        AttributionFacts = null;
        AcceptedFacts = null;
        _postLegalCalls = 0;
    }
}

[TalentRule("network_test_action_global_observer", "Action Global Observer", "test",
    TalentTier.Small, 0)]
internal sealed class ActionFactsGlobalObserverTalent : TalentRule
{
    public override TalentScope Scope => TalentScope.Global;
    public static int Calls { get; private set; }
    public static TalentRoundActionLedgerSnapshot LedgerAtHook { get; private set; }
    public static List<TalentActionCommittedFacts> Facts { get; } =
        new List<TalentActionCommittedFacts>();

    public override void OnActionCommitted(TalentActionCommittedContext context)
    {
        Calls++;
        Facts.Add(context.Facts);
        LedgerAtHook = context.RoundActions;
    }

    public static void Reset()
    {
        Calls = 0;
        Facts.Clear();
        LedgerAtHook = null;
    }
}

[TalentRule("network_test_action_self_observer", "Action Self Observer", "test",
    TalentTier.Small, 0)]
internal sealed class ActionFactsSelfObserverTalent : TalentRule
{
    public static List<int> OwnerSeats { get; } = new List<int>();
    public static TalentRoundActionLedgerSnapshot RoundEndLedger { get; private set; }

    public override void OnActionCommitted(TalentActionCommittedContext context) =>
        OwnerSeats.Add(context.OwnerSeatIndex);

    public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome) =>
        RoundEndLedger = context.RoundActions;

    public static void Reset()
    {
        OwnerSeats.Clear();
        RoundEndLedger = null;
    }
}

[TalentRule("network_test_choice_contract", "Choice Contract", "test",
    TalentTier.Small, 0,
    ActivationWindow = TalentActivationWindow.MainTurn)]
internal sealed class ChoiceContractTestTalent : TalentRule
{
    public static int AuthoritativeCalls { get; private set; }
    public static string AcceptedChoiceId { get; private set; }

    public override void GetAvailableActions(
        TalentActionQueryContext context,
        List<TalentActionOption> output)
    {
        output.Add(new TalentActionOption
        {
            TalentId = Id,
            Choice = new TalentChoiceSet(
                TalentChoiceKind.Mode,
                promptKey: "choose_contract",
                defaultChoiceId: "safe",
                new[]
                {
                    new TalentChoiceOption("safe", "contract_safe", value: 1),
                    new TalentChoiceOption("risk", "contract_risk", value: 2)
                })
        });
    }

    public override TalentActionResult TryActivate(
        TalentActivationContext context,
        TalentActionRequest request)
    {
        AuthoritativeCalls++;
        AcceptedChoiceId = request.ChoiceId;
        return TalentActionResult.Success(effectApplied: true);
    }

    public static void Reset()
    {
        AuthoritativeCalls = 0;
        AcceptedChoiceId = null;
    }
}

[TalentRule("network_test_initial_hand_observer", "Initial Hand Observer", "test",
    TalentTier.Small, 0, TalentPhase.InitialHandCompleted)]
internal sealed class InitialHandObserverTalent : TalentRule
{
    public static Dictionary<int, TalentInitialHandFacts> ByOwner { get; } =
        new Dictionary<int, TalentInitialHandFacts>();

    public override TalentScope Scope => TalentScope.Global;

    public override void OnInitialHandCompleted(TalentInitialHandContext context)
    {
        ByOwner[OwnerSeatIndex] = context.Facts;
    }

    public static void Reset() => ByOwner.Clear();
}

[TalentRule("network_test_initial_hand_transformer", "Initial Hand Transformer", "test",
    TalentTier.Small, 0, TalentPhase.InitialHandCompleted)]
internal sealed class InitialHandTransformerTalent : TalentRule
{
    public override TalentScope Scope => TalentScope.Global;
    public override int Priority => 10;

    public override void OnInitialHandCompleted(TalentInitialHandContext context) =>
        context.TryTransformTile("staged-edge", Suit.Man, 2);
}

[TalentRule("network_test_initial_hand_mutation_observer", "Initial Hand Mutation Observer", "test",
    TalentTier.Small, 0, TalentPhase.InitialHandCompleted)]
internal sealed class InitialHandMutationObserverTalent : TalentRule
{
    public override TalentScope Scope => TalentScope.Global;
    public static int ObservedValue { get; private set; }

    public override void OnInitialHandCompleted(TalentInitialHandContext context) =>
        ObservedValue = context.Facts.Tiles.Single(tile => tile.Id == "staged-edge").Value;

    public static void Reset() => ObservedValue = 0;
}

[TalentRule("network_test_initial_hand_atomic_failure", "Initial Hand Atomic Failure", "test",
    TalentTier.Small, 0, TalentPhase.InitialHandCompleted)]
internal sealed class InitialHandAtomicFailureTalent : TalentRule
{
    public override TalentScope Scope => TalentScope.Global;

    public override void OnInitialHandCompleted(TalentInitialHandContext context)
    {
        if (OwnerSeatIndex == 0)
            context.TryTransformTile("atomic-seat-0", Suit.Man, 2);
        else
            context.TryTransformTile("missing-physical-id", Suit.Pin, 8);
    }
}
