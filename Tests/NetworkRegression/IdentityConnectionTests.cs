using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

internal static class IdentityConnectionTests
{
    public static void Run(RegressionRunner runner)
    {
        TestProtocolAndIdentity(runner);
        TestHelloHandshake(runner);
        TestConnectionRegistry(runner);
        TestMessageLimitsAndLiveness(runner);
        TestSeatMessageStream(runner);
        TestClientSequenceGate(runner);
        TestServerOptions(runner);
    }

    private static void TestProtocolAndIdentity(RegressionRunner runner)
    {
        runner.Check(NetworkProtocol.IsSupported(4) && !NetworkProtocol.IsSupported(3),
            "Phase 3 payload rollout must accept protocol v4 and reject v3.");
        runner.Check(new HelloMessage().protocolVersion == 4
            && new HeartbeatAckMessage() != null
            && new PlayerLoadoutMessage() != null,
            "Protocol v4 must expose Hello, heartbeat acknowledgement, and loadout DTOs.");

        string publicSeatJson = UnityEngine.JsonUtility.ToJson(new RoomSeatMessage
        {
            seatIndex = 1,
            isOccupied = true,
            displayName = "Opponent"
        });
        runner.Check(!publicSeatJson.Contains("totalAlienation", StringComparison.OrdinalIgnoreCase)
            && !publicSeatJson.Contains("TalentSlot", StringComparison.OrdinalIgnoreCase),
            "Public room seat projections must not serialize exact alienation or hidden talent slots.");

        runner.Check(UsernameIdentityPolicy.TryNormalize("  Alice  ", out var displayName, out var playerId, out var errorCode)
            && displayName == "Alice"
            && !string.IsNullOrWhiteSpace(playerId)
            && errorCode == null,
            "Username normalization must trim display names and derive a stable player ID.");
        runner.Check(UsernameIdentityPolicy.TryNormalize("alice", out _, out var lowerPlayerId, out _)
            && lowerPlayerId == playerId,
            "Username identity must be case-insensitive.");
        runner.Check(!UsernameIdentityPolicy.TryNormalize("   ", out _, out _, out var emptyError)
            && emptyError == NetworkErrorCodes.InvalidUsername,
            "Blank usernames must be rejected.");
        runner.Check(!UsernameIdentityPolicy.TryNormalize(new string('a', 33), out _, out _, out var longError)
            && longError == NetworkErrorCodes.InvalidUsername,
            "Usernames longer than 32 characters must be rejected.");

        var authenticator = new DevelopmentAccountAuthenticator();
        runner.Check(authenticator.TryAuthenticate(" Alice ", out var identity, out var authenticationError)
            && identity.PlayerId == playerId
            && identity.DisplayName == "Alice"
            && authenticationError == null,
            "The development authenticator must expose the normalized identity.");

        var hello = ClientHelloProtocol.Create("Alice");
        runner.Check(hello.protocolVersion == 4 && hello.username == "Alice",
            "Client Hello must carry protocol v4 and the selected username.");
        runner.Check(RoomErrorPresentationPolicy.GetDisplayMessage(new RoomErrorMessage
            {
                code = NetworkErrorCodes.IdentityInUse,
                message = "The identity is already online."
            }) == "IdentityInUse: The identity is already online.",
            "Stable room error codes must remain visible to the lobby.");
    }

    private static void TestHelloHandshake(RegressionRunner runner)
    {
        var accepted = new ClientHelloHandshakePolicy();
        runner.Check(accepted.BeginRoomCommand() == ClientHelloHandshakeAction.SendHello
            && accepted.BeginRoomCommand() == ClientHelloHandshakeAction.AwaitingHello
            && accepted.AcceptHello()
            && accepted.BeginRoomCommand() == ClientHelloHandshakeAction.SendRoomCommand,
            "Room commands must wait for HelloAccepted.");

        var rejected = new ClientHelloHandshakePolicy();
        rejected.BeginRoomCommand();
        runner.Check(rejected.RejectHello() && !rejected.AcceptHello(),
            "A rejected Hello must cancel its queued room command.");
    }

    private static void TestConnectionRegistry(RegressionRunner runner)
    {
        var registry = new ConnectionRegistry();
        var firstEndpoint = new GameEndpoint();
        var secondEndpoint = new GameEndpoint();

        runner.Check(registry.Register("C1", firstEndpoint, 4)
            && registry.Register("C2", secondEndpoint, 1),
            "Physical connections must register with positive ingress generations.");
        runner.Check(!registry.CanSubmitRoomCommands("C1", firstEndpoint),
            "Unauthenticated connections must not submit room commands.");

        var identity = new AuthenticatedIdentity("dev:alice", "Alice");
        runner.Check(registry.TryAuthenticate("C1", firstEndpoint, identity, DateTime.UtcNow, out _)
            && registry.CanSubmitRoomCommands("C1", firstEndpoint),
            "Authenticated current endpoints must submit room commands.");
        runner.Check(!registry.TryAuthenticate("C2", secondEndpoint, identity, DateTime.UtcNow, out var duplicateError)
            && duplicateError == NetworkErrorCodes.IdentityInUse,
            "A concurrently online identity must be rejected.");

        runner.Check(registry.TryGetGeneration("C1", firstEndpoint, out var oldGeneration)
            && oldGeneration == 4,
            "The registry must preserve the ingress generation.");
        var replacementEndpoint = new GameEndpoint();
        runner.Check(registry.TryGetSupersededRecord("C1", 5, out var superseded)
            && ReferenceEquals(superseded.Endpoint, firstEndpoint)
            && registry.Register("C1", replacementEndpoint, 5)
            && !registry.IsActiveConnection("C1", firstEndpoint, 4)
            && registry.IsActiveConnection("C1", replacementEndpoint, 5),
            "A newer endpoint generation must supersede and invalidate the old endpoint.");

        runner.Check(registry.Remove("C1", replacementEndpoint, out _)
            && registry.TryAuthenticate("C2", secondEndpoint, identity, DateTime.UtcNow, out _),
            "Removing the active endpoint must release the logical identity for reclaim.");
    }

    private static void TestMessageLimitsAndLiveness(RegressionRunner runner)
    {
        runner.Check(NetworkMessageLimits.IsWithinClientTextLimit(new string('a', 64 * 1024))
            && !NetworkMessageLimits.IsWithinClientTextLimit(new string('a', (64 * 1024) + 1))
            && NetworkMessageLimits.IsWithinClientTextLimit(new string('\u4e2d', (64 * 1024) / 3))
            && !NetworkMessageLimits.IsWithinClientTextLimit(new string('\u4e2d', ((64 * 1024) / 3) + 1)),
            "Client messages must be limited to 64 KiB by UTF-8 byte count.");

        var connectedAt = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc);
        runner.Check(!ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(9))
            && ConnectionLivenessPolicy.IsExpired(connectedAt, connectedAt.AddSeconds(10)),
            "Server heartbeat expiry must occur at 10 seconds.");
        runner.Check(!ConnectionLivenessPolicy.IsClientAcknowledgementExpired(100f, 109.999f)
            && ConnectionLivenessPolicy.IsClientAcknowledgementExpired(100f, 110f),
            "Client heartbeat acknowledgement expiry must occur at 10 seconds.");
    }

    private static void TestSeatMessageStream(RegressionRunner runner)
    {
        var firstEndpoint = new GameEndpoint();
        var stream = new SeatMessageStream(firstEndpoint, 256);
        for (var sequence = 1; sequence <= 257; sequence++)
        {
            stream.Send("RoomSeatUpdated", new RoomErrorMessage
            {
                code = sequence.ToString(),
                message = "public"
            });
        }

        runner.Check(firstEndpoint.SentMessages.Count == 257
            && MessageSerializer.DeserializeEnvelope(firstEndpoint.SentMessages[0]).seq == 1
            && MessageSerializer.DeserializeEnvelope(firstEndpoint.SentMessages[256]).seq == 257,
            "A seat stream must assign continuous non-zero sequences.");
        runner.Check(!stream.TryGetMessagesAfter(0, out _)
            && stream.TryGetMessagesAfter(1, out var cached)
            && cached.Length == 256
            && cached[0].seq == 2
            && cached[^1].seq == 257,
            "A 256-entry stream cache must reject gaps and replay a contiguous suffix.");

        var reboundEndpoint = new GameEndpoint();
        stream.RebindEndpoint(reboundEndpoint);
        stream.Send("RoomSeatUpdated", new RoomErrorMessage());
        runner.Check(reboundEndpoint.SentMessages.Count == 1
            && MessageSerializer.DeserializeEnvelope(reboundEndpoint.SentMessages[0]).seq == 258,
            "Endpoint rebinding must preserve the room-lifetime sequence.");

        var privateEndpoint = new GameEndpoint();
        var otherEndpoint = new GameEndpoint();
        var privateStream = new SeatMessageStream(privateEndpoint, 4);
        var otherStream = new SeatMessageStream(otherEndpoint, 4);
        privateStream.Send("RoomSeatUpdated", new RoomErrorMessage());
        otherStream.Send("RoomSeatUpdated", new RoomErrorMessage());
        privateStream.Send("GameStart", new GameStartMessage());
        privateStream.Send("TalentInfo", new TalentInfoMessage());
        privateStream.Send("PeekWall", new PeekWallMessage());
        runner.Check(otherStream.TryGetMessagesAfter(0, out var otherMessages)
            && otherMessages.Length == 1
            && otherMessages[0].type == "RoomSeatUpdated",
            "Private seat messages must never enter another seat stream.");
    }

    private static void TestClientSequenceGate(RegressionRunner runner)
    {
        var gate = new ClientSequenceGate();
        runner.Check(gate.Apply(1) == ClientSequenceDisposition.Accepted
            && gate.Apply(1) == ClientSequenceDisposition.IgnoredDuplicate
            && gate.Apply(2) == ClientSequenceDisposition.Accepted
            && gate.Apply(4) == ClientSequenceDisposition.ResyncRequired
            && gate.IsResyncRequired,
            "The client sequence gate must ignore duplicates and require resync on gaps.");

        gate.RestoreBaseline(41);
        runner.Check(gate.LastSequence == 41
            && !gate.IsResyncRequired
            && gate.Apply(41) == ClientSequenceDisposition.IgnoredDuplicate
            && gate.Apply(42) == ClientSequenceDisposition.Accepted,
            "A full snapshot baseline must reset the sequence gate.");
    }

    private static void TestServerOptions(RegressionRunner runner)
    {
        var defaults = ServerBootstrapOptions.Parse(Array.Empty<string>());
        var configured = ServerBootstrapOptions.Parse(new[]
        {
            "--port", "9999",
            "--maxRooms", "3",
            "--aiFill", "false",
            "--reconnectWindowSeconds", "121",
            "--messageCacheSize", "257",
            "--heartbeatTimeoutSeconds", "11"
        });

        runner.Check(defaults.Port == 9876
            && defaults.ReconnectWindowSeconds == 120
            && defaults.MessageCacheSize == 256
            && defaults.HeartbeatTimeoutSeconds == 10,
            "Dedicated Server reconnect defaults must remain stable.");
        runner.Check(configured.Port == 9999
            && configured.MaxRooms == 3
            && !configured.AiFill
            && configured.ReconnectWindowSeconds == 121
            && configured.MessageCacheSize == 257
            && configured.HeartbeatTimeoutSeconds == 11,
            "Dedicated Server reconnect options must parse command-line overrides.");
    }
}
