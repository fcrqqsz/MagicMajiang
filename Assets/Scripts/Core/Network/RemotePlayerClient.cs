using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network
{
    public class RemotePlayerClient : Agents.IPlayerClient, Agents.IResolvedMeldPlayerClient, INetworkDecisionClient
    {
        private readonly int _playerId;
        private readonly SeatMessageStream _messageStream;
        private GameSession _session;
        private long _activeDecisionId;
        private NetworkDecisionContext _activeDecision;
        
        public RemotePlayerClient(int playerId, SeatMessageStream messageStream, GameSession session = null)
        {
            _playerId = playerId;
            _messageStream = messageStream ?? throw new System.ArgumentNullException(nameof(messageStream));
            _session = session;
        }

        public int PlayerId => _playerId;
        public SeatMessageStream MessageStream => _messageStream;

        public CancellationToken TurnCancellationToken { get; set; }

        public void SetSession(GameSession session)
        {
            _session = session;
        }

        public void SetActiveDecision(NetworkDecisionContext decision)
        {
            _activeDecisionId = decision?.DecisionId ?? 0;
            _activeDecision = decision;
        }

        public void CloseDecision(long decisionId)
        {
            if (_activeDecisionId != decisionId) return;
            _activeDecisionId = 0;
            _activeDecision = null;
        }

        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex)
        {
            var msg = new RoundStartMessage
            {
                roundNumber = roundNumber,
                prevalentWind = (int)prevalentWind,
                seatWind = (int)seatWind,
                dealerIndex = dealerIndex,
                scores = GetScoreSnapshot()
            };
            Send("RoundStart", msg);
        }

        public void OnTalentInfo(ScoringOptions options)
        {
            var msg = new TalentInfoMessage
            {
                bonusFan = options.BonusFan,
                minimumFan = options.MinimumFan,
                relaxedPureStraight = options.RelaxedPureStraight
            };
            Send("TalentInfo", msg);
        }

        public void OnGameStart(List<TileData> startingHand)
        {
            var msg = new GameStartMessage
            {
                tiles = startingHand.Select(t => new SimpleTileData(t, true)).ToArray()
            };
            Send("GameStart", msg);
        }

        public void OnPeekWallTiles(List<TileData> topTiles)
        {
            var msg = new PeekWallMessage
            {
                tiles = topTiles.Select(t => new SimpleTileData(t, true)).ToArray()
            };
            Send("PeekWall", msg);
        }

        public void OnPrivateTileReveal(TalentPrivateTileReveal reveal)
        {
            if (reveal == null || reveal.ViewerSeatIndex != _playerId) return;
            var msg = new PrivateTileRevealMessage
            {
                talentId = reveal.TalentId,
                viewerSeatIndex = reveal.ViewerSeatIndex,
                targetSeatIndex = reveal.TargetSeatIndex,
                roundNumber = reveal.RoundNumber,
                tiles = reveal.Tiles?.Select(t => new SnapshotRevealedTile
                {
                    suit = (int)t.TileSuit,
                    value = t.Value,
                    isModified = t.IsModified
                }).ToArray() ?? System.Array.Empty<SnapshotRevealedTile>()
            };
            Send("PrivateTileReveal", msg);
        }

        public void OnPrivateKnownTilesChanged(PrivateKnownTilesProjection projection)
        {
            if (projection == null || projection.ViewerSeatIndex != _playerId) return;
            Send("PrivateKnownTiles", new PrivateKnownTilesMessage
            {
                viewerSeatIndex = projection.ViewerSeatIndex,
                hands = (projection.Hands ?? System.Array.Empty<PrivateKnownHandProjection>())
                    .Where(hand => hand != null)
                    .Select(hand => new SnapshotKnownHand
                    {
                        targetSeatIndex = hand.TargetSeatIndex,
                        tiles = (hand.Tiles ?? System.Array.Empty<PrivateKnownTileFace>())
                            .Where(tile => tile != null)
                            .Select(tile => new SnapshotKnownTile
                            {
                                suit = (int)tile.Suit,
                                value = tile.Value,
                                isModified = tile.IsModified
                            })
                            .ToArray()
                    })
                    .ToArray()
            });
        }

        public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
        {
            var msg = new TileDrawnMessage
            {
                decisionId = _activeDecisionId,
                decision = RoomGameSnapshotBuilder.CreateDecisionSnapshot(_activeDecision),
                tile = new SimpleTileData(drawnTile, true)
            };
            Send("TileDrawn", msg);
        }

        public void OnPlayerDrawn(int playerId)
        {
            var msg = new PlayerDrewMessage { playerId = playerId };
            Send("PlayerDrew", msg);
        }

        public void OnTurnWithoutDraw()
        {
            Send("TurnWithoutDraw", new TurnWithoutDrawMessage
            {
                decisionId = _activeDecisionId,
                decision = RoomGameSnapshotBuilder.CreateDecisionSnapshot(_activeDecision)
            });
        }

        public void OnWallCountChanged(int remainingCount)
        {
            Send("WallCount", new WallCountMessage { remainingCount = remainingCount });
        }

        public void OnOtherPlayerDiscarded(int playerId, TileData tile)
        {
            var msg = new DiscardedMessage
            {
                decisionId = _activeDecisionId,
                decision = RoomGameSnapshotBuilder.CreateDecisionSnapshot(_activeDecision),
                playerId = playerId,
                tile = new SimpleTileData(tile, playerId == _playerId)
            };
            Send("Discarded", msg);
        }

        public void OnAddedKongDeclared(int playerId, TileData tile)
        {
            var msg = new AddedKongDeclaredMessage
            {
                decisionId = _activeDecisionId,
                decision = RoomGameSnapshotBuilder.CreateDecisionSnapshot(_activeDecision),
                playerId = playerId,
                tile = new SimpleTileData(tile, playerId == _playerId)
            };
            Send("AddedKongDeclared", msg);
        }

        public void OnActionResolved(int playerId, ClientActionType actionType, TileData tile, int[] chiCombinations)
        {
            OnActionResolved(playerId, actionType, tile, chiCombinations, null);
        }

        public void OnActionResolved(
            int playerId,
            ClientActionType actionType,
            TileData tile,
            int[] chiCombinations,
            IReadOnlyList<TileData> resolvedMeldTiles)
        {
            bool includeOwnerPrivateState = playerId == _playerId;
            var msg = new ActionResolvedMessage
            {
                playerId = playerId,
                actionType = (int)actionType,
                tile = tile != null ? new SimpleTileData(tile) : null,
                chiCombinations = chiCombinations,
                meldTiles = (resolvedMeldTiles ?? System.Array.Empty<TileData>())
                    .Where(t => t != null)
                    .Select(t => new SimpleTileData(t, includeOwnerPrivateState))
                    .ToArray()
            };
            Send("ActionResolved", msg);
        }

        public void OnTimeout(TileData forceDiscardTile)
        {
            var msg = new TimeoutMessage
            {
                tile = forceDiscardTile != null ? new SimpleTileData(forceDiscardTile, true) : null
            };
            Send("Timeout", msg);
            _activeDecisionId = 0;
            _activeDecision = null;
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown)
        {
            var msg = new PlayerWinMessage
            {
                winnerId = winnerId,
                totalFan = totalFan,
                fanDetails = fanDetails?.ToArray(),
                isSelfDraw = isSelfDraw,
                winKind = winKind,
                loserId = loserId,
                winningHand = winningHand,
                talentFanBreakdown = TalentFanBreakdownMessage.Clone(talentFanBreakdown),
                scores = GetScoreSnapshot(),
                completedRounds = GetCompletedRoundsAfterCurrentRound()
            };
            Send("PlayerWin", msg);
        }

        public void OnDrawGame()
        {
            var msg = new DrawGameMessage
            {
                scores = GetScoreSnapshot(),
                completedRounds = GetCompletedRoundsAfterCurrentRound()
            };
            Send("DrawGame", msg);
        }

        public void OnSessionEnd(int[] finalScores)
        {
            var msg = new SessionEndMessage
            {
                scores = finalScores
            };
            Send("SessionEnd", msg);
        }

        private void Send<T>(string type, T payload)
        {
            _messageStream.Send(type, payload);
        }

        private int[] GetScoreSnapshot()
        {
            return _session?.Scores?.ToArray();
        }

        private int GetCompletedRoundsAfterCurrentRound()
        {
            return _session != null ? _session.TotalRoundsPlayed + 1 : 0;
        }
    }
}
