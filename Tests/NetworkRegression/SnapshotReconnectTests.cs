using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

internal static class SnapshotReconnectTests
{
    public static void Run(RegressionRunner runner)
    {
        TestAuthoritativeTableState(runner);
        TestDecisionTracker(runner);
        TestSnapshotPrivacyAndSerialization(runner);
        TestCompletedEastOnlyProjection(runner);
        TestClientProjection(runner);
        TestReconnectStream(runner);
        TestSeatLifecycleAndControl(runner);
        TestTicketAndRecoveryPolicies(runner);
        TestConcealedKanProjection(runner);
    }

    private static void TestAuthoritativeTableState(RegressionRunner runner)
    {
        var state = new ServerGameState(2);
        var discarded = new TileData(Suit.Man, 5, 0);
        state.InitHand(0, new List<TileData> { discarded });
        state.InitHand(1, new List<TileData>
        {
            new(Suit.Man, 5, 1),
            new(Suit.Man, 5, 1)
        });
        state.RemoveTile(0, discarded);
        state.RecordDiscard(0, discarded);
        var claimed = state.TryClaimDiscard(0, discarded);
        state.ApplyMeld(1, ClientActionType.Pon, discarded, null);
        runner.Check(claimed
            && state.GetRiver(0).Count == 0
            && state.GetHand(1).Count == 0
            && state.GetMelds(1).Count == 1,
            "Claimed discards must leave the river and enter the claimant's meld.");

        var isolated = new ServerGameState(1);
        isolated.InitHand(0, new List<TileData> { new(Suit.Pin, 7, 0) });
        var handCopy = isolated.GetHand(0);
        handCopy[0].Value = 1;
        runner.Check(isolated.GetHand(0)[0].Value == 7,
            "ServerGameState accessors must return defensive copies.");
    }

    private static void TestDecisionTracker(RegressionRunner runner)
    {
        var tracker = new NetworkDecisionTracker();
        var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
        var main = tracker.OpenMainTurn(2, deadline);

        runner.Check(main.DecisionId == 1
            && !tracker.TrySubmitNetworkAction(main.DecisionId, 1, ClientActionType.Discard, out var wrongController)
            && wrongController == "WrongController"
            && !tracker.TrySubmitNetworkAction(main.DecisionId, 2, ClientActionType.Pon, out var wrongPhase)
            && wrongPhase == "WrongPhase"
            && tracker.TrySubmitNetworkAction(main.DecisionId, 2, ClientActionType.Discard, out _)
            && !tracker.TrySubmitNetworkAction(main.DecisionId, 2, ClientActionType.Discard, out var duplicate)
            && duplicate == "DuplicateAction",
            "Main-turn decisions must reject wrong controllers, phases, and duplicate actions.");
        runner.Check(tracker.Close(main.DecisionId), "The active main-turn decision must close by ID.");

        var response = tracker.OpenResponse(0, new TileData(Suit.Man, 3, 0), new[] { 1, 2, 3 }, deadline);
        runner.Check(response.DecisionId == 2
            && !tracker.TrySubmitNetworkAction(main.DecisionId, 1, ClientActionType.Skip, out var stale)
            && stale == "StaleDecision"
            && !tracker.TrySubmitNetworkAction(response.DecisionId, 0, ClientActionType.Skip, out var ineligible)
            && ineligible == "NotEligible"
            && tracker.TrySubmitNetworkAction(response.DecisionId, 1, ClientActionType.Skip, out _),
            "Response decisions must reject stale and ineligible actions.");

        var expiredTracker = new NetworkDecisionTracker();
        var expired = expiredTracker.OpenMainTurn(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        runner.Check(!expiredTracker.TrySubmitNetworkAction(expired.DecisionId, 0, ClientActionType.Discard, out var expiredError)
            && expiredError == "DecisionExpired",
            "Expired decisions must reject submissions.");
    }

    private static void TestSnapshotPrivacyAndSerialization(RegressionRunner runner)
    {
        var session = new GameSession(GameMode.EastOnly)
        {
            DealerIndex = 2,
            TotalRoundsPlayed = 1,
            Scores = new[] { 10, 20, 30, 40 }
        };
        var decisionTracker = new NetworkDecisionTracker();
        var source = new RoomGameSnapshotSource
        {
            RoomId = "snapshot-room",
            RoomState = RoomState.InRound,
            GameMode = GameMode.EastOnly,
            Session = session,
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsAi = index == 3,
                IsOnline = index != 3,
                DisplayName = $"Seat {index}",
                Controller = index == 3 ? "PermanentAi" : "OnlineHuman"
            }).ToArray(),
            Hands = new[]
            {
                new List<TileData> { new(Suit.Man, 1, 0), new(Suit.Man, 2, 0) },
                new List<TileData> { new(Suit.Pin, 9, 1), new(Suit.Pin, 8, 1) },
                new List<TileData> { new(Suit.Sou, 7, 2) },
                new List<TileData> { new(Suit.Wind, 1, 3) }
            },
            Melds = new[]
            {
                new List<Meld>(),
                new List<Meld>
                {
                    new(MeldType.Kan_Concealed,
                        Enumerable.Range(0, 4).Select(_ => new TileData(Suit.Pin, 9, 1)).ToList(),
                        1,
                        true)
                },
                new List<Meld>(),
                new List<Meld>()
            },
            Rivers = new[]
            {
                new List<TileData> { new(Suit.Man, 3, 0) },
                new List<TileData> { new(Suit.Pin, 4, 1) },
                new List<TileData>(),
                new List<TileData>()
            },
            RemainingWallCount = 67,
            ScoringOptions = new[]
            {
                new ScoringOptions { BonusFan = 2, RelaxedPureStraight = true },
                new ScoringOptions { BonusFan = 99 },
                new ScoringOptions(),
                new ScoringOptions()
            },
            PeekWallTiles = new[]
            {
                new List<TileData> { new(Suit.Sou, 1, 0) },
                new List<TileData> { new(Suit.Pin, 9, 1) },
                new List<TileData>(),
                new List<TileData>()
            },
            ActiveDecision = decisionTracker.OpenResponse(
                0,
                new TileData(Suit.Sou, 3, 0),
                new[] { 1, 2, 3 },
                123456789),
            WinnerId = -1,
            LoserId = -1
        };

        var snapshot = RoomGameSnapshotBuilder.Build(source, 0);
        var exposed = snapshot.privateSeat.concealedHand
            .Concat(snapshot.privateSeat.peekWallTiles)
            .Concat(snapshot.rivers.SelectMany(river => river.tiles))
            .Concat(snapshot.seats.SelectMany(seat => seat.publicMelds).SelectMany(meld => meld.tiles))
            .ToArray();
        runner.Check(snapshot.privateSeat.concealedHand.Length == 2
            && snapshot.privateSeat.concealedHand.All(tile => tile.suit == (int)Suit.Man)
            && snapshot.privateSeat.scoringOptions.bonusFan == 2
            && snapshot.privateSeat.peekWallTiles.Length == 1
            && snapshot.seats[1].concealedTileCount == 2
            && snapshot.seats[1].publicMelds.Single().isConcealed
            && snapshot.rivers[0].tiles.Length == 1
            && snapshot.remainingWallCount == 67
            && !exposed.Any(tile => tile.suit == (int)Suit.Pin && tile.value == 8),
            "A seat snapshot must expose only the requesting seat's private information.");

        var envelope = MessageSerializer.DeserializeEnvelope(
            MessageSerializer.Serialize("RoomSnapshot", 7, snapshot));
        var restored = MessageSerializer.DeserializePayload<RoomGameSnapshot>(envelope.data);
        runner.Check(restored.rivers.Length == 4
            && restored.rivers[0].seatIndex == 0
            && restored.rivers[0].tiles.Single().value == 3,
            "Per-seat river DTOs must survive protocol serialization.");

        source.ActiveDecision = new NetworkDecisionTracker().OpenMainTurn(0, 123456790);
        source.MainTurnDrawnTile = new TileData(Suit.Pin, 8, 0);
        runner.Check(RoomGameSnapshotBuilder.Build(source, 0).mainTurnDrawnTile?.value == 8
            && RoomGameSnapshotBuilder.Build(source, 1).mainTurnDrawnTile == null,
            "A main-turn drawn tile must be private to the acting seat.");
    }

    private static void TestCompletedEastOnlyProjection(RegressionRunner runner)
    {
        var session = new GameSession(GameMode.EastOnly);
        for (var round = 0; round < 4; round++) session.AdvanceRound();

        var snapshot = RoomGameSnapshotBuilder.Build(new RoomGameSnapshotSource
        {
            RoomId = "east-final",
            RoomState = RoomState.SessionCompleted,
            GameMode = GameMode.EastOnly,
            Session = session,
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsOnline = true,
                Controller = "OnlineHuman"
            }).ToArray(),
            Hands = EmptyTileLists(),
            Melds = EmptyMeldLists(),
            Rivers = EmptyTileLists(),
            ScoringOptions = new ScoringOptions[4],
            PeekWallTiles = EmptyTileLists()
        }, 3);

        runner.Check(snapshot.roundNumber == 4
            && snapshot.prevalentWind == (int)WindDirection.East
            && snapshot.dealerIndex == 3
            && snapshot.requestingSeatWind == (int)WindDirection.East
            && snapshot.result.isSessionOver,
            "A completed EastOnly snapshot must remain at East 4.");
    }

    private static void TestClientProjection(RegressionRunner runner)
    {
        var state = new ClientGameState();
        var baseline = new RoomGameSnapshot
        {
            roomId = "projection-room",
            requestingSeatIndex = 1,
            scores = new[] { 1, 2, 3, 4 },
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 1,
                concealedHand = new[] { Tile(Suit.Man, 2, 1) }
            },
            rivers = EmptyRivers(),
            activeDecision = new SnapshotDecision
            {
                decisionId = 41,
                phase = (int)NetworkDecisionPhase.Response,
                discardingSeatIndex = 0,
                eligibleSeats = new[] { 1, 2 },
                submittedSeats = new[] { 2 },
                deadlineUnixMilliseconds = 9000
            }
        };
        runner.Check(state.ApplySnapshot(baseline, 10), "A valid snapshot must establish the client baseline.");
        baseline.privateSeat.concealedHand[0].value = 9;
        runner.Check(state.Snapshot.privateSeat.concealedHand[0].value == 2
            && state.LastSequence == 10,
            "ClientGameState must clone an applied snapshot.");

        var draw = new NetworkMessageEnvelope
        {
            type = "TileDrawn",
            seq = 11,
            data = UnityEngine.JsonUtility.ToJson(new TileDrawnMessage
            {
                decisionId = 42,
                tile = Tile(Suit.Pin, 5, 1)
            })
        };
        runner.Check(state.ApplyEnvelope(draw) == ClientSequenceDisposition.Accepted
            && state.ApplyEnvelope(draw) == ClientSequenceDisposition.IgnoredDuplicate
            && state.Snapshot.mainTurnDrawnTile.value == 5,
            "Projection updates must be ordered and idempotent.");

        var win = new NetworkMessageEnvelope
        {
            type = "PlayerWin",
            seq = 12,
            data = UnityEngine.JsonUtility.ToJson(new PlayerWinMessage
            {
                winnerId = 1,
                totalFan = 24,
                isSelfDraw = true,
                scores = new[] { 100, -20, -30, -50 }
            })
        };
        runner.Check(state.ApplyEnvelope(win) == ClientSequenceDisposition.Accepted
            && state.Snapshot.result.winnerId == 1
            && state.Snapshot.result.fanCount == 24
            && state.Snapshot.scores.SequenceEqual(new[] { 100, -20, -30, -50 })
            && state.Snapshot.activeDecision == null,
            "Result envelopes must atomically update scores and close the decision.");

        state.Reset();
        runner.Check(state.Snapshot == null && state.LastSequence == 0 && !state.IsResyncRequired,
            "Reset must remove stale room projections.");
    }

    private static void TestReconnectStream(RegressionRunner runner)
    {
        var initialEndpoint = new GameEndpoint();
        var replayStream = new SeatMessageStream(initialEndpoint, 2);
        replayStream.Send("One", new WallCountMessage { remainingCount = 30 });
        replayStream.Send("Two", new WallCountMessage { remainingCount = 29 });

        var replayEndpoint = new GameEndpoint();
        var builtSnapshot = false;
        var replay = replayStream.DeliverReconnectState(replayEndpoint, 1, true, () =>
        {
            builtSnapshot = true;
            return new RoomGameSnapshot();
        });
        runner.Check(!builtSnapshot
            && replay.baselineSeq == 1
            && replay.snapshot == null
            && replay.missedMessages.Length == 1
            && replay.missedMessages[0].seq == 2,
            "A matching projection with continuous cache must receive only missed messages.");

        var snapshotStream = new SeatMessageStream(new GameEndpoint(), 2);
        snapshotStream.Send("One", new WallCountMessage());
        snapshotStream.Send("Two", new WallCountMessage());
        snapshotStream.Send("Three", new WallCountMessage());
        var restoredEndpoint = new GameEndpoint();
        var full = snapshotStream.DeliverReconnectState(restoredEndpoint, 0, true, () =>
        {
            snapshotStream.Send("DuringRecovery", new WallCountMessage());
            return new RoomGameSnapshot { roomId = "snapshot-room" };
        });
        runner.Check(full.baselineSeq == 3
            && full.snapshot.roomId == "snapshot-room"
            && full.missedMessages.Length == 0
            && restoredEndpoint.SentMessages.Count == 2
            && MessageSerializer.DeserializeEnvelope(restoredEndpoint.SentMessages[1]).seq == 4,
            "A cache gap must produce a full snapshot and flush newer messages afterward.");
    }

    private static void TestSeatLifecycleAndControl(RegressionRunner runner)
    {
        runner.Check(RoomLifecyclePolicy.SelectDecisionController(true, false) == DecisionControllerKind.Human
            && RoomLifecyclePolicy.SelectDecisionController(false, false) == DecisionControllerKind.AI
            && RoomLifecyclePolicy.SelectDecisionController(false, true) == DecisionControllerKind.Human,
            "An open human decision must retain its controller until the deadline.");
        runner.Check(RoomLifecyclePolicy.GetDisconnectDisposition(RoomState.InRound, true)
                == RoomSeatDepartureDisposition.OfflineReserved
            && RoomLifecyclePolicy.GetDisconnectDisposition(RoomState.InRound, false)
                == RoomSeatDepartureDisposition.CloseRoom
            && RoomLifecyclePolicy.GetExpiryDisposition(RoomState.WaitingForMatchReady)
                == RoomSeatExpiryDisposition.Vacant
            && RoomLifecyclePolicy.GetExpiryDisposition(RoomState.InRound)
                == RoomSeatExpiryDisposition.PermanentAi,
            "Disconnect and expiry policies must preserve or release seats by room phase.");

        var latch = new SeatDecisionControlLatch();
        runner.Check(latch.OpenDecision(11, true) == DecisionControllerKind.Human,
            "An online human must own a newly opened decision.");
        latch.MarkOffline();
        runner.Check(latch.IsHumanSubmissionAllowed(11),
            "Disconnecting must not steal an already-open human decision.");
        latch.CloseDecision(11);
        runner.Check(latch.OpenDecision(12, false) == DecisionControllerKind.AI,
            "A later decision must be AI-controlled while the human remains offline.");
        latch.MarkOnline();
        runner.Check(!latch.IsHumanSubmissionAllowed(12),
            "Reconnecting must not steal an already-open AI decision.");
        latch.CloseDecision(12);
        runner.Check(latch.OpenDecision(13, true) == DecisionControllerKind.Human
            && latch.IsHumanSubmissionAllowed(13),
            "Control must return to the human at the next decision boundary.");

        runner.Check(RoomMembershipPolicy.RequiresReconnect(new[] { "Alice", "Bob" }, " alice ")
            && !RoomMembershipPolicy.RequiresReconnect(new[] { "Alice", "Bob" }, "Carol")
            && RoomMembershipPolicy.RequiresReconnectForDisconnectedHumanSeat(false, false, "Alice", "alice")
            && !RoomMembershipPolicy.RequiresReconnectForDisconnectedHumanSeat(true, false, "Alice", "alice"),
            "An offline logical human membership must be reclaimed instead of duplicated.");

        var expiredAt = DateTime.UtcNow.AddSeconds(-1);
        runner.Check(RoomLifecyclePolicy.ShouldCountAsOnlineHuman(false, true)
            && !RoomLifecyclePolicy.ShouldCountAsOnlineHuman(false, false)
            && !RoomLifecyclePolicy.ShouldExpireOfflineSeat(true, expiredAt, DateTime.UtcNow)
            && RoomLifecyclePolicy.ShouldExpireOfflineSeat(false, expiredAt, DateTime.UtcNow),
            "Physical online presence must be independent from temporary AI control.");
    }

    private static void TestTicketAndRecoveryPolicies(RegressionRunner runner)
    {
        var ticket = new ClientReconnectTicket
        {
            serverAddress = "ws://127.0.0.1:9876/game",
            username = "Alice",
            roomId = "R0001",
            streamId = "stream-a"
        };
        var store = new InMemoryClientReconnectTicketStore();
        store.Save(ticket);
        runner.Check(store.TryLoad(out var restored)
            && restored.roomId == "R0001"
            && restored.streamId == "stream-a"
            && ClientReconnectTicketPolicy.MatchesUsername(restored, " alice ")
            && !ClientReconnectTicketPolicy.MatchesUsername(restored, "Bob"),
            "Reconnect tickets must persist only the logical recovery hint for the same identity.");
        restored.roomId = "changed";
        store.TryLoad(out var isolated);
        runner.Check(isolated.roomId == "R0001",
            "Reconnect ticket stores must return defensive copies.");
        store.Clear();
        runner.Check(!store.TryLoad(out _), "Clearing a reconnect ticket must remove recovery eligibility.");

        runner.Check(ClientReconnectTicketPolicy.ShouldClearForRoomError(NetworkErrorCodes.RoomNotFound)
            && ClientReconnectTicketPolicy.ShouldClearForRoomError(NetworkErrorCodes.SeatExpired)
            && !ClientReconnectTicketPolicy.ShouldClearForRoomError(NetworkErrorCodes.IdentityInUse)
            && !ClientReconnectRecoveryPolicy.ShouldUseCachedProjection(),
            "Terminal errors must clear tickets and reconnect must request an authoritative snapshot.");
        runner.Check(ClientReconnectRetryPolicy.GetDelaySeconds(0) == 0
            && ClientReconnectRetryPolicy.GetDelaySeconds(1) == 1
            && ClientReconnectRetryPolicy.GetDelaySeconds(2) == 2
            && ClientReconnectRetryPolicy.GetDelaySeconds(3) == 4
            && ClientReconnectRetryPolicy.GetDelaySeconds(4) == 8
            && ClientReconnectRetryPolicy.GetDelaySeconds(5) == 10
            && ClientReconnectRetryPolicy.GetDelaySeconds(99) == 10,
            "Reconnect retry delays must remain 0, 1, 2, 4, 8, 10 seconds.");
        runner.Check(ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.IdentityInUse)
            && !ClientReconnectRetryPolicy.ShouldRetryAfterError(NetworkErrorCodes.RoomNotFound),
            "Transient identity contention must retry while terminal room errors stop.");

        runner.Check(ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.WaitingForPlayers)
                == ClientRecoverySceneTarget.Lobby
            && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.InRound)
                == ClientRecoverySceneTarget.Game
            && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.SessionCompleted)
                == ClientRecoverySceneTarget.Game
            && ClientRecoverySceneRoutingPolicy.GetTarget(RoomState.Closed)
                == ClientRecoverySceneTarget.None,
            "Recovery snapshots must route waiting rooms to Lobby and active/completed sessions to Game.");

        var localSeat = new RoomSnapshotSeat
        {
            seatIndex = 1,
            isOccupied = true,
            isOnline = true,
            isAi = false,
            controller = "OnlineHuman"
        };
        var decision = new SnapshotDecision
        {
            decisionId = 99,
            phase = (int)NetworkDecisionPhase.MainTurn,
            controllerSeatIndex = 1,
            deadlineUnixMilliseconds = 1001
        };
        runner.Check(ClientRecoveryInputPolicy.CanRestoreInput(decision, localSeat, 1, 1000)
            && !ClientRecoveryInputPolicy.CanRestoreInput(decision, localSeat, 1, 1001),
            "Recovered input must require an unexpired decision controlled by the local human.");

        var lineage = new ClientProjectionLineage();
        lineage.Bind("R0001", "stream-a");
        runner.Check(lineage.Matches("R0001", "stream-a")
            && !lineage.Matches("R0002", "stream-b"),
            "Client projection lineage must match both room and stream.");
        lineage.Clear();
        runner.Check(!lineage.Matches("R0001", "stream-a"),
            "Clearing projection lineage must prevent stale replay reuse.");
    }

    private static void TestConcealedKanProjection(RegressionRunner runner)
    {
        var endpoint = new GameEndpoint();
        var remote = new RemotePlayerClient(1, new SeatMessageStream(endpoint, 4));
        remote.OnActionResolved(0, ClientActionType.AnGan, new TileData(Suit.Pin, 8, 0), null);
        var envelope = MessageSerializer.DeserializeEnvelope(endpoint.SentMessages.Single());
        var payload = MessageSerializer.DeserializePayload<ActionResolvedMessage>(envelope.data);
        runner.Check(payload.tile.suit == (int)Suit.Pin && payload.tile.value == 8,
            "MCR concealed-kan declarations must reveal the declared tile face.");

        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 1,
            seats = new[]
            {
                new RoomSnapshotSeat { seatIndex = 0, concealedTileCount = 4 },
                new RoomSnapshotSeat { seatIndex = 1 },
                new RoomSnapshotSeat { seatIndex = 2 },
                new RoomSnapshotSeat { seatIndex = 3 }
            },
            privateSeat = new SnapshotPrivateSeat { seatIndex = 1 },
            rivers = EmptyRivers()
        }, 0);
        var disposition = state.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "ActionResolved",
            seq = 1,
            data = UnityEngine.JsonUtility.ToJson(payload)
        });
        var meld = state.Snapshot.seats[0].publicMelds.Single();
        runner.Check(disposition == ClientSequenceDisposition.Accepted
            && meld.isConcealed
            && meld.tileCount == 4
            && meld.tiles.All(tile => tile.suit == (int)Suit.Pin && tile.value == 8),
            "Client projection must retain the public concealed-kan declaration.");
    }

    private static List<TileData>[] EmptyTileLists() =>
        Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray();

    private static List<Meld>[] EmptyMeldLists() =>
        Enumerable.Range(0, 4).Select(_ => new List<Meld>()).ToArray();

    private static SeatRiverSnapshot[] EmptyRivers() =>
        Enumerable.Range(0, 4).Select(index => new SeatRiverSnapshot
        {
            seatIndex = index,
            tiles = Array.Empty<SimpleTileData>()
        }).ToArray();

    private static SimpleTileData Tile(Suit suit, int value, int ownerId) =>
        new(new TileData(suit, value, ownerId));
}
