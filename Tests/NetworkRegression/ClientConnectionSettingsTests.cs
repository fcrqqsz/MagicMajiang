using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.UI;

internal static class ClientConnectionSettingsTests
{
    private const string OnlineAddress = "ws://123.207.13.148:9876/game";
    private const string LocalAddress = "ws://127.0.0.1:9876/game";

    public static void Run(RegressionRunner runner)
    {
        TestEndpointSelection(runner);
        TestStartupConnectionDecision(runner);
        TestLobbyPresentationMapping(runner);
        TestLobbyProfileSettingsMigration(runner);
        TestSwitchConnectsAndRequiresHelloAcceptance(runner);
        TestTimeoutRetryAndFailureDiagnostics(runner);
        TestSocketFailureDuringAuthentication(runner);
        TestHeartbeatAcknowledgementsMeasureRoundTripTime(runner);
        TestFailedHeartbeatSendDoesNotPolluteRtt(runner);
        TestReadyLobbyDisconnectPublishesDisconnected(runner);
        TestServerSwitchDoesNotPublishIntermediateDisconnect(runner);
        TestRoomCommandDuringConnectionDoesNotReplaceHandshake(runner);
        TestLobbyRoomErrorKeepsReadyDiagnostics(runner);
        TestActiveRoomAndRecoveryRejectServerChanges(runner);
        TestRoomCommandsUseSelectedServer(runner);
        TestRecoveredRoomReturnsToSelectedServerAfterLeaveDelivery(runner);
        TestFailedRecoveredLeaveDeliveryReturnsToSelectedServer(runner);
        TestTerminalRecoveryReturnsToSelectedServer(runner);
    }

    private static void TestEndpointSelection(RegressionRunner runner)
    {
        runner.Check(
            ClientServerEndpointPolicy.Resolve(ClientServerEnvironment.Online) == OnlineAddress
            && ClientServerEndpointPolicy.Resolve(ClientServerEnvironment.Local) == LocalAddress,
            "Endpoint policy must resolve online and local server choices to their real game routes.");
    }

    private static void TestStartupConnectionDecision(RegressionRunner runner)
    {
        runner.Check(
            ClientServerStartupPolicy.InitialEnvironment == ClientServerEnvironment.Online
            && !ClientServerStartupPolicy.ShouldConnectSelectedServerAfterLogin(reconnectStarted: true)
            && ClientServerStartupPolicy.ShouldConnectSelectedServerAfterLogin(reconnectStarted: false),
            "Startup must default to Online and only connect the selected server after login when saved-room recovery did not start.");
    }

    private static void TestLobbyPresentationMapping(RegressionRunner runner)
    {
        var ready = new ClientConnectionDiagnostics(
            LocalAddress,
            ClientConnectionPhase.Ready,
            protocolVersion: 10,
            roundTripTimeMilliseconds: 42,
            lastCheckedUtc: new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc),
            lastError: null);
        LobbyConnectionPresentationView readyView = LobbyConnectionPresentationPolicy.Build(ready);

        runner.Check(readyView.StatusText == "已就绪"
            && readyView.StatusClass == "connection-status-green"
            && readyView.SocketPhaseText == "套接字阶段：已就绪"
            && readyView.HandshakeText == "v10 握手：已完成"
            && readyView.RoundTripTimeText == "RTT：42 ms"
            && readyView.LastCheckedText == "上次检查：2026-08-30 12:34:56 UTC"
            && readyView.LastErrorText == "最近错误：--"
            && readyView.ReadinessText == "就绪状态：可创建或加入房间"
            && !readyView.ActionsDisabled,
            "lobby diagnostics maps a Ready v10 snapshot to the green, actionable summary without inferring transport state");

        var connecting = new ClientConnectionDiagnostics(
            OnlineAddress,
            ClientConnectionPhase.Connecting,
            protocolVersion: 10,
            roundTripTimeMilliseconds: null,
            lastCheckedUtc: null,
            lastError: "socket opening");
        LobbyConnectionPresentationView connectingView = LobbyConnectionPresentationPolicy.Build(connecting);

        runner.Check(connectingView.StatusText == "连接中"
            && connectingView.StatusClass == "connection-status-yellow"
            && connectingView.HandshakeText == "v10 握手：等待套接字连接"
            && connectingView.RoundTripTimeText == "RTT：--"
            && connectingView.LastCheckedText == "上次检查：--"
            && connectingView.LastErrorText == "最近错误：socket opening"
            && connectingView.ActionsDisabled,
            "lobby diagnostics disables switching and retesting only while the authoritative phase is Connecting");

        var failed = new ClientConnectionDiagnostics(
            LocalAddress,
            ClientConnectionPhase.Failed,
            protocolVersion: 10,
            roundTripTimeMilliseconds: null,
            lastCheckedUtc: null,
            lastError: "Connection refused.");
        LobbyConnectionPresentationView failedView = LobbyConnectionPresentationPolicy.Build(failed);

        runner.Check(failedView.StatusText == "连接失败"
            && failedView.StatusClass == "connection-status-red"
            && failedView.HandshakeText == "v10 握手：失败"
            && failedView.ReadinessText == "就绪状态：请检查服务器后重试"
            && !failedView.ActionsDisabled,
            "a failed selection remains retryable and exposes the raw latest error through the presentation model");

        LobbyConnectionPresentationView disconnectedView = LobbyConnectionPresentationPolicy.Build(
            new ClientConnectionDiagnostics(OnlineAddress, ClientConnectionPhase.Disconnected, 10, null, null, null));
        LobbyConnectionPresentationView authenticatingView = LobbyConnectionPresentationPolicy.Build(
            new ClientConnectionDiagnostics(OnlineAddress, ClientConnectionPhase.Authenticating, 10, null, null, null));
        runner.Check(disconnectedView.StatusClass == "connection-status-gray"
            && authenticatingView.StatusClass == "connection-status-blue"
            && authenticatingView.ActionsDisabled,
            "the remaining authoritative phases map to gray Disconnected and blue Authenticating status pills");
    }

    private static void TestLobbyProfileSettingsMigration(RegressionRunner runner)
    {
        const string legacyProfileJson = "{\"Settings\":{\"MasterVolume\":0.35,\"MusicVolume\":0.5,\"SFXVolume\":0.75,\"DebugMode\":true,\"SelectedGameMode\":3}}";
        PlayerProfile profile = UnityEngine.JsonUtility.FromJson<PlayerProfile>(legacyProfileJson);
        profile.Normalize();

        string persistedProfileJson = UnityEngine.JsonUtility.ToJson(profile);
        runner.Check(profile.Settings.SelectedGameMode == 3
            && !persistedProfileJson.Contains("MasterVolume", StringComparison.Ordinal)
            && !persistedProfileJson.Contains("MusicVolume", StringComparison.Ordinal)
            && !persistedProfileJson.Contains("SFXVolume", StringComparison.Ordinal)
            && !persistedProfileJson.Contains("DebugMode", StringComparison.Ordinal),
            "legacy profile JSON ignores removed audio and debug fields while retaining the selected game mode");
    }

    private static void TestSwitchConnectsAndRequiresHelloAcceptance(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());
        var phases = new System.Collections.Generic.List<ClientConnectionPhase>();
        service.ConnectionDiagnosticsChanged += diagnostics => phases.Add(diagnostics.Phase);

        runner.Check(service.TrySwitchServer(LocalAddress, " Alice ")
            && service.SelectedServerAddress == LocalAddress
            && service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Connecting
            && client.ConnectAddresses.Single() == LocalAddress,
            "Switching servers must preserve the selected target and begin a new connection to it.");

        client.CompleteConnect();
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Authenticating
            && GetSentTypes(client).SequenceEqual(new[] { "Hello" }),
            "A connected selected server must receive Hello before it becomes ready.");

        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Ready
            && phases.SequenceEqual(new[]
            {
                ClientConnectionPhase.Connecting,
                ClientConnectionPhase.Authenticating,
                ClientConnectionPhase.Ready
            }),
            "Connection diagnostics must publish Connecting, Authenticating, and Ready only after HelloAccepted.");
    }

    private static void TestTimeoutRetryAndFailureDiagnostics(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());

        UnityEngine.Time.unscaledTime = 0f;
        runner.Check(service.TrySwitchServer(LocalAddress, "RetryUser"),
            "An idle client must allow a selected server test.");
        service.Tick(10f);
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Failed
            && service.ConnectionDiagnostics.LastError.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            && service.SelectedServerAddress == LocalAddress,
            "A non-recovery Hello attempt must fail after ten seconds without changing the selected server.");

        runner.Check(service.TryReconnectSelectedServer("RetryUser")
            && service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Connecting
            && client.ConnectAddresses.Count == 2,
            "Retrying the selected server after failure must start a fresh attempt on the same target.");

        client.Fail("Connection refused.");
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Failed
            && service.ConnectionDiagnostics.LastError.Contains("Connection refused.", StringComparison.Ordinal),
            "A socket error during a selected-server test must publish Failed diagnostics.");

        runner.Check(service.TryReconnectSelectedServer("RetryUser")
            && client.ConnectAddresses.Count == 3,
            "A failed selected-server test must remain retryable without a fallback target.");
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("RoomError", 0, new RoomErrorMessage
        {
            code = NetworkErrorCodes.ProtocolMismatch,
            message = "Client protocol is unsupported."
        }));
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Failed
            && service.ConnectionDiagnostics.LastError.Contains(NetworkErrorCodes.ProtocolMismatch, StringComparison.Ordinal)
            && service.SelectedServerAddress == LocalAddress,
            "Hello rejection and protocol mismatch must publish Failed without automatically falling back.");
    }

    private static void TestSocketFailureDuringAuthentication(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());

        service.TrySwitchServer(LocalAddress, "CloseUser");
        client.CompleteConnect();
        client.Disconnect();
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Failed
            && service.ConnectionDiagnostics.LastError.Contains("closed", StringComparison.OrdinalIgnoreCase),
            "A close during authentication must publish Failed diagnostics instead of leaving a connecting state.");
    }

    private static void TestHeartbeatAcknowledgementsMeasureRoundTripTime(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());

        UnityEngine.Time.unscaledTime = 0f;
        service.TrySwitchServer(LocalAddress, "LatencyUser");
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        UnityEngine.Time.unscaledTime = 1f;
        service.Tick(1f);
        UnityEngine.Time.unscaledTime = 4f;
        service.Tick(4f);
        client.Receive(MessageSerializer.Serialize("HeartbeatAck", 0, new HeartbeatAckMessage()));

        runner.Check(service.ConnectionDiagnostics.RoundTripTimeMilliseconds == 3000
            && service.ConnectionDiagnostics.LastCheckedUtc.HasValue,
            "The oldest unacknowledged heartbeat must determine RTT when acknowledgements arrive in order.");

        service.TrySwitchServer(OnlineAddress, "LatencyUser");
        runner.Check(!service.ConnectionDiagnostics.RoundTripTimeMilliseconds.HasValue,
            "Switching servers must clear stale heartbeat latency diagnostics.");
    }

    private static void TestFailedHeartbeatSendDoesNotPolluteRtt(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = CreateReadyService(client, "SendFailureUser");

        client.SendCompletionResults.Enqueue(false);
        UnityEngine.Time.unscaledTime = 1f;
        service.Tick(1f);
        UnityEngine.Time.unscaledTime = 4f;
        service.Tick(4f);
        UnityEngine.Time.unscaledTime = 6f;
        client.Receive(MessageSerializer.Serialize("HeartbeatAck", 0, new HeartbeatAckMessage()));

        runner.Check(service.ConnectionDiagnostics.RoundTripTimeMilliseconds == 2000,
            "A failed asynchronous heartbeat send must not enter the RTT acknowledgement queue.");
    }

    private static void TestReadyLobbyDisconnectPublishesDisconnected(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = CreateReadyService(client, "LobbyDisconnectUser");

        client.Disconnect();
        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Disconnected,
            "A post-Ready disconnect without a room must stop exposing stale Ready diagnostics.");
    }

    private static void TestServerSwitchDoesNotPublishIntermediateDisconnect(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = CreateReadyService(client, "SwitchingUser");
        var phases = new System.Collections.Generic.List<ClientConnectionPhase>();
        service.ConnectionDiagnosticsChanged += diagnostics => phases.Add(diagnostics.Phase);

        service.TrySwitchServer(OnlineAddress, "SwitchingUser");
        runner.Check(phases.SequenceEqual(new[] { ClientConnectionPhase.Connecting }),
            "An intentional selected-server switch must begin Connecting without reporting its replaced socket as disconnected.");
    }

    private static void TestRoomCommandDuringConnectionDoesNotReplaceHandshake(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());

        service.TrySwitchServer(LocalAddress, "InflightUser");
        runner.Check(!service.QueryRoomList("InflightUser")
            && client.ConnectAddresses.Count == 1,
            "A room command during a selected-server handshake must be rejected without replacing its connection attempt.");
    }

    private static void TestLobbyRoomErrorKeepsReadyDiagnostics(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = CreateReadyService(client, "LobbyErrorUser");

        service.QueryRoomList("LobbyErrorUser");
        client.Receive(MessageSerializer.Serialize("RoomError", 0, new RoomErrorMessage
        {
            code = NetworkErrorCodes.RoomNotFound,
            message = "The requested room no longer exists."
        }));

        runner.Check(service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Ready,
            "A normal lobby RoomError after HelloAccepted must not invalidate healthy connection diagnostics.");
    }

    private static void TestActiveRoomAndRecoveryRejectServerChanges(RegressionRunner runner)
    {
        var client = CreateClient();
        var tickets = new InMemoryClientReconnectTicketStore();
        using var service = new ClientRoomService(OnlineAddress, tickets);

        service.TrySwitchServer(OnlineAddress, "RoomUser");
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.Receive(MessageSerializer.Serialize("RoomJoined", 1, new RoomJoinedMessage
        {
            roomId = "R1000",
            streamId = "stream-1",
            seatIndex = 0,
            seats = new RoomSeatMessage[4]
        }));

        runner.Check(!service.TrySwitchServer(LocalAddress, "RoomUser")
            && !service.TryReconnectSelectedServer("RoomUser")
            && service.SelectedServerAddress == OnlineAddress,
            "An active room must retain connection ownership and reject server changes or retests.");

        client.Disconnect();
        runner.Check(service.IsConnectionRecoveryRequired
            && !service.TrySwitchServer(LocalAddress, "RoomUser"),
            "Recovery ownership must reject server changes until the room recovery is resolved.");
    }

    private static void TestRoomCommandsUseSelectedServer(RegressionRunner runner)
    {
        var client = CreateClient();
        using var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());

        service.TrySwitchServer(LocalAddress, "Browser");
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.Disconnect();

        runner.Check(service.QueryRoomList("Browser")
            && client.ConnectAddresses.Last() == LocalAddress,
            "Room list commands must reconnect to the selected server rather than accept an address override.");

        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        runner.Check(GetSentTypes(client).Last() == "QueryRoomList",
            "A room command queued for a selected-server reconnect must be sent only after its new Hello is accepted.");
    }

    private static void TestRecoveredRoomReturnsToSelectedServerAfterLeaveDelivery(RegressionRunner runner)
    {
        var client = CreateClient();
        var tickets = new InMemoryClientReconnectTicketStore();
        tickets.Save(new ClientReconnectTicket
        {
            serverAddress = LocalAddress,
            username = "RecoveryUser",
            roomId = "R2000",
            streamId = "recovery-stream"
        });
        using var service = new ClientRoomService(OnlineAddress, tickets);

        runner.Check(service.ReconnectSavedRoom("RecoveryUser"),
            "A matching saved ticket must start recovery before any selected-server connection.");
        service.Tick(0f);
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.Receive(MessageSerializer.Serialize("ReconnectState", 0, new ReconnectStateMessage
        {
            baselineSeq = 0,
            snapshot = CreateRecoveredRoomSnapshot(),
            missedMessages = Array.Empty<NetworkMessageEnvelope>()
        }));

        client.AutoCompleteSends = false;
        service.LeaveRoom();

        bool selectedTargetPreserved = service.SelectedServerAddress == OnlineAddress;
        bool recoverySocketPreserved = client.ActiveAddress == LocalAddress;
        bool leaveWasSent = GetSentTypes(client).Last() == "LeaveRoom";
        bool commandsBlocked = !service.QueryRoomList("RecoveryUser");
        bool noReplacementConnection = client.ConnectAddresses.Count == 1;
        runner.Check(selectedTargetPreserved
            && recoverySocketPreserved
            && leaveWasSent
            && commandsBlocked
            && noReplacementConnection,
            "Leaving a recovered room must block commands and keep its socket active until LeaveRoom delivery succeeds.");

        client.CompleteNextSend();
        runner.Check(
            client.ConnectAddresses.Last() == OnlineAddress
            && service.SelectedServerAddress == OnlineAddress
            && !tickets.TryLoad(out _),
            "After LeaveRoom delivery, recovery must clear its ticket and return to the selected server without replacing the selection.");

        runner.Check(!service.TryReconnectSelectedServer("RecoveryUser"),
            "Server switching and retesting must remain blocked until the selected-server return handshake completes.");

        client.Fail("Selected target refused the return handshake.");
        runner.Check(
            service.ConnectionDiagnostics.Phase == ClientConnectionPhase.Failed
            && service.CanSubmitCommands
            && service.TryReconnectSelectedServer("RecoveryUser"),
            "A selected-target return handshake failure must settle the transition so the user can retry.");
    }

    private static void TestFailedRecoveredLeaveDeliveryReturnsToSelectedServer(RegressionRunner runner)
    {
        var client = CreateClient();
        var tickets = new InMemoryClientReconnectTicketStore();
        tickets.Save(new ClientReconnectTicket
        {
            serverAddress = LocalAddress,
            username = "LeaveFailureUser",
            roomId = "R2100",
            streamId = "leave-failure-stream"
        });
        using var service = new ClientRoomService(OnlineAddress, tickets);

        service.ReconnectSavedRoom("LeaveFailureUser");
        service.Tick(0f);
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.Receive(MessageSerializer.Serialize("ReconnectState", 0, new ReconnectStateMessage
        {
            baselineSeq = 0,
            snapshot = CreateRecoveredRoomSnapshot(),
            missedMessages = Array.Empty<NetworkMessageEnvelope>()
        }));

        client.SendCompletionResults.Enqueue(false);
        client.AutoCompleteSends = false;
        service.LeaveRoom();
        client.CompleteNextSend();
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));

        runner.Check(
            client.ConnectAddresses.Last() == OnlineAddress
            && service.SelectedServerAddress == OnlineAddress
            && !tickets.TryLoad(out _)
            && service.CanSubmitCommands,
            "A failed recovered-room LeaveRoom delivery must deterministically return to the selected target without leaving command authority latched.");
    }

    private static void TestTerminalRecoveryReturnsToSelectedServer(RegressionRunner runner)
    {
        var client = CreateClient();
        var tickets = new InMemoryClientReconnectTicketStore();
        tickets.Save(new ClientReconnectTicket
        {
            serverAddress = LocalAddress,
            username = "TerminalRecoveryUser",
            roomId = "R3000",
            streamId = "terminal-recovery-stream"
        });
        using var service = new ClientRoomService(OnlineAddress, tickets);
        ClientRecoveryProgress terminalProgress = null;
        service.RecoveryProgressChanged += progress =>
        {
            if (progress.Stage == ClientRecoveryStage.TerminalFailure) terminalProgress = progress;
        };

        service.ReconnectSavedRoom("TerminalRecoveryUser");
        service.Tick(0f);
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.Receive(MessageSerializer.Serialize("ReconnectRejected", 0, new ReconnectRejectedMessage
        {
            code = NetworkErrorCodes.RoomNotFound,
            message = "The saved room is unavailable."
        }));

        runner.Check(
            client.ConnectAddresses.SequenceEqual(new[] { LocalAddress, OnlineAddress })
            && service.SelectedServerAddress == OnlineAddress
            && !tickets.TryLoad(out _)
            && terminalProgress?.Stage == ClientRecoveryStage.TerminalFailure,
            "A terminal recovery rejection must clear the authoritative ticket, preserve terminal UI progress, and return to the selected server.");
    }

    private static RoomGameSnapshot CreateRecoveredRoomSnapshot() => new RoomGameSnapshot
    {
        roomId = "R2000",
        roomState = (int)RoomState.WaitingForPlayers,
        gameMode = (int)GameMode.Single,
        alienationPreset = (int)AlienationPreset.Standard,
        requestingSeatIndex = 0,
        seats = new[]
        {
            new RoomSnapshotSeat { seatIndex = 0, isOccupied = true, isOnline = true, displayName = "RecoveryUser" },
            new RoomSnapshotSeat { seatIndex = 1 },
            new RoomSnapshotSeat { seatIndex = 2 },
            new RoomSnapshotSeat { seatIndex = 3 }
        },
        knownTalents = Array.Empty<SnapshotKnownTalent>(),
        scores = new int[4],
        rivers = Array.Empty<SeatRiverSnapshot>(),
        privateSeat = new SnapshotPrivateSeat
        {
            seatIndex = 0,
            concealedHand = Array.Empty<SimpleTileData>(),
            melds = Array.Empty<SnapshotMeld>(),
            scoringOptions = new SnapshotScoringOptions(),
            peekWallTiles = Array.Empty<SimpleTileData>(),
            knownOpponentHands = Array.Empty<SnapshotKnownHand>(),
            ownTalents = Array.Empty<SnapshotOwnTalent>(),
            availableTalentActions = Array.Empty<SnapshotTalentActionOption>()
        },
        sideboard = new SnapshotSideboardState { seatLocked = Array.Empty<bool>() },
        result = new RoundResultSnapshot { fanDetails = Array.Empty<string>() }
    };

    private static WebSocketClient CreateClient()
    {
        WebSocketClient.ResetForTests();
        UnityEngine.Time.unscaledTime = 0f;
        return new WebSocketClient { AutoCompleteConnect = false };
    }

    private static ClientRoomService CreateReadyService(WebSocketClient client, string username)
    {
        var service = new ClientRoomService(OnlineAddress, new InMemoryClientReconnectTicketStore());
        service.TrySwitchServer(LocalAddress, username);
        client.CompleteConnect();
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        return service;
    }

    private static string[] GetSentTypes(WebSocketClient client) => client.SentMessages
        .Select(MessageSerializer.DeserializeEnvelope)
        .Where(envelope => envelope != null)
        .Select(envelope => envelope.type)
        .ToArray();
}
