using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;

internal static class BattleExitTests
{
    public static void Run(RegressionRunner runner)
    {
        TestSendCompletionAndDuplicateExit(runner);
        TestTimeoutAndFailure(runner);
        TestDisconnectedRecovery(runner);
        TestRecoveryReturnDoesNotWaitForHello(runner);
        TestExitWhileSnapshotIsPending(runner);
        TestLateMessagesAndNewJoin(runner);
        TestTerminalRaces(runner);
        TestServerExplicitDeparture(runner);
    }

    private static Task Leave(ClientRoomService service) => service.LeaveRoomForLobbyAsync();

    private static void TestSendCompletionAndDuplicateExit(RegressionRunner runner)
    {
        using var fixture = new Fixture();
        fixture.Client.AutoCompleteSends = false;
        Task first = Leave(fixture.Service);
        Task second = Leave(fixture.Service);
        fixture.Service.LeaveRoomOrAbandonRecovery();
        runner.Check(ReferenceEquals(first, second) && !first.IsCompleted && fixture.Count("LeaveRoom") == 1,
            "Repeated exit calls share one pending Task and send LeaveRoom exactly once.");
        runner.Check(!fixture.Tickets.TryLoad(out _) && !fixture.Service.HasRoom
            && !fixture.Service.CanSubmitCommands && !fixture.Service.JoinRoom("new-room", "User")
            && fixture.Client.ReadyState == WebSocketSharp.WebSocketState.Open,
            "Exit immediately drops recovery and room authority, blocks new commands, and keeps the sending socket open.");
        fixture.Client.CompleteNextSend();
        runner.Check(first.IsCompletedSuccessfully && fixture.Client.ReadyState == WebSocketSharp.WebSocketState.Open
            && fixture.Service.CanSubmitCommands && fixture.Count("LeaveRoom") == 1,
            "Successful LeaveRoom delivery completes exit and preserves the healthy selected-server socket.");
    }

    private static void TestTimeoutAndFailure(RegressionRunner runner)
    {
        using (var fixture = new Fixture())
        {
            fixture.Client.AutoCompleteSends = false;
            Task pending = Leave(fixture.Service);
            fixture.Service.Tick(104.99f);
            runner.Check(!pending.IsCompleted && fixture.Client.ReadyState == WebSocketSharp.WebSocketState.Open,
                "Exit keeps the original transport until the five-second deadline.");
            fixture.Service.Tick(105f);
            runner.Check(pending.IsCompletedSuccessfully && !fixture.Service.HasRoom
                && !fixture.Tickets.TryLoad(out _) && fixture.Client.ReadyState == WebSocketSharp.WebSocketState.Closed,
                "At five seconds exit closes the ambiguous socket and completes without waiting for Hello.");
            fixture.Client.CompleteNextSend();
            runner.Check(fixture.Count("LeaveRoom") == 1 && !fixture.Service.HasRoom,
                "A late LeaveRoom send callback after timeout cannot restart exit or room state.");
        }
        using (var fixture = new Fixture())
        {
            fixture.Client.SendCompletionResults.Enqueue(false);
            Task failed = Leave(fixture.Service);
            runner.Check(failed.IsCompletedSuccessfully && !fixture.Tickets.TryLoad(out _)
                && fixture.Client.ReadyState == WebSocketSharp.WebSocketState.Closed,
                "Failed LeaveRoom delivery abandons recovery, closes the ambiguous socket, and completes exit.");
        }
    }

    private static void TestDisconnectedRecovery(RegressionRunner runner)
    {
        using var fixture = new Fixture();
        fixture.Client.Disconnect();
        runner.Check(fixture.Service.IsConnectionRecoveryRequired, "Fixture enters disconnected recovery.");
        Task exit = Leave(fixture.Service);
        fixture.Service.Tick(200f);
        fixture.Send("ReconnectState", 0, Snapshot());
        runner.Check(exit.IsCompletedSuccessfully && !fixture.Service.HasRoom
            && !fixture.Service.IsConnectionRecoveryRequired && fixture.Count("LeaveRoom") == 0
            && fixture.Count("Reconnect") == 0 && !fixture.Tickets.TryLoad(out _),
            "Disconnected exit completes immediately and cancels old retries and recovery messages.");
    }

    private static void TestLateMessagesAndNewJoin(RegressionRunner runner)
    {
        using var fixture = new Fixture();
        int ready = 0;
        int recovered = 0;
        fixture.Service.RoomReady += () => ready++;
        fixture.Service.ReconnectSnapshotApplied += _ => recovered++;
        fixture.Client.AutoCompleteSends = false;
        Task exit = Leave(fixture.Service);
        fixture.Send("RoomReady", 2, new RoomReadyMessage());
        fixture.Send("ReconnectState", 0, Snapshot());
        fixture.Send("RoomJoined", 1, Joined("old-room", "old-stream"));
        fixture.Client.CompleteNextSend();
        fixture.Send("RoomReady", 2, new RoomReadyMessage());
        fixture.Send("ReconnectState", 0, Snapshot());
        runner.Check(exit.IsCompletedSuccessfully && !fixture.Service.HasRoom && ready == 0 && recovered == 0
            && !fixture.Tickets.TryLoad(out _) && !fixture.Service.IsResyncRequired,
            "Late joined, ready, and recovery messages cannot resurrect an exited room or trigger scene entry.");

        fixture.Client.AutoCompleteSends = true;
        bool joined = fixture.Service.JoinRoom("new-room", "User");
        fixture.Send("RoomReady", 2, new RoomReadyMessage());
        fixture.Send("RoomJoined", 1, Joined("old-room", "old-stream"));
        runner.Check(!fixture.Service.HasRoom && ready == 0,
            "Starting a new join does not admit an old binding or ready message before the new RoomJoined.");
        fixture.Send("RoomJoined", 1, Joined("new-room", "new-stream"));
        fixture.Send("RoomReady", 2, new RoomReadyMessage());
        runner.Check(joined && fixture.Service.RoomId == "new-room" && ready == 1
            && fixture.Tickets.TryLoad(out var ticket) && ticket.roomId == "new-room",
            "An explicit new join opens a fresh message lineage after exit.");
        fixture.Send("ReconnectState", 0, Snapshot());
        fixture.Send("RoomJoined", 1, Joined("old-room", "old-stream"));
        runner.Check(fixture.Service.RoomId == "new-room" && recovered == 0,
            "An old recovery or binding delivered after a new room joined cannot replace that new room.");
    }

    private static void TestRecoveryReturnDoesNotWaitForHello(RegressionRunner runner)
    {
        foreach (bool timeout in new[] { false, true })
        {
            WebSocketClient.ResetForTests();
            UnityEngine.Time.unscaledTime = 0f;
            var client = new WebSocketClient { AutoCompleteConnect = false };
            var tickets = new InMemoryClientReconnectTicketStore();
            tickets.Save(new ClientReconnectTicket
            {
                serverAddress = "ws://recovered", username = "User", roomId = "old-room", streamId = "old-stream"
            });
            using var service = new ClientRoomService("ws://selected", tickets);
            service.ReconnectSavedRoom("User");
            service.Tick(0f);
            client.CompleteConnect();
            client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
            client.Receive(MessageSerializer.Serialize("ReconnectState", 0, Snapshot()));
            client.AutoCompleteSends = false;
            Task exit = Leave(service);
            if (timeout) service.Tick(5f);
            else client.CompleteNextSend();
            runner.Check(exit.IsCompletedSuccessfully && client.ActiveAddress == "ws://selected"
                && client.ReadyState == WebSocketSharp.WebSocketState.Connecting && !tickets.TryLoad(out _)
                && service.SelectedServerAddress == "ws://selected" && !service.CanSubmitCommands,
                $"Recovered-server exit completes before selected-server Hello, preserving selection (timeout={timeout}).");
        }
    }

    private static void TestExitWhileSnapshotIsPending(RegressionRunner runner)
    {
        WebSocketClient.ResetForTests();
        UnityEngine.Time.unscaledTime = 0f;
        var client = new WebSocketClient();
        var tickets = new InMemoryClientReconnectTicketStore();
        tickets.Save(new ClientReconnectTicket
        {
            serverAddress = "ws://selected", username = "User", roomId = "old-room", streamId = "old-stream"
        });
        using var service = new ClientRoomService("ws://selected", tickets);
        service.ReconnectSavedRoom("User");
        service.Tick(0f);
        client.Receive(MessageSerializer.Serialize("HelloAccepted", 0, new HelloAcceptedMessage()));
        client.AutoCompleteSends = false;
        Task exit = Leave(service);
        runner.Check(!exit.IsCompleted && client.SentMessages.Count(json => MessageSerializer.DeserializeEnvelope(json)?.type == "LeaveRoom") == 1,
            "Authenticated recovery with a Reconnect already sent leaves the potentially rebound room while its snapshot is still pending.");
        client.Receive(MessageSerializer.Serialize("ReconnectState", 0, Snapshot()));
        client.CompleteNextSend();
        runner.Check(exit.IsCompletedSuccessfully && !service.HasRoom && !tickets.TryLoad(out _),
            "The in-flight recovery snapshot cannot undo exit while its LeaveRoom delivery completes.");
    }

    private static void TestTerminalRaces(RegressionRunner runner)
    {
        using (var fixture = new Fixture())
        {
            fixture.Send("SessionEnd", 2, new SessionEndMessage());
            fixture.Send("RoomClosed", 3, new RoomClosedMessage { roomId = "old-room" });
            Task exit = Leave(fixture.Service);
            runner.Check(exit.IsCompletedSuccessfully && fixture.Count("LeaveRoom") == 0,
                "A completed session followed by room closure exits without redundant LeaveRoom.");
        }
        using (var fixture = new Fixture())
        {
            fixture.Send("RoomClosed", 2, new RoomClosedMessage { roomId = "old-room" });
            fixture.Send("SessionEnd", 3, new SessionEndMessage());
            Task exit = Leave(fixture.Service);
            runner.Check(exit.IsCompletedSuccessfully && fixture.Count("LeaveRoom") == 0,
                "RoomClosed followed by a late session result exits without redundant LeaveRoom.");
        }
        using (var fixture = new Fixture())
        {
            fixture.Client.AutoCompleteSends = false;
            Task exit = Leave(fixture.Service);
            fixture.Send("SessionEnd", 2, new SessionEndMessage());
            fixture.Send("RoomClosed", 3, new RoomClosedMessage { roomId = "old-room" });
            fixture.Client.CompleteNextSend();
            runner.Check(exit.IsCompletedSuccessfully && fixture.Count("LeaveRoom") == 1 && !fixture.Service.HasRoom,
                "Terminal messages racing an in-flight leave neither resurrect state nor duplicate the send.");
        }
    }

    private static RoomJoinedMessage Joined(string roomId, string streamId) => new RoomJoinedMessage
    {
        roomId = roomId, streamId = streamId, seatIndex = 0,
        roomState = (int)RoomState.InRound, seats = new RoomSeatMessage[4]
    };

    private static void TestServerExplicitDeparture(RegressionRunner runner)
    {
        using (var game = new ServerFixture(twoHumans: true))
        {
            string playerId = game.Room.Seats[0].PlayerId;
            string streamId = game.Room.Seats[0].MessageStream.StreamId;
            game.Send(0, "LeaveRoom", new LeaveRoomMessage());
            runner.Check(game.Room.State == RoomState.InRound && game.Room.Seats[0].IsAi
                && game.Room.Seats[0].ControlState == RoomSeatControlState.PermanentAi
                && !game.Room.TryReconnect(playerId, streamId, "returning", new GameEndpoint(), 0, false,
                    DateTime.UtcNow, out _, out _, out var error) && error == NetworkErrorCodes.SeatExpired,
                "An explicit in-round leave permanently gives the seat to AI and cannot reclaim it by reconnecting.");
        }
        using (var game = new ServerFixture(twoHumans: false))
        {
            game.Send(0, "LeaveRoom", new LeaveRoomMessage());
            runner.Check(game.Room.State == RoomState.Closed
                && game.Connections.TryGet("human-0", out var connection) && connection.IsAuthenticated
                && string.IsNullOrEmpty(connection.RoomId),
                "The final human's explicit leave closes the real Room but keeps the authenticated WebSocket reusable.");
        }
        using (var game = new ServerFixture(twoHumans: true))
        {
            game.Room.GameServer.CompleteDrawRound();
            game.Send(1, "Ready", new ReadyMessage { phase = (int)ReadyPhase.NextRound });
            game.Send(0, "LeaveRoom", new LeaveRoomMessage());
            runner.Check(game.Room.State == RoomState.InRound && game.Room.Session.TotalRoundsPlayed == 1
                && game.Room.Seats[0].IsAi,
                "Leaving while the other human is ready for the next round advances the real Room immediately.");
        }
        using (var game = new ServerFixture(twoHumans: true))
        {
            for (int round = 1; round <= 4; round++)
            {
                game.Room.GameServer.CompleteDrawRound();
                if (round == 4) break;
                game.Send(0, "Ready", new ReadyMessage { phase = (int)ReadyPhase.NextRound });
                game.Send(1, "Ready", new ReadyMessage { phase = (int)ReadyPhase.NextRound });
            }
            var startedEnvelope = game.Endpoints[1].SentMessages.Select(MessageSerializer.DeserializeEnvelope)
                .Last(message => message.type == "SideboardStarted");
            var started = MessageSerializer.DeserializePayload<SideboardStartedMessage>(startedEnvelope.data);
            game.Send(0, "LeaveRoom", new LeaveRoomMessage());
            game.Send(1, "SideboardSubmit", new SideboardSubmitMessage
            {
                decisionId = started.decisionId, activeTalentIds = Array.Empty<string>()
            });
            runner.Check(game.Room.State == RoomState.InRound && game.Room.Session.TotalRoundsPlayed == 4
                && game.Room.Seats[0].IsAi,
                "Leaving during sideboard locks the departed seat legally and lets the remaining human start the second half.");
        }
    }

    private sealed class ServerFixture : IDisposable
    {
        public readonly ConnectionRegistry Connections = new();
        public readonly RoomManager Manager;
        public readonly GameEndpoint[] Endpoints = { new(), new() };
        public readonly Room Room;
        public ServerFixture(bool twoHumans)
        {
            Manager = new RoomManager(4, true, Connections);
            var loadout = PlayerLoadoutCodec.CreateMessage(DeckConfig.CreateStandard(), new TalentSlotConfig());
            int count = twoHumans ? 2 : 1;
            for (int seat = 0; seat < count; seat++)
            {
                Endpoints[seat].Connect($"human-{seat}", 1);
                Send(seat, "Hello", new HelloMessage { username = $"ExitUser{seat}", protocolVersion = NetworkProtocol.Version });
            }
            Send(0, "CreateRoom", new CreateRoomMessage
            {
                gameMode = (int)GameMode.HalfGame, alienationPreset = (int)AlienationPreset.Standard, loadout = loadout
            });
            var rooms = (Dictionary<string, Room>)typeof(RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Manager);
            Room = rooms.Values.Single();
            if (twoHumans) Send(1, "JoinRoom", new JoinRoomMessage { roomId = Room.RoomId, loadout = loadout });
            for (int seat = 0; seat < count; seat++) Send(seat, "Ready", new ReadyMessage { phase = (int)ReadyPhase.MatchStart });
            for (int seat = 0; seat < count; seat++) Send(seat, "Ready", new ReadyMessage { phase = (int)ReadyPhase.GameSceneLoaded });
            if (Room.State != RoomState.InRound) throw new InvalidOperationException("Could not start server exit fixture.");
        }
        public void Send<T>(int seat, string type, T value) => Endpoints[seat].Receive($"human-{seat}", 1, MessageSerializer.Serialize(type, 0, value));
        public void Dispose() => Manager.Dispose();
    }

    private static ReconnectStateMessage Snapshot() => new ReconnectStateMessage
    {
        baselineSeq = 2,
        snapshot = new RoomGameSnapshot
        {
            roomId = "old-room", requestingSeatIndex = 0, roomState = (int)RoomState.InRound,
            seats = Array.Empty<RoomSnapshotSeat>(), scores = new int[4]
        },
        missedMessages = Array.Empty<NetworkMessageEnvelope>()
    };

    private sealed class Fixture : IDisposable
    {
        public readonly WebSocketClient Client;
        public readonly InMemoryClientReconnectTicketStore Tickets = new();
        public readonly ClientRoomService Service;
        public Fixture()
        {
            WebSocketClient.ResetForTests();
            UnityEngine.Time.unscaledTime = 100f;
            Client = new WebSocketClient();
            Service = new ClientRoomService("ws://selected", Tickets);
            Service.TrySwitchServer("ws://selected", "User");
            Send("HelloAccepted", 0, new HelloAcceptedMessage());
            Send("RoomJoined", 1, Joined("old-room", "old-stream"));
        }
        public void Send<T>(string type, int sequence, T value) => Client.Receive(MessageSerializer.Serialize(type, sequence, value));
        public int Count(string type) => Client.SentMessages.Count(json => MessageSerializer.DeserializeEnvelope(json)?.type == type);
        public void Dispose() => Service.Dispose();
    }
}
