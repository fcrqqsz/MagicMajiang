using System;
using System.Collections.Generic;
using System.Linq;
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
        private const float ReconnectHandshakeTimeoutSeconds = 10f;
        private readonly string _defaultAddress;
        private readonly IClientReconnectTicketStore _ticketStore;
        private Action _pendingAfterConnect;
        private Action _pendingRoomCommandAfterHello;
        private bool _disposed;
        private bool _hasHelloAccepted;
        private float _nextHeartbeatAt;
        private float _lastHeartbeatAcknowledgementAt;
        private bool _resyncRequiredRaised;
        private bool _pendingReconnect;
        private bool _isComposingRecovery;
        private bool _reconnectAttemptInFlight;
        private bool _reconnectRetryScheduled;
        private int _reconnectAttemptIndex;
        private float _nextReconnectAt;
        private float _reconnectHandshakeDeadlineAt;
        private string _activeUsername;
        private string _activeServerAddress;
        private readonly ClientSequenceGate _sequenceGate = new ClientSequenceGate();
        private readonly ClientHelloHandshakePolicy _helloHandshake = new ClientHelloHandshakePolicy();
        private readonly ClientRoomState _roomState = new ClientRoomState();
        private readonly ClientGameState _gameState = new ClientGameState();
        private readonly ClientProjectionLineage _projectionLineage = new ClientProjectionLineage();

        public string RoomId => _roomState.RoomId;
        public int SeatIndex => _roomState.SeatIndex;
        public GameMode GameMode => _roomState.GameMode;
        public RoomState RoomState => (RoomState)_roomState.RoomStateValue;
        public bool AiFillEnabled => _roomState.AiFillEnabled;
        public AlienationPreset AlienationPreset => _roomState.AlienationPreset;
        public RoomSeatMessage[] Seats => _roomState.Seats;
        public bool HasRoom => _roomState.HasRoom;
        public bool IsSessionCompleted => _roomState.IsSessionCompleted;
        public bool HasResultSeatSnapshot => HasRoom || IsSessionCompleted;
        public int ResultSeatIndex => HasRoom ? SeatIndex : _roomState.ResultSeatIndex;
        public RoomSeatMessage[] ResultSeats => HasRoom ? Seats : _roomState.ResultSeats;
        public int OwnTotalAlienation => _roomState.OwnTotalAlienation;
        public string LastRoomClosureReason { get; private set; }
        public bool IsResyncRequired => _sequenceGate.IsResyncRequired;
        public bool IsConnectionRecoveryRequired { get; private set; }
        public string ConnectionRecoveryReason { get; private set; }
        public bool CanSubmitCommands => !_pendingReconnect && !IsResyncRequired && !IsConnectionRecoveryRequired;
        public ClientGameState GameState => _gameState;
        public SnapshotSideboardState Sideboard => _roomState.Sideboard;
        public TalentActionOption[] AvailableTalentActions => _gameState.AvailableTalentActions;
        /// <summary>Monotonic presentation token for a completed full-snapshot recovery.</summary>
        public int RecoveryPresentationVersion { get; private set; }

        public event Action<RoomJoinedMessage> RoomJoined;
        public event Action<RoomSummaryMessage[]> RoomListReceived;
        public event Action RoomReady;
        public event Action<RoomSeatMessage[]> SeatSnapshotChanged;
        public event Action<string> RoomError;
        public event Action<string> RoomClosed;
        public event Action ResyncRequired;
        public event Action<string> ConnectionRecoveryRequired;
        public event Action<ClientRecoveryProgress> RecoveryProgressChanged;
        public event Action<RoomGameSnapshot> ReconnectSnapshotApplied;
        /// <summary>Raised after a positive sequence has passed the gate and room state has been applied.</summary>
        public event Action<NetworkMessageEnvelope> AcceptedSequenceEnvelope;

        public ClientRoomService(string defaultAddress, IClientReconnectTicketStore ticketStore = null)
        {
            _defaultAddress = defaultAddress;
            _ticketStore = ticketStore ?? new PlayerPrefsClientReconnectTicketStore();
            var client = EnsureWebSocketClient();
            client.OnConnected += HandleConnected;
            client.OnMessageReceived += HandleMessage;
            client.OnDisconnected += HandleDisconnected;
        }

        public bool CreateRoom(
            GameMode gameMode,
            AlienationPreset roomPreset,
            string nickname,
            string address = null)
        {
            if (!CanStartNewRoomCommand()) return false;
            if (!AlienationBudgetPolicy.IsDefined(roomPreset)) return false;
            if (!TryBuildSelectedLoadout(out var loadout)) return false;
            if (!BeginRoomCommand(nickname,
                    () => Send("CreateRoom", new CreateRoomMessage
                    {
                        gameMode = (int)gameMode,
                        alienationPreset = (int)roomPreset,
                        loadout = loadout
                    }),
                    address)) return false;
            ClearCompletedStateForNewRoom();
            return true;
        }

        public bool JoinRoom(string roomId, string nickname, string address = null)
        {
            if (!CanStartNewRoomCommand()) return false;
            if (!TryBuildSelectedLoadout(out var loadout)) return false;
            if (!BeginRoomCommand(nickname,
                    () => Send("JoinRoom", new JoinRoomMessage { roomId = roomId, loadout = loadout }),
                    address)) return false;
            ClearCompletedStateForNewRoom();
            return true;
        }

        public bool QueryRoomList(string nickname = null, string address = null)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                nickname = !string.IsNullOrWhiteSpace(_activeUsername) ? _activeUsername : "Player";

            return BeginRoomCommand(nickname, () => Send("QueryRoomList", new QueryRoomListMessage()), address);
        }

        public void SendReady(ReadyPhase phase)
        {
            if (!CanSubmitRoomCommand()) return;
            if (!HasRoom) { RoomError?.Invoke("No active room."); return; }
            Send("Ready", new ReadyMessage { phase = (int)phase });
        }

        public bool SubmitTalentAction(TalentActionOption option)
        {
            if (!CanSubmitRoomCommand()
                || !HasRoom
                || option == null
                || string.IsNullOrWhiteSpace(option.TalentId)) return false;

            ClientTalentRecoveryProjection projection = _gameState.CreateTalentRecoveryProjection();
            if (projection.DecisionId <= 0) return false;

            Send("TalentAction", new TalentActionMessage
            {
                decisionId = projection.DecisionId,
                talentId = option.TalentId,
                targetSeatIndex = option.TargetSeatIndex,
                targetTalentId = option.TargetTalentId,
                selectedChoiceId = option.SelectedChoiceId
            });
            return true;
        }

        public bool CreateRoom(GameMode gameMode, string nickname, string address = null) =>
            CreateRoom(gameMode, GetSelectedLoadoutAlienationPreset(), nickname, address);

        public bool SubmitSideboard(IReadOnlyCollection<string> activeTalentIds)
        {
            if (!CanSubmitRoomCommand()
                || !HasRoom
                || activeTalentIds == null) return false;

            SnapshotSideboardState sideboard = _roomState.Sideboard;
            if (sideboard == null
                || !sideboard.isActive
                || sideboard.ownLocked
                || sideboard.decisionId <= 0) return false;

            Send("SideboardSubmit", new SideboardSubmitMessage
            {
                decisionId = sideboard.decisionId,
                activeTalentIds = activeTalentIds.ToArray()
            });
            return true;
        }

        public void LeaveRoom()
        {
            if (!CanSubmitRoomCommand()) return;
            if (!HasRoom)
            {
                if (IsSessionCompleted) ResetRoomState();
                return;
            }
            Send("LeaveRoom", new LeaveRoomMessage());
            ResetRoomState(true);
        }

        /// <summary>Starts an E3 protocol recovery from a previously persisted non-secret ticket.</summary>
        public bool ReconnectSavedRoom() => ReconnectSavedRoom(null);

        /// <summary>Starts recovery only when the just-authenticated development identity owns the saved ticket.</summary>
        public bool ReconnectSavedRoom(string authenticatedUsername)
        {
            if (!_ticketStore.TryLoad(out var ticket)) return false;
            if (!string.IsNullOrWhiteSpace(authenticatedUsername)
                && !ClientReconnectTicketPolicy.ShouldAutoReconnectAfterLogin(ticket, authenticatedUsername)) return false;
            if (_pendingReconnect) return true;
            BeginReconnect(ticket, "Restoring the saved room.");
            return true;
        }

        /// <summary>
        /// Leaves a recovered room when the socket is healthy, or abandons the local recovery hint
        /// when the network is unavailable. A later server-side offline-seat expiry remains authoritative.
        /// </summary>
        public void LeaveRoomOrAbandonRecovery()
        {
            var client = WebSocketClient.Instance;
            if (HasRoom && _hasHelloAccepted && client?.ReadyState == WebSocketState.Open)
                Send("LeaveRoom", new LeaveRoomMessage());

            ResetRoomState(true);
            client?.Disconnect();
        }

        public void Tick(float unscaledTime)
        {
            if (_pendingReconnect || IsConnectionRecoveryRequired)
            {
                TickReconnect(unscaledTime);
                return;
            }
            if (!_hasHelloAccepted) return;
            if (ConnectionLivenessPolicy.IsClientAcknowledgementExpired(_lastHeartbeatAcknowledgementAt, unscaledTime))
            {
                const string reason = "The server did not acknowledge a heartbeat within 10 seconds.";
                if (!_ticketStore.TryLoad(out var ticket))
                {
                    HandleTerminalReconnectFailure(NetworkErrorCodes.RoomNotFound, "The saved room information is unavailable.");
                    return;
                }

                // Disconnect intentionally suppresses stale socket callbacks. Start the
                // recovery state machine first so the next Tick owns the reconnect attempt.
                BeginReconnect(ticket, reason);
                WebSocketClient.Instance?.Disconnect();
                return;
            }

            if (unscaledTime < _nextHeartbeatAt) return;
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

            if (envelope.type == "ReconnectState")
            {
                HandleReconnectState(MessageSerializer.DeserializePayload<ReconnectStateMessage>(envelope.data));
                return;
            }

            if (envelope.type == "RoomJoined")
                PrepareForRoomJoined(MessageSerializer.DeserializePayload<RoomJoinedMessage>(envelope.data));

            ApplyEnvelope(envelope);
        }

        private void ApplyEnvelope(NetworkMessageEnvelope envelope)
        {
            if (envelope == null) return;
            if (envelope.seq > 0)
            {
                var sequenceDisposition = _sequenceGate.Apply(envelope.seq);
                if (sequenceDisposition == ClientSequenceDisposition.IgnoredDuplicate) return;
                if (sequenceDisposition == ClientSequenceDisposition.ResyncRequired)
                {
                    EnterResyncRequired();
                    return;
                }
                _gameState.ApplyEnvelope(envelope);
            }

            switch (envelope.type)
            {
                case "HelloAccepted":
                    HandleHelloAccepted();
                    break;
                case "HeartbeatAck":
                    HandleHeartbeatAcknowledgement();
                    break;
                case "RoomJoined":
                    var joined = MessageSerializer.DeserializePayload<RoomJoinedMessage>(envelope.data);
                    if (joined == null) return;
                    _gameState.ApplyRoomJoined(joined);
                    _projectionLineage.Bind(joined.roomId, joined.streamId);
                    _roomState.ApplyJoined(joined);
                    _gameState.ApplyRoomSeats(Seats);
                    SaveReconnectTicket(joined);
                    LastRoomClosureReason = null;
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
                    {
                        _gameState.ApplyRoomSeats(Seats);
                        SeatSnapshotChanged?.Invoke(Seats);
                    }
                    break;
                case "RoomReady": _roomState.SetRoomState((int)RoomState.LoadingGameScene); RoomReady?.Invoke(); break;
                case "SideboardStarted":
                    _roomState.ApplySideboardStarted(
                        MessageSerializer.DeserializePayload<SideboardStartedMessage>(envelope.data));
                    break;
                case "SideboardLocked":
                    _roomState.ApplySideboardLocked(
                        MessageSerializer.DeserializePayload<SideboardLockedMessage>(envelope.data));
                    break;
                case "SideboardProgress":
                    _roomState.ApplySideboardProgress(
                        MessageSerializer.DeserializePayload<SideboardProgressMessage>(envelope.data));
                    break;
                case "SessionEnd": CompleteSessionRoomState(); break;
                case "RoomList":
                    var roomList = MessageSerializer.DeserializePayload<RoomListMessage>(envelope.data);
                    RoomListReceived?.Invoke(roomList?.rooms ?? Array.Empty<RoomSummaryMessage>());
                    break;
                case "RoomClosed":
                    var closed = MessageSerializer.DeserializePayload<RoomClosedMessage>(envelope.data);
                    if (closed == null || closed.roomId != RoomId) return;
                    LastRoomClosureReason = string.IsNullOrWhiteSpace(closed.reason) ? "The room was closed." : closed.reason;
                    ResetRoomState(true);
                    RoomClosed?.Invoke(LastRoomClosureReason);
                    break;
                case "RoomError":
                    var error = MessageSerializer.DeserializePayload<RoomErrorMessage>(envelope.data);
                    if (_helloHandshake.RejectHello()) _pendingRoomCommandAfterHello = null;
                    if (_pendingReconnect)
                    {
                        HandleReconnectFailure(error?.code, error?.message);
                        break;
                    }
                    if (ClientReconnectTicketPolicy.ShouldClearForRoomError(error?.code)) ResetRoomState(true);
                    RoomError?.Invoke(RoomErrorPresentationPolicy.GetDisplayMessage(error));
                    break;
                case "ReconnectRejected":
                    var rejected = MessageSerializer.DeserializePayload<ReconnectRejectedMessage>(envelope.data);
                    HandleReconnectFailure(rejected?.code, rejected?.message);
                    break;
            }

            if (envelope.seq > 0 && !_isComposingRecovery)
                AcceptedSequenceEnvelope?.Invoke(envelope);
        }

        private void ApplyPlayerJoined(PlayerJoinedMessage message)
        {
            if (message == null || message.roomId != RoomId || message.seat == null || message.seat.seatIndex < 0 || message.seat.seatIndex > 3) return;
            var snapshot = (RoomSeatMessage[])Seats.Clone();
            if (snapshot.Length != 4) snapshot = new RoomSeatMessage[4];
            snapshot[message.seat.seatIndex] = message.seat;
            _roomState.SetSeats(snapshot);
            _gameState.ApplyRoomSeats(Seats);
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private void ApplyPlayerLeft(PlayerLeftMessage message)
        {
            if (message == null || message.roomId != RoomId || message.seat == null || message.seatIndex < 0 || message.seatIndex > 3) return;
            var snapshot = (RoomSeatMessage[])Seats.Clone();
            if (snapshot.Length != 4) snapshot = new RoomSeatMessage[4];
            snapshot[message.seatIndex] = message.seat;
            _roomState.SetSeats(snapshot);
            _gameState.ApplyRoomSeats(Seats);
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private bool TryBuildSelectedLoadout(out PlayerLoadoutMessage loadout)
        {
            loadout = null;
            DeckConfig deckConfig;
            TalentSlotConfig talentConfig;
            AlienationPreset alienationPreset;
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile == null || profile.SavedDecks == null || profile.SavedDecks.Count == 0)
            {
                deckConfig = DeckConfig.CreateStandard();
                talentConfig = new TalentSlotConfig();
                alienationPreset = AlienationPreset.Standard;
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
                alienationPreset = savedDeck?.AlienationPreset ?? AlienationPreset.Standard;
            }

            if (PlayerLoadoutCodec.TryCreateMessage(
                    deckConfig, talentConfig, alienationPreset, out loadout, out string errorCode)) return true;
            RoomError?.Invoke($"The selected local loadout is invalid ({errorCode}). Fix it before entering a room.");
            return false;
        }

        private static AlienationPreset GetSelectedLoadoutAlienationPreset()
        {
            var profile = ProfileManager.Instance?.CurrentProfile;
            if (profile?.SavedDecks == null || profile.SavedDecks.Count == 0)
                return AlienationPreset.Standard;
            int index = profile.SelectedDeckIndex;
            if (index < 0 || index >= profile.SavedDecks.Count)
                return AlienationPreset.Standard;
            AlienationPreset preset = profile.SavedDecks[index]?.AlienationPreset ?? AlienationPreset.Standard;
            return AlienationBudgetPolicy.IsDefined(preset) ? preset : AlienationPreset.Standard;
        }

        private void HandleDisconnected(string reason)
        {
            _pendingAfterConnect = null;
            _pendingRoomCommandAfterHello = null;
            _hasHelloAccepted = false;
            _helloHandshake.Reset();

            if (_pendingReconnect)
            {
                if (!_reconnectRetryScheduled)
                    ScheduleReconnect(Time.unscaledTime, string.IsNullOrWhiteSpace(reason)
                        ? "Reconnect attempt was disconnected."
                        : $"Reconnect attempt was disconnected: {reason}");
                return;
            }

            if (HasRoom && _ticketStore.TryLoad(out var ticket))
            {
                BeginReconnect(ticket, string.IsNullOrWhiteSpace(reason)
                    ? "Disconnected from the room server."
                    : $"Disconnected from the room server: {reason}");
            }
        }

        private void ResetRoomState()
        {
            ResetRoomState(false);
        }

        private void ResetRoomState(bool clearTicket)
        {
            if (clearTicket) _ticketStore.Clear();
            _roomState.Reset();
            _sequenceGate.Reset();
            _gameState.Reset();
            _projectionLineage.Clear();
            _resyncRequiredRaised = false;
            _pendingReconnect = false;
            _reconnectAttemptInFlight = false;
            _reconnectRetryScheduled = false;
            _reconnectAttemptIndex = 0;
            _nextReconnectAt = 0f;
            _reconnectHandshakeDeadlineAt = 0f;
            _isComposingRecovery = false;
            RecoveryPresentationVersion = 0;
            IsConnectionRecoveryRequired = false;
            ConnectionRecoveryReason = null;
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private void CompleteSessionRoomState()
        {
            if (!HasRoom) return;
            _roomState.CompleteSession();
            SeatSnapshotChanged?.Invoke(Seats);
        }

        private void ClearCompletedStateForNewRoom()
        {
            if (!HasRoom && IsSessionCompleted) ResetRoomState();
        }

        private bool CanSubmitRoomCommand()
        {
            if (CanSubmitCommands) return true;
            RoomError?.Invoke(IsResyncRequired
                ? "Room synchronization is required before more room commands can be sent."
                : _pendingReconnect
                    ? "Restoring the saved room before more room commands can be sent."
                : "Connection recovery is required before more room commands can be sent.");
            return false;
        }

        private bool CanStartNewRoomCommand()
        {
            if (!CanSubmitRoomCommand()) return false;
            if (!HasRoom) return true;
            RoomError?.Invoke("AlreadyInRoom: Leave the current room before creating or joining another room.");
            return false;
        }

        private void HandleHelloAccepted()
        {
            bool shouldSendPendingRoomCommand = _helloHandshake.AcceptHello();
            _hasHelloAccepted = true;
            _lastHeartbeatAcknowledgementAt = Time.unscaledTime;
            _nextHeartbeatAt = Time.unscaledTime;

            if (_pendingReconnect && _ticketStore.TryLoad(out var ticket))
            {
                bool hasProjection = ClientReconnectRecoveryPolicy.ShouldUseCachedProjection()
                    && _gameState.Snapshot != null
                    && _projectionLineage.Matches(ticket.roomId, ticket.streamId);
                if (!hasProjection)
                {
                    _sequenceGate.Reset();
                    _gameState.Reset();
                    _projectionLineage.Clear();
                }
                Send("Reconnect", new ReconnectMessage
                {
                    roomId = ticket.roomId,
                    streamId = ticket.streamId,
                    lastSeq = hasProjection ? _sequenceGate.LastSequence : 0,
                    hasProjection = hasProjection
                });
                PublishRecoveryProgress(ClientRecoveryStage.Resynchronizing, "Connected. Requesting an authoritative table snapshot.", _reconnectAttemptIndex);
                return;
            }

            _pendingReconnect = false;

            if (shouldSendPendingRoomCommand)
            {
                var pending = _pendingRoomCommandAfterHello;
                _pendingRoomCommandAfterHello = null;
                pending?.Invoke();
            }
        }

        private void HandleHeartbeatAcknowledgement()
        {
            if (!_hasHelloAccepted) return;
            _lastHeartbeatAcknowledgementAt = Time.unscaledTime;
        }

        private void EnterResyncRequired()
        {
            if (_resyncRequiredRaised) return;
            _resyncRequiredRaised = true;
            if (HasRoom && _ticketStore.TryLoad(out var ticket))
            {
                Send("Resync", new ResyncMessage { roomId = RoomId, streamId = ticket.streamId, lastSeq = _sequenceGate.LastSequence });
            }
            ResyncRequired?.Invoke();
        }

        private void EnterConnectionRecoveryRequired(string reason)
        {
            if (IsConnectionRecoveryRequired) return;
            IsConnectionRecoveryRequired = true;
            ConnectionRecoveryReason = reason;
            ConnectionRecoveryRequired?.Invoke(reason);
        }

        private bool BeginRoomCommand(string nickname, Action roomCommand, string address)
        {
            _activeUsername = nickname?.Trim();
            _activeServerAddress = string.IsNullOrWhiteSpace(address) ? _defaultAddress : address.Trim();
            switch (_helloHandshake.BeginRoomCommand())
            {
                case ClientHelloHandshakeAction.SendRoomCommand:
                    SendWhenConnected(roomCommand, address);
                    return true;
                case ClientHelloHandshakeAction.SendHello:
                    _pendingRoomCommandAfterHello = roomCommand;
                    SendWhenConnected(() => Send("Hello", ClientHelloProtocol.Create(nickname)), address);
                    return true;
                default:
                    RoomError?.Invoke("Authentication is in progress. Wait for the server response before sending another room command.");
                    return false;
            }
        }

        private static WebSocketClient EnsureWebSocketClient()
        {
            if (WebSocketClient.Instance != null) return WebSocketClient.Instance;
            return new GameObject("WebSocketClient").AddComponent<WebSocketClient>();
        }

        private static void Send<T>(string type, T payload) => WebSocketClient.Instance?.SendNetworkMessage(MessageSerializer.Serialize(type, 0, payload));

        private void HandleReconnectState(ReconnectStateMessage recovery)
        {
            if (recovery == null) return;

            _isComposingRecovery = true;
            try
            {
                bool hasSnapshot = recovery.snapshot != null;
                if (recovery.snapshot != null)
                {
                    _gameState.ApplySnapshot(recovery.snapshot, recovery.baselineSeq);
                    _roomState.ApplyRecoverySnapshot(recovery.snapshot);
                    if (_ticketStore.TryLoad(out var ticket)) _projectionLineage.Bind(ticket.roomId, ticket.streamId);
                }
                _sequenceGate.RestoreBaseline(recovery.baselineSeq);
                _resyncRequiredRaised = false;
                _pendingReconnect = false;
                _reconnectAttemptInFlight = false;
                _reconnectRetryScheduled = false;
                _reconnectAttemptIndex = 0;
                IsConnectionRecoveryRequired = false;
                ConnectionRecoveryReason = null;

                foreach (var envelope in recovery.missedMessages ?? Array.Empty<NetworkMessageEnvelope>())
                    ApplyEnvelope(envelope);

                // Present only after the projector has incorporated the authoritative baseline and
                // every contiguous envelope that arrived while recovery was being composed.
                if (hasSnapshot)
                {
                    RecoveryPresentationVersion++;
                    ReconnectSnapshotApplied?.Invoke(_gameState.Snapshot);
                    SeatSnapshotChanged?.Invoke(Seats);
                }
            }
            finally
            {
                _isComposingRecovery = false;
            }
            PublishRecoveryProgress(ClientRecoveryStage.Restored, "Room state restored.");
        }

        private void PrepareForRoomJoined(RoomJoinedMessage joined)
        {
            if (joined == null || string.IsNullOrWhiteSpace(joined.roomId) || string.IsNullOrWhiteSpace(joined.streamId)
                || _projectionLineage.Matches(joined.roomId, joined.streamId)) return;

            _sequenceGate.Reset();
            _gameState.Reset();
            _projectionLineage.Clear();
            _resyncRequiredRaised = false;
        }

        private void SaveReconnectTicket(RoomJoinedMessage joined)
        {
            if (joined == null || string.IsNullOrWhiteSpace(joined.roomId) || string.IsNullOrWhiteSpace(joined.streamId)
                || string.IsNullOrWhiteSpace(_activeUsername) || string.IsNullOrWhiteSpace(_activeServerAddress)) return;
            _ticketStore.Save(new ClientReconnectTicket
            {
                serverAddress = _activeServerAddress,
                username = _activeUsername,
                roomId = joined.roomId,
                streamId = joined.streamId
            });
        }

        private void BeginReconnect(ClientReconnectTicket ticket, string reason)
        {
            if (ticket == null)
            {
                HandleTerminalReconnectFailure(NetworkErrorCodes.RoomNotFound, "The saved room information is unavailable.");
                return;
            }

            _activeUsername = ticket.username;
            _activeServerAddress = ticket.serverAddress;
            _pendingReconnect = true;
            _hasHelloAccepted = false;
            _helloHandshake.Reset();
            _reconnectAttemptInFlight = false;
            _reconnectRetryScheduled = false;
            _reconnectAttemptIndex = 0;
            EnterConnectionRecoveryRequired(reason);
            ScheduleReconnect(Time.unscaledTime, reason);
        }

        private void TickReconnect(float unscaledTime)
        {
            if (!_pendingReconnect)
            {
                IsConnectionRecoveryRequired = false;
                return;
            }

            if (_reconnectAttemptInFlight && unscaledTime >= _reconnectHandshakeDeadlineAt)
            {
                _reconnectAttemptInFlight = false;
                WebSocketClient.Instance?.Disconnect();
                if (!_reconnectRetryScheduled)
                    ScheduleReconnect(unscaledTime, "Reconnect handshake timed out.");
            }

            if (_reconnectRetryScheduled && unscaledTime >= _nextReconnectAt)
                StartScheduledReconnect(unscaledTime);
        }

        private void ScheduleReconnect(float now, string reason)
        {
            if (!_ticketStore.TryLoad(out _))
            {
                HandleTerminalReconnectFailure(NetworkErrorCodes.RoomNotFound, "The saved room information is unavailable.");
                return;
            }

            int delay = ClientReconnectRetryPolicy.GetDelaySeconds(_reconnectAttemptIndex);
            _nextReconnectAt = now + delay;
            _reconnectRetryScheduled = true;
            _reconnectAttemptInFlight = false;
            PublishRecoveryProgress(ClientRecoveryStage.Connecting,
                delay == 0 ? reason : $"{reason} Retrying in {delay} seconds.",
                _reconnectAttemptIndex + 1,
                delay);
            _reconnectAttemptIndex++;
        }

        private void StartScheduledReconnect(float now)
        {
            if (!_ticketStore.TryLoad(out var ticket))
            {
                HandleTerminalReconnectFailure(NetworkErrorCodes.RoomNotFound, "The saved room information is unavailable.");
                return;
            }

            _reconnectRetryScheduled = false;
            _reconnectAttemptInFlight = true;
            _reconnectHandshakeDeadlineAt = now + ReconnectHandshakeTimeoutSeconds;
            _hasHelloAccepted = false;
            _helloHandshake.Reset();
            PublishRecoveryProgress(ClientRecoveryStage.Connecting, "Connecting to the room server.", _reconnectAttemptIndex);
            SendWhenConnected(() => Send("Hello", ClientHelloProtocol.Create(ticket.username)), ticket.serverAddress);
        }

        private void HandleReconnectFailure(string errorCode, string message)
        {
            if (!ClientReconnectRetryPolicy.ShouldRetryAfterError(errorCode))
            {
                HandleTerminalReconnectFailure(errorCode, message);
                return;
            }

            _hasHelloAccepted = false;
            _helloHandshake.Reset();
            _reconnectAttemptInFlight = false;
            ScheduleReconnect(Time.unscaledTime, string.IsNullOrWhiteSpace(message) ? "Reconnect was rejected." : message);
        }

        private void HandleTerminalReconnectFailure(string errorCode, string message)
        {
            string display = RoomErrorPresentationPolicy.GetDisplayMessage(new RoomErrorMessage { code = errorCode, message = message });
            var pendingCommand = _pendingRoomCommandAfterHello;
            _pendingRoomCommandAfterHello = null;
            _pendingReconnect = false;
            _ticketStore.Clear();
            ResetRoomState(true);

            if (pendingCommand != null)
            {
                // A new user action was waiting for connection. Hello has been accepted,
                // so execute the pending action instead of closing the socket.
                pendingCommand.Invoke();
                return;
            }

            _hasHelloAccepted = false;
            _helloHandshake.Reset();
            _pendingAfterConnect = null;
            WebSocketClient.Instance?.Disconnect();
            PublishRecoveryProgress(ClientRecoveryStage.TerminalFailure, display);
            RoomError?.Invoke(display);
        }

        private void PublishRecoveryProgress(ClientRecoveryStage stage, string message, int attempt = 0, int retryDelaySeconds = 0)
        {
            RecoveryProgressChanged?.Invoke(new ClientRecoveryProgress(stage, message, attempt, retryDelaySeconds));
        }

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
