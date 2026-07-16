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
        public string ConnectionId;
        public GameEndpoint Endpoint;
        public string DisplayName;
        public bool IsAi;
        public bool MatchReady;
        public bool SceneReady;
        public bool IsLoadoutLocked;
        public TrustedPlayerLoadout Loadout;
    }

    /// <summary>Dedicated-server room and the sole owner of its GameSession/GameServer lifecycle.</summary>
    public sealed class Room : IDisposable
    {
        private readonly bool _aiFill;
        private readonly Action<string, object> _send;
        private readonly RoomSeat[] _seats = new RoomSeat[4];
        private readonly List<DeckConfig> _deckConfigs = new List<DeckConfig>();
        private readonly Dictionary<int, TalentSlotConfig> _talentConfigs = new Dictionary<int, TalentSlotConfig>();
        private bool _startingCapitalApplied;

        public string RoomId { get; }
        public GameMode GameMode { get; }
        public GameSession Session { get; }
        public GameServer GameServer { get; private set; }
        public RoomState State { get; private set; } = RoomState.WaitingForPlayers;
        public string HostConnectionId { get; }
        public bool AiFillEnabled => _aiFill;
        public IReadOnlyList<RoomSeat> Seats => _seats;
        public bool HasHumanPlayers => _seats.Any(s => s != null && !s.IsAi);
        public event Action<Room> OnClosed;

        public Room(string roomId, GameMode gameMode, string hostConnectionId, bool aiFill, Action<string, object> send)
        {
            RoomId = roomId;
            GameMode = gameMode;
            HostConnectionId = hostConnectionId;
            _aiFill = aiFill;
            _send = send;
            Session = new GameSession(gameMode);
        }

        public bool TryAddHuman(string connectionId, GameEndpoint endpoint, string displayName, TrustedPlayerLoadout loadout, out int seatIndex)
        {
            seatIndex = -1;
            if (loadout == null || (State != RoomState.WaitingForPlayers && State != RoomState.WaitingForMatchReady)) return false;
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i] != null) continue;
                var trustedLoadout = PlayerLoadoutCodec.CloneTrustedLoadout(loadout);
                if (trustedLoadout == null) return false;
                _seats[i] = new RoomSeat
                {
                    SeatIndex = i,
                    ConnectionId = connectionId,
                    Endpoint = endpoint,
                    DisplayName = displayName,
                    IsAi = false,
                    Loadout = trustedLoadout
                };
                seatIndex = i;
                State = RoomState.WaitingForMatchReady;
                return true;
            }
            return false;
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

        /// <summary>Preserves a non-playing room after a human leaves, keeping pre-match seats open for rejoining players.</summary>
        public bool HandleWaitingHumanDeparture(string connectionId, out int seatIndex, out bool replacedByAi, out string replacementDisplayName)
        {
            seatIndex = -1;
            replacedByAi = false;
            replacementDisplayName = null;

            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i]?.ConnectionId != connectionId) continue;

                bool hasRemainingHumans = _seats.Any(seat => seat != null && !seat.IsAi && seat.ConnectionId != connectionId);
                if (!RoomDeparturePolicy.ShouldKeepRoomAfterDeparture(State, hasRemainingHumans, _aiFill)) return false;

                var departingSeat = _seats[i];
                seatIndex = i;
                if (RoomDeparturePolicy.ShouldReplaceWithAi(State, _aiFill))
                {
                    bool preserveLockedLoadout = departingSeat.IsLoadoutLocked
                        && (State == RoomState.LoadingGameScene || State == RoomState.WaitingForNextRound);
                    _seats[i] = preserveLockedLoadout
                        ? CreateAiSeat(i, departingSeat.Loadout, true)
                        : CreateAiSeat(i, PlayerLoadoutCodec.CreateStandardLoadout(), false);
                    replacementDisplayName = _seats[i].DisplayName;
                    replacedByAi = true;
                }
                else
                {
                    _seats[i] = null;
                }
                return true;
            }

            return false;
        }

        /// <summary>Continues a waiting-stage transition after a human seat changed ownership.</summary>
        public void AdvanceAfterWaitingMemberChange()
        {
            if (!_aiFill || !HasHumanPlayers) return;

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
            if (State != RoomState.InRound || GameServer == null || seatIndex < 0 || seatIndex > 3 || message == null) return false;
            Debug.Log($"[Room {RoomId}] Action {((ClientActionType)message.actionType)} received from bound seat {seatIndex}.");
            var action = new ClientAction(seatIndex, (ClientActionType)message.actionType, message.targetTile?.ToTileData(), message.chiCombinations);
            action.SetHuDetails(message.totalFan, message.fanDetails?.ToList());
            GameServer.SubmitAction(action);
            return true;
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
                isReady = seat != null && (seat.MatchReady || seat.SceneReady),
                displayName = seat?.DisplayName,
                totalAlienation = seat?.Loadout?.TotalAlienation ?? 0
            };
        }

        public void Broadcast(string type, object payload)
        {
            foreach (var seat in _seats)
                if (seat != null && !seat.IsAi) _send?.Invoke(type, new EndpointPayload(seat.Endpoint, payload));
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
                clients.Add(_seats[i].IsAi ? (IPlayerClient)new SimpleAIClient(i, null) : new RemotePlayerClient(i, _seats[i].Endpoint, Session));
            }

            SessionTalentPolicy.ApplyStartingCapitalOnce(Session, _talentConfigs, ref _startingCapitalApplied);

            GameServer = new GameServer(new WallService(), new GameServerOptions());
            foreach (var client in clients.OfType<SimpleAIClient>()) client.SetServer(GameServer);
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
                    if (seat != null && !seat.IsAi) new RemotePlayerClient(seat.SeatIndex, seat.Endpoint, Session).OnSessionEnd(Session.Scores);
                Close();
                return;
            }

            foreach (var seat in _seats.Where(s => s != null && !s.IsAi)) seat.MatchReady = false;
            State = RoomState.WaitingForNextRound;
        }
    }

    internal sealed class EndpointPayload
    {
        public readonly GameEndpoint Endpoint;
        public readonly object Payload;
        public EndpointPayload(GameEndpoint endpoint, object payload) { Endpoint = endpoint; Payload = payload; }
    }
}
