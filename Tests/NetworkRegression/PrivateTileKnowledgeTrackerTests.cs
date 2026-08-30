using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

internal static class PrivateTileKnowledgeTrackerTests
{
    public static void Run(RegressionRunner runner)
    {
        ObservedWallTileMovesIntoOpponentKnowledge(runner);
        OwnDrawConsumesWallObservationWithoutKnownHandEntry(runner);
        HiddenDrawMutationInvalidatesObservation(runner);
        MutatedPublicDepartureDoesNotConsumeAnotherKnownFace(runner);
        DuplicateKnownFacesAreConsumedOneAtATime(runner);
        PublicTilesConsumeMatchingKnowledgeConservatively(runner);
        ViewersRemainIsolatedAndRoundClearRemovesKnowledge(runner);
        SnapshotProjectionIsViewerPrivateAndSanitized(runner);
        ClientProjectionAtomicallyReplacesKnownHands(runner);
        RemotePlayerSerializesSanitizedKnownHands(runner);
    }

    private static void ObservedWallTileMovesIntoOpponentKnowledge(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        TileData wallTile = Tile("wall-five", Suit.Man, 5, false);
        tracker.ObserveWallTiles(0, new[] { wallTile });

        tracker.ProcessDraw(1, Clone(wallTile), Clone(wallTile));

        PrivateKnownHandProjection hand = tracker.GetProjection(0).Hands.Single();
        runner.Check(hand.TargetSeatIndex == 1
                     && hand.Tiles.Count == 1
                     && hand.Tiles[0].Suit == Suit.Man
                     && hand.Tiles[0].Value == 5
                     && tracker.GetObservedWallTiles(0).Count == 0,
            "observed wall tile moves to the drawing opponent without exposing physical identity");
    }

    private static void OwnDrawConsumesWallObservationWithoutKnownHandEntry(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        TileData wallTile = Tile("own-draw", Suit.Pin, 4, false);
        tracker.ObserveWallTiles(0, new[] { wallTile });

        tracker.ProcessDraw(0, Clone(wallTile), Clone(wallTile));

        runner.Check(tracker.GetProjection(0).Hands.Count == 0
                     && tracker.GetObservedWallTiles(0).Count == 0,
            "viewer own draw consumes wall observation without creating an opponent known-hand entry");
    }

    private static void HiddenDrawMutationInvalidatesObservation(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        TileData before = Tile("mutated-draw", Suit.Wind, 1, false);
        TileData after = Tile("mutated-draw", Suit.Dragon, 2, true);
        tracker.ObserveWallTiles(0, new[] { before });

        tracker.ProcessDraw(2, before, after);

        runner.Check(tracker.GetProjection(0).Hands.Count == 0
                     && tracker.GetObservedWallTiles(0).Count == 0,
            "hidden draw mutation invalidates the observed face instead of revealing its new face");
    }

    private static void PublicTilesConsumeMatchingKnowledgeConservatively(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        tracker.ObserveConcealedHand(0, 1, new[]
        {
            Tile("ordinary-five", Suit.Man, 5, false),
            Tile("modified-five", Suit.Man, 5, true),
            Tile("three-sou", Suit.Sou, 3, false)
        });

        tracker.ProcessConcealedTilesBecamePublic(1, new[]
        {
            Tile("untracked-modified-five", Suit.Man, 5, true)
        });
        PrivateKnownHandProjection afterExact = tracker.GetProjection(0).Hands.Single();
        bool keptOrdinaryFive = afterExact.Tiles.Any(tile => tile.Suit == Suit.Man && tile.Value == 5 && !tile.IsModified);
        bool removedModifiedFive = !afterExact.Tiles.Any(tile => tile.Suit == Suit.Man && tile.Value == 5 && tile.IsModified);

        tracker.ProcessConcealedTilesBecamePublic(1, new[]
        {
            Tile("untracked-ordinary-five", Suit.Man, 5, false),
            Tile("nonmatching-nine", Suit.Pin, 9, false)
        });
        PrivateKnownHandProjection finalHand = tracker.GetProjection(0).Hands.Single();
        runner.Check(keptOrdinaryFive
                     && removedModifiedFive
                     && finalHand.Tiles.Count == 1
                     && finalHand.Tiles[0].Suit == Suit.Sou
                     && finalHand.Tiles[0].Value == 3,
            "public hand contributions consume one matching known face, prefer matching modified state, and ignore nonmatches");
    }

    private static void DuplicateKnownFacesAreConsumedOneAtATime(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        tracker.ObserveConcealedHand(0, 1, new[]
        {
            Tile("duplicate-one", Suit.Pin, 4, false),
            Tile("duplicate-two", Suit.Pin, 4, false)
        });

        tracker.ProcessConcealedTilesBecamePublic(1, new[]
        {
            Tile("public-four", Suit.Pin, 4, false)
        });

        PrivateKnownHandProjection hand = tracker.GetProjection(0).Hands.Single();
        runner.Check(hand.Tiles.Count == 1
                     && hand.Tiles[0].Suit == Suit.Pin
                     && hand.Tiles[0].Value == 4,
            "duplicate known faces remain separate and one public tile consumes only one known copy");
    }

    private static void MutatedPublicDepartureDoesNotConsumeAnotherKnownFace(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        tracker.ObserveConcealedHand(0, 1, new[]
        {
            Tile("known-a", Suit.Man, 2, false),
            Tile("known-b", Suit.Pin, 6, true)
        });

        tracker.ProcessHiddenPipelineTileBecamePublic(
            1,
            Tile("known-a", Suit.Man, 2, false),
            Tile("known-a", Suit.Pin, 6, true));

        PrivateKnownHandProjection afterKnownMutation = tracker.GetProjection(0).Hands.Single();
        bool keptOtherKnownB = afterKnownMutation.Tiles.Count == 1
                               && afterKnownMutation.Tiles[0].Suit == Suit.Pin
                               && afterKnownMutation.Tiles[0].Value == 6;

        tracker.ProcessHiddenPipelineTileBecamePublic(
            1,
            Tile("unknown-a", Suit.Sou, 3, false),
            Tile("unknown-a", Suit.Pin, 6, true));

        runner.Check(keptOtherKnownB
                     && tracker.GetProjection(0).Hands.Single().Tiles.Count == 1,
            "a hidden transformed departure invalidates only its physical old knowledge and never consumes another known copy of the new face");
    }

    private static void ViewersRemainIsolatedAndRoundClearRemovesKnowledge(RegressionRunner runner)
    {
        var tracker = new PrivateTileKnowledgeTracker(4);
        tracker.ObserveConcealedHand(0, 2, new[] { Tile("viewer-zero", Suit.Man, 2, false) });
        tracker.ObserveConcealedHand(1, 2, new[] { Tile("viewer-one", Suit.Pin, 7, false) });

        tracker.ProcessHiddenTileMutation(
            2,
            Tile("viewer-zero", Suit.Man, 2, false),
            Tile("viewer-zero", Suit.Man, 3, false));

        bool viewerZeroLostMutatedTile = tracker.GetProjection(0).Hands.Count == 0;
        bool viewerOneKeptIndependentTile = tracker.GetProjection(1).Hands.Single().Tiles.Single().Value == 7;
        tracker.ClearRound();

        runner.Check(viewerZeroLostMutatedTile
                     && viewerOneKeptIndependentTile
                     && tracker.GetProjection(0).Hands.Count == 0
                     && tracker.GetProjection(1).Hands.Count == 0,
            "hidden mutation invalidation and round clear stay isolated per viewer");
    }

    private static void SnapshotProjectionIsViewerPrivateAndSanitized(RegressionRunner runner)
    {
        var projection = new PrivateKnownTilesProjection(0, new[]
        {
            new PrivateKnownHandProjection(2, new[]
            {
                new PrivateKnownTileFace(Suit.Man, 9, true)
            })
        });
        var source = new RoomGameSnapshotSource
        {
            RoomId = "private-known-snapshot",
            RoomState = RoomState.InRound,
            GameMode = GameMode.Single,
            Session = new GameSession(GameMode.Single),
            PrivateKnownTiles = projection
        };

        RoomGameSnapshot owner = RoomGameSnapshotBuilder.Build(source, 0);
        RoomGameSnapshot other = RoomGameSnapshotBuilder.Build(source, 1);
        string ownerJson = UnityEngine.JsonUtility.ToJson(owner.privateSeat.knownOpponentHands);

        runner.Check(owner.privateSeat.knownOpponentHands.Length == 1
                     && owner.privateSeat.knownOpponentHands[0].targetSeatIndex == 2
                     && owner.privateSeat.knownOpponentHands[0].tiles.Single().suit == (int)Suit.Man
                     && owner.privateSeat.knownOpponentHands[0].tiles.Single().value == 9
                     && owner.privateSeat.knownOpponentHands[0].tiles.Single().isModified,
            "snapshot builder includes the requesting viewer's current known opponent hand projection");
        runner.Check(other.privateSeat.knownOpponentHands.Length == 0,
            "snapshot builder refuses another viewer's private known-hand projection");
        runner.Check(!ownerJson.Contains("instanceId", StringComparison.OrdinalIgnoreCase)
                     && !ownerJson.Contains("ownerId", StringComparison.OrdinalIgnoreCase)
                     && !ownerJson.Contains("specialEffectId", StringComparison.OrdinalIgnoreCase),
            "private known-hand snapshot serializes no physical identity, owner provenance, or internal effect id");
    }

    private static void ClientProjectionAtomicallyReplacesKnownHands(RegressionRunner runner)
    {
        RoomGameSnapshot initial = RoomGameSnapshotBuilder.Build(new RoomGameSnapshotSource
        {
            RoomId = "private-known-client",
            RoomState = RoomState.InRound,
            GameMode = GameMode.Single,
            Session = new GameSession(GameMode.Single),
            PrivateKnownTiles = new PrivateKnownTilesProjection(0, new[]
            {
                new PrivateKnownHandProjection(1, new[]
                {
                    new PrivateKnownTileFace(Suit.Pin, 2, false)
                })
            })
        }, 0);
        var state = new ClientGameState();
        state.ApplySnapshot(initial, 10);

        NetworkMessageEnvelope replacement = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
            "PrivateKnownTiles",
            11,
            new PrivateKnownTilesMessage
            {
                viewerSeatIndex = 0,
                hands = new[]
                {
                    new SnapshotKnownHand
                    {
                        targetSeatIndex = 3,
                        tiles = new[]
                        {
                            new SnapshotKnownTile { suit = (int)Suit.Sou, value = 7, isModified = true }
                        }
                    }
                }
            }));

        ClientSequenceDisposition disposition = state.ApplyEnvelope(replacement);
        SnapshotKnownHand[] hands = state.Snapshot.privateSeat.knownOpponentHands;
        runner.Check(disposition == ClientSequenceDisposition.Accepted
                     && hands.Length == 1
                     && hands[0].targetSeatIndex == 3
                     && hands[0].tiles.Single().suit == (int)Suit.Sou
                     && hands[0].tiles.Single().value == 7,
            "ordered private known-hand message atomically replaces the previous projection");

        NetworkMessageEnvelope wrongViewer = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
            "PrivateKnownTiles",
            12,
            new PrivateKnownTilesMessage
            {
                viewerSeatIndex = 1,
                hands = Array.Empty<SnapshotKnownHand>()
            }));
        state.ApplyEnvelope(wrongViewer);
        runner.Check(state.Snapshot.privateSeat.knownOpponentHands.Single().targetSeatIndex == 3,
            "client ignores a private known-hand projection addressed to another viewer");
    }

    private static void RemotePlayerSerializesSanitizedKnownHands(RegressionRunner runner)
    {
        var endpoint = new GameEndpoint();
        var stream = new SeatMessageStream(endpoint);
        var remote = new RemotePlayerClient(0, stream);

        remote.OnPrivateKnownTilesChanged(new PrivateKnownTilesProjection(0, new[]
        {
            new PrivateKnownHandProjection(1, new[]
            {
                new PrivateKnownTileFace(Suit.Dragon, 2, true)
            })
        }));

        NetworkMessageEnvelope envelope = endpoint.SentMessages
            .Select(MessageSerializer.DeserializeEnvelope)
            .Single();
        PrivateKnownTilesMessage message = MessageSerializer.DeserializePayload<PrivateKnownTilesMessage>(envelope.data);
        string json = endpoint.SentMessages.Single();
        runner.Check(envelope.type == "PrivateKnownTiles"
                     && message.viewerSeatIndex == 0
                     && message.hands.Single().targetSeatIndex == 1
                     && message.hands.Single().tiles.Single().suit == (int)Suit.Dragon
                     && message.hands.Single().tiles.Single().value == 2
                     && message.hands.Single().tiles.Single().isModified,
            "remote player serializes the viewer-private complete known-hand projection");
        runner.Check(!json.Contains("instanceId", StringComparison.OrdinalIgnoreCase)
                     && !json.Contains("ownerId", StringComparison.OrdinalIgnoreCase)
                     && !json.Contains("specialEffectId", StringComparison.OrdinalIgnoreCase),
            "remote known-hand message contains no physical identity, owner provenance, or internal effect id");
    }

    private static TileData Tile(string id, Suit suit, int value, bool modified) => new TileData(suit, value, 0)
    {
        ID = id,
        IsModified = modified,
        SpecialEffectID = modified ? "internal-only" : null
    };

    private static TileData Clone(TileData tile) => new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID)
    {
        ID = tile.ID,
        IsModified = tile.IsModified,
        SpecialEffectID = tile.SpecialEffectID
    };
}
