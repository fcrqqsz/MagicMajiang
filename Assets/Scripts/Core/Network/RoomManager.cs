using System;
using System.Collections.Generic;
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
        private readonly ConnectionRegistry _connections;
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private int _nextRoomId = 1;
        private bool _disposed;

        public RoomManager(int maxRooms, bool aiFill, ConnectionRegistry connections)
        {
            _maxRooms = Math.Max(1, maxRooms);
            _aiFill = aiFill;
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            GameEndpoint.OnClientConnected += HandleConnected;
            GameEndpoint.OnMessageReceived += HandleMessage;
            GameEndpoint.OnClientDisconnected += HandleDisconnected;
        }

        private void HandleConnected(string connectionId, GameEndpoint endpoint)
        {
            _connections.Register(connectionId, endpoint);
        }

        private void HandleMessage(string connectionId, string json, GameEndpoint endpoint)
        {
            if (!_connections.TryGet(connectionId, out var record) || record.Endpoint != endpoint) return;
            _connections.Touch(connectionId, DateTime.UtcNow);
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.type)) { SendError(endpoint, "InvalidMessage", "Malformed network message."); return; }

            try
            {
                switch (envelope.type)
                {
                    case "Hello":
                        var hello = MessageSerializer.DeserializePayload<HelloMessage>(envelope.data);
                        _connections.SetNickname(connectionId, hello?.nickname);
                        break;
                    case "LeaveRoom": HandleLeaveRoom(connectionId, endpoint); break;
                    case "Heartbeat": break;
                    case "CreateRoom": HandleCreateRoom(connectionId, endpoint, MessageSerializer.DeserializePayload<CreateRoomMessage>(envelope.data)); break;
                    case "JoinRoom": HandleJoinRoom(connectionId, endpoint, MessageSerializer.DeserializePayload<JoinRoomMessage>(envelope.data)); break;
                    case "Ready": HandleReady(connectionId, endpoint, MessageSerializer.DeserializePayload<ReadyMessage>(envelope.data)); break;
                    case "Action": HandleAction(connectionId, endpoint, MessageSerializer.DeserializePayload<ClientActionMessage>(envelope.data)); break;
                    default: SendError(endpoint, "UnsupportedMessage", $"Message type '{envelope.type}' is not valid here."); break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RoomManager] Rejected message from {connectionId}: {ex}");
                SendError(endpoint, "ServerError", "The request could not be processed.");
            }
        }

        /// <summary>Expires connections whose process or network vanished without a WebSocket close event.</summary>
        public void Tick(DateTime utcNow)
        {
            foreach (var connectionId in _connections.GetExpiredRoomConnections(utcNow))
            {
                RemoveMemberFromRoom(connectionId, "Connection heartbeat timed out.", true);
            }
        }

        private void HandleCreateRoom(string connectionId, GameEndpoint endpoint, CreateRoomMessage request)
        {
            if (!_connections.TryGet(connectionId, out var record) || !string.IsNullOrEmpty(record.RoomId)) { SendError(endpoint, "AlreadyInRoom", "Leave the current room before creating another."); return; }
            if (request == null || request.gameMode < (int)GameMode.Single || request.gameMode > (int)GameMode.FullGame) { SendError(endpoint, "InvalidGameMode", "The requested game mode is invalid."); return; }
            if (!PlayerLoadoutCodec.TryDecode(request.loadout, out var loadout, out var loadoutError)) { SendError(endpoint, loadoutError, "The submitted player loadout is invalid."); return; }
            RemoveClosedRooms();
            if (_rooms.Count >= _maxRooms) { SendError(endpoint, "RoomLimitReached", "The server has reached its room limit."); return; }

            string roomId = $"R{_nextRoomId++:D4}";
            var room = new Room(roomId, (GameMode)request.gameMode, connectionId, _aiFill, SendToEndpoint);
            room.OnClosed += HandleRoomClosed;
            if (!room.TryAddHuman(connectionId, endpoint, record.Nickname, loadout, out int seat))
            {
                room.OnClosed -= HandleRoomClosed;
                room.Dispose();
                SendError(endpoint, "RoomCreateFailed", "Could not allocate a room seat.");
                return;
            }
            if (!_connections.BindRoomSeat(connectionId, roomId, seat))
            {
                room.RemoveHuman(connectionId, out _);
                room.OnClosed -= HandleRoomClosed;
                room.Dispose();
                SendError(endpoint, "RoomCreateFailed", "Could not bind the allocated room seat.");
                return;
            }
            _rooms.Add(roomId, room);
            SendRoomJoined(endpoint, room, seat, true, loadout);
        }

        private void HandleJoinRoom(string connectionId, GameEndpoint endpoint, JoinRoomMessage request)
        {
            if (!_connections.TryGet(connectionId, out var record) || !string.IsNullOrEmpty(record.RoomId)) { SendError(endpoint, "AlreadyInRoom", "Leave the current room before joining another."); return; }
            if (request == null || string.IsNullOrWhiteSpace(request.roomId) || !_rooms.TryGetValue(request.roomId.Trim(), out var room) || room.State == RoomState.Closed) { SendError(endpoint, "RoomNotFound", "The requested room does not exist."); return; }
            if (!PlayerLoadoutCodec.TryDecode(request.loadout, out var loadout, out var loadoutError)) { SendError(endpoint, loadoutError, "The submitted player loadout is invalid."); return; }
            if (!room.TryAddHuman(connectionId, endpoint, record.Nickname, loadout, out int seat)) { SendError(endpoint, "RoomFullOrStarted", "The room is full or has already started."); return; }
            if (!_connections.BindRoomSeat(connectionId, room.RoomId, seat))
            {
                room.RemoveHuman(connectionId, out _);
                SendError(endpoint, "RoomJoinFailed", "Could not bind the allocated room seat.");
                return;
            }
            SendRoomJoined(endpoint, room, seat, false, loadout);
            room.Broadcast("PlayerJoined", new PlayerJoinedMessage { roomId = room.RoomId, seat = room.GetSeatMessage(seat) });
        }

        private void HandleReady(string connectionId, GameEndpoint endpoint, ReadyMessage request)
        {
            if (!TryGetRoomMember(connectionId, endpoint, out var room, out int seatIndex)) return;
            if (request == null || request.phase < (int)ReadyPhase.MatchStart || request.phase > (int)ReadyPhase.NextRound) { SendError(endpoint, "InvalidReady", "Ready phase is invalid."); return; }
            if (!room.SetReady(connectionId, (ReadyPhase)request.phase, out string error))
            {
                SendError(endpoint, "InvalidReady", error);
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
            if (!room.SubmitAction(seatIndex, message)) SendError(endpoint, "InvalidAction", "Action is not valid for the current room state.");
        }

        private void HandleLeaveRoom(string connectionId, GameEndpoint endpoint)
        {
            if (!_connections.TryGet(connectionId, out var record) || string.IsNullOrEmpty(record.RoomId))
            {
                SendError(endpoint, "NotInRoom", "Join a room first.");
                return;
            }

            RemoveMemberFromRoom(connectionId, "Player left the room.", false);
        }

        private bool TryGetRoomMember(string connectionId, GameEndpoint endpoint, out Room room, out int seatIndex)
        {
            room = null; seatIndex = -1;
            if (!_connections.TryGet(connectionId, out var record) || record.Endpoint != endpoint || string.IsNullOrEmpty(record.RoomId) || !_rooms.TryGetValue(record.RoomId, out room) || room.State == RoomState.Closed) { SendError(endpoint, "NotInRoom", "Join a room first."); return false; }
            seatIndex = record.SeatIndex;
            return true;
        }

        private void HandleDisconnected(string connectionId)
        {
            RemoveMemberFromRoom(connectionId, "Connection closed.", true);
        }

        private void RemoveMemberFromRoom(string connectionId, string reason, bool removeConnection)
        {
            if (!_connections.TryGet(connectionId, out var record)) return;

            string roomId = record.RoomId;
            if (!string.IsNullOrEmpty(roomId) && _rooms.TryGetValue(roomId, out var room))
            {
                if (room.HandleWaitingHumanDeparture(connectionId, out int seatIndex, out bool replacedByAi, out string replacementDisplayName))
                {
                    room.Broadcast("PlayerLeft", new PlayerLeftMessage
                    {
                        roomId = roomId,
                        seatIndex = seatIndex,
                        reason = reason,
                        seat = room.GetSeatMessage(seatIndex)
                    });
                    room.AdvanceAfterWaitingMemberChange();
                }
                else if (room.RemoveHuman(connectionId, out _))
                {
                    Debug.Log($"[RoomManager] Closing room {roomId} after connection {connectionId} left: {reason}");
                    room.Broadcast("RoomClosed", new RoomClosedMessage { roomId = roomId, reason = reason });
                    room.Close();
                }
            }

            if (removeConnection)
                _connections.Remove(connectionId, out _);
            else
                _connections.UnbindRoomSeat(connectionId);
        }

        private void SendRoomJoined(GameEndpoint endpoint, Room room, int seatIndex, bool isHost, TrustedPlayerLoadout loadout) => SendToEndpoint("RoomJoined", new EndpointPayload(endpoint, new RoomJoinedMessage
        {
            roomId = room.RoomId,
            seatIndex = seatIndex,
            gameMode = (int)room.GameMode,
            roomState = (int)room.State,
            isHost = isHost,
            aiFillEnabled = room.AiFillEnabled,
            acceptedSchemaVersion = loadout.SchemaVersion,
            acceptedTotalAlienation = loadout.TotalAlienation,
            seats = room.GetSeatSnapshot()
        }));

        private void SendToEndpoint(string type, object payload)
        {
            if (!(payload is EndpointPayload target) || target.Endpoint == null) return;
            target.Endpoint.SendMessage(MessageSerializer.Serialize(type, 0, target.Payload));
        }

        private static void SendError(GameEndpoint endpoint, string code, string message) => endpoint?.SendMessage(MessageSerializer.Serialize("RoomError", 0, new RoomErrorMessage { code = code, message = message }));

        private void RemoveClosedRooms()
        {
            var ids = new List<string>();
            foreach (var pair in _rooms) if (pair.Value.State == RoomState.Closed) ids.Add(pair.Key);
            foreach (var id in ids) { _rooms[id].Dispose(); _rooms.Remove(id); }
        }

        private void HandleRoomClosed(Room room)
        {
            if (room == null) return;
            foreach (var seat in room.Seats)
            {
                if (seat == null || seat.IsAi || string.IsNullOrEmpty(seat.ConnectionId)) continue;
                if (_connections.TryGet(seat.ConnectionId, out var record) && record.RoomId == room.RoomId) _connections.UnbindRoomSeat(seat.ConnectionId);
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
