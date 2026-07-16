using System;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Core.Network.Data;
using MahjongGame.Systems;
using MahjongGame.Talents;
using UnityEngine;
using WebSocketSharp;

namespace MahjongGame.Core.Network
{
    /// <summary>Persistent client-side room identity/state. It is the only owner of room protocol messages.</summary>
    public sealed class ClientRoomService : IDisposable
    {
        private const float HeartbeatIntervalSeconds = 3f;
        private readonly string _defaultAddress;
        private Action _pendingAfterConnect;
        private bool _disposed;
        private float _nextHeartbeatAt;
        private readonly ClientRoomState _roomState = new ClientRoomState();

        public string RoomId => _roomState.RoomId;
        public int SeatIndex => _roomState.SeatIndex;
        public GameMode GameMode => _roomState.GameMode;
        public RoomState RoomState => (RoomState)_roomState.RoomStateValue;
        public bool AiFillEnabled => _roomState.AiFillEnabled;
        public RoomSeatMessage[] Seats => _roomState.Seats;
        public bool HasRoom => _roomState.HasRoom;
        public bool IsSessionCompleted => _roomState.IsSessionCompleted;
        public bool HasResultSeatSnapshot => HasRoom || IsSessionCompleted;
        public int ResultSeatIndex => HasRoom ? SeatIndex : _roomState.ResultSeatIndex;
        public RoomSeatMessage[] ResultSeats => HasRoom ? Seats : _roomState.ResultSeats;
        public int AcceptedTotalAlienation => _roomState.AcceptedTotalAlienation;
        public string LastRoomClosureReason { get; private set; }

        public event Action<RoomJoinedMessage> RoomJoined;
        public event Action RoomReady;
        public event Action<RoomSeatMessage[]> SeatSnapshotChanged;
        public event Action<string> RoomError;
        public event Action<string> RoomClosed;

        public ClientRoomService(string defaultAddress)
        {
            _defaultAddress = defaultAddress;
            var client = EnsureWebSocketClient();
            client.OnConnected += HandleConnected;
            client.OnMessageReceived += HandleMessage;
            client.OnDisconnected += HandleDisconnected;
        }

        public bool CreateRoom(GameMode gameMode, string nickname, string address = null)
        {
            if (!TryBuildSelectedLoadout(out var loadout)) return false;
            ClearCompletedStateForNewRoom();
            SendWhenConnected(() =>
            {
                Send("Hello", new HelloMessage { nickname = nickname });
                Send("CreateRoom", new CreateRoomMessage { gameMode = (int)gameMode, loadout = loadout });
            }, address);
            return true;
        }

        public bool JoinRoom(string roomId, string nickname, string address = null)
        {
            if (!TryBuildSelectedLoadout(out var loadout)) return false;
            ClearCompletedStateForNewRoom();
            SendWhenConnected(() =>
            {
                Send("Hello", new HelloMessage { nickname = nickname });
                Send("JoinRoom", new JoinRoomMessage { roomId = roomId, loadout = loadout });
            }, address);
            return true;
        }

        public void SendReady(ReadyPhase phase)
        {
            if (!HasRoom) { RoomError?.Invoke("No active room."); return; }
            Send("Ready", new ReadyMessage { phase = (int)phase });
        }

        public void LeaveRoom()
        {
            if (!HasRoom)
            {
                if (IsSessionCompleted) ResetRoomState();
                return;
            }
            Send("LeaveRoom", new LeaveRoomMessage());
            ResetRoomState();
        }

        public void Tick(float unscaledTime)
        {
            if (!HasRoom || unscaledTime < _nextHeartbeatAt) return;
            Send("Heartbeat", new HeartbeatMessage());
            _nextHeartbeatAt = unscaledTime + HeartbeatIntervalSeconds;
        }

        private void SendWhenConnected(Action action, string address)
        {
            var client = EnsureWebSocketClient();
            if (client.ReadyState == WebSocketState.Open) { action(); return; }
            _pendingAfterConnect = action;
            client.Connect(string.IsNullOrWhiteSpace(address) ? _defaultAddress : address.Trim());
        }

        private void HandleConnected()
        {
            var pending = _pendingAfterConnect;
            _pendingAfterConnect = null;
            pending?.Invoke();
        }

        private void HandleMessage(string json)
        {
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (envelope == null) return;
            switch (envelope.type)
            {
                case "RoomJoined":
                    var joined = MessageSerializer.DeserializePayload<RoomJoinedMessage>(envelope.data);
                    if (joined == null) return;
                    _roomState.ApplyJoined(joined);
                    LastRoomClosureReason = null;
                    _nextHeartbeatAt = Time.unscaledTime;
                    RoomJoined?.Invoke(joined); SeatSnapshotChanged?.Invoke(Seats);
                    break;
                case "PlayerJoined":
                    ApplyPlayerJoined(MessageSerializer.DeserializePayload<PlayerJoinedMessage>(envelope.data));
                    break;
                case "PlayerLeft":
                    ApplyPlayerLeft(MessageSerializer.DeserializePayload<PlayerLeftMessage>(envelope.data));
                    break;
                case "RoomSeatUpdated":
                    var seatUpdated = MessageSerializer.DeserializePayload<RoomSeatUpdatedMessage>(envelope.data);
                    if (seatUpdated != null && seatUpdated.roomId == RoomId && _roomState.ApplySeatUpdate(seatUpdated.seat))
                        SeatSnapshotChanged?.Invoke(Seats);
                    break;
                case "RoomReady": _roomState.SetRoomState((int)RoomState.LoadingGameScene); RoomReady?.Invoke(); break;
                case "SessionEnd": CompleteSessionRoomState(); break;
                case "RoomClosed":
                    var closed = MessageSerializer.DeserializePayload<RoomClosedMessage>(envelope.data);
                    if (closed == null || closed.roomId != RoomId) return;
                    LastRoomClosureReason = string.IsNullOrWhiteSpace(closed.reason) ? "The room was closed." : closed.reason;
                    ResetRoomState();
                    RoomClosed?.Invoke(LastRoomClosureReason);
                    break;
                case "RoomError":
                    var error = MessageSerializer.DeserializePayload<RoomErrorMessage>(envelope.data);
                    RoomError?.Invoke(error?.message ?? "Room request failed.");
                    break;
            }
        }

        private void ApplyPlayerJoined(PlayerJoinedMessage message)
        {
            if (message == null || message.roomId != RoomId || message.seat == null || message.seat.seatIndex < 0 || message.seat.seatIndex > 3) return;
            var snapshot = (RoomSeatMessage[])Seats.Clone();
            if (snapshot.Length != 4) snapshot = new RoomSeatMessage[4];
            snapshot[message.seat.seatIndex] = message.seat;
            _roomState.SetSeats(snapshot); SeatSnapshotChanged?.Invoke(Seats);
        }

        private void ApplyPlayerLeft(PlayerLeftMessage message)
        {
            if (message == null || message.roomId != RoomId || message.seat == null || message.seatIndex < 0 || message.seatIndex > 3) return;
            var snapshot = (RoomSeatMessage[])Seats.Clone();
            if (snapshot.Length != 4) snapshot = new RoomSeatMessage[4];
            snapshot[message.seatIndex] = message.seat;
            _roomState.SetSeats(snapshot); SeatSnapshotChanged?.Invoke(Seats);
        }

        private bool TryBuildSelectedLoadout(out PlayerLoadoutMessage loadout)
        {
            loadout = null;
            DeckConfig deckConfig;
            TalentSlotConfig talentConfig;
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile == null || profile.SavedDecks == null || profile.SavedDecks.Count == 0)
            {
                deckConfig = DeckConfig.CreateStandard();
                talentConfig = new TalentSlotConfig();
            }
            else
            {
                int index = profile.SelectedDeckIndex;
                if (index < 0 || index >= profile.SavedDecks.Count)
                {
                    RoomError?.Invoke("The selected local deck index is invalid. Choose a deck before entering a room.");
                    return false;
                }

                SavedDeck savedDeck = profile.SavedDecks[index];
                deckConfig = savedDeck?.Config;
                talentConfig = savedDeck?.Talents ?? new TalentSlotConfig();
            }

            if (PlayerLoadoutCodec.TryCreateMessage(deckConfig, talentConfig, out loadout, out string errorCode)) return true;
            RoomError?.Invoke($"The selected local loadout is invalid ({errorCode}). Fix it before entering a room.");
            return false;
        }

        private void HandleDisconnected(string reason)
        {
            if (!HasRoom) return;
            LastRoomClosureReason = "Disconnected from the room server. Reconnect is not available yet.";
            ResetRoomState();
            RoomClosed?.Invoke(LastRoomClosureReason);
        }

        private void ResetRoomState()
        {
            _roomState.Reset();
            _nextHeartbeatAt = 0f;
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private void CompleteSessionRoomState()
        {
            if (!HasRoom) return;
            _roomState.CompleteSession();
            _nextHeartbeatAt = 0f;
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private void ClearCompletedStateForNewRoom()
        {
            if (!HasRoom && IsSessionCompleted) ResetRoomState();
        }

        private static WebSocketClient EnsureWebSocketClient()
        {
            if (WebSocketClient.Instance != null) return WebSocketClient.Instance;
            return new GameObject("WebSocketClient").AddComponent<WebSocketClient>();
        }

        private static void Send<T>(string type, T payload) => WebSocketClient.Instance?.SendNetworkMessage(MessageSerializer.Serialize(type, 0, payload));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (WebSocketClient.Instance == null) return;
            WebSocketClient.Instance.OnConnected -= HandleConnected;
            WebSocketClient.Instance.OnMessageReceived -= HandleMessage;
            WebSocketClient.Instance.OnDisconnected -= HandleDisconnected;
        }
    }
}
