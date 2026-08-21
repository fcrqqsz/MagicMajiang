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
