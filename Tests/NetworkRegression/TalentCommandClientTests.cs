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
        RemoteProxyBindsAndUnbindsTalentPresentationClient(runner);
        SameSceneRecoveryPublishesTalentProjectionOnce(runner);
        CurrentSnapshotTalentProjectionReplaysAfterSceneConstruction(runner);
        RemoteProxyPublishesOrderedTalentPresentationBoundaries(runner);
        BaseActionClearsTalentPresentationWithoutLosingDecision(runner);
        LiveMainTurnDecisionAuthorizesTalentAction(runner);
        TalentActionRejectsWrongPhaseAndResync(runner);
        RemoteProxySerializesSideboardWithoutChangingLocalState(runner);
        LiveSideboardDecisionAuthorizesSubmission(runner);
        SideboardRejectsLockedWrongPhaseAndConnectionRecovery(runner);
        WebSocketClient.ResetForTests();
    }

    private static void CurrentSnapshotTalentProjectionReplaysAfterSceneConstruction(RegressionRunner runner)
    {
        using ClientRoomService service = CreateService(CreateMainTurnSnapshot());
        var local = new TalentPresentationClientStub();
        using var proxy = new ProxyLifetime(new RemoteServerProxy(local, service));
        var presentations = new List<(long DecisionId, TalentActionOption[] Options)>();
        int pickerResets = 0;
        int runtimeFeedback = 0;
        proxy.Value.TalentActionsChanged += (decisionId, options) =>
            presentations.Add((decisionId, options?.ToArray() ?? Array.Empty<TalentActionOption>()));
        proxy.Value.TalentPickerResetRequested += () => pickerResets++;
        proxy.Value.TalentRuntimeEventReceived += _ => runtimeFeedback++;

        proxy.Value.ApplyCurrentTalentRecoveryProjection();
        int recoveryPickerResets = pickerResets;
        int recoveryPresentations = presentations.Count;
        WebSocketClient.Instance.SentMessages.Clear();
        proxy.Value.SubmitAction(ClientAction.Discard(0, new TileData(Suit.Man, 3, 101)));
        ClientActionMessage submitted = GetOnlySentPayload<ClientActionMessage>("Action");

        runner.Check(recoveryPickerResets == 1
            && recoveryPresentations == 1
            && presentations[0].DecisionId == MainDecisionId
            && presentations[0].Options.Single().TalentId == "interception"
            && presentations[0].Options.Single().TargetSeatIndex == 2
            && pickerResets == 2
            && presentations.Count == 2
            && presentations[1].DecisionId == 0
            && submitted?.decisionId == MainDecisionId,
            "a proxy created after snapshot application explicitly replays long decision and talent options once before base submission");
        runner.Check(runtimeFeedback == 0,
            "replaying current talent recovery projection is silent and emits no historical runtime feedback");

        using ClientRoomService wrongSeat = CreateService(CreateMainTurnSnapshot(
            NetworkDecisionPhase.MainTurn, actingSeatIndex: 1));
        using var wrongSeatProxy = new ProxyLifetime(new RemoteServerProxy(new TalentPresentationClientStub(), wrongSeat));
        (long DecisionId, int Count) wrongSeatPresentation = (-1, -1);
        int wrongSeatReset = 0;
        wrongSeatProxy.Value.TalentActionsChanged += (decisionId, options) =>
            wrongSeatPresentation = (decisionId, options?.Count ?? 0);
        wrongSeatProxy.Value.TalentPickerResetRequested += () => wrongSeatReset++;
        wrongSeatProxy.Value.ApplyCurrentTalentRecoveryProjection();
        runner.Check(wrongSeatPresentation == (0L, 0) && wrongSeatReset == 1,
            "current recovery replay publishes no talent actions for a non-owned main decision");

        using ClientRoomService response = CreateService(CreateMainTurnSnapshot(
            NetworkDecisionPhase.Response, actingSeatIndex: 0));
        using var responseProxy = new ProxyLifetime(new RemoteServerProxy(new TalentPresentationClientStub(), response));
        (long DecisionId, int Count) responsePresentation = (-1, -1);
        responseProxy.Value.TalentActionsChanged += (decisionId, options) =>
            responsePresentation = (decisionId, options?.Count ?? 0);
        responseProxy.Value.ApplyCurrentTalentRecoveryProjection();
        runner.Check(responsePresentation == (0L, 0),
            "current recovery replay publishes no talent actions outside the own main-turn phase");
    }

    private static void BaseActionClearsTalentPresentationWithoutLosingDecision(RegressionRunner runner)
    {
        using ClientRoomService service = CreateLiveRoomService();
        var local = new TalentPresentationClientStub();
        using var proxy = new ProxyLifetime(new RemoteServerProxy(local, service));
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
            tile = new SimpleTileData { suit = (int)Suit.Man, value = 2, isValid = true }
        }));
        WebSocketClient.Instance.SentMessages.Clear();
        int clearCount = 0;
        proxy.Value.TalentActionsChanged += (decisionId, options) =>
        {
            if (decisionId == 0 && (options?.Count ?? 0) == 0) clearCount++;
        };

        proxy.Value.SubmitAction(ClientAction.Discard(0,
            new TileData(Suit.Man, 2, 100)));
        ClientActionMessage sent = GetOnlySentPayload<ClientActionMessage>("Action");

        runner.Check(clearCount == 1 && sent?.decisionId == MainDecisionId,
            "base action submission clears supplemental controls without erasing its authoritative decision ID");
    }

    private static void RemoteProxyPublishesOrderedTalentPresentationBoundaries(RegressionRunner runner)
    {
        using ClientRoomService service = CreateService(CreateMainTurnSnapshot());
        var local = new TalentPresentationClientStub();
        using var proxy = new ProxyLifetime(new RemoteServerProxy(local, service));
        var presentations = new List<(long DecisionId, int Count)>();
        int resets = 0;
        proxy.Value.TalentActionsChanged += (decisionId, options) =>
            presentations.Add((decisionId, options?.Count ?? 0));
        proxy.Value.TalentPickerResetRequested += () => resets++;

        WebSocketClient.Instance.Receive(MessageSerializer.Serialize(
            "TalentPrivateState", 1, new TalentPrivateStateMessage
            {
                ownerSeatIndex = 0,
                talents = Array.Empty<SnapshotOwnTalent>(),
                availableTalentActions = new[]
                {
                    new SnapshotTalentActionOption { talentId = "sheathed_edge" }
                }
            }));
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize(
            "Discarded", 2, new DiscardedMessage
            {
                playerId = 0,
                decisionId = MainDecisionId + 1,
                tile = new SimpleTileData { suit = (int)Suit.Man, value = 1, isValid = true }
            }));

        runner.Check(presentations.Count == 2
            && presentations[0] == (MainDecisionId, 1)
            && presentations[1] == (0L, 0)
            && resets == 1,
            "ordered TalentPrivateState opens supplemental actions and Discarded clears actions plus picker");
    }

    private static void RemoteProxyBindsAndUnbindsTalentPresentationClient(RegressionRunner runner)
    {
        using ClientRoomService service = CreateService(CreateMainTurnSnapshot());
        var presentationClient = new TalentPresentationClientStub();
        var replacementClient = new TalentPresentationClientStub();
        var proxy = new RemoteServerProxy(presentationClient, service);
        runner.Check(presentationClient.BindCount == 1 && presentationClient.LastProxy == proxy,
            "RemoteServerProxy binds the local supplemental talent presentation at construction");

        int pickerResets = 0;
        var presentations = new List<(long DecisionId, int Count)>();
        proxy.TalentPickerResetRequested += () => pickerResets++;
        proxy.TalentActionsChanged += (decisionId, options) =>
            presentations.Add((decisionId, options?.Count ?? 0));
        proxy.SetLocalClient(replacementClient);
        proxy.ApplyCurrentTalentRecoveryProjection();
        runner.Check(presentationClient.UnbindCount == 1
                     && replacementClient.BindCount == 1
                     && pickerResets == 1
                     && presentations.Count == 1
                     && presentations[0] == (MainDecisionId, 1),
            "a newly bound local presentation receives one explicit replay even when the authoritative projection is unchanged");

        proxy.Cleanup();
        runner.Check(presentationClient.UnbindCount == 1
                     && replacementClient.UnbindCount == 1
                     && replacementClient.LastProxy == proxy,
            "RemoteServerProxy unbinds the local supplemental talent presentation during cleanup");
    }

    private static void SameSceneRecoveryPublishesTalentProjectionOnce(RegressionRunner runner)
    {
        using ClientRoomService service = CreateLiveRoomService();
        using var proxy = new ProxyLifetime(new RemoteServerProxy(new TalentPresentationClientStub(), service));
        int pickerResets = 0;
        int actionPresentations = 0;
        int runtimeFeedback = 0;
        proxy.Value.TalentPickerResetRequested += () => pickerResets++;
        proxy.Value.TalentActionsChanged += (_, _) => actionPresentations++;
        proxy.Value.TalentRuntimeEventReceived += _ => runtimeFeedback++;

        // Mirrors NetworkManager -> GameManager after the same service has applied the envelope.
        service.ReconnectSnapshotApplied += _ => proxy.Value.ApplyCurrentTalentRecoveryProjection();
        WebSocketClient.Instance.Receive(MessageSerializer.Serialize("ReconnectState", 0, new ReconnectStateMessage
        {
            baselineSeq = 2,
            snapshot = CreateMainTurnSnapshot(),
            missedMessages = Array.Empty<NetworkMessageEnvelope>()
        }));

        runner.Check(pickerResets == 1 && actionPresentations == 1,
            "same-scene recovery has one presentation owner and publishes one picker reset plus one action refresh");
        runner.Check(runtimeFeedback == 0,
            "same-scene recovery does not replay historical runtime feedback");
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

    private sealed class TalentPresentationClientStub : IPlayerClient, ITalentActionPresentationClient
    {
        public int BindCount { get; private set; }
        public int UnbindCount { get; private set; }
        public RemoteServerProxy LastProxy { get; private set; }

        public int PlayerId => 0;
        public System.Threading.CancellationToken TurnCancellationToken { get; set; }

        public void BindTalentActionPresentation(RemoteServerProxy proxy)
        {
            BindCount++;
            LastProxy = proxy;
        }

        public void UnbindTalentActionPresentation(RemoteServerProxy proxy)
        {
            UnbindCount++;
            LastProxy = proxy;
        }

        public void OnGameStart(List<TileData> startingHand) { }
        public void OnTileDrawn(TileData drawnTile) { }
        public void OnPlayerDrawn(int playerId) { }
        public void OnTurnWithoutDraw() { }
        public void OnWallCountChanged(int remainingCount) { }
        public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile) { }
        public void OnAddedKongDeclared(int playerId, TileData targetTile) { }
        public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null) { }
        public void OnDrawGame() { }
        public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown) { }
        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex) { }
        public void OnSessionEnd(int[] finalScores) { }
        public void OnTimeout(TileData autoDiscardedTile) { }
        public void OnTalentInfo(ScoringOptions scoringOptions) { }
        public void OnPeekWallTiles(List<TileData> topTiles) { }
    }
}
