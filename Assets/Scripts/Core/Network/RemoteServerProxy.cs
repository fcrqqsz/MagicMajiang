using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.UI;

namespace MahjongGame.Core.Network
{
    public class RemoteServerProxy : IServer
    {
        private Agents.IPlayerClient _localClient;

        public RemoteServerProxy(Agents.IPlayerClient localClient)
        {
            _localClient = localClient;
            
            // Register message callback
            if (WebSocketClient.Instance != null)
            {
                WebSocketClient.Instance.OnMessageReceived += HandleMessage;
            }
        }

        public void SetLocalClient(Agents.IPlayerClient localClient)
        {
            _localClient = localClient;
        }

        public void Cleanup()
        {
            if (WebSocketClient.Instance != null)
            {
                WebSocketClient.Instance.OnMessageReceived -= HandleMessage;
            }
        }

        public void SubmitAction(ClientAction action)
        {
            var msg = new ClientActionMessage
            {
                actionType = (int)action.ActionType,
                targetTile = action.TargetTile != null ? new SimpleTileData(action.TargetTile) : null,
                chiCombinations = action.ChiCombinations,
                totalFan = action.TotalFan,
                fanDetails = action.FanDetails?.ToArray()
            };

            string json = MessageSerializer.Serialize("Action", 0, msg);
            WebSocketClient.Instance.SendNetworkMessage(json);
        }

        private void HandleMessage(string json)
        {
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (envelope == null) return;

            switch (envelope.type)
            {
                case "RoundStart":
                    var roundMsg = MessageSerializer.DeserializePayload<RoundStartMessage>(envelope.data);
                    _localClient.OnRoundStart(roundMsg.roundNumber, (WindDirection)roundMsg.prevalentWind, (WindDirection)roundMsg.seatWind, roundMsg.dealerIndex);
                    break;
                case "TalentInfo":
                    var talentMsg = MessageSerializer.DeserializePayload<TalentInfoMessage>(envelope.data);
                    var opts = new ScoringOptions { BonusFan = talentMsg.bonusFan, RelaxedPureStraight = talentMsg.relaxedPureStraight };
                    _localClient.OnTalentInfo(opts);
                    break;
                case "GameStart":
                    var startMsg = MessageSerializer.DeserializePayload<GameStartMessage>(envelope.data);
                    _localClient.OnGameStart(startMsg.tiles.Select(t => t.ToTileData()).ToList());
                    break;
                case "PeekWall":
                    var peekMsg = MessageSerializer.DeserializePayload<PeekWallMessage>(envelope.data);
                    _localClient.OnPeekWallTiles(peekMsg.tiles.Select(t => t.ToTileData()).ToList());
                    break;
                case "TileDrawn":
                    var drawnMsg = MessageSerializer.DeserializePayload<TileDrawnMessage>(envelope.data);
                    _localClient.OnTileDrawn(drawnMsg.tile?.ToTileData());
                    break;
                case "PlayerDrew":
                    var pDrewMsg = MessageSerializer.DeserializePayload<PlayerDrewMessage>(envelope.data);
                    _localClient.OnPlayerDrawn(pDrewMsg.playerId);
                    break;
                case "Discarded":
                    var discMsg = MessageSerializer.DeserializePayload<DiscardedMessage>(envelope.data);
                    _localClient.OnOtherPlayerDiscarded(discMsg.playerId, discMsg.tile?.ToTileData());
                    break;
                case "ActionResolved":
                    var resolvedMsg = MessageSerializer.DeserializePayload<ActionResolvedMessage>(envelope.data);
                    _localClient.OnActionResolved(resolvedMsg.playerId, (ClientActionType)resolvedMsg.actionType, resolvedMsg.tile?.ToTileData(), resolvedMsg.chiCombinations);
                    break;
                case "Timeout":
                    var timeoutMsg = MessageSerializer.DeserializePayload<TimeoutMessage>(envelope.data);
                    _localClient.OnTimeout(timeoutMsg.tile?.ToTileData());
                    break;
                case "PlayerWin":
                    var winMsg = MessageSerializer.DeserializePayload<PlayerWinMessage>(envelope.data);
                    if (winMsg == null) break;
                    SyncSessionAfterRound(winMsg.scores, winMsg.completedRounds);
                    _localClient.OnPlayerWin(winMsg.winnerId, winMsg.totalFan, winMsg.fanDetails?.ToList(), winMsg.isSelfDraw);
                    break;
                case "DrawGame":
                    var drawMsg = MessageSerializer.DeserializePayload<DrawGameMessage>(envelope.data);
                    if (drawMsg == null) break;
                    SyncSessionAfterRound(drawMsg.scores, drawMsg.completedRounds);
                    _localClient.OnDrawGame();
                    break;
                case "SessionEnd":
                    var endMsg = MessageSerializer.DeserializePayload<SessionEndMessage>(envelope.data);
                    _localClient.OnSessionEnd(endMsg.scores);
                    break;
                default:
                    Debug.LogWarning($"[RemoteServerProxy] Unhandled message type: {envelope.type}");
                    break;
            }
        }

        private void SyncSessionAfterRound(int[] scores, int completedRounds)
        {
            var session = GameManager.Instance?.Session;
            if (session == null) return;

            if (scores != null)
            {
                int count = Mathf.Min(scores.Length, session.Scores.Length);
                for (int i = 0; i < count; i++)
                {
                    session.Scores[i] = scores[i];
                }
            }

            int targetCompletedRounds = Mathf.Clamp(completedRounds, 0, session.GetTotalRounds());
            while (session.TotalRoundsPlayed < targetCompletedRounds)
            {
                session.AdvanceRound();
            }

            ResultPanelController.Instance?.SetSessionInfo(session);
            GameHUDController.Instance?.UpdateScores(session.Scores);
        }
    }
}
