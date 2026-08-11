using MahjongGame.Core;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class TalentCommandClientTests
{
    private const long MainDecisionId = 701;
    private const long SideboardDecisionId = 801;

    public static void Run(RegressionRunner runner)
    {
        RemoteProxySerializesTalentActionFromAuthoritativeMainDecision(runner);
        LiveMainTurnDecisionAuthorizesTalentAction(runner);
        TalentActionRejectsWrongPhaseAndResync(runner);
        RemoteProxySerializesSideboardWithoutChangingLocalState(runner);
        LiveSideboardDecisionAuthorizesSubmission(runner);
        SideboardRejectsLockedWrongPhaseAndConnectionRecovery(runner);
        WebSocketClient.ResetForTests();
    }

    private static void LiveMainTurnDecisionAuthorizesTalentAction(RegressionRunner runner)
    {
        using ClientRoomService service = CreateLiveRoomService();
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("TileDrawn", 3, new TileDrawnMessage
        {
            decisionId = MainDecisionId,
            decision = new SnapshotDecision
            {
                decisionId = MainDecisionId,
                phase = (int)NetworkDecisionPhase.MainTurn,
                actingSeatIndex = 0,
                controllerSeatIndex = 0,
                eligibleSeats = Array.Empty<int>(),
                submittedSeats = Array.Empty<int>(),
                deadlineUnixMilliseconds = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
            },
            tile = new SimpleTileData { suit = (int)Suit.Man, value = 1, isValid = true }
        }));
        WebSocketClient.Instance.SentMessages.Clear();

        bool submitted = service.SubmitTalentAction(new TalentActionOption { TalentId = "sheathed_edge" });
        TalentActionMessage message = GetOnlySentPayload<TalentActionMessage>("TalentAction");

        runner.Check(service.RoomState == RoomState.LoadingGameScene
                     && submitted
                     && message?.decisionId == MainDecisionId,
            "a live ordered own-main-turn decision authorizes TalentAction without relying on stale room-state metadata");
    }

    private static void RemoteProxySerializesTalentActionFromAuthoritativeMainDecision(RegressionRunner runner)
    {
        using ClientRoomService service = CreateService(CreateMainTurnSnapshot());
        using var proxy = new ProxyLifetime(new RemoteServerProxy(new SimpleAIClient(0, null), service));
        var option = new TalentActionOption
        {
            TalentId = "interception",
            TargetSeatIndex = 2,
            TargetTalentId = "sheathed_edge"
        };
        RoomGameSnapshot before = service.GameState.Snapshot;

        bool submitted = proxy.Value.SubmitTalentAction(option);

        NetworkMessageEnvelope envelope = GetOnlySentEnvelope();
        TalentActionMessage message = envelope?.type == "TalentAction"
            ? MessageSerializer.DeserializePayload<TalentActionMessage>(envelope.data)
            : null;
        RoomGameSnapshot after = service.GameState.Snapshot;
        runner.Check(
            submitted
            && envelope?.seq == 0
            && message?.decisionId == MainDecisionId
            && message.talentId == "interception"
            && message.targetSeatIndex == 2
            && message.targetTalentId == "sheathed_edge",
            "RemoteServerProxy serializes a typed TalentAction with the current authoritative own-main-turn decision");
        runner.Check(
            before.activeDecision.decisionId == after.activeDecision.decisionId
            && before.privateSeat.availableTalentActions.Length == after.privateSeat.availableTalentActions.Length
            && after.privateSeat.availableTalentActions.Single().talentId == "interception",
            "sending a TalentAction request does not mutate the local authoritative projection");
    }

    private static void TalentActionRejectsWrongPhaseAndResync(RegressionRunner runner)
    {
        using (ClientRoomService wrongPhase = CreateService(CreateMainTurnSnapshot(
                   NetworkDecisionPhase.Response,
                   actingSeatIndex: 1)))
        {
            bool submitted = wrongPhase.SubmitTalentAction(new TalentActionOption { TalentId = "interception" });
            runner.Check(!submitted && WebSocketClient.Instance.SentMessages.Count == 0,
                "ClientRoomService rejects TalentAction outside the authoritative own main-turn phase");
        }

        using (ClientRoomService resync = CreateService(CreateMainTurnSnapshot()))
        {
            WebSocketClient.Instance.Receive(MessageSerializer.Serialize(
                "HeartbeatAck", 2, new HeartbeatAckMessage()));
            bool submitted = resync.SubmitTalentAction(new TalentActionOption { TalentId = "interception" });
            runner.Check(resync.IsResyncRequired
                         && !submitted
                         && WebSocketClient.Instance.SentMessages.Count == 0,
                "ClientRoomService rejects TalentAction while ordered state requires resynchronization");
        }
    }

    private static void RemoteProxySerializesSideboardWithoutChangingLocalState(RegressionRunner runner)
    {
        using ClientRoomService service = CreateService(CreateSideboardSnapshot());
        using var proxy = new ProxyLifetime(new RemoteServerProxy(new SimpleAIClient(0, null), service));
        RoomGameSnapshot before = service.GameState.Snapshot;
        SnapshotSideboardState sideboardBefore = service.Sideboard;

        bool submitted = proxy.Value.SubmitSideboard(new[] { "peek", "draw_reward" });

        NetworkMessageEnvelope envelope = GetOnlySentEnvelope();
        SideboardSubmitMessage message = envelope?.type == "SideboardSubmit"
            ? MessageSerializer.DeserializePayload<SideboardSubmitMessage>(envelope.data)
            : null;
        RoomGameSnapshot after = service.GameState.Snapshot;
        SnapshotSideboardState sideboardAfter = service.Sideboard;
        runner.Check(
            submitted
            && envelope?.seq == 0
            && message?.decisionId == SideboardDecisionId
            && message.activeTalentIds.SequenceEqual(new[] { "peek", "draw_reward" }),
            "RemoteServerProxy serializes only the requested active IDs with the current unlocked sideboard decision");
        runner.Check(
            sideboardBefore.isActive == sideboardAfter.isActive
            && sideboardBefore.ownLocked == sideboardAfter.ownLocked
            && sideboardBefore.decisionId == sideboardAfter.decisionId
            && before.privateSeat.ownTalents.Select(talent => (talent.talentId, talent.isActive))
                .SequenceEqual(after.privateSeat.ownTalents.Select(talent => (talent.talentId, talent.isActive))),
            "sending a SideboardSubmit request does not change the local active talent set or lock state");
    }

    private static void LiveSideboardDecisionAuthorizesSubmission(RegressionRunner runner)
    {
        using ClientRoomService service = CreateLiveRoomService();
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("SideboardStarted", 3, new SideboardStartedMessage
        {
            decisionId = SideboardDecisionId,
            deadlineUnixMilliseconds = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeMilliseconds(),
            carriedMainTalentIds = Array.Empty<string>(),
            carriedReserveTalentIds = Array.Empty<string>(),
            currentActiveTalentIds = new[] { "draw_reward" }
        }));
        WebSocketClient.Instance.SentMessages.Clear();

        bool submitted = service.SubmitSideboard(new[] { "draw_reward" });
        SideboardSubmitMessage message = GetOnlySentPayload<SideboardSubmitMessage>("SideboardSubmit");

        runner.Check(service.RoomState == RoomState.LoadingGameScene
                     && submitted
                     && message?.decisionId == SideboardDecisionId,
            "a live active unlocked sideboard decision authorizes submission without relying on stale room-state metadata");
    }

    private static void SideboardRejectsLockedWrongPhaseAndConnectionRecovery(RegressionRunner runner)
    {
        using (ClientRoomService locked = CreateService(CreateSideboardSnapshot(ownLocked: true)))
        {
            bool submitted = locked.SubmitSideboard(new[] { "peek" });
            runner.Check(!submitted && WebSocketClient.Instance.SentMessages.Count == 0,
                "ClientRoomService rejects SideboardSubmit after the authoritative own seat is locked");
        }

        RoomGameSnapshot inactiveSideboard = CreateSideboardSnapshot();
        inactiveSideboard.sideboard.isActive = false;
        using (ClientRoomService wrongPhase = CreateService(inactiveSideboard))
        {
            bool submitted = wrongPhase.SubmitSideboard(new[] { "peek" });
            runner.Check(!submitted && WebSocketClient.Instance.SentMessages.Count == 0,
                "ClientRoomService rejects SideboardSubmit outside the authoritative sideboard room phase");
        }

        var store = new InMemoryClientReconnectTicketStore();
        store.Save(new ClientReconnectTicket
        {
            serverAddress = "ws://test",
            username = "client-command-recovery",
            roomId = "client-command-room",
            streamId = "client-command-stream"
        });
        using (ClientRoomService recovery = CreateService(CreateSideboardSnapshot(), store))
        {
            bool recoveryStarted = recovery.ReconnectSavedRoom();
            bool submitted = recovery.SubmitSideboard(new[] { "peek" });
            runner.Check(recoveryStarted
                         && recovery.IsConnectionRecoveryRequired
                         && !submitted
                         && WebSocketClient.Instance.SentMessages.Count == 0,
                "ClientRoomService rejects SideboardSubmit while connection recovery owns command authority");
        }
    }

    private static ClientRoomService CreateLiveRoomService()
    {
        WebSocketClient.ResetForTests();
        var service = new ClientRoomService("ws://test", new InMemoryClientReconnectTicketStore());
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("RoomJoined", 1, new RoomJoinedMessage
        {
            roomId = "client-command-room",
            streamId = "client-command-stream",
            seatIndex = 0,
            gameMode = (int)GameMode.HalfGame,
            alienationPreset = (int)AlienationPreset.Standard,
            roomState = (int)RoomState.WaitingForMatchReady,
            seats = Array.Empty<RoomSeatMessage>()
        }));
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("RoomReady", 2, new RoomReadyMessage
        {
            roomId = "client-command-room"
        }));
        return service;
    }

    private static ClientRoomService CreateService(
        RoomGameSnapshot snapshot,
        IClientReconnectTicketStore store = null)
    {
        WebSocketClient.ResetForTests();
        var service = new ClientRoomService("ws://test", store ?? new InMemoryClientReconnectTicketStore());
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("ReconnectState", 0, new ReconnectStateMessage
        {
            baselineSeq = 0,
            snapshot = snapshot,
            missedMessages = Array.Empty<NetworkMessageEnvelope>()
        }));
        WebSocketClient.Instance.SentMessages.Clear();
        return service;
    }

    private static RoomGameSnapshot CreateMainTurnSnapshot(
        NetworkDecisionPhase phase = NetworkDecisionPhase.MainTurn,
        int actingSeatIndex = 0)
    {
        RoomGameSnapshot snapshot = CreateSnapshot(RoomState.InRound);
        snapshot.activeDecision = new SnapshotDecision
        {
            decisionId = MainDecisionId,
            phase = (int)phase,
            actingSeatIndex = actingSeatIndex,
            controllerSeatIndex = actingSeatIndex,
            deadlineUnixMilliseconds = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
            eligibleSeats = Array.Empty<int>(),
            submittedSeats = Array.Empty<int>()
        };
        snapshot.privateSeat.availableTalentActions = new[]
        {
            new SnapshotTalentActionOption
            {
                talentId = "interception",
                targetSeatIndex = 2,
                targetTalentId = "sheathed_edge"
            }
        };
        return snapshot;
    }

    private static RoomGameSnapshot CreateSideboardSnapshot(
        bool ownLocked = false,
        RoomState roomState = RoomState.WaitingForSideboard)
    {
        RoomGameSnapshot snapshot = CreateSnapshot(roomState);
        snapshot.sideboard = new SnapshotSideboardState
        {
            isActive = true,
            decisionId = SideboardDecisionId,
            deadlineUnixMilliseconds = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeMilliseconds(),
            ownLocked = ownLocked,
            seatLocked = new[] { ownLocked, true, true, true }
        };
        snapshot.privateSeat.ownTalents = new[]
        {
            new SnapshotOwnTalent { talentId = "draw_reward", isActive = true },
            new SnapshotOwnTalent { talentId = "peek", isActive = false }
        };
        return snapshot;
    }

    private static RoomGameSnapshot CreateSnapshot(RoomState roomState) => new RoomGameSnapshot
    {
        roomId = "client-command-room",
        roomState = (int)roomState,
        gameMode = (int)GameMode.HalfGame,
        alienationPreset = (int)AlienationPreset.Standard,
        requestingSeatIndex = 0,
        seats = Array.Empty<RoomSnapshotSeat>(),
        knownTalents = Array.Empty<SnapshotKnownTalent>(),
        scores = new[] { 0, 0, 0, 0 },
        privateSeat = new SnapshotPrivateSeat
        {
            seatIndex = 0,
            concealedHand = Array.Empty<SimpleTileData>(),
            melds = Array.Empty<SnapshotMeld>(),
            peekWallTiles = Array.Empty<SimpleTileData>(),
            ownTalents = Array.Empty<SnapshotOwnTalent>(),
            availableTalentActions = Array.Empty<SnapshotTalentActionOption>()
        },
        rivers = Array.Empty<SeatRiverSnapshot>(),
        sideboard = new SnapshotSideboardState { seatLocked = new bool[4] },
        result = new RoundResultSnapshot()
    };

    private static NetworkMessageEnvelope GetOnlySentEnvelope()
    {
        return WebSocketClient.Instance.SentMessages.Count == 1
            ? MessageSerializer.DeserializeEnvelope(WebSocketClient.Instance.SentMessages[0])
            : null;
    }

    private static T GetOnlySentPayload<T>(string type)
    {
        NetworkMessageEnvelope envelope = GetOnlySentEnvelope();
        return envelope?.type == type
            ? MessageSerializer.DeserializePayload<T>(envelope.data)
            : default;
    }

    private sealed class ProxyLifetime : IDisposable
    {
        public RemoteServerProxy Value { get; }

        public ProxyLifetime(RemoteServerProxy value) => Value = value;

        public void Dispose() => Value.Cleanup();
    }
}
