using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;

namespace MahjongGame.Core.Network
{
    public class RemotePlayerClient : Agents.IPlayerClient
    {
        private readonly int _playerId;
        private readonly GameEndpoint _endpoint;
        
        public RemotePlayerClient(int playerId, GameEndpoint endpoint)
        {
            _playerId = playerId;
            _endpoint = endpoint;
        }

        public int PlayerId => _playerId;
        public GameEndpoint Endpoint => _endpoint;

        public CancellationToken TurnCancellationToken { get; set; }

        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex)
        {
            var msg = new RoundStartMessage
            {
                roundNumber = roundNumber,
                prevalentWind = (int)prevalentWind,
                seatWind = (int)seatWind,
                dealerIndex = dealerIndex
            };
            Send("RoundStart", msg);
        }

        public void OnTalentInfo(ScoringOptions options)
        {
            var msg = new TalentInfoMessage
            {
                bonusFan = options.BonusFan,
                relaxedPureStraight = options.RelaxedPureStraight
            };
            Send("TalentInfo", msg);
        }

        public void OnGameStart(List<TileData> startingHand)
        {
            var msg = new GameStartMessage
            {
                tiles = startingHand.Select(t => new SimpleTileData(t)).ToArray()
            };
            Send("GameStart", msg);
        }

        public void OnPeekWallTiles(List<TileData> topTiles)
        {
            var msg = new PeekWallMessage
            {
                tiles = topTiles.Select(t => new SimpleTileData(t)).ToArray()
            };
            Send("PeekWall", msg);
        }

        public void OnTileDrawn(TileData drawnTile)
        {
            var msg = new TileDrawnMessage
            {
                tile = new SimpleTileData(drawnTile)
            };
            Send("TileDrawn", msg);
        }

        public void OnPlayerDrawn(int playerId)
        {
            var msg = new PlayerDrewMessage { playerId = playerId };
            Send("PlayerDrew", msg);
        }

        public void OnOtherPlayerDiscarded(int playerId, TileData tile)
        {
            var msg = new DiscardedMessage
            {
                playerId = playerId,
                tile = new SimpleTileData(tile)
            };
            Send("Discarded", msg);
        }

        public void OnActionResolved(int playerId, ClientActionType actionType, TileData tile, int[] chiCombinations)
        {
            var msg = new ActionResolvedMessage
            {
                playerId = playerId,
                actionType = (int)actionType,
                tile = tile != null ? new SimpleTileData(tile) : null,
                chiCombinations = chiCombinations
            };
            Send("ActionResolved", msg);
        }

        public void OnTimeout(TileData forceDiscardTile)
        {
            var msg = new TimeoutMessage
            {
                tile = forceDiscardTile != null ? new SimpleTileData(forceDiscardTile) : null
            };
            Send("Timeout", msg);
        }

        public void OnPlayerWin(int winnerId, int totalFan, List<string> fanDetails, bool isSelfDraw)
        {
            var msg = new PlayerWinMessage
            {
                winnerId = winnerId,
                totalFan = totalFan,
                fanDetails = fanDetails?.ToArray(),
                isSelfDraw = isSelfDraw
            };
            Send("PlayerWin", msg);
        }

        public void OnDrawGame()
        {
            // JsonUtility cannot serialize raw strings for payload if it expects an object. 
            // We use an empty anonymous object or simple class.
            Send("DrawGame", new DrawGameMessage());
        }

        public void OnSessionEnd(int[] finalScores)
        {
            var msg = new SessionEndMessage
            {
                scores = finalScores
            };
            Send("SessionEnd", msg);
        }

        private int _seq = 0;
        private void Send<T>(string type, T payload)
        {
            _seq++;
            string json = MessageSerializer.Serialize(type, _seq, payload);
            _endpoint.SendMessage(json);
        }
    }
}
