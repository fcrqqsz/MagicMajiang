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
        private const float ConnectionHandshakeTimeoutSeconds = 10f;
        private readonly IClientReconnectTicketStore _ticketStore;
        private Action _pendingAfterConnect;
        private Action _pendingRoomCommandAfterHello;
        private bool _disposed;
        private bool _hasHelloAccepted;
        private float _nextHeartbeatAt;
        private float _lastHeartbeatAcknowledgementAt;
        private readonly Queue<float> _pendingHeartbeatSentAt = new Queue<float>();
        private bool _nonRecoveryHandshakeInFlight;
        private float _nonRecoveryHandshakeDeadlineAt;
        private bool _isReplacingNonRecoverySocket;
        private bool _resyncRequiredRaised;
        private bool _pendingReconnect;
        private bool _isComposingRecovery;
        private bool _reconnectAttemptInFlight;
        private bool _reconnectRetryScheduled;
        private bool _isUsingRecoveryServerOverride;
        private bool _isReturningToSelectedServer;
        private bool _awaitingRecoveryLeaveDelivery;
        private int _reconnectAttemptIndex;
        private float _nextReconnectAt;
        private float _reconnectHandshakeDeadlineAt;
        private string _activeUsername;
        private string _activeServerAddress;
        private string _selectedServerAddress;
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
        public bool CanSubmitCommands => !_pendingReconnect
                                         && !IsResyncRequired
                                         && !IsConnectionRecoveryRequired
                                         && !_isReturningToSelectedServer;
        public ClientGameState GameState => _gameState;
        public SnapshotSideboardState Sideboard => _roomState.Sideboard;
        public TalentActionOption[] AvailableTalentActions => _gameState.AvailableTalentActions;
        /// <summary>Monotonic presentation token for a completed full-snapshot recovery.</summary>
        public int RecoveryPresentationVersion { get; private set; }
        public string SelectedServerAddress => _selectedServerAddress;
        public ClientConnectionDiagnostics ConnectionDiagnostics { get; private set; }

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
        public event Action<ClientConnectionDiagnostics> ConnectionDiagnosticsChanged;
        /// <summary>Raised after a positive sequence has passed the gate and room state has been applied.</summary>
        public event Action<NetworkMessageEnvelope> AcceptedSequenceEnvelope;

        public ClientRoomService(string defaultAddress, IClientReconnectTicketStore ticketStore = null)
        {
            _selectedServerAddress = defaultAddress?.Trim();
            ConnectionDiagnostics = new ClientConnectionDiagnostics(
                _selectedServerAddress,
                ClientConnectionPhase.Disconnected,
                NetworkProtocol.Version,
                null,
                null,
                null);
            _ticketStore = ticketStore ?? new PlayerPrefsClientReconnectTicketStore();
            var client = EnsureWebSocketClient();
            client.OnConnected += HandleConnected;
            client.OnMessageReceived += HandleMessage;
            client.OnDisconnected += HandleDisconnected;
            client.OnError += HandleSocketError;
            client.OnMessageSent += HandleMessageSent;
            client.OnMessageSendFailed += HandleMessageSendFailed;
        }

        public bool TrySwitchServer(string address, string username)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            if (HasRoom || _pendingReconnect || IsConnectionRecoveryRequired || _isReturningToSelectedServer) return false;

            _selectedServerAddress = address.Trim();
            StartNonRecoveryHandshake(_selectedServerAddress, username);
            return true;
        }

        public bool TryReconnectSelectedServer(string username)
        {
            if (string.IsNullOrWhiteSpace(_selectedServerAddress)) return false;
            return TrySwitchServer(_selectedServerAddress, username);
        }

        public bool CreateRoom(
            GameMode gameMode,
            AlienationPreset roomPreset,
            string nickname)
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
                    }))) return false;
            ClearCompletedStateForNewRoom();
            return true;
        }

        public bool JoinRoom(string roomId, string nickname)
        {
            if (!CanStartNewRoomCommand()) return false;
            if (!TryBuildSelectedLoadout(out var loadout)) return false;
            if (!BeginRoomCommand(nickname,
                    () => Send("JoinRoom", new JoinRoomMessage { roomId = roomId, loadout = loadout }))) return false;
            ClearCompletedStateForNewRoom();
            return true;
        }

        public bool QueryRoomList(string nickname = null)
        {
            if (!CanSubmitRoomCommand()) return false;
            if (string.IsNullOrWhiteSpace(nickname))
                nickname = !string.IsNullOrWhiteSpace(_activeUsername) ? _activeUsername : "Player";

            return BeginRoomCommand(nickname, () => Send("QueryRoomList", new QueryRoomListMessage()));
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

        public bool CreateRoom(GameMode gameMode, string nickname) =>
            CreateRoom(gameMode, GetSelectedLoadoutAlienationPreset(), nickname);

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
                if (IsSessionCompleted)
                {
                    if (_isUsingRecoveryServerOverride) BeginReturnToSelectedServerAfterRecovery();
                    else ResetRoomState();
                }
                return;
            }
            if (_isUsingRecoveryServerOverride)
            {
                BeginReturnToSelectedServerAfterRecovery();
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
            if (_isUsingRecoveryServerOverride || _pendingReconnect || IsConnectionRecoveryRequired)
            {
                BeginReturnToSelectedServerAfterRecovery();
                return;
            }

            var client = WebSocketClient.Instance;
            if (HasRoom && _hasHelloAccepted && client?.ReadyState == WebSocketState.Open)
                Send("LeaveRoom", new LeaveRoomMessage());

            ResetRoomState(true);
            client?.Disconnect();
        }

        public void Tick(float unscaledTime)
        {
            if (_nonRecoveryHandshakeInFlight && unscaledTime >= _nonRecoveryHandshakeDeadlineAt)
            {
                FailNonRecoveryHandshake("Connection attempt timed out after 10 seconds.");
                WebSocketClient.Instance?.Disconnect();
                return;
            }
            if (_isReturningToSelectedServer) return;
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
            if (client.ReadyState == WebSocketState.Open
                && string.Equals(client.ActiveAddress, address, StringComparison.Ordinal))
            {
                action();
                return;
            }
            _pendingAfterConnect = action;
            client.Connect(address);
        }

        private void HandleConnected()
        {
            if (_nonRecoveryHandshakeInFlight)
                PublishConnectionDiagnostics(ClientConnectionPhase.Authenticating, null);
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

        private void HandleMessageSent(string json)
        {
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (_awaitingRecoveryLeaveDelivery && envelope?.type == "LeaveRoom")
            {
                FinishReturnToSelectedServer();
                return;
            }

            if (!_hasHelloAccepted) return;
            if (envelope?.type == "Heartbeat")
                _pendingHeartbeatSentAt.Enqueue(Time.unscaledTime);
        }

        private void HandleMessageSendFailed(string json)
        {
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (!_awaitingRecoveryLeaveDelivery || envelope?.type != "LeaveRoom") return;

            _awaitingRecoveryLeaveDelivery = false;
            FinishReturnToSelectedServer();
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
                    if (_nonRecoveryHandshakeInFlight
                        || (!_hasHelloAccepted && !_pendingReconnect)
                        || error?.code == NetworkErrorCodes.ProtocolMismatch)
                        FailNonRecoveryHandshake(RoomErrorPresentationPolicy.GetDisplayMessage(error));
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
            _pendingHeartbeatSentAt.Clear();

            if (_isReturningToSelectedServer)
            {
                if (_awaitingRecoveryLeaveDelivery)
                {
                    _awaitingRecoveryLeaveDelivery = false;
                    FinishReturnToSelectedServer();
                }
                else if (_nonRecoveryHandshakeInFlight)
                {
                    FailNonRecoveryHandshake(string.IsNullOrWhiteSpace(reason)
                        ? "Connection closed before authentication completed."
                        : $"Connection closed before authentication completed: {reason}");
                }
                return;
            }

            if (_nonRecoveryHandshakeInFlight)
            {
                FailNonRecoveryHandshake(string.IsNullOrWhiteSpace(reason)
                    ? "Connection closed before authentication completed."
                    : $"Connection closed before authentication completed: {reason}");
                return;
            }

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
                return;
            }

            if (!_isReplacingNonRecoverySocket
                && ConnectionDiagnostics.Phase == ClientConnectionPhase.Ready)
                PublishConnectionDiagnostics(ClientConnectionPhase.Disconnected, null, clearMeasurement: true);
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
            _nonRecoveryHandshakeInFlight = false;
            _nonRecoveryHandshakeDeadlineAt = 0f;
            _pendingHeartbeatSentAt.Clear();
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

            if (_nonRecoveryHandshakeInFlight)
            {
                _nonRecoveryHandshakeInFlight = false;
                _nonRecoveryHandshakeDeadlineAt = 0f;
                PublishConnectionDiagnostics(ClientConnectionPhase.Ready, null);
            }

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
            if (_isReturningToSelectedServer)
                _isReturningToSelectedServer = false;

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
            if (_pendingHeartbeatSentAt.Count == 0) return;
            float sentAt = _pendingHeartbeatSentAt.Dequeue();
            int roundTripMilliseconds = (int)Math.Round((Time.unscaledTime - sentAt) * 1000f);
            PublishConnectionDiagnostics(
                ConnectionDiagnostics.Phase,
                ConnectionDiagnostics.LastError,
                roundTripMilliseconds,
                DateTime.UtcNow);
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

        private bool BeginRoomCommand(string nickname, Action roomCommand)
        {
            _activeUsername = nickname?.Trim();
            _activeServerAddress = _selectedServerAddress;
            var client = EnsureWebSocketClient();
            if (_helloHandshake.IsHelloAccepted
                && (client.ReadyState != WebSocketState.Open
                    || !string.Equals(client.ActiveAddress, _selectedServerAddress, StringComparison.Ordinal)))
            {
                _hasHelloAccepted = false;
                _helloHandshake.Reset();
            }
            switch (_helloHandshake.BeginRoomCommand())
            {
                case ClientHelloHandshakeAction.SendRoomCommand:
                    SendWhenConnected(roomCommand, _selectedServerAddress);
                    return true;
                case ClientHelloHandshakeAction.SendHello:
                    _pendingRoomCommandAfterHello = roomCommand;
                    StartNonRecoveryHandshake(_selectedServerAddress, nickname, false, false);
                    return true;
                default:
                    RoomError?.Invoke("Authentication is in progress. Wait for the server response before sending another room command.");
                    return false;
            }
        }

        private void StartNonRecoveryHandshake(
            string address,
            string username,
            bool resetPendingRoomCommand = true,
            bool resetHelloHandshake = true)
        {
            var client = EnsureWebSocketClient();
            var pendingRoomCommand = resetPendingRoomCommand ? null : _pendingRoomCommandAfterHello;
            _nonRecoveryHandshakeInFlight = false;
            _nonRecoveryHandshakeDeadlineAt = 0f;
            _isReplacingNonRecoverySocket = true;
            try
            {
                client.Disconnect();
            }
            finally
            {
                _isReplacingNonRecoverySocket = false;
            }

            _activeUsername = username?.Trim();
            _activeServerAddress = address;
            _pendingAfterConnect = null;
            _pendingRoomCommandAfterHello = pendingRoomCommand;
            _hasHelloAccepted = false;
            if (resetHelloHandshake)
            {
                _helloHandshake.Reset();
                _helloHandshake.BeginRoomCommand();
            }
            _pendingHeartbeatSentAt.Clear();
            _nonRecoveryHandshakeInFlight = true;
            _nonRecoveryHandshakeDeadlineAt = Time.unscaledTime + ConnectionHandshakeTimeoutSeconds;
            PublishConnectionDiagnostics(ClientConnectionPhase.Connecting, null, clearMeasurement: true);
            SendWhenConnected(() => Send("Hello", ClientHelloProtocol.Create(_activeUsername)), address);
        }

        private void FailNonRecoveryHandshake(string error)
        {
            _nonRecoveryHandshakeInFlight = false;
            _nonRecoveryHandshakeDeadlineAt = 0f;
            _pendingAfterConnect = null;
            _pendingRoomCommandAfterHello = null;
            _hasHelloAccepted = false;
            _helloHandshake.Reset();
            _pendingHeartbeatSentAt.Clear();
            _isReturningToSelectedServer = false;
            PublishConnectionDiagnostics(ClientConnectionPhase.Failed, error);
        }

        private void HandleSocketError(string error)
        {
            if (_isReturningToSelectedServer && _awaitingRecoveryLeaveDelivery)
            {
                _awaitingRecoveryLeaveDelivery = false;
                FinishReturnToSelectedServer();
                return;
            }

            if (_nonRecoveryHandshakeInFlight)
            {
                FailNonRecoveryHandshake(string.IsNullOrWhiteSpace(error) ? "Socket error." : error);
                return;
            }

            if (!_pendingReconnect)
                PublishConnectionDiagnostics(ClientConnectionPhase.Failed,
                    string.IsNullOrWhiteSpace(error) ? "Socket error." : error);
        }

        private void PublishConnectionDiagnostics(
            ClientConnectionPhase phase,
            string error,
            int? roundTripTimeMilliseconds = null,
            DateTime? lastCheckedUtc = null,
            bool clearMeasurement = false)
        {
            int? rtt = clearMeasurement ? null : roundTripTimeMilliseconds ?? ConnectionDiagnostics?.RoundTripTimeMilliseconds;
            DateTime? checkedUtc = clearMeasurement ? null : lastCheckedUtc ?? ConnectionDiagnostics?.LastCheckedUtc;
            ConnectionDiagnostics = new ClientConnectionDiagnostics(
                _selectedServerAddress,
                phase,
                NetworkProtocol.Version,
                rtt,
                checkedUtc,
                error);
            ConnectionDiagnosticsChanged?.Invoke(ConnectionDiagnostics);
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
                if (_gameState.Snapshot != null)
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
            _isUsingRecoveryServerOverride = true;
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
            _pendingRoomCommandAfterHello = null;
            BeginReturnToSelectedServerAfterRecovery();
            PublishRecoveryProgress(ClientRecoveryStage.TerminalFailure, display);
            RoomError?.Invoke(display);
        }

        private void BeginReturnToSelectedServerAfterRecovery()
        {
            if (_isReturningToSelectedServer) return;
            _isReturningToSelectedServer = true;

            var client = WebSocketClient.Instance;
            if (_isUsingRecoveryServerOverride
                && HasRoom
                && _hasHelloAccepted
                && client?.ReadyState == WebSocketState.Open)
            {
                _awaitingRecoveryLeaveDelivery = true;
                Send("LeaveRoom", new LeaveRoomMessage());
                return;
            }

            FinishReturnToSelectedServer();
        }

        private void FinishReturnToSelectedServer()
        {
            _awaitingRecoveryLeaveDelivery = false;
            var client = WebSocketClient.Instance;
            bool selectedTargetIsAlreadyReady = _hasHelloAccepted
                                                && client?.ReadyState == WebSocketState.Open
                                                && string.Equals(client.ActiveAddress, _selectedServerAddress, StringComparison.Ordinal);

            ResetRoomState(true);
            _isUsingRecoveryServerOverride = false;
            if (selectedTargetIsAlreadyReady)
            {
                _isReturningToSelectedServer = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedServerAddress))
            {
                _isReturningToSelectedServer = false;
                return;
            }

            StartNonRecoveryHandshake(_selectedServerAddress, _activeUsername);
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
            WebSocketClient.Instance.OnError -= HandleSocketError;
            WebSocketClient.Instance.OnMessageSent -= HandleMessageSent;
            WebSocketClient.Instance.OnMessageSendFailed -= HandleMessageSendFailed;
        }
    }
}
