using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using UnityEngine;

namespace MahjongGame.Core.Network
{
    /// <summary>The only dedicated-server subscriber to GameEndpoint static events.</summary>
    public sealed class RoomManager : IDisposable
    {
        private readonly int _maxRooms;
        private readonly bool _aiFill;
        private readonly int _messageCacheSize;
        private readonly TimeSpan _reconnectWindow;
        private readonly ConnectionRegistry _connections;
        private readonly IAccountAuthenticator _accountAuthenticator;
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private int _nextRoomId = 1;
        private bool _disposed;

        public RoomManager(
            int maxRooms,
            bool aiFill,
            ConnectionRegistry connections,
            IAccountAuthenticator accountAuthenticator = null,
            int messageCacheSize = SeatMessageStream.DefaultCacheCapacity,
            int reconnectWindowSeconds = ServerBootstrapOptions.DefaultReconnectWindowSeconds)
        {
            _maxRooms = Math.Max(1, maxRooms);
            _aiFill = aiFill;
            _messageCacheSize = Math.Max(1, messageCacheSize);
            _reconnectWindow = TimeSpan.FromSeconds(Math.Max(1, reconnectWindowSeconds));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _accountAuthenticator = accountAuthenticator ?? new DevelopmentAccountAuthenticator();
            GameEndpoint.OnClientConnected += HandleConnected;
            GameEndpoint.OnMessageReceived += HandleMessage;
            GameEndpoint.OnClientDisconnected += HandleDisconnected;
        }

        private void HandleConnected(string connectionId, GameEndpoint endpoint, long generation)
        {
            if (_connections.TryGetSupersededRecord(connectionId, generation, out var superseded))
                RemoveMemberFromRoom(connectionId, superseded.Endpoint, superseded.Generation, "Connection generation superseded.", true);
            _connections.Register(connectionId, endpoint, generation);
        }

        private void HandleMessage(string connectionId, string json, GameEndpoint endpoint, long generation)
        {
            if (!_connections.IsActiveConnection(connectionId, endpoint, generation)) return;
            if (!NetworkMessageLimits.IsWithinClientTextLimit(json))
            {
                SendError(connectionId, endpoint, NetworkErrorCodes.MessageTooLarge, "The network message exceeds the 64 KiB limit.");
                return;
            }

            _connections.Touch(connectionId, endpoint, DateTime.UtcNow);
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.type)) { SendError(connectionId, endpoint, "InvalidMessage", "Malformed network message."); return; }

            if (!_connections.CanSubmitRoomCommands(connectionId, endpoint))
            {
                if (envelope.type != "Hello")
                    SendError(connectionId, endpoint, NetworkErrorCodes.AuthenticationRequired, "Authenticate with Hello before sending room commands.");
                else
                    HandleHello(connectionId, endpoint, MessageSerializer.DeserializePayload<HelloMessage>(envelope.data));
                return;
            }

            try
            {
                switch (envelope.type)
                {
                    case "Hello":
                        SendError(connectionId, endpoint, "AlreadyAuthenticated", "This connection has already completed Hello.");
                        break;
                    case "LeaveRoom": HandleLeaveRoom(connectionId, endpoint, generation); break;
                    case "Heartbeat": Send(endpoint, "HeartbeatAck", new HeartbeatAckMessage()); break;
                    case "Reconnect": HandleReconnect(connectionId, endpoint, MessageSerializer.DeserializePayload<ReconnectMessage>(envelope.data)); break;
                    case "Resync": HandleResync(connectionId, endpoint, MessageSerializer.DeserializePayload<ResyncMessage>(envelope.data)); break;
                    case "CreateRoom": HandleCreateRoom(connectionId, endpoint, MessageSerializer.DeserializePayload<CreateRoomMessage>(envelope.data)); break;
                    case "JoinRoom": HandleJoinRoom(connectionId, endpoint, MessageSerializer.DeserializePayload<JoinRoomMessage>(envelope.data)); break;
                    case "Ready": HandleReady(connectionId, endpoint, MessageSerializer.DeserializePayload<ReadyMessage>(envelope.data)); break;
                    case "SideboardSubmit": HandleSideboardSubmit(connectionId, endpoint, MessageSerializer.DeserializePayload<SideboardSubmitMessage>(envelope.data)); break;
                    case "TalentAction": HandleTalentAction(connectionId, endpoint, MessageSerializer.DeserializePayload<TalentActionMessage>(envelope.data)); break;
                    case "Action": HandleAction(connectionId, endpoint, MessageSerializer.DeserializePayload<ClientActionMessage>(envelope.data)); break;
                    default: SendError(connectionId, endpoint, "UnsupportedMessage", $"Message type '{envelope.type}' is not valid here."); break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RoomManager] Rejected message from {connectionId}: {ex}");
                SendError(connectionId, endpoint, "ServerError", "The request could not be processed.");
            }
        }

        private void HandleHello(string connectionId, GameEndpoint endpoint, HelloMessage hello)
        {
            if (hello == null || !NetworkProtocol.IsSupported(hello.protocolVersion))
            {
                SendError(endpoint, NetworkErrorCodes.ProtocolMismatch, "This server requires protocol version 3.");
                return;
            }

            if (!_accountAuthenticator.TryAuthenticate(hello.username, out var identity, out var authenticationError))
            {
                SendError(endpoint, authenticationError ?? NetworkErrorCodes.InvalidUsername, "The supplied username is invalid.");
                return;
            }

            if (!_connections.TryAuthenticate(connectionId, endpoint, identity, DateTime.UtcNow, out var registryError))
            {
                SendError(endpoint, registryError ?? NetworkErrorCodes.AuthenticationRequired, "The supplied identity cannot use this connection.");
                return;
            }

            Send(endpoint, "HelloAccepted", new HelloAcceptedMessage
            {
                protocolVersion = NetworkProtocol.Version,
                playerId = identity.PlayerId,
                displayName = identity.DisplayName
            });
        }

        /// <summary>Expires connections whose process or network vanished without a WebSocket close event.</summary>
        public void Tick(DateTime utcNow)
        {
            foreach (Room room in _rooms.Values.ToArray()) room.ProcessSideboardDeadline(utcNow);

            foreach (var connectionId in _connections.GetExpiredAuthenticatedConnections(utcNow))
            {
                if (_connections.TryGet(connectionId, out var record))
                {
                    if (!string.IsNullOrEmpty(record.RoomId))
                        RemoveMemberFromRoom(connectionId, record.Endpoint, record.Generation, "Connection heartbeat timed out.", true);
                    else
                        _connections.Remove(connectionId, record.Endpoint, out _);
                }
            }

            ExpireOfflineSeats(utcNow);
        }

        private void HandleCreateRoom(string connectionId, GameEndpoint endpoint, CreateRoomMessage request)
        {
            if (!_connections.TryGet(connectionId, out var record) || !record.IsAuthenticated || !string.IsNullOrEmpty(record.RoomId)) { SendError(connectionId, endpoint, "AlreadyInRoom", "Leave the current room before creating another."); return; }
            if (request == null || request.gameMode < (int)GameMode.Single || request.gameMode > (int)GameMode.FullGame) { SendError(connectionId, endpoint, "InvalidGameMode", "The requested game mode is invalid."); return; }
            var alienationPreset = (AlienationPreset)request.alienationPreset;
            if (!AlienationBudgetPolicy.IsDefined(alienationPreset)) { SendError(connectionId, endpoint, PlayerLoadoutErrorCodes.InvalidAlienationPreset, "The requested alienation preset is invalid."); return; }
            if (!PlayerLoadoutCodec.TryDecode(request.loadout, alienationPreset, out var loadout, out var loadoutError)) { SendLoadoutError(connectionId, endpoint, request.loadout, alienationPreset, loadoutError); return; }
            ExpireOfflineSeats(DateTime.UtcNow);
            if (HasOfflineReservation(record.PlayerId)) { SendError(connectionId, endpoint, NetworkErrorCodes.ReconnectRequired, "Reconnect to the reserved room seat before creating a new room."); return; }
            RemoveClosedRooms();
            if (_rooms.Count >= _maxRooms) { SendError(connectionId, endpoint, "RoomLimitReached", "The server has reached its room limit."); return; }

            string roomId = $"R{_nextRoomId++:D4}";
            var room = new Room(roomId, (GameMode)request.gameMode, alienationPreset, connectionId, _aiFill, _messageCacheSize);
            room.OnClosed += HandleRoomClosed;
            if (!room.TryAddHuman(connectionId, endpoint, record.PlayerId, record.DisplayName, loadout, out int seat))
            {
                room.OnClosed -= HandleRoomClosed;
                room.Dispose();
                SendError(connectionId, endpoint, "RoomCreateFailed", "Could not allocate a room seat.");
                return;
            }
            if (!_connections.BindRoomSeat(connectionId, roomId, seat))
            {
                room.RemoveHuman(connectionId, out _);
                room.OnClosed -= HandleRoomClosed;
                room.Dispose();
                SendError(connectionId, endpoint, "RoomCreateFailed", "Could not bind the allocated room seat.");
                return;
            }
            _rooms.Add(roomId, room);
            SendRoomJoined(room, seat, true, loadout);
        }

        private void HandleJoinRoom(string connectionId, GameEndpoint endpoint, JoinRoomMessage request)
        {
            if (!_connections.TryGet(connectionId, out var record) || !record.IsAuthenticated || !string.IsNullOrEmpty(record.RoomId)) { SendError(connectionId, endpoint, "AlreadyInRoom", "Leave the current room before joining another."); return; }
            if (request == null || string.IsNullOrWhiteSpace(request.roomId) || !_rooms.TryGetValue(request.roomId.Trim(), out var room) || room.State == RoomState.Closed) { SendError(connectionId, endpoint, "RoomNotFound", "The requested room does not exist."); return; }
            if (!PlayerLoadoutCodec.TryDecode(request.loadout, room.AlienationPreset, out var loadout, out var loadoutError)) { SendLoadoutError(connectionId, endpoint, request.loadout, room.AlienationPreset, loadoutError); return; }
            ExpireOfflineSeats(DateTime.UtcNow);
            if (room.State == RoomState.Closed) { SendError(connectionId, endpoint, NetworkErrorCodes.RoomNotFound, "The requested room does not exist."); return; }
            if (HasOfflineReservation(record.PlayerId)) { SendError(connectionId, endpoint, NetworkErrorCodes.ReconnectRequired, "Reconnect to the reserved room seat before joining another room."); return; }
            if (!room.TryAddHuman(connectionId, endpoint, record.PlayerId, record.DisplayName, loadout, out int seat)) { SendError(connectionId, endpoint, "RoomFullOrStarted", "The room is full or has already started."); return; }
            if (!_connections.BindRoomSeat(connectionId, room.RoomId, seat))
            {
                room.RemoveHuman(connectionId, out _);
                SendError(connectionId, endpoint, "RoomJoinFailed", "Could not bind the allocated room seat.");
                return;
            }
            SendRoomJoined(room, seat, false, loadout);
            room.Broadcast("PlayerJoined", new PlayerJoinedMessage { roomId = room.RoomId, seat = room.GetSeatMessage(seat) });
        }

        private void HandleReady(string connectionId, GameEndpoint endpoint, ReadyMessage request)
        {
            if (!TryGetRoomMember(connectionId, endpoint, out var room, out int seatIndex)) return;
            if (request == null || request.phase < (int)ReadyPhase.MatchStart || request.phase > (int)ReadyPhase.NextRound) { SendError(connectionId, endpoint, "InvalidReady", "Ready phase is invalid."); return; }
            if (!room.SetReady(connectionId, (ReadyPhase)request.phase, out string error))
            {
                SendError(connectionId, endpoint, "InvalidReady", error);
                return;
            }

            room.Broadcast("RoomSeatUpdated", new RoomSeatUpdatedMessage
            {
                roomId = room.RoomId,
                seat = room.GetSeatMessage(seatIndex)
            });
        }

        private void HandleAction(string connectionId, GameEndpoint endpoint, ClientActionMessage message)
        {
            if (!TryGetRoomMember(connectionId, endpoint, out var room, out int seatIndex)) return;
            if (!room.SubmitAction(seatIndex, message, out var errorCode))
                SendError(connectionId, endpoint, string.IsNullOrEmpty(errorCode) ? NetworkErrorCodes.InvalidAction : errorCode,
                    "Action is not valid for the current authoritative decision.");
        }

        private void HandleSideboardSubmit(string connectionId, GameEndpoint endpoint, SideboardSubmitMessage message)
        {
            if (!TryGetRoomMember(connectionId, endpoint, out Room room, out int seatIndex)) return;
            if (!room.SubmitSideboard(seatIndex, message, out string errorCode))
            {
                SendError(
                    connectionId,
                    endpoint,
                    string.IsNullOrEmpty(errorCode) ? SideboardErrorCodes.InvalidSelection : errorCode,
                    "The sideboard submission is not valid for the current halftime decision.");
            }
        }

        private void HandleTalentAction(string connectionId, GameEndpoint endpoint, TalentActionMessage message)
        {
            if (!TryGetRoomMember(connectionId, endpoint, out Room room, out int seatIndex)) return;
            room.SubmitTalentAction(seatIndex, message, out _);
        }

        private void HandleLeaveRoom(string connectionId, GameEndpoint endpoint, long generation)
        {
            if (!_connections.TryGet(connectionId, out var record) || !record.IsAuthenticated || string.IsNullOrEmpty(record.RoomId))
            {
                SendError(connectionId, endpoint, "NotInRoom", "Join a room first.");
                return;
            }

            if (!_connections.IsActiveConnection(connectionId, endpoint, generation)
                || !_rooms.TryGetValue(record.RoomId, out var room)) return;
            if (!room.HandleExplicitLeave(record.PlayerId, connectionId, out int seatIndex, out bool shouldClose))
            {
                SendError(connectionId, endpoint, "NotInRoom", "Join a room first.");
                return;
            }

            room.Broadcast("PlayerLeft", new PlayerLeftMessage
            {
                roomId = room.RoomId,
                seatIndex = seatIndex,
                reason = "Player left the room.",
                seat = room.GetSeatMessage(seatIndex)
            });
            _connections.UnbindRoomSeat(connectionId);
            if (shouldClose)
            {
                room.Broadcast("RoomClosed", new RoomClosedMessage { roomId = room.RoomId, reason = "No human player remains online." });
                room.Close();
            }
            else room.AdvanceAfterWaitingMemberChange();
        }

        private bool TryGetRoomMember(string connectionId, GameEndpoint endpoint, out Room room, out int seatIndex)
        {
            room = null; seatIndex = -1;
            if (!_connections.CanSubmitRoomCommands(connectionId, endpoint)
                || !_connections.TryGet(connectionId, out var record)
                || string.IsNullOrEmpty(record.RoomId)
                || !_rooms.TryGetValue(record.RoomId, out room)
                || room.State == RoomState.Closed) { SendError(connectionId, endpoint, "NotInRoom", "Join a room first."); return false; }
            seatIndex = record.SeatIndex;
            return true;
        }

        private void HandleDisconnected(string connectionId, GameEndpoint endpoint, long generation)
        {
            RemoveMemberFromRoom(connectionId, endpoint, generation, "Connection closed.", true);
        }

        private void RemoveMemberFromRoom(string connectionId, GameEndpoint endpoint, long generation, string reason, bool removeConnection)
        {
            if (!_connections.IsActiveConnection(connectionId, endpoint, generation)
                || !_connections.TryGet(connectionId, out var record)) return;

            string roomId = record.RoomId;
            if (!string.IsNullOrEmpty(roomId) && _rooms.TryGetValue(roomId, out var room)
                && room.HandleDisconnect(record.PlayerId, connectionId, endpoint, DateTime.UtcNow, _reconnectWindow, out int seatIndex, out bool shouldClose))
            {
                room.Broadcast("PlayerLeft", new PlayerLeftMessage
                {
                    roomId = roomId,
                    seatIndex = seatIndex,
                    reason = reason,
                    seat = room.GetSeatMessage(seatIndex)
                });
                if (shouldClose)
                {
                    Debug.Log($"[RoomManager] Closing room {roomId}: no human player remains online.");
                    room.Broadcast("RoomClosed", new RoomClosedMessage { roomId = roomId, reason = "No human player remains online." });
                    room.Close();
                }
                else room.AdvanceAfterWaitingMemberChange();
            }

            if (removeConnection)
                _connections.Remove(connectionId, endpoint, out _);
            else
                _connections.UnbindRoomSeat(connectionId);
        }

        private void HandleReconnect(string connectionId, GameEndpoint endpoint, ReconnectMessage request)
        {
            if (!_connections.TryGet(connectionId, out var record) || request == null || string.IsNullOrWhiteSpace(request.roomId)
                || string.IsNullOrWhiteSpace(request.streamId) || !_rooms.TryGetValue(request.roomId.Trim(), out var room)
                || room.State == RoomState.Closed)
            {
                SendReconnectRejected(endpoint, NetworkErrorCodes.RoomNotFound, "The requested room no longer exists.");
                return;
            }

            if (!string.IsNullOrEmpty(record.RoomId))
            {
                SendReconnectRejected(endpoint, "AlreadyInRoom", "Leave the current room before reconnecting.");
                return;
            }

            if (!room.TryReconnect(record.PlayerId, request.streamId, connectionId, endpoint, request.lastSeq, request.hasProjection,
                    DateTime.UtcNow, out int seatIndex, out _, out var errorCode))
            {
                SendReconnectRejected(endpoint, errorCode ?? NetworkErrorCodes.SeatExpired, "The saved room seat can no longer be reclaimed.");
                return;
            }
            if (!_connections.BindRoomSeat(connectionId, room.RoomId, seatIndex))
            {
                SendReconnectRejected(endpoint, NetworkErrorCodes.SeatExpired, "The recovered room seat could not be bound.");
                return;
            }

            room.Broadcast("RoomSeatUpdated", new RoomSeatUpdatedMessage { roomId = room.RoomId, seat = room.GetSeatMessage(seatIndex) });
        }

        private void HandleResync(string connectionId, GameEndpoint endpoint, ResyncMessage request)
        {
            if (request == null || !TryGetRoomMember(connectionId, endpoint, out var room, out int seatIndex)) return;
            string errorCode = null;
            if (!string.Equals(request.roomId, room.RoomId, StringComparison.Ordinal)
                || !room.TryResync(seatIndex, request.streamId, request.lastSeq, endpoint, out _, out errorCode))
            {
                SendReconnectRejected(endpoint, errorCode ?? NetworkErrorCodes.StreamMismatch, "The room stream cannot be synchronized.");
            }
        }

        private void SendRoomJoined(Room room, int seatIndex, bool isHost, TrustedPlayerLoadout loadout)
        {
            room.TrySendToHumanSeat(seatIndex, "RoomJoined", new RoomJoinedMessage
            {
                roomId = room.RoomId,
                seatIndex = seatIndex,
                gameMode = (int)room.GameMode,
                alienationPreset = (int)room.AlienationPreset,
                roomState = (int)room.State,
                isHost = isHost,
                aiFillEnabled = room.AiFillEnabled,
                acceptedSchemaVersion = loadout.SchemaVersion,
                ownTotalAlienation = loadout.TotalAlienation,
                streamId = room.Seats[seatIndex]?.MessageStream?.StreamId,
                seats = room.GetSeatSnapshot()
            });
        }

        private void SendLoadoutError(string connectionId, GameEndpoint endpoint, PlayerLoadoutMessage message,
            AlienationPreset preset, string errorCode)
        {
            int actual = 0;
            int limit = 0;
            int loadoutAlienationPreset = 0;
            int roomAlienationPreset = 0;
            if (errorCode == PlayerLoadoutErrorCodes.AlienationPresetMismatch)
            {
                loadoutAlienationPreset = message?.alienationPreset ?? 0;
                roomAlienationPreset = (int)preset;
            }
            if (errorCode == PlayerLoadoutErrorCodes.AlienationLimitExceeded
                && PlayerLoadoutCodec.TryDecode(message, out var unboundedLoadout, out _))
            {
                actual = unboundedLoadout.TotalAlienation;
                limit = AlienationBudgetPolicy.GetLimit(preset);
            }

            SendError(connectionId, endpoint, errorCode, "The submitted player loadout is invalid.",
                actual, limit, loadoutAlienationPreset, roomAlienationPreset);
        }

        private void SendError(
            string connectionId,
            GameEndpoint endpoint,
            string code,
            string message,
            int actual = 0,
            int limit = 0,
            int loadoutAlienationPreset = 0,
            int roomAlienationPreset = 0)
        {
            if (_connections.TryGet(connectionId, out var record)
                && record.IsAuthenticated
                && !string.IsNullOrEmpty(record.RoomId)
                && _rooms.TryGetValue(record.RoomId, out var room)
                && room.State != RoomState.Closed
                && room.TrySendToHumanSeat(record.SeatIndex, "RoomError", new RoomErrorMessage
                {
                    code = code,
                    message = message,
                    loadoutAlienationPreset = loadoutAlienationPreset,
                    roomAlienationPreset = roomAlienationPreset,
                    actual = actual,
                    limit = limit
                })) return;

            SendError(endpoint, code, message, actual, limit, loadoutAlienationPreset, roomAlienationPreset);
        }

        private static void Send(GameEndpoint endpoint, string type, object payload) => endpoint?.SendMessage(MessageSerializer.Serialize(type, 0, payload));
        private static void SendError(
            GameEndpoint endpoint,
            string code,
            string message,
            int actual = 0,
            int limit = 0,
            int loadoutAlienationPreset = 0,
            int roomAlienationPreset = 0) =>
            endpoint?.SendMessage(MessageSerializer.Serialize("RoomError", 0, new RoomErrorMessage
            {
                code = code,
                message = message,
                loadoutAlienationPreset = loadoutAlienationPreset,
                roomAlienationPreset = roomAlienationPreset,
                actual = actual,
                limit = limit
            }));
        private static void SendReconnectRejected(GameEndpoint endpoint, string code, string message) => endpoint?.SendMessage(MessageSerializer.Serialize("ReconnectRejected", 0, new ReconnectRejectedMessage { code = code, message = message }));

        private void RemoveClosedRooms()
        {
            var ids = new List<string>();
            foreach (var pair in _rooms) if (pair.Value.State == RoomState.Closed) ids.Add(pair.Key);
            foreach (var id in ids) { _rooms[id].Dispose(); _rooms.Remove(id); }
        }

        private bool HasOfflineReservation(string playerId)
        {
            return _rooms.Values.Any(room => room.State != RoomState.Closed && room.HasOfflineReservation(playerId));
        }

        private void ExpireOfflineSeats(DateTime utcNow)
        {
            foreach (var room in new List<Room>(_rooms.Values))
            {
                if (!room.ExpireOfflineSeats(utcNow, out var changedSeats, out bool shouldClose)) continue;
                foreach (var seatIndex in changedSeats)
                    room.Broadcast("PlayerLeft", new PlayerLeftMessage
                    {
                        roomId = room.RoomId,
                        seatIndex = seatIndex,
                        reason = "Reconnect window expired.",
                        seat = room.GetSeatMessage(seatIndex)
                    });
                if (shouldClose)
                {
                    room.Broadcast("RoomClosed", new RoomClosedMessage { roomId = room.RoomId, reason = "No human player remains online." });
                    room.Close();
                }
                else room.AdvanceAfterWaitingMemberChange();
            }
            RemoveClosedRooms();
        }

        private void HandleRoomClosed(Room room)
        {
            if (room == null) return;
            foreach (var seat in room.Seats)
            {
                if (seat == null || seat.IsAi || string.IsNullOrEmpty(seat.ConnectionId)) continue;
                if (_connections.TryGet(seat.ConnectionId, out var record)
                    && ReferenceEquals(record.Endpoint, seat.Endpoint)
                    && record.RoomId == room.RoomId) _connections.UnbindRoomSeat(seat.ConnectionId);
            }
            room.OnClosed -= HandleRoomClosed;
            _rooms.Remove(room.RoomId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GameEndpoint.OnClientConnected -= HandleConnected;
            GameEndpoint.OnMessageReceived -= HandleMessage;
            GameEndpoint.OnClientDisconnected -= HandleDisconnected;
            var rooms = new List<Room>(_rooms.Values);
            foreach (var room in rooms)
            {
                room.OnClosed -= HandleRoomClosed;
                room.Dispose();
            }
            _rooms.Clear();
        }
    }
}
