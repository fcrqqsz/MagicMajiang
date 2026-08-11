using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;
using MahjongGame.UI;

internal static class SnapshotReconnectTests
{
    public static void Run(RegressionRunner runner)
    {
        TestAuthoritativeTableState(runner);
        TestAddedKongPublicCommitUsesAuthoritativeTile(runner);
        TestDecisionTracker(runner);
        TestSnapshotPrivacyAndSerialization(runner);
        TestAuthoritativeTalentSnapshotFiltering(runner);
        TestRuntimeSnapshotProjectionUsesRuleApprovedPrivateValues(runner);
        TestTalentRecoveryPresentationUsesOnlyAuthoritativeMainDecision(runner);
        TestTalentProjectionOrderingAndSeatIsolation(runner);
        TestAlienationSnapshotPrivacy(runner);
        TestWinningHandNormalization(runner);
        TestWinningHandSnapshotCodec(runner);
        TestWinningHandResultVisibility(runner);
        TestResultHandLayoutPolicy(runner);
        TestCompletedEastOnlyProjection(runner);
        TestClientProjection(runner);
        TestTalentRuntimeEventProjection(runner);
        TestRemoteWinningHandNotification(runner);
        TestReconnectStream(runner);
        TestSeatLifecycleAndControl(runner);
        TestTicketAndRecoveryPolicies(runner);
        TestConcealedKanProjection(runner);
        TestConcealedKongVisualPolicy(runner);
        TestAddedKanProjection(runner);
        TestOpponentAddedKongUpgradesPon(runner);
        TestOpponentAddedKongRejectsMissingPon(runner);
        TestSelfTurnKongOptions(runner);
        TestRobKongDecisionPhase(runner);
        TestRobKongDeclarationProjection(runner);
        TestRobKongRemoteNotification(runner);
    }

    private static void TestRuntimeSnapshotProjectionUsesRuleApprovedPrivateValues(RegressionRunner runner)
    {
        var loadouts = Enumerable.Range(0, 4).ToDictionary(
            seatIndex => seatIndex,
            seatIndex => new TalentSlotConfig
            {
                SlotTalentIds = seatIndex == 0
                    ? new[] { "sheathed_edge", null, null, null, null, null }
                    : new string[TalentSlotConfig.MainSlotCount],
                ReserveTalentIds = seatIndex == 0
                    ? new[] { "interception", null, null }
                    : new string[TalentSlotConfig.ReserveSlotCount]
            });
        var session = new GameSession(GameMode.HalfGame);
        var runtime = new TalentMatchRuntime(loadouts, TalentRegistry.Instance);
        runtime.BeginMatch(session);
        runtime.BeginRound(new TalentRoundContext(session));
        runtime.ApplyWallBuilding(new TalentWallContext(session, new List<TileData>()));
        runtime.ResolvePostShuffle(new TalentPostShuffleContext(session, new List<TileData>()));
        runtime.EndRound(new TalentRoundOutcome { WinnerSeatIndex = 1 }, session);

        TalentSnapshotEntry[] entries = runtime.GetSnapshotEntries().Where(entry => entry.OwnerSeatIndex == 0).ToArray();
        runner.Check(entries.Single(entry => entry.TalentId == "interception").PrivateValue == 3
                     && entries.Single(entry => entry.TalentId == "sheathed_edge").PrivateValue == 1,
            "runtime snapshot privateValue exposes only each rule's approved single counter projection");
    }

    private static void TestAuthoritativeTalentSnapshotFiltering(RegressionRunner runner)
    {
        var source = CreateEmptySnapshotSource("talent-snapshot", RoomState.InRound);
        source.ActiveDecision = new NetworkDecisionTracker().OpenMainTurn(0, 987654321);
        source.Talents = new[]
        {
            new RoomSnapshotTalentSource
            {
                OwnerSeatIndex = 0,
                TalentId = "interception",
                IsActive = true,
                IsRevealed = true,
                PrivateValue = 2,
                LastPublicEventType = "uses_remaining",
                LastPublicValue = 2
            },
            new RoomSnapshotTalentSource
            {
                OwnerSeatIndex = 0,
                TalentId = "peek",
                IsActive = false,
                IsRevealed = false,
                PrivateValue = 0
            },
            new RoomSnapshotTalentSource
            {
                OwnerSeatIndex = 1,
                TalentId = "sheathed_edge",
                IsActive = false,
                IsRevealed = true,
                PrivateValue = 91,
                LastPublicEventType = "edge",
                LastPublicValue = 3
            },
            new RoomSnapshotTalentSource
            {
                OwnerSeatIndex = 2,
                TalentId = "interception",
                IsActive = true,
                IsRevealed = false,
                PrivateValue = 77
            }
        };
        source.AvailableTalentActions = new[]
        {
            new TalentActionOption
            {
                TalentId = "interception",
                TargetSeatIndex = 1,
                TargetTalentId = "sheathed_edge"
            }
        };
        source.Sideboard = new RoomSnapshotSideboardSource
        {
            IsActive = false,
            DecisionId = 44,
            DeadlineUnixMilliseconds = 555,
            OwnLocked = true,
            SeatLocked = new[] { true, false, true, true }
        };

        RoomGameSnapshot owner = RoomGameSnapshotBuilder.Build(source, 0);
        RoomGameSnapshot opponent = RoomGameSnapshotBuilder.Build(source, 1);
        string ownerJson = UnityEngine.JsonUtility.ToJson(owner);
        string knownJson = UnityEngine.JsonUtility.ToJson(owner.knownTalents);

        runner.Check(owner.activeDecision?.decisionId == 1
                     && owner.activeDecision.phase == (int)NetworkDecisionPhase.MainTurn
                     && owner.privateSeat.ownTalents.Length == 2
                     && owner.privateSeat.ownTalents.Single(talent => talent.talentId == "interception").isActive
                     && owner.privateSeat.ownTalents.Single(talent => talent.talentId == "interception").privateValue == 2
                     && owner.privateSeat.availableTalentActions.Single().targetTalentId == "sheathed_edge"
                     && owner.knownTalents.Length == 1
                     && owner.knownTalents[0].ownerSeatIndex == 1
                     && owner.knownTalents[0].lastPublicValue == 3,
            "a main-turn reconnect snapshot restores only authoritative decision, own talent state, known reveals, and recomputed actions");
        runner.Check(!ownerJson.Contains("transientTalentSelection", StringComparison.Ordinal)
                     && !knownJson.Contains("isActive", StringComparison.OrdinalIgnoreCase)
                     && !ownerJson.Contains("\"privateValue\":91", StringComparison.Ordinal)
                     && !ownerJson.Contains("\"privateValue\":77", StringComparison.Ordinal)
                     && opponent.privateSeat.ownTalents.Single().talentId == "sheathed_edge"
                     && opponent.privateSeat.ownTalents.Single().privateValue == 91,
            "talent snapshots never serialize picker drafts, opponent active flags, hidden talents, or opponent private values");
    }

    private static void TestTalentProjectionOrderingAndSeatIsolation(RegressionRunner runner)
    {
        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            scores = new int[4],
            seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeat { seatIndex = index }).ToArray(),
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 0,
                ownTalents = new[]
                {
                    new SnapshotOwnTalent { talentId = "interception", isActive = true, privateValue = 3 }
                },
                availableTalentActions = Array.Empty<SnapshotTalentActionOption>()
            },
            knownTalents = Array.Empty<SnapshotKnownTalent>(),
            sideboard = new SnapshotSideboardState { seatLocked = new bool[4] },
            rivers = EmptyRivers()
        }, 0);

        NetworkMessageEnvelope reveal = MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
            "TalentRuntimeEvent", 1, new TalentRuntimeEventMessage
            {
                eventId = 10,
                ownerSeatIndex = 1,
                talentId = "sheathed_edge",
                eventType = "edge",
                visibility = (int)TalentEventVisibility.Public,
                value = 3
            }));
        ClientSequenceDisposition accepted = state.ApplyEnvelope(reveal);
        ClientSequenceDisposition duplicate = state.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentRuntimeEvent", 1, new TalentRuntimeEventMessage
                {
                    eventId = 11,
                    ownerSeatIndex = 1,
                    talentId = "sheathed_edge",
                    eventType = "edge",
                    visibility = (int)TalentEventVisibility.Public,
                    value = 99
                })));
        ClientSequenceDisposition wrongOwner = state.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentPrivateState", 2, new TalentPrivateStateMessage
                {
                    ownerSeatIndex = 2,
                    talents = new[]
                    {
                        new SnapshotOwnTalent { talentId = "interception", isActive = true, privateValue = 88 }
                    }
                })));
        ClientSequenceDisposition privateUpdate = state.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentPrivateState", 3, new TalentPrivateStateMessage
                {
                    ownerSeatIndex = 0,
                    talents = new[]
                    {
                        new SnapshotOwnTalent { talentId = "interception", isActive = true, privateValue = 2 }
                    }
                })));

        runner.Check(accepted == ClientSequenceDisposition.Accepted
                     && duplicate == ClientSequenceDisposition.IgnoredDuplicate
                     && wrongOwner == ClientSequenceDisposition.Accepted
                     && privateUpdate == ClientSequenceDisposition.Accepted
                     && state.Snapshot.knownTalents.Single().lastPublicValue == 3
                     && state.Snapshot.privateSeat.ownTalents.Single().privateValue == 2,
            "ordered talent projection applies a public event once and ignores another seat's private runtime state");

        var gapState = new ClientGameState();
        gapState.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeat { seatIndex = index }).ToArray(),
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 0,
                ownTalents = new[]
                {
                    new SnapshotOwnTalent { talentId = "interception", isActive = true, privateValue = 3 }
                }
            },
            rivers = EmptyRivers()
        }, 0);
        ClientSequenceDisposition gap = gapState.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentPrivateState", 2, new TalentPrivateStateMessage
                {
                    ownerSeatIndex = 0,
                    talents = new[]
                    {
                        new SnapshotOwnTalent { talentId = "interception", isActive = true, privateValue = 1 }
                    }
                })));
        runner.Check(gap == ClientSequenceDisposition.ResyncRequired
                     && gapState.Snapshot.privateSeat.ownTalents.Single().privateValue == 3,
            "an out-of-order private talent envelope requests resync without applying the private value");
    }

    private static void TestTalentRecoveryPresentationUsesOnlyAuthoritativeMainDecision(RegressionRunner runner)
    {
        var snapshot = new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            activeDecision = new SnapshotDecision
            {
                decisionId = 72,
                phase = (int)NetworkDecisionPhase.MainTurn,
                actingSeatIndex = 0
            },
            seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeat { seatIndex = index }).ToArray(),
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 0,
                availableTalentActions = new[]
                {
                    new SnapshotTalentActionOption
                    {
                        talentId = "interception",
                        targetSeatIndex = 1,
                        targetTalentId = "sheathed_edge"
                    }
                }
            },
            sideboard = new SnapshotSideboardState
            {
                isActive = true,
                decisionId = 9,
                ownLocked = true,
                seatLocked = new[] { true, false, true, true }
            },
            rivers = EmptyRivers()
        };
        var game = new ClientGameState();
        game.ApplySnapshot(snapshot, 0);
        ClientTalentRecoveryProjection projection = game.CreateTalentRecoveryProjection();
        var room = new ClientRoomState();
        room.ApplyRecoverySnapshot(snapshot);

        runner.Check(projection.CloseTransientPicker
                     && projection.DecisionId == 72
                     && projection.AvailableActions.Single().TargetTalentId == "sheathed_edge"
                     && room.Sideboard.ownLocked
                     && room.Sideboard.seatLocked.SequenceEqual(new[] { true, false, true, true }),
            "recovery closes local talent pickers and exposes only authoritative main-turn actions and sideboard locks");

        ClientSequenceDisposition resolved = game.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "ActionResolved", 1, new ActionResolvedMessage { playerId = 0 })));
        var locked = new SideboardLockedMessage
        {
            decisionId = 9,
            acceptedSelection = true,
            ownTotalAlienation = 55
        };
        ClientSequenceDisposition lockedApplied = game.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize("SideboardLocked", 2, locked)));
        room.ApplySideboardLocked(locked);
        runner.Check(resolved == ClientSequenceDisposition.Accepted
                     && game.AvailableTalentActions.Length == 0
                     && game.CreateTalentRecoveryProjection().AvailableActions.Length == 0
                     && lockedApplied == ClientSequenceDisposition.Accepted
                     && game.Snapshot.privateSeat.ownTotalAlienation == 55
                     && room.OwnTotalAlienation == 55,
            "decision completion clears cached talent actions and a live sideboard lock updates the authoritative own total");

        snapshot.activeDecision.actingSeatIndex = 1;
        game.ApplySnapshot(snapshot, 0);
        ClientTalentRecoveryProjection foreignTurn = game.CreateTalentRecoveryProjection();
        runner.Check(foreignTurn.CloseTransientPicker
                     && foreignTurn.DecisionId == 0
                     && foreignTurn.AvailableActions.Length == 0,
            "recovery never restores talent actions for another seat's decision");
    }

    private static void TestAlienationSnapshotPrivacy(RegressionRunner runner)
    {
        var source = new RoomGameSnapshotSource
        {
            RoomId = "alienation-privacy",
            RoomState = RoomState.InRound,
            GameMode = GameMode.Single,
            Session = new GameSession(GameMode.Single),
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsOnline = true,
                DisplayName = $"Seat {index}"
            }).ToArray(),
            Hands = EmptyTileLists(),
            Melds = EmptyMeldLists(),
            Rivers = EmptyTileLists(),
            ScoringOptions = Enumerable.Range(0, 4).Select(_ => new ScoringOptions()).ToArray(),
            PeekWallTiles = EmptyTileLists()
        };
        typeof(RoomGameSnapshotSource).GetField("AlienationPreset")?.SetValue(source, AlienationPreset.Standard);
        typeof(RoomGameSnapshotSource).GetField("OwnTotalAlienation")?.SetValue(source, 45);

        RoomGameSnapshot snapshot = RoomGameSnapshotBuilder.Build(source, 0);
        string json = UnityEngine.JsonUtility.ToJson(snapshot);
        var presetField = typeof(RoomGameSnapshot).GetField("alienationPreset");
        var privateTotalField = typeof(SnapshotPrivateSeat).GetField("ownTotalAlienation");
        runner.Check(presetField?.GetValue(snapshot) is int preset && preset == (int)AlienationPreset.Standard
            && privateTotalField?.GetValue(snapshot.privateSeat) is int ownTotal && ownTotal == 45
            && snapshot.seats.All(seat => !UnityEngine.JsonUtility.ToJson(seat)
                .Contains("totalAlienation", StringComparison.OrdinalIgnoreCase)),
            "Recovery snapshots must restore the public preset and owner total without exposing opponent totals.");

        var client = new ClientGameState();
        runner.Check(client.ApplySnapshot(snapshot, 7)
            && presetField?.GetValue(client.Snapshot) is int appliedPreset && appliedPreset == (int)AlienationPreset.Standard
            && privateTotalField?.GetValue(client.Snapshot.privateSeat) is int appliedTotal && appliedTotal == 45,
            "ClientGameState must atomically apply the room preset and private owner total from a snapshot.");
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
        runner.Check(tracker.Close(response.DecisionId), "The active response decision must close by ID.");

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
            result = new RoundResultSnapshot
            {
                winnerId = 1,
                winningHand = new WinningHandSnapshot
                {
                    concealedTiles = new[] { Tile(Suit.Man, 2, 1) },
                    winningTile = Tile(Suit.Man, 3, 1),
                    melds = Array.Empty<SnapshotMeld>()
                }
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
        baseline.result.winningHand.concealedTiles[0].value = 9;
        runner.Check(state.Snapshot.privateSeat.concealedHand[0].value == 2
            && state.Snapshot.result.winningHand.concealedTiles[0].value == 2
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
                isSelfDraw = false,
                winKind = WinKind.RobKong,
                loserId = 2,
                scores = new[] { 100, -20, -30, -50 },
                winningHand = new WinningHandSnapshot
                {
                    concealedTiles = new[] { Tile(Suit.Pin, 3, 1), Tile(Suit.Pin, 4, 1) },
                    winningTile = Tile(Suit.Pin, 5, 1),
                    melds = Array.Empty<SnapshotMeld>()
                }
            })
        };
        runner.Check(state.ApplyEnvelope(win) == ClientSequenceDisposition.Accepted
            && state.Snapshot.result.winnerId == 1
            && state.Snapshot.result.fanCount == 24
            && state.Snapshot.result.winKind == WinKind.RobKong
            && state.Snapshot.result.loserId == 2
            && !state.Snapshot.result.isSelfDraw
            && state.Snapshot.result.winningHand.concealedTiles.Length == 2
            && state.Snapshot.result.winningHand.winningTile.value == 5
            && state.Snapshot.scores.SequenceEqual(new[] { 100, -20, -30, -50 })
            && state.Snapshot.activeDecision == null,
            "Result envelopes must atomically update scores and close the decision.");

        var legacyWin = new NetworkMessageEnvelope
        {
            type = "PlayerWin",
            seq = 13,
            data = "{\"winnerId\":2,\"totalFan\":8,\"isSelfDraw\":false,\"scores\":[100,-20,0,-80]}"
        };
        runner.Check(state.ApplyEnvelope(legacyWin) == ClientSequenceDisposition.Accepted
            && state.Snapshot.result.winKind == WinKind.Discard
            && state.Snapshot.result.loserId == -1,
            "A legacy v2 win without explicit outcome metadata must not interpret JSON's zero default as seat zero.");

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

        var robKongDecision = new SnapshotDecision
        {
            decisionId = 100,
            phase = 2,
            discardingSeatIndex = 0,
            eligibleSeats = new[] { 1, 2, 3 },
            deadlineUnixMilliseconds = 1001
        };
        runner.Check(ClientRecoveryInputPolicy.CanRestoreInput(robKongDecision, localSeat, 1, 1000),
            "Recovered eligible humans must regain an unexpired rob-kong Hu-or-skip decision.");

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

    private static void TestAddedKongPublicCommitUsesAuthoritativeTile(RegressionRunner runner)
    {
        TileData target = new TileData(Suit.Dragon, 2, 0);

        ServerGameState robbedState = BuildAddedKongState(target, out TileData robbedAuthoritativeTile);
        bool robbedCommitted = robbedState.TryResolveAddedKong(
            0,
            target,
            wasRobbed: true,
            out TileData robbedPublicTile);
        Meld robbedMeld = robbedState.GetMelds(0).Single();
        runner.Check(!robbedCommitted
                     && robbedPublicTile == null
                     && robbedMeld.Type == MeldType.Pon
                     && robbedState.GetHand(0).Single().ID == robbedAuthoritativeTile.ID,
            "a robbed added-kong leaves the authoritative tile concealed and yields no public tile");

        ServerGameState committedState = BuildAddedKongState(target, out TileData committedAuthoritativeTile);
        bool committed = committedState.TryResolveAddedKong(
            0,
            target,
            wasRobbed: false,
            out TileData committedPublicTile);
        Meld committedMeld = committedState.GetMelds(0).Single();
        runner.Check(committed
                     && committedState.GetHand(0).Count == 0
                     && committedMeld.Type == MeldType.Kan_Added
                     && committedMeld.Tiles.Count == 4
                     && committedMeld.Tiles.Last().ID == committedAuthoritativeTile.ID
                     && committedPublicTile != null
                     && !ReferenceEquals(committedPublicTile, committedAuthoritativeTile)
                     && committedPublicTile.ID == committedAuthoritativeTile.ID
                     && committedPublicTile.IsModified
                     && committedPublicTile.SpecialEffectID == "midas_touch",
            "a successful added-kong exposes the authoritative modified tile only after the meld is committed");
    }

    private static ServerGameState BuildAddedKongState(TileData target, out TileData authoritativeTile)
    {
        var state = new ServerGameState(4);
        state.InitHand(0, new List<TileData>
        {
            new TileData(target.TileSuit, target.Value, 0),
            new TileData(target.TileSuit, target.Value, 0)
        });
        state.ApplyMeld(0, ClientActionType.Pon, target, null);
        authoritativeTile = new TileData(target.TileSuit, target.Value, 0);
        authoritativeTile.ID = "authoritative-added-kong";
        authoritativeTile.IsModified = true;
        authoritativeTile.SpecialEffectID = "midas_touch";
        state.AddTile(0, authoritativeTile);
        return state;
    }

    private static void TestTalentRuntimeEventProjection(RegressionRunner runner)
    {
        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            scores = new[] { 0, 0, 0, 0 },
            seats = Enumerable.Range(0, 4)
                .Select(seatIndex => new RoomSnapshotSeat { seatIndex = seatIndex })
                .ToArray(),
            privateSeat = new SnapshotPrivateSeat { seatIndex = 0 },
            rivers = EmptyRivers()
        }, 0);

        ClientSequenceDisposition scoreDisposition = state.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentRuntimeEvent",
                1,
                new TalentRuntimeEventMessage
                {
                    eventId = 10,
                    ownerSeatIndex = 2,
                    talentId = "network_test_score_effect",
                    eventType = "score_effect",
                    visibility = 1,
                    value = 5,
                    isScoreDelta = true
                })));
        ClientSequenceDisposition revealDisposition = state.ApplyEnvelope(
            MessageSerializer.DeserializeEnvelope(MessageSerializer.Serialize(
                "TalentRuntimeEvent",
                2,
                new TalentRuntimeEventMessage
                {
                    eventId = 11,
                    ownerSeatIndex = 2,
                    talentId = "network_test_reveal_only",
                    eventType = "talent_revealed",
                    visibility = 1,
                    value = 99,
                    isScoreDelta = false
                })));

        runner.Check(scoreDisposition == ClientSequenceDisposition.Accepted
                     && revealDisposition == ClientSequenceDisposition.Accepted
                     && state.Snapshot.scores.SequenceEqual(new[] { 0, 0, 5, 0 }),
            "client projection applies typed runtime score deltas without treating reveal values as scores");
    }

    private static void TestRemoteWinningHandNotification(RegressionRunner runner)
    {
        var endpoint = new GameEndpoint();
        var remote = new RemotePlayerClient(0, new SeatMessageStream(endpoint, 4));
        var winningHand = new WinningHandSnapshot
        {
            concealedTiles = new[] { Tile(Suit.Sou, 6, 0), Tile(Suit.Sou, 7, 0) },
            winningTile = Tile(Suit.Sou, 8, 0),
            melds = Array.Empty<SnapshotMeld>()
        };

        remote.OnPlayerWin(0, 16, new List<string> { "清龙(16)" }, false,
            WinKind.RobKong, 3, winningHand);
        var envelope = MessageSerializer.DeserializeEnvelope(endpoint.SentMessages.Single());
        var payload = MessageSerializer.DeserializePayload<PlayerWinMessage>(envelope.data);
        runner.Check(envelope.type == "PlayerWin"
            && payload.winKind == WinKind.RobKong
            && payload.loserId == 3
            && payload.winningHand.concealedTiles.Length == 2
            && payload.winningHand.winningTile.value == 8,
            "Remote win notifications must carry the authoritative winning-hand snapshot.");
    }

    private static void TestWinningHandNormalization(RegressionRunner runner)
    {
        var selfDrawWin = new TileData(Suit.Pin, 5, 1);
        var selfDrawHand = new List<TileData>
        {
            new(Suit.Man, 1, 1),
            new(Suit.Man, 2, 1),
            new(Suit.Man, 3, 1),
            new(Suit.Pin, 5, 1),
            selfDrawWin
        };
        var melds = new List<Meld>
        {
            new(MeldType.Kan_Added,
                Enumerable.Range(0, 4).Select(_ => new TileData(Suit.Dragon, 1, 1)).ToList(),
                0)
        };

        var selfDraw = WinningHandSnapshotCodec.Create(
            selfDrawHand, melds, selfDrawWin, true);
        runner.Check(selfDraw.concealedTiles.Length == 4
            && selfDraw.winningTile.suit == (int)Suit.Pin
            && selfDraw.winningTile.value == 5
            && selfDraw.concealedTiles.Count(tile => tile.suit == (int)Suit.Pin && tile.value == 5) == 1
            && selfDraw.melds.Single().meldType == (int)MeldType.Kan_Added
            && selfDraw.melds.Single().tiles.Length == 4,
            "A self-draw result must separate exactly one winning tile and retain full meld data.");

        var discardHand = new List<TileData>
        {
            new(Suit.Sou, 2, 2),
            new(Suit.Sou, 3, 2),
            new(Suit.Sou, 4, 2)
        };
        var discardWin = WinningHandSnapshotCodec.Create(
            discardHand, new List<Meld>(), new TileData(Suit.Wind, 1, 0), false);
        runner.Check(discardWin.concealedTiles.Length == 3
            && discardWin.winningTile.suit == (int)Suit.Wind
            && discardWin.winningTile.value == 1,
            "A discard win must keep the concealed hand intact and expose the external winning tile separately.");
    }

    private static void TestWinningHandSnapshotCodec(RegressionRunner runner)
    {
        var malformed = new WinningHandSnapshot
        {
            concealedTiles = new[]
            {
                Tile(Suit.Man, 1, 0),
                null,
                new SimpleTileData { suit = 99, value = 99, ownerId = 0, isValid = true },
                new SimpleTileData { suit = (int)Suit.Pin, value = 2, ownerId = 0, isValid = false }
            },
            winningTile = Tile(Suit.Man, 2, 0),
            melds = new[]
            {
                new SnapshotMeld
                {
                    meldType = (int)MeldType.Kan_Concealed,
                    sourceSeatIndex = 0,
                    isConcealed = false,
                    tileCount = 99,
                    tiles = Enumerable.Range(0, 4).Select(_ => Tile(Suit.Pin, 8, 0)).ToArray()
                },
                new SnapshotMeld
                {
                    meldType = (int)MeldType.Pon,
                    sourceSeatIndex = 1,
                    tileCount = 3,
                    tiles = new[]
                    {
                        Tile(Suit.Sou, 3, 0),
                        Tile(Suit.Sou, 3, 0),
                        new SimpleTileData { suit = (int)Suit.Sou, value = 10, isValid = true }
                    }
                }
            }
        };

        runner.Check(!WinningHandSnapshotCodec.TryValidate(malformed, out _),
            "Validation must reject invalid tiles and inconsistent derived meld fields.");

        var normalized = WinningHandSnapshotCodec.Normalize(malformed);
        runner.Check(WinningHandSnapshotCodec.TryValidate(normalized, out _)
            && normalized.concealedTiles.Length == 1
            && normalized.melds.Length == 1
            && normalized.melds[0].tileCount == 4
            && normalized.melds[0].isConcealed,
            "Normalization must remove invalid tiles and recompute derived meld fields.");

        malformed.concealedTiles[0].value = 9;
        malformed.melds[0].tiles[0].value = 7;
        runner.Check(normalized.concealedTiles[0].value == 1
            && normalized.melds[0].tiles[0].value == 8,
            "A normalized winning hand must be a deep copy of its input.");

        var invalidWinningTile = WinningHandSnapshotCodec.Normalize(new WinningHandSnapshot
        {
            concealedTiles = Array.Empty<SimpleTileData>(),
            winningTile = new SimpleTileData { suit = (int)Suit.Man, value = 10, isValid = true },
            melds = Array.Empty<SnapshotMeld>()
        });
        runner.Check(!WinningHandSnapshotCodec.TryValidate(invalidWinningTile, out _),
            "Validation must reject a snapshot without a valid winning tile.");
    }

    private static void TestWinningHandResultVisibility(RegressionRunner runner)
    {
        var winningHand = WinningHandSnapshotCodec.Create(
            new List<TileData> { new(Suit.Man, 7, 1), new(Suit.Man, 8, 1), new(Suit.Man, 9, 1) },
            new List<Meld>(),
            new TileData(Suit.Dragon, 3, 0),
            false);
        var source = new RoomGameSnapshotSource
        {
            RoomId = "winning-hand-visibility",
            RoomState = RoomState.InRound,
            GameMode = GameMode.Single,
            Session = new GameSession(GameMode.Single),
            Seats = Enumerable.Range(0, 4).Select(index => new RoomSnapshotSeatSource
            {
                SeatIndex = index,
                IsOccupied = true,
                IsOnline = true,
                Controller = "OnlineHuman"
            }).ToArray(),
            Hands = new[]
            {
                new List<TileData> { new(Suit.Man, 1, 0) },
                new List<TileData> { new(Suit.Man, 7, 1), new(Suit.Man, 8, 1), new(Suit.Man, 9, 1) },
                new List<TileData> { new(Suit.Pin, 1, 2) },
                new List<TileData> { new(Suit.Sou, 1, 3) }
            },
            Melds = EmptyMeldLists(),
            Rivers = EmptyTileLists(),
            ScoringOptions = Enumerable.Range(0, 4).Select(_ => new ScoringOptions()).ToArray(),
            PeekWallTiles = EmptyTileLists(),
            WinnerId = -1,
            WinningHand = winningHand
        };

        runner.Check(RoomGameSnapshotBuilder.Build(source, 0).result.winningHand == null,
            "A live round must not expose a concealed winning-hand candidate.");

        source.WinnerId = 1;
        source.WinKind = WinKind.RobKong;
        source.LoserId = 0;
        var seatZero = RoomGameSnapshotBuilder.Build(source, 0);
        var seatTwo = RoomGameSnapshotBuilder.Build(source, 2);
        runner.Check(seatZero.result.winningHand.concealedTiles.Length == 3
            && seatTwo.result.winningHand.concealedTiles.Length == 3
            && seatZero.result.winningHand.winningTile.value == 3
            && seatZero.result.winKind == WinKind.RobKong
            && seatZero.result.loserId == 0
            && seatTwo.result.winKind == WinKind.RobKong
            && seatTwo.result.loserId == 0
            && seatZero.seats[1].concealedTileCount == 3
            && seatZero.privateSeat.concealedHand.All(tile => tile.ownerId == 0),
            "After a win, every requesting seat must receive the same result hand without widening normal seat privacy.");
    }

    private static void TestResultHandLayoutPolicy(RegressionRunner runner)
    {
        var hand = new WinningHandSnapshot
        {
            concealedTiles = Enumerable.Range(1, 10).Select(value => Tile(Suit.Man, value, 0)).ToArray(),
            winningTile = Tile(Suit.Pin, 5, 0),
            melds = new[]
            {
                new SnapshotMeld
                {
                    meldType = (int)MeldType.Kan_Added,
                    tiles = Enumerable.Range(0, 4).Select(_ => Tile(Suit.Dragon, 1, 0)).ToArray()
                }
            }
        };

        runner.Check(ResultHandLayoutPolicy.CountVisibleTiles(hand) == 15,
            "Result layout must count every added-kong tile as a normal horizontal tile.");
        runner.Check(ResultHandLayoutPolicy.ShouldUseTileBack(MeldType.Kan_Concealed, 0, 4)
            && !ResultHandLayoutPolicy.ShouldUseTileBack(MeldType.Kan_Concealed, 1, 4)
            && !ResultHandLayoutPolicy.ShouldUseTileBack(MeldType.Kan_Concealed, 2, 4)
            && ResultHandLayoutPolicy.ShouldUseTileBack(MeldType.Kan_Concealed, 3, 4)
            && !ResultHandLayoutPolicy.ShouldUseTileBack(MeldType.Kan_Added, 0, 4),
            "Result concealed kongs must use backs only on the two outside tiles.");

        float standardWidth = ResultHandLayoutPolicy.CalculateTileWidth(720f, 14, 1);
        float crowdedWidth = ResultHandLayoutPolicy.CalculateTileWidth(720f, 18, 4);
        float shortWidth = ResultHandLayoutPolicy.CalculateTileWidth(720f, 3, 0);
        runner.Check(standardWidth >= 48f && standardWidth <= 52f
            && crowdedWidth >= 32f && crowdedWidth < standardWidth
            && shortWidth == 52f,
            "Result tiles must shrink to fit crowded hands and cap their maximum thumbnail width.");
    }

    private static void TestConcealedKongVisualPolicy(RegressionRunner runner)
    {
        var policyType = typeof(Meld).Assembly.GetType("MahjongGame.Core.MeldVisualPolicy");
        var method = policyType?.GetMethod("IsTileFaceDown");
        if (method == null)
        {
            runner.Check(false,
                "Concealed-kong visuals must have a shared policy instead of inheriting exposed-meld faces.");
            return;
        }

        bool FaceDown(MeldType type, int index) => (bool)method.Invoke(null, new object[] { type, index });
        runner.Check(Enumerable.Range(0, 4).All(index => FaceDown(MeldType.Kan_Concealed, index))
            && !FaceDown(MeldType.Kan_Exposed, 0)
            && !FaceDown(MeldType.Kan_Added, 3)
            && !FaceDown(MeldType.Pon, 0),
            "MCR concealed kongs must render every tile face-down while exposed melds remain face-up.");
    }

    private static void TestSelfTurnKongOptions(RegressionRunner runner)
    {
        var empty = SelfTurnKongResolver.Resolve(null, null);
        runner.Check(!empty.HasAny,
            "An empty self-turn state must have no kong targets.");

        var hand = new List<TileData>
        {
            new(Suit.Man, 5, 0), new(Suit.Man, 5, 0), new(Suit.Man, 5, 0), new(Suit.Man, 5, 0),
            new(Suit.Sou, 3, 0), new(Suit.Sou, 3, 0), new(Suit.Sou, 3, 0), new(Suit.Sou, 3, 0),
            new(Suit.Pin, 9, 0), new(Suit.Dragon, 1, 0)
        };
        var pinNinePon = new Meld(MeldType.Pon, new List<TileData>
        {
            new(Suit.Pin, 9, 1), new(Suit.Pin, 9, 1), new(Suit.Pin, 9, 1)
        }, 1);
        var redDragonPon = new Meld(MeldType.Pon, new List<TileData>
        {
            new(Suit.Dragon, 1, 2), new(Suit.Dragon, 1, 2), new(Suit.Dragon, 1, 2)
        }, 2);
        var duplicatePinNinePon = new Meld(MeldType.Pon, new List<TileData>
        {
            new(Suit.Pin, 9, 3), new(Suit.Pin, 9, 3), new(Suit.Pin, 9, 3)
        }, 3);

        var options = SelfTurnKongResolver.Resolve(hand, new[] { pinNinePon, redDragonPon, duplicatePinNinePon });
        runner.Check(options.AnGangTargets.Select(tile => tile.GetName()).SequenceEqual(new[] { "5万", "3条" }),
            "Self-turn options must preserve every concealed-kong target in hand order.");
        runner.Check(options.JiaGangTargets.Select(tile => tile.GetName()).SequenceEqual(new[] { "9筒", "中" }),
            "Self-turn options must preserve separate added-kong targets without duplicate pon entries.");
    }

    private static void TestAddedKanProjection(RegressionRunner runner)
    {
        var ponTile = new SimpleTileData(new TileData(Suit.Pin, 9, 0));
        var pon = new SnapshotMeld
        {
            meldType = (int)MeldType.Pon,
            tileCount = 3,
            tiles = new[] { ponTile, ponTile, ponTile }
        };
        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 0,
            seats = new[]
            {
                new RoomSnapshotSeat { seatIndex = 0, concealedTileCount = 5, publicMelds = new[] { pon } },
                new RoomSnapshotSeat { seatIndex = 1 },
                new RoomSnapshotSeat { seatIndex = 2 },
                new RoomSnapshotSeat { seatIndex = 3 }
            },
            privateSeat = new SnapshotPrivateSeat
            {
                seatIndex = 0,
                concealedHand = new[]
                {
                    ponTile,
                    new SimpleTileData(new TileData(Suit.Man, 1, 0)),
                    new SimpleTileData(new TileData(Suit.Man, 2, 0))
                },
                melds = new[] { pon }
            },
            rivers = EmptyRivers()
        }, 0);

        var disposition = state.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "ActionResolved",
            seq = 1,
            data = UnityEngine.JsonUtility.ToJson(new ActionResolvedMessage
            {
                playerId = 0,
                actionType = (int)ClientActionType.JiaGang,
                tile = ponTile
            })
        });

        var publicMeld = state.Snapshot.seats[0].publicMelds.Single();
        var privateMeld = state.Snapshot.privateSeat.melds.Single();
        runner.Check(disposition == ClientSequenceDisposition.Accepted
            && state.Snapshot.seats[0].concealedTileCount == 4
            && state.Snapshot.privateSeat.concealedHand.Length == 2
            && publicMeld.meldType == (int)MeldType.Kan_Added
            && publicMeld.tileCount == 4
            && privateMeld.meldType == (int)MeldType.Kan_Added,
            "Added-kong projection must upgrade the original pon and consume exactly one private tile.");
    }

    private static void TestOpponentAddedKongUpgradesPon(RegressionRunner runner)
    {
        var pinNine = new TileData(Suit.Pin, 9, 1);
        var state = new OpponentMeldState();
        state.Replace(new[]
        {
            new Meld(MeldType.Pon, new List<TileData>
            {
                pinNine,
                new(Suit.Pin, 9, 1),
                new(Suit.Pin, 9, 1)
            }, 1)
        });

        var upgraded = state.TryApply(MeldType.Kan_Added, new[]
        {
            new TileData(Suit.Pin, 9, 0)
        });
        var meld = state.Melds.Single();

        runner.Check(upgraded
            && state.Melds.Count == 1
            && meld.Type == MeldType.Kan_Added
            && meld.Tiles.Count == 4,
            "An opponent added kong must upgrade the matching pon without adding another meld.");
    }

    private static void TestOpponentAddedKongRejectsMissingPon(RegressionRunner runner)
    {
        var state = new OpponentMeldState();
        state.Replace(new[]
        {
            new Meld(MeldType.Pon, new List<TileData>
            {
                new(Suit.Sou, 3, 2),
                new(Suit.Sou, 3, 2),
                new(Suit.Sou, 3, 2)
            }, 2)
        });

        var upgraded = state.TryApply(MeldType.Kan_Added, new[]
        {
            new TileData(Suit.Dragon, 1, 0)
        });

        runner.Check(!upgraded
            && state.Melds.Count == 1
            && state.Melds.Single().Type == MeldType.Pon
            && state.Melds.Single().Tiles.Count == 3,
            "An opponent added kong without a matching pon must not create an orphan meld.");
    }

    private static void TestRobKongDecisionPhase(RegressionRunner runner)
    {
        var tracker = new NetworkDecisionTracker();
        var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
        var openRobKong = typeof(NetworkDecisionTracker).GetMethod("OpenRobKong");
        if (openRobKong == null)
        {
            runner.Check(false,
                "Rob-kong declarations must open a dedicated network decision instead of a discard response.");
            return;
        }

        var decision = (NetworkDecisionContext)openRobKong.Invoke(tracker, new object[]
        {
            0,
            new TileData(Suit.Pin, 7, 0),
            new[] { 1, 2, 3 },
            deadline
        });
        runner.Check((int)decision.Phase == 2
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 0, ClientActionType.Skip, out var declarerError)
            && declarerError == "NotEligible"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.Pon, out var ponError)
            && ponError == "WrongPhase"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.MingGan, out var mingGanError)
            && mingGanError == "WrongPhase"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.Chi, out var chiError)
            && chiError == "WrongPhase"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.AnGan, out var anGanError)
            && anGanError == "WrongPhase"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.JiaGang, out var jiaGangError)
            && jiaGangError == "WrongPhase"
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.Discard, out var discardError)
            && discardError == "WrongPhase"
            && tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.Hu, out _)
            && !tracker.TrySubmitNetworkAction(decision.DecisionId, 1, ClientActionType.Hu, out var duplicateError)
            && duplicateError == "DuplicateAction"
            && tracker.TrySubmitNetworkAction(decision.DecisionId, 2, ClientActionType.Skip, out _),
            "Rob-kong decisions must admit only Hu-or-skip from non-declarers and retain normal decision safeguards.");
    }

    private static void TestRobKongDeclarationProjection(RegressionRunner runner)
    {
        var ponTile = new SimpleTileData(new TileData(Suit.Sou, 6, 0));
        var pon = new SnapshotMeld
        {
            meldType = (int)MeldType.Pon,
            tileCount = 3,
            tiles = new[] { ponTile, ponTile, ponTile }
        };
        var state = new ClientGameState();
        state.ApplySnapshot(new RoomGameSnapshot
        {
            requestingSeatIndex = 1,
            seats = new[]
            {
                new RoomSnapshotSeat { seatIndex = 0, concealedTileCount = 5, publicMelds = new[] { pon } },
                new RoomSnapshotSeat { seatIndex = 1 },
                new RoomSnapshotSeat { seatIndex = 2 },
                new RoomSnapshotSeat { seatIndex = 3 }
            },
            privateSeat = new SnapshotPrivateSeat { seatIndex = 1 },
            rivers = EmptyRivers()
        }, 0);

        var declaration = new NetworkMessageEnvelope
        {
            type = "AddedKongDeclared",
            seq = 1,
            data = UnityEngine.JsonUtility.ToJson(new DiscardedMessage
            {
                decisionId = 52,
                playerId = 0,
                tile = ponTile,
                decision = new SnapshotDecision
                {
                    decisionId = 52,
                    phase = 2,
                    discardingSeatIndex = 0,
                    targetTile = ponTile,
                    eligibleSeats = new[] { 1, 2, 3 },
                    deadlineUnixMilliseconds = 9999
                }
            })
        };
        var declarationDisposition = state.ApplyEnvelope(declaration);
        var declaredMeld = state.Snapshot.seats[0].publicMelds.Single();
        runner.Check(declarationDisposition == ClientSequenceDisposition.Accepted
            && state.Snapshot.activeDecision != null
            && state.Snapshot.activeDecision.phase == 2
            && state.Snapshot.seats[0].concealedTileCount == 5
            && declaredMeld.meldType == (int)MeldType.Pon
            && state.Snapshot.rivers[0].tiles.Length == 0,
            "An added-kong declaration must publish the rob-kong decision without changing melds, hand counts, or rivers.");

        var confirmationDisposition = state.ApplyEnvelope(new NetworkMessageEnvelope
        {
            type = "ActionResolved",
            seq = 2,
            data = UnityEngine.JsonUtility.ToJson(new ActionResolvedMessage
            {
                playerId = 0,
                actionType = (int)ClientActionType.JiaGang,
                tile = ponTile
            })
        });
        runner.Check(confirmationDisposition == ClientSequenceDisposition.Accepted
            && state.Snapshot.seats[0].publicMelds.Single().meldType == (int)MeldType.Kan_Added,
            "Only an accepted added-kong action may upgrade the declared pon in the client projection.");
    }

    private static void TestRobKongRemoteNotification(RegressionRunner runner)
    {
        var endpoint = new GameEndpoint();
        var remote = new RemotePlayerClient(1, new SeatMessageStream(endpoint, 4));
        var method = typeof(RemotePlayerClient).GetMethod("OnAddedKongDeclared");
        if (method == null)
        {
            runner.Check(false,
                "Remote clients must publish a sequenced added-kong declaration to the local projection.");
            return;
        }

        method.Invoke(remote, new object[] { 0, new TileData(Suit.Wind, 1, 0) });
        var envelope = MessageSerializer.DeserializeEnvelope(endpoint.SentMessages.Single());
        var payload = MessageSerializer.DeserializePayload<DiscardedMessage>(envelope.data);
        runner.Check(envelope.type == "AddedKongDeclared"
            && payload.playerId == 0
            && payload.tile.suit == (int)Suit.Wind
            && payload.tile.value == 1,
            "Remote added-kong declarations must preserve the declaring seat and public target tile.");
    }

    private static List<TileData>[] EmptyTileLists() =>
        Enumerable.Range(0, 4).Select(_ => new List<TileData>()).ToArray();

    private static RoomGameSnapshotSource CreateEmptySnapshotSource(string roomId, RoomState roomState) =>
        new RoomGameSnapshotSource
        {
            RoomId = roomId,
            RoomState = roomState,
            GameMode = GameMode.HalfGame,
            Session = new GameSession(GameMode.HalfGame),
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
            ScoringOptions = Enumerable.Range(0, 4).Select(_ => new ScoringOptions()).ToArray(),
            PeekWallTiles = EmptyTileLists()
        };

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
