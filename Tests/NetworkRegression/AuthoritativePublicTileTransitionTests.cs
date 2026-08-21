using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Talents;

internal static class AuthoritativePublicTileTransitionTests
{
    public static void Run(RegressionRunner runner)
    {
        AcceptedConcealedKongPublishesAuthoritativePhysicalTiles(runner);
        WinningResultPublishesAuthoritativeConcealedHand(runner);
        SuccessfulAddedKongRevealsAtDeclarationAndCommitsOnce(runner);
        RobbedAddedKongRevealsAtDeclarationWithoutCommitting(runner);
    }

    private static void AcceptedConcealedKongPublishesAuthoritativePhysicalTiles(RegressionRunner runner)
    {
        var (session, runtime) = CreateReadyMidasRuntime();
        var state = new ServerGameState(4);
        List<TileData> authoritativeTiles = Enumerable.Range(0, 4)
            .Select(index => PhysicalTile($"angang-{index}", isModified: index == 2))
            .ToList();
        state.InitHand(0, authoritativeTiles);
        var transition = new AuthoritativePublicTileTransition(state, runtime, session);
        ClientAction publishedAction = null;

        bool committed = transition.TryCommitConcealedKong(
            0,
            ClientIntentTile(),
            chiCombinations: null,
            publish: action => publishedAction = action,
            out _);

        Meld committedMeld = state.GetMelds(0).Single();
        string[] committedIds = committedMeld.Tiles.Select(tile => tile.ID).ToArray();
        runner.Check(committed
                     && publishedAction?.TargetTile?.ID != "client-intent"
                     && publishedAction?.TargetTile?.OriginalOwnerID == 0
                     && committedMeld.SourcePlayerID == 0
                     && committedIds.OrderBy(id => id).SequenceEqual(
                         authoritativeTiles.Select(tile => tile.ID).OrderBy(id => id))
                     && EverySeatReceivedOneMidasReveal(runtime),
            "accepted AnGang commits and reveals authoritative physical hand tiles, never client provenance");
    }

    private static void WinningResultPublishesAuthoritativeConcealedHand(RegressionRunner runner)
    {
        var (session, runtime) = CreateReadyMidasRuntime();
        var state = new ServerGameState(4);
        state.InitHand(0, new List<TileData>
        {
            PhysicalTile("winning-modified", isModified: true),
            new TileData(Suit.Pin, 3, 0) { ID = "winning-ordinary" }
        });
        var transition = new AuthoritativePublicTileTransition(state, runtime, session);
        bool resultPublished = false;

        transition.PublishWinningResult(0, () => resultPublished = true);

        runner.Check(resultPublished && EverySeatReceivedOneMidasReveal(runtime),
            "accepted win reveals modified tiles from the authoritative winning concealed hand");
    }

    private static void SuccessfulAddedKongRevealsAtDeclarationAndCommitsOnce(RegressionRunner runner)
    {
        var (session, runtime) = CreateReadyMidasRuntime();
        ServerGameState state = CreateAddedKongState("jiagang-success");
        var transition = new AuthoritativePublicTileTransition(state, runtime, session);

        bool prepared = transition.TryPrepareAddedKong(0, ClientIntentTile(), out TileData authoritativeTile);
        TileData declaredTile = null;
        transition.PublishAddedKongDeclaration(0, authoritativeTile, tile => declaredTile = tile);
        bool committed = transition.TryCommitAddedKong(
            0,
            authoritativeTile,
            chiCombinations: null,
            publish: _ => { },
            out _);
        bool firstReveal = EverySeatReceivedOneMidasReveal(runtime);
        bool noDuplicateReveal = Enumerable.Range(0, 4)
            .All(seatIndex => runtime.DrainEventsForSeat(seatIndex).Count == 0);

        Meld meld = state.GetMelds(0).Single();
        runner.Check(prepared
                     && declaredTile?.ID == "jiagang-success"
                     && declaredTile.IsModified
                     && committed
                     && meld.Type == MeldType.Kan_Added
                     && meld.Tiles.Count(tile => tile.ID == "jiagang-success") == 1
                     && firstReveal
                     && noDuplicateReveal,
            "successful JiaGang reveals the authoritative tile at declaration and commits it without duplicate reveal");
    }

    private static void RobbedAddedKongRevealsAtDeclarationWithoutCommitting(RegressionRunner runner)
    {
        var (session, runtime) = CreateReadyMidasRuntime();
        ServerGameState state = CreateAddedKongState("jiagang-robbed");
        var transition = new AuthoritativePublicTileTransition(state, runtime, session);

        bool prepared = transition.TryPrepareAddedKong(0, ClientIntentTile(), out TileData authoritativeTile);
        TileData declaredTile = null;
        transition.PublishAddedKongDeclaration(0, authoritativeTile, tile => declaredTile = tile);

        Meld meld = state.GetMelds(0).Single();
        runner.Check(prepared
                     && declaredTile?.ID == "jiagang-robbed"
                     && declaredTile.IsModified
                     && EverySeatReceivedOneMidasReveal(runtime)
                     && state.GetHand(0).Any(tile => tile.ID == "jiagang-robbed")
                     && meld.Type == MeldType.Pon
                     && meld.Tiles.Count == 3,
            "robbed JiaGang still reveals its authoritative public declaration without committing the meld");
    }

    private static ServerGameState CreateAddedKongState(string authoritativeId)
    {
        var state = new ServerGameState(4);
        state.InitHand(0, new List<TileData>
        {
            PhysicalTile(authoritativeId, isModified: true),
            PhysicalTile($"{authoritativeId}-pon-a", isModified: false),
            PhysicalTile($"{authoritativeId}-pon-b", isModified: false)
        });
        state.ApplyMeld(0, ClientActionType.Pon, ClientIntentTile(), null);
        return state;
    }

    private static (GameSession Session, TalentMatchRuntime Runtime) CreateReadyMidasRuntime()
    {
        var config = new TalentSlotConfig();
        config.SlotTalentIds[0] = "midas_touch";
        var runtime = new TalentMatchRuntime(
            new Dictionary<int, TalentSlotConfig> { [0] = config },
            TalentRegistry.Instance);
        var session = new GameSession(GameMode.Single);
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.CompleteInitialHands(new TalentInitialHandsContext(session, new ServerGameState(4)));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
        return (session, runtime);
    }

    private static TileData PhysicalTile(string id, bool isModified) => new TileData(Suit.Man, 5, 0)
    {
        ID = id,
        IsModified = isModified,
        SpecialEffectID = isModified ? "midas_touch" : null
    };

    private static TileData ClientIntentTile() => new TileData(Suit.Man, 5, 3)
    {
        ID = "client-intent",
        IsModified = false,
        SpecialEffectID = null
    };

    private static bool EverySeatReceivedOneMidasReveal(TalentMatchRuntime runtime) =>
        Enumerable.Range(0, 4).All(seatIndex =>
        {
            IReadOnlyList<TalentRuntimeEvent> events = runtime.DrainEventsForSeat(seatIndex);
            return events.Count == 1
                   && events[0].TalentId == "midas_touch"
                   && events[0].EventType == "talent_revealed";
        });
}
