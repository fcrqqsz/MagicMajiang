using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Network.Transport;
using MahjongGame.Talents;
using MahjongGame.UI;

namespace MahjongGame.Core.Network
{
    public interface ITalentActionPresentationClient
    {
        void BindTalentActionPresentation(RemoteServerProxy proxy);
        void UnbindTalentActionPresentation(RemoteServerProxy proxy);
    }

    public class RemoteServerProxy : IServer
    {
        private Agents.IPlayerClient _localClient;
        private readonly ClientRoomService _roomService;
        private long _activeDecisionId;

        public event System.Action TalentPickerResetRequested;
        public event System.Action<long, IReadOnlyList<TalentActionOption>> TalentActionsChanged;
        public event System.Action<TalentRuntimeEventMessage> TalentRuntimeEventReceived;
        public event System.Action<TalentActionResolvedMessage> TalentActionResolvedReceived;

        public RemoteServerProxy(Agents.IPlayerClient localClient, ClientRoomService roomService)
        {
            _localClient = localClient;
            _roomService = roomService ?? throw new System.ArgumentNullException(nameof(roomService));
            _roomService.AcceptedSequenceEnvelope += HandleAcceptedSequenceEnvelope;
            _roomService.ReconnectSnapshotApplied += HandleReconnectSnapshot;
            GameHUDController.Instance?.BindServerProxy(this);
            (_localClient as ITalentActionPresentationClient)?.BindTalentActionPresentation(this);
        }

        public void SetLocalClient(Agents.IPlayerClient localClient)
        {
            (_localClient as ITalentActionPresentationClient)?.UnbindTalentActionPresentation(this);
            _localClient = localClient;
            (_localClient as ITalentActionPresentationClient)?.BindTalentActionPresentation(this);
        }

        public void Cleanup()
        {
            _roomService.AcceptedSequenceEnvelope -= HandleAcceptedSequenceEnvelope;
            _roomService.ReconnectSnapshotApplied -= HandleReconnectSnapshot;
            (_localClient as ITalentActionPresentationClient)?.UnbindTalentActionPresentation(this);
            GameHUDController.Instance?.UnbindServerProxy(this);
        }

        public void SubmitAction(ClientAction action)
        {
            if (_roomService.IsResyncRequired || !_roomService.CanSubmitCommands)
            {
                Debug.LogWarning("[RemoteServerProxy] Ignoring action while client recovery is required.");
                return;
            }

            ClearTalentActionsPresentation(clearDecisionId: false);

            var msg = new ClientActionMessage
            {
                decisionId = _activeDecisionId,
                actionType = (int)action.ActionType,
                targetTile = action.TargetTile != null ? new SimpleTileData(action.TargetTile) : null,
                chiCombinations = action.ChiCombinations,
                totalFan = action.TotalFan,
                fanDetails = action.FanDetails?.ToArray()
            };

            string json = MessageSerializer.Serialize("Action", 0, msg);
            WebSocketClient.Instance?.SendNetworkMessage(json);
        }

        public bool SubmitTalentAction(TalentActionOption option) =>
            _roomService.SubmitTalentAction(option);

        public bool SubmitSideboard(IReadOnlyCollection<string> activeTalentIds) =>
            _roomService.SubmitSideboard(activeTalentIds);

        private void HandleAcceptedSequenceEnvelope(NetworkMessageEnvelope envelope)
        {
            if (envelope == null) return;

            switch (envelope.type)
            {
                case "RoundStart":
                    var roundMsg = MessageSerializer.DeserializePayload<RoundStartMessage>(envelope.data);
                    if (roundMsg == null) break;
                    SyncSessionAtRoundStart(roundMsg);
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
                    _activeDecisionId = drawnMsg?.decisionId ?? 0;
                    GameHUDController.Instance?.CloseTalentDrawers();
                    _localClient.OnTileDrawn(drawnMsg.tile?.ToTileData());
                    break;
                case "PlayerDrew":
                    var pDrewMsg = MessageSerializer.DeserializePayload<PlayerDrewMessage>(envelope.data);
                    _localClient.OnPlayerDrawn(pDrewMsg.playerId);
                    break;
                case "TurnWithoutDraw":
                    var turnWithoutDrawMsg = MessageSerializer.DeserializePayload<TurnWithoutDrawMessage>(envelope.data);
                    _activeDecisionId = turnWithoutDrawMsg?.decisionId ?? 0;
                    GameHUDController.Instance?.CloseTalentDrawers();
                    _localClient.OnTurnWithoutDraw();
                    break;
                case "WallCount":
                    var wallCountMsg = MessageSerializer.DeserializePayload<WallCountMessage>(envelope.data);
                    if (wallCountMsg != null) _localClient.OnWallCountChanged(wallCountMsg.remainingCount);
                    break;
                case "Discarded":
                    var discMsg = MessageSerializer.DeserializePayload<DiscardedMessage>(envelope.data);
                    _activeDecisionId = discMsg?.decisionId ?? 0;
                    GameHUDController.Instance?.CloseTalentDrawers();
                    ClearTalentActionsPresentation(clearDecisionId: false);
                    _localClient.OnOtherPlayerDiscarded(discMsg.playerId, discMsg.tile?.ToTileData());
                    break;
                case "AddedKongDeclared":
                    var addedKongMsg = MessageSerializer.DeserializePayload<AddedKongDeclaredMessage>(envelope.data);
                    _activeDecisionId = addedKongMsg?.decisionId ?? 0;
                    GameHUDController.Instance?.CloseTalentDrawers();
                    ClearTalentActionsPresentation(clearDecisionId: false);
                    _localClient.OnAddedKongDeclared(addedKongMsg.playerId, addedKongMsg.tile?.ToTileData());
                    break;
                case "ActionResolved":
                    var resolvedMsg = MessageSerializer.DeserializePayload<ActionResolvedMessage>(envelope.data);
                    ClearTalentActionsPresentation();
                    _localClient.OnActionResolved(resolvedMsg.playerId, (ClientActionType)resolvedMsg.actionType, resolvedMsg.tile?.ToTileData(), resolvedMsg.chiCombinations);
                    break;
                case "Timeout":
                    var timeoutMsg = MessageSerializer.DeserializePayload<TimeoutMessage>(envelope.data);
                    ClearTalentActionsPresentation();
                    _localClient.OnTimeout(timeoutMsg.tile?.ToTileData());
                    break;
                case "TalentRuntimeEvent":
                    var runtimeEvent = MessageSerializer.DeserializePayload<TalentRuntimeEventMessage>(envelope.data);
                    if (runtimeEvent != null) TalentRuntimeEventReceived?.Invoke(runtimeEvent);
                    break;
                case "TalentPrivateState":
                    ClientTalentRecoveryProjection liveTalentProjection =
                        _roomService.GameState.CreateTalentRecoveryProjection();
                    TalentActionsChanged?.Invoke(
                        liveTalentProjection.DecisionId,
                        liveTalentProjection.AvailableActions);
                    break;
                case "TalentActionResolved":
                    var talentResolved = MessageSerializer.DeserializePayload<TalentActionResolvedMessage>(envelope.data);
                    if (talentResolved != null) TalentActionResolvedReceived?.Invoke(talentResolved);
                    break;
                case "PlayerWin":
                    var winMsg = MessageSerializer.DeserializePayload<PlayerWinMessage>(envelope.data);
                    if (winMsg == null) break;
                    var winResult = WinResultNormalizer.Normalize(
                        winMsg.winKind, winMsg.isSelfDraw, winMsg.loserId);
                    ClearTalentActionsPresentation();
                    SyncSessionAfterRound(winMsg.scores, winMsg.completedRounds);
                    _localClient.OnPlayerWin(winMsg.winnerId, winMsg.totalFan, winMsg.fanDetails?.ToList(),
                        winResult.IsSelfDraw, winResult.Kind, winResult.LoserId,
                        WinningHandSnapshotCodec.Normalize(winMsg.winningHand),
                        TalentFanBreakdownMessage.Clone(winMsg.talentFanBreakdown));
                    break;
                case "DrawGame":
                    var drawMsg = MessageSerializer.DeserializePayload<DrawGameMessage>(envelope.data);
                    if (drawMsg == null) break;
                    ClearTalentActionsPresentation();
                    SyncSessionAfterRound(drawMsg.scores, drawMsg.completedRounds);
                    _localClient.OnDrawGame();
                    break;
                case "SessionEnd":
                    var endMsg = MessageSerializer.DeserializePayload<SessionEndMessage>(envelope.data);
                    ClearTalentActionsPresentation();
                    _localClient.OnSessionEnd(endMsg.scores);
                    break;
                // Room-control messages are consumed by ClientRoomService on the same WebSocket.
                case "RoomJoined":
                case "PlayerJoined":
                case "PlayerLeft":
                case "RoomSeatUpdated":
                case "RoomReady":
                case "RoomClosed":
                case "RoomError":
                case "SideboardStarted":
                case "SideboardLocked":
                case "SideboardProgress":
                    break;
                default:
                    Debug.LogWarning($"[RemoteServerProxy] Unhandled message type: {envelope.type}");
                    break;
            }
        }

        private void HandleReconnectSnapshot(RoomGameSnapshot snapshot)
        {
            ClientTalentRecoveryProjection projection = _roomService.GameState.CreateTalentRecoveryProjection();
            _activeDecisionId = projection.DecisionId;
            if (projection.CloseTransientPicker) TalentPickerResetRequested?.Invoke();
            TalentActionsChanged?.Invoke(projection.DecisionId, projection.AvailableActions);
        }

        private void ClearTalentActionsPresentation(bool clearDecisionId = true)
        {
            if (clearDecisionId) _activeDecisionId = 0;
            TalentPickerResetRequested?.Invoke();
            TalentActionsChanged?.Invoke(0, System.Array.Empty<TalentActionOption>());
        }

        private void SyncSessionAfterRound(int[] scores, int completedRounds)
        {
            var session = GameManager.Instance?.Session;
            if (session == null) return;

            session.Mode = _roomService.GameMode;
            SyncSessionScores(scores);

            int targetCompletedRounds = Mathf.Clamp(completedRounds, 0, session.GetTotalRounds());
            while (session.TotalRoundsPlayed < targetCompletedRounds)
            {
                session.AdvanceRound();
            }

            ResultPanelController.Instance?.SetSessionInfo(session);
        }

        private void SyncSessionAtRoundStart(RoundStartMessage roundStart)
        {
            var session = GameManager.Instance?.Session;
            if (session == null || roundStart == null) return;

            session.Mode = _roomService.GameMode;
            session.PrevalentWind = (WindDirection)roundStart.prevalentWind;
            session.DealerIndex = Mathf.Clamp(roundStart.dealerIndex, 0, 3);
            session.TotalRoundsPlayed = Mathf.Clamp(roundStart.roundNumber - 1, 0, session.GetTotalRounds());
            session.RoundInWind = session.TotalRoundsPlayed % 4;
            SyncSessionScores(roundStart.scores);
            GameHUDController.Instance?.UpdateRoundInfo(session);
        }

        private void SyncSessionScores(int[] scores)
        {
            var session = GameManager.Instance?.Session;
            if (session == null || !SessionScorePolicy.ApplyAuthoritativeScores(session, scores)) return;

            GameHUDController.Instance?.UpdateScores(session.Scores);
        }
    }
}
