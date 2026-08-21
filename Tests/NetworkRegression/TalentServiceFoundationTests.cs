using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Talents;

internal static class TalentServiceFoundationTests
{
    public static void Run(RegressionRunner runner)
    {
        WinFactsOwnDeepImmutablePhysicalSnapshots(runner);
        RuntimeCarriesOneWinFactsInstanceThroughEvaluationAndAcceptance(runner);
        CommittedActionsRouteOnceAndBuildAnImmutableRoundLedger(runner);
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

    private static void BeginReadyRound(TalentMatchRuntime runtime, GameSession session)
    {
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
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
