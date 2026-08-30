using System;
using System.Linq;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

internal static class ClientConnectionSettingsTests
{
    private const string OnlineAddress = "ws://123.207.13.148:9876/game";
    private const string LocalAddress = "ws://127.0.0.1:9876/game";

    public static void Run(RegressionRunner runner)
    {
        TestEndpointSelection(runner);
        TestSwitchConnectsAndRequiresHelloAcceptance(runner);
        TestTimeoutRetryAndFailureDiagnostics(runner);
        TestSocketFailureDuringAuthentication(runner);
        TestHeartbeatAcknowledgementsMeasureRoundTripTime(runner);
        TestActiveRoomAndRecoveryRejectServerChanges(runner);
        TestRoomCommandsUseSelectedServer(runner);
    }

    private static void TestEndpointSelection(RegressionRunner runner)
    {
        runner.Check(
            ClientServerEndpointPolicy.Resolve(ClientServerEnvironment.Online) == OnlineAddress
            && ClientServerEndpointPolicy.Resolve(ClientServerEnvironment.Local) == LocalAddress,
            "Endpoint policy must resolve online and local server choices to their real game routes.");
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

    private static WebSocketClient CreateClient()
    {
        WebSocketClient.ResetForTests();
        UnityEngine.Time.unscaledTime = 0f;
        return new WebSocketClient { AutoCompleteConnect = false };
    }

    private static string[] GetSentTypes(WebSocketClient client) => client.SentMessages
        .Select(MessageSerializer.DeserializeEnvelope)
        .Where(envelope => envelope != null)
        .Select(envelope => envelope.type)
        .ToArray();
}
