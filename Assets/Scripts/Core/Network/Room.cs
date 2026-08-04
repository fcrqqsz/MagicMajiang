using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Core.Services;
using MahjongGame.Talents;
using UnityEngine;

namespace MahjongGame.Core.Network
{
    public sealed class RoomSeat
    {
        public int SeatIndex;
        public string PlayerId;
        public string ConnectionId;
        public GameEndpoint Endpoint;
        public SeatMessageStream MessageStream;
        public string DisplayName;
        public bool IsAi;
        /// <summary>Physical endpoint presence. This does not change when temporary AI owns one decision.</summary>
        public bool IsOnline;
        public RoomSeatControlState ControlState;
        public DateTime OfflineExpiresAtUtc;
        public bool MatchReady;
        public bool SceneReady;
        public bool IsLoadoutLocked;
        public TrustedPlayerLoadout Loadout;
        public StableSeatController Controller;
    }

    /// <summary>Dedicated-server room and the sole owner of its GameSession/GameServer lifecycle.</summary>
    public sealed class Room : IDisposable
    {
        private readonly bool _aiFill;
        private readonly int _messageCacheSize;
        private readonly RoomSeat[] _seats = new RoomSeat[4];
        private readonly List<DeckConfig> _deckConfigs = new List<DeckConfig>();
        private readonly Dictionary<int, TalentSlotConfig> _talentConfigs = new Dictionary<int, TalentSlotConfig>();
        private readonly NetworkDecisionTracker _decisionTracker = new NetworkDecisionTracker();
        private readonly HashSet<string> _expiredPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _startingCapitalApplied;

        public string RoomId { get; }
        public GameMode GameMode { get; }
        public AlienationPreset AlienationPreset { get; }
        public GameSession Session { get; }
        public GameServer GameServer { get; private set; }
        public RoomState State { get; private set; } = RoomState.WaitingForPlayers;
        public string HostConnectionId { get; }
        public bool AiFillEnabled => _aiFill;
        public IReadOnlyList<RoomSeat> Seats => _seats;
        public bool HasHumanPlayers => _seats.Any(s => s != null && !s.IsAi);
        public int OnlineHumanCount => _seats.Count(s => s != null
            && RoomLifecyclePolicy.ShouldCountAsOnlineHuman(s.IsAi, s.IsOnline));
        public event Action<Room> OnClosed;

        public Room(string roomId, GameMode gameMode, AlienationPreset alienationPreset, string hostConnectionId, bool aiFill,
            int messageCacheSize = SeatMessageStream.DefaultCacheCapacity)
        {
            RoomId = roomId;
            GameMode = gameMode;
            AlienationPreset = AlienationBudgetPolicy.IsDefined(alienationPreset)
                ? alienationPreset
                : throw new ArgumentOutOfRangeException(nameof(alienationPreset));
            HostConnectionId = hostConnectionId;
            _aiFill = aiFill;
            _messageCacheSize = Math.Max(1, messageCacheSize);
            Session = new GameSession(gameMode);
        }

        public bool TryAddHuman(string connectionId, GameEndpoint endpoint, string playerId, string displayName, TrustedPlayerLoadout loadout, out int seatIndex)
        {
            seatIndex = -1;
            if (loadout == null || string.IsNullOrWhiteSpace(playerId)
                || loadout.TotalAlienation > AlienationBudgetPolicy.GetLimit(AlienationPreset)
                || (State != RoomState.WaitingForPlayers && State != RoomState.WaitingForMatchReady)) return false;
            if (RoomMembershipPolicy.RequiresReconnect(
                    _seats.Where(seat => seat != null && !seat.IsAi).Select(seat => seat.PlayerId), playerId)) return false;
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i] != null) continue;
                var trustedLoadout = PlayerLoadoutCodec.CloneTrustedLoadout(loadout);
                if (trustedLoadout == null) return false;
                _expiredPlayerIds.Remove(playerId);
                _seats[i] = new RoomSeat
                {
                    SeatIndex = i,
                    PlayerId = playerId,
                    ConnectionId = connectionId,
                    Endpoint = endpoint,
                    MessageStream = new SeatMessageStream(endpoint, _messageCacheSize),
                    DisplayName = displayName,
                    IsAi = false,
                    IsOnline = true,
                    ControlState = RoomSeatControlState.OnlineHuman,
                    Loadout = trustedLoadout
                };
                seatIndex = i;
                State = RoomState.WaitingForMatchReady;
                return true;
            }
            return false;
        }

        public bool HasOfflineReservation(string playerId)
        {
            return _seats.Any(seat => seat != null
                && RoomMembershipPolicy.RequiresReconnectForDisconnectedHumanSeat(
                    seat.IsAi, seat.IsOnline, seat.PlayerId, playerId));
        }

        public bool RemoveHuman(string connectionId, out int seatIndex)
        {
            seatIndex = -1;
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i]?.ConnectionId != connectionId) continue;
                seatIndex = i;
                _seats[i] = null;
                if (State == RoomState.WaitingForMatchReady || State == RoomState.WaitingForPlayers) State = HasHumans ? RoomState.WaitingForMatchReady : RoomState.WaitingForPlayers;
                return true;
            }
            return false;
        }

        public bool HandleDisconnect(string playerId, string connectionId, GameEndpoint endpoint, DateTime utcNow,
            TimeSpan reconnectWindow, out int seatIndex, out bool shouldCloseRoom)
        {
            seatIndex = -1;
            shouldCloseRoom = false;
            var seat = FindHumanSeat(playerId, connectionId);
            if (seat == null || seat.IsAi) return false;

            seatIndex = seat.SeatIndex;
            seat.ConnectionId = null;
            seat.MessageStream?.DetachEndpoint(endpoint);
            seat.Endpoint = null;
            seat.IsOnline = false;
            seat.OfflineExpiresAtUtc = utcNow.Add(reconnectWindow);
            seat.ControlState = RoomSeatControlState.OfflineReserved;
            seat.Controller?.MarkOffline();
            if (State == RoomState.LoadingGameScene) seat.SceneReady = true;
            if (RoomLifecyclePolicy.ShouldAutoReadyOfflineSeat(State)) seat.MatchReady = true;
            shouldCloseRoom = RoomLifecyclePolicy.ShouldCloseWhenNoHumanOnline(OnlineHumanCount);
            return true;
        }

        public bool HandleExplicitLeave(string playerId, string connectionId, out int seatIndex, out bool shouldCloseRoom)
        {
            seatIndex = -1;
            shouldCloseRoom = false;
            var seat = FindHumanSeat(playerId, connectionId);
            if (seat == null || seat.IsAi) return false;

            seatIndex = seat.SeatIndex;
            if (State == RoomState.WaitingForPlayers || State == RoomState.WaitingForMatchReady)
            {
                _expiredPlayerIds.Add(seat.PlayerId);
                _seats[seat.SeatIndex] = null;
            }
            else
            {
                ConvertToPermanentAi(seat);
            }
            shouldCloseRoom = RoomLifecyclePolicy.ShouldCloseWhenNoHumanOnline(OnlineHumanCount);
            return true;
        }

        public bool TryReconnect(string playerId, string streamId, string connectionId, GameEndpoint endpoint,
            int lastSeq, bool hasProjection, DateTime utcNow, out int seatIndex, out ReconnectStateMessage recovery, out string errorCode)
        {
            seatIndex = -1;
            recovery = null;
            errorCode = null;
            var seat = _seats.FirstOrDefault(candidate => candidate != null && !candidate.IsAi
                && string.Equals(candidate.PlayerId, playerId, StringComparison.OrdinalIgnoreCase));
            if (seat == null || seat.ControlState == RoomSeatControlState.PermanentAi || _expiredPlayerIds.Contains(playerId))
            {
                errorCode = NetworkErrorCodes.SeatExpired;
                return false;
            }
            if (!string.Equals(seat.MessageStream?.StreamId, streamId, StringComparison.Ordinal))
            {
                errorCode = NetworkErrorCodes.StreamMismatch;
                return false;
            }
            if (seat.OfflineExpiresAtUtc != default && utcNow >= seat.OfflineExpiresAtUtc)
            {
                errorCode = NetworkErrorCodes.SeatExpired;
                return false;
            }

            seat.ConnectionId = connectionId;
            seat.Endpoint = endpoint;
            seat.IsOnline = true;
            seat.OfflineExpiresAtUtc = default;
            seat.Controller?.MarkOnline();
            seat.ControlState = GameServer?.ActiveDecision != null && seat.Controller?.IsAiControllingActiveDecision == true
                ? RoomSeatControlState.AiControlled
                : RoomSeatControlState.OnlineHuman;
            int recoveredSeatIndex = seat.SeatIndex;
            seatIndex = recoveredSeatIndex;
            recovery = seat.MessageStream.DeliverReconnectState(endpoint, lastSeq, hasProjection, () => BuildSnapshot(recoveredSeatIndex));
            return true;
        }

        public bool TryResync(int seatIndex, string streamId, int lastSeq, GameEndpoint endpoint, out ReconnectStateMessage recovery, out string errorCode)
        {
            recovery = null;
            errorCode = null;
            if (seatIndex < 0 || seatIndex >= _seats.Length || _seats[seatIndex] == null || _seats[seatIndex].IsAi)
            {
                errorCode = NetworkErrorCodes.SeatExpired;
                return false;
            }
            var seat = _seats[seatIndex];
            if (!string.Equals(seat.MessageStream?.StreamId, streamId, StringComparison.Ordinal))
            {
                errorCode = NetworkErrorCodes.StreamMismatch;
                return false;
            }
            recovery = seat.MessageStream.DeliverReconnectState(endpoint, lastSeq, true, () => BuildSnapshot(seatIndex));
            return true;
        }

        public bool ExpireOfflineSeats(DateTime utcNow, out List<int> changedSeats, out bool shouldCloseRoom)
        {
            changedSeats = new List<int>();
            shouldCloseRoom = false;
            for (int i = 0; i < _seats.Length; i++)
            {
                var seat = _seats[i];
                if (seat == null || seat.IsAi
                    || !RoomLifecyclePolicy.ShouldExpireOfflineSeat(seat.IsOnline, seat.OfflineExpiresAtUtc, utcNow)) continue;

                _expiredPlayerIds.Add(seat.PlayerId);
                if (RoomLifecyclePolicy.GetExpiryDisposition(State) == RoomSeatExpiryDisposition.Vacant)
                {
                    _seats[i] = null;
                }
                else
                {
                    ConvertToPermanentAi(seat);
                }
                changedSeats.Add(i);
            }

            shouldCloseRoom = changedSeats.Count > 0 && RoomLifecyclePolicy.ShouldCloseWhenNoHumanOnline(OnlineHumanCount);
            return changedSeats.Count > 0;
        }

        /// <summary>Legacy departure entry point retained for callers compiled before E3.</summary>
        public bool HandleWaitingHumanDeparture(string connectionId, out int seatIndex, out bool replacedByAi, out string replacementDisplayName)
        {
            seatIndex = -1;
            replacedByAi = false;
            replacementDisplayName = null;
            if (!HandleExplicitLeave(null, connectionId, out seatIndex, out bool shouldCloseRoom) || shouldCloseRoom) return false;
            var seat = seatIndex >= 0 && seatIndex < _seats.Length ? _seats[seatIndex] : null;
            replacedByAi = seat?.IsAi ?? false;
            replacementDisplayName = replacedByAi ? seat.DisplayName : null;
            return true;
        }

        /// <summary>Continues a waiting-stage transition after a human seat changed ownership.</summary>
        public void AdvanceAfterWaitingMemberChange()
        {
            if (!RoomLifecyclePolicy.ShouldAdvanceAfterWaitingMemberChange(_aiFill, HasHumanPlayers)) return;

            if (State == RoomState.WaitingForMatchReady && AllHumans(seat => seat.MatchReady))
            {
                TryBeginLoadingGameScene();
            }
            else if (State == RoomState.LoadingGameScene && AllHumans(seat => seat.SceneReady))
            {
                StartRound();
            }
            else if (State == RoomState.WaitingForNextRound && AllHumans(seat => seat.MatchReady))
            {
                StartRound();
            }
        }

        public bool SetReady(string connectionId, ReadyPhase phase, out string error)
        {
            error = null;
            var seat = _seats.FirstOrDefault(s => s?.ConnectionId == connectionId);
            if (seat == null) { error = "You are not a member of this room."; return false; }

            if (phase == ReadyPhase.MatchStart && State == RoomState.WaitingForMatchReady)
            {
                if (!RoomReadyPolicy.CanMarkMatchReady(_aiFill, HumanCount))
                {
                    error = "This server requires four human players when AI fill is disabled.";
                    return false;
                }

                seat.MatchReady = true;
                if (AllHumans(s => s.MatchReady))
                {
                    if (!TryBeginLoadingGameScene())
                    {
                        error = "Could not lock all room seat loadouts.";
                        return false;
                    }
                }
                return true;
            }

            if (phase == ReadyPhase.GameSceneLoaded && State == RoomState.LoadingGameScene)
            {
                seat.SceneReady = true;
                if (AllHumans(s => s.SceneReady)) StartRound();
                return true;
            }

            if (phase == ReadyPhase.NextRound && State == RoomState.WaitingForNextRound)
            {
                seat.MatchReady = true;
                if (AllHumans(s => s.MatchReady)) StartRound();
                return true;
            }

            error = "Ready is not valid in the current room state.";
            return false;
        }

        public bool SubmitAction(int seatIndex, ClientActionMessage message)
        {
            return SubmitAction(seatIndex, message, out _);
        }

        public bool SubmitAction(int seatIndex, ClientActionMessage message, out string errorCode)
        {
            errorCode = null;
            if (State != RoomState.InRound || GameServer == null)
            {
                errorCode = NetworkErrorCodes.NoActiveDecision;
                return false;
            }
            if (seatIndex < 0 || seatIndex > 3 || message == null)
            {
                errorCode = NetworkErrorCodes.InvalidAction;
                return false;
            }
            if (_seats[seatIndex]?.Controller != null && !_seats[seatIndex].Controller.IsHumanSubmissionAllowed(message.decisionId))
            {
                errorCode = NetworkErrorCodes.WrongController;
                return false;
            }
            Debug.Log($"[Room {RoomId}] Action {((ClientActionType)message.actionType)} received from bound seat {seatIndex}.");
            var action = new ClientAction(seatIndex, (ClientActionType)message.actionType, message.targetTile?.ToTileData(), message.chiCombinations);
            action.SetHuDetails(message.totalFan, message.fanDetails?.ToList());
            return GameServer.SubmitNetworkAction(seatIndex, message.decisionId, action, out errorCode);
        }

        public RoomSeatMessage[] GetSeatSnapshot()
        {
            return Enumerable.Range(0, 4).Select(GetSeatMessage).ToArray();
        }

        public RoomSeatMessage GetSeatMessage(int seatIndex)
        {
            var seat = seatIndex >= 0 && seatIndex < _seats.Length ? _seats[seatIndex] : null;
            return new RoomSeatMessage
            {
                seatIndex = seatIndex,
                isOccupied = seat != null,
                isAi = seat?.IsAi ?? false,
                isOnline = seat != null && (seat.IsAi || seat.IsOnline),
                isTemporarilyAiControlled = seat?.ControlState == RoomSeatControlState.AiControlled,
                controlState = seat?.ControlState.ToString() ?? RoomSeatControlState.Vacant.ToString(),
                isReady = seat != null && (seat.MatchReady || seat.SceneReady),
                displayName = seat?.DisplayName
            };
        }

        /// <summary>Builds a privacy-filtered authoritative table snapshot for one already-bound seat.</summary>
        public RoomGameSnapshot BuildSnapshot(int requestingSeatIndex)
        {
            if (requestingSeatIndex < 0 || requestingSeatIndex >= _seats.Length)
                throw new ArgumentOutOfRangeException(nameof(requestingSeatIndex));

            var source = new RoomGameSnapshotSource
            {
                RoomId = RoomId,
                RoomState = State,
                GameMode = GameMode,
                AlienationPreset = AlienationPreset,
                OwnTotalAlienation = _seats[requestingSeatIndex]?.Loadout?.TotalAlienation ?? 0,
                Session = Session,
                Seats = new RoomSnapshotSeatSource[4],
                Hands = new List<TileData>[4],
                Melds = new List<Meld>[4],
                Rivers = new List<TileData>[4],
                ScoringOptions = new ScoringOptions[4],
                PeekWallTiles = new List<TileData>[4],
                RemainingWallCount = GameServer?.RemainingWallCount ?? 0,
                ActiveDecision = GameServer?.ActiveDecision,
                MainTurnDrawnTile = GameServer?.LastDrawnTile,
                WinnerId = GameServer?.WinnerId ?? Session.LastWinnerId,
                WinFan = GameServer?.WinFan ?? Session.LastFanCount,
                FanDetails = GameServer?.WinFanDetails?.ToArray() ?? Array.Empty<string>(),
                WinIsSelfDraw = GameServer?.WinIsSelfDraw ?? Session.LastIsSelfDraw,
                WinKind = GameServer?.WinResultKind ?? (Session.LastWinnerId >= 0
                    ? Session.LastIsSelfDraw ? WinKind.SelfDraw : WinKind.Discard
                    : WinKind.Unknown),
                LoserId = GameServer?.LoserId ?? Session.LastLoserId,
                IsDrawGame = GameServer?.IsDrawGame ?? false,
                WinningHand = GameServer?.WinningHandSnapshot
            };

            for (int i = 0; i < _seats.Length; i++)
            {
                var seat = _seats[i];
                source.Seats[i] = new RoomSnapshotSeatSource
                {
                    SeatIndex = i,
                    IsOccupied = seat != null,
                    IsAi = seat?.IsAi ?? false,
                    IsOnline = seat != null && (seat.IsAi || seat.IsOnline),
                    DisplayName = seat?.DisplayName,
                    Controller = seat?.ControlState.ToString()
                };
                source.Hands[i] = GameServer?.GetHandSnapshot(i) ?? new List<TileData>();
                source.Melds[i] = GameServer?.GetMeldSnapshot(i) ?? new List<Meld>();
                source.Rivers[i] = GameServer?.GetRiverSnapshot(i) ?? new List<TileData>();
                source.ScoringOptions[i] = GameServer?.GetScoringOptionsSnapshot(i) ?? new ScoringOptions();
                source.PeekWallTiles[i] = GameServer?.GetPeekWallSnapshot(i) ?? new List<TileData>();
            }

            return RoomGameSnapshotBuilder.Build(source, requestingSeatIndex);
        }

        public void Broadcast(string type, object payload)
        {
            foreach (var seat in _seats)
                if (seat != null && !seat.IsAi) seat.MessageStream.Send(type, payload);
        }

        public bool TrySendToHumanSeat(int seatIndex, string type, object payload)
        {
            if (seatIndex < 0 || seatIndex >= _seats.Length) return false;
            var seat = _seats[seatIndex];
            if (seat == null || seat.IsAi || seat.MessageStream == null) return false;

            seat.MessageStream.Send(type, payload);
            return true;
        }

        public void Close()
        {
            if (State == RoomState.Closed) return;
            State = RoomState.Closed;
            if (GameServer != null)
            {
                GameServer.OnRoundFinished -= OnRoundFinished;
                GameServer.StopGame();
                GameServer = null;
            }
            OnClosed?.Invoke(this);
        }

        public void Dispose() => Close();

        private bool HasHumans => HasHumanPlayers;
        private int HumanCount => _seats.Count(s => s != null && !s.IsAi);
        private bool AllHumans(Func<RoomSeat, bool> predicate) => _seats.Where(s => s != null && !s.IsAi).All(predicate);

        private static RoomSeat CreateAiSeat(int seatIndex, TrustedPlayerLoadout loadout, bool loadoutLocked)
        {
            return new RoomSeat
            {
                SeatIndex = seatIndex,
                IsAi = true,
                IsOnline = true,
                ControlState = RoomSeatControlState.PermanentAi,
                DisplayName = $"AI {seatIndex + 1}",
                MatchReady = true,
                SceneReady = true,
                IsLoadoutLocked = loadoutLocked,
                Loadout = PlayerLoadoutCodec.CloneTrustedLoadout(loadout) ?? PlayerLoadoutCodec.CreateStandardLoadout()
            };
        }

        private bool TryBeginLoadingGameScene()
        {
            if (!TryLockSeatLoadouts()) return false;
            State = RoomState.LoadingGameScene;
            Broadcast("RoomReady", new RoomReadyMessage { roomId = RoomId });
            return true;
        }

        private bool TryLockSeatLoadouts()
        {
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i] == null)
                {
                    if (!_aiFill) return false;
                    _seats[i] = CreateAiSeat(i, PlayerLoadoutCodec.CreateStandardLoadout(), false);
                }

                if (_seats[i].Loadout == null)
                {
                    if (!_seats[i].IsAi) return false;
                    _seats[i].Loadout = PlayerLoadoutCodec.CreateStandardLoadout();
                }

                _seats[i].IsLoadoutLocked = true;
            }

            return true;
        }

        private void StartRound()
        {
            if (GameServer != null)
            {
                GameServer.OnRoundFinished -= OnRoundFinished;
                GameServer.StopGame();
            }

            if (_seats.Any(s => s == null || !s.IsLoadoutLocked || s.Loadout == null)) { State = RoomState.WaitingForMatchReady; return; }

            _deckConfigs.Clear();
            _talentConfigs.Clear();
            var clients = new List<IPlayerClient>(4);
            for (int i = 0; i < 4; i++)
            {
                _deckConfigs.Add(_seats[i].Loadout.DeckConfig);
                _talentConfigs[i] = _seats[i].Loadout.TalentConfig;
                if (_seats[i].IsAi)
                {
                    _seats[i].Controller = null;
                    clients.Add(new SimpleAIClient(i, null));
                }
                else
                {
                    var seat = _seats[i];
                    seat.Controller ??= new StableSeatController(i, seat.MessageStream, Session,
                        () => seat.IsOnline,
                        controller => UpdateSeatControllerState(seat, controller));
                    seat.Controller.SetSession(Session);
                    clients.Add(seat.Controller);
                }
            }

            SessionTalentPolicy.ApplyStartingCapitalOnce(Session, _talentConfigs, ref _startingCapitalApplied);

            GameServer = new GameServer(new WallService(), new GameServerOptions
            {
                DecisionTracker = _decisionTracker
            });
            foreach (var client in clients.OfType<SimpleAIClient>()) client.SetServer(GameServer);
            foreach (var seat in _seats.Where(seat => seat?.Controller != null)) seat.Controller.SetServer(GameServer);
            GameServer.OnRoundFinished += OnRoundFinished;
            State = RoomState.InRound;
            GameServer.StartGame(clients, _deckConfigs, Session, _talentConfigs);
        }

        private void OnRoundFinished()
        {
            if (GameServer != null) GameServer.OnRoundFinished -= OnRoundFinished;
            Session.AdvanceRound();
            if (Session.IsSessionOver())
            {
                foreach (var seat in _seats)
                    if (seat != null && !seat.IsAi) seat.Controller?.OnSessionEnd(Session.Scores);
                State = RoomState.SessionCompleted;
                return;
            }

            foreach (var seat in _seats.Where(s => s != null && !s.IsAi))
                seat.MatchReady = RoomLifecyclePolicy.ShouldAutoReadyNextRoundSeat(seat.IsOnline);
            State = RoomState.WaitingForNextRound;
        }

        private RoomSeat FindHumanSeat(string playerId, string connectionId)
        {
            return _seats.FirstOrDefault(seat => seat != null && !seat.IsAi
                && ((!string.IsNullOrEmpty(playerId) && string.Equals(seat.PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(connectionId) && string.Equals(seat.ConnectionId, connectionId, StringComparison.Ordinal))));
        }

        private void ConvertToPermanentAi(RoomSeat seat)
        {
            if (seat == null) return;
            seat.MessageStream?.DetachEndpoint(seat.Endpoint);
            seat.Endpoint = null;
            seat.ConnectionId = null;
            seat.IsOnline = false;
            seat.OfflineExpiresAtUtc = default;
            seat.IsAi = true;
            seat.ControlState = RoomSeatControlState.PermanentAi;
            seat.MatchReady = true;
            seat.SceneReady = true;
            seat.Controller?.SetPermanentAi();
        }

        private void UpdateSeatControllerState(RoomSeat seat, DecisionControllerKind controller)
        {
            if (seat == null || seat.IsAi || State == RoomState.Closed) return;
            seat.ControlState = controller == DecisionControllerKind.AI
                ? RoomSeatControlState.AiControlled
                : seat.IsOnline ? RoomSeatControlState.OnlineHuman : RoomSeatControlState.OfflineReserved;
            Broadcast("RoomSeatUpdated", new RoomSeatUpdatedMessage { roomId = RoomId, seat = GetSeatMessage(seat.SeatIndex) });
        }
    }

}
