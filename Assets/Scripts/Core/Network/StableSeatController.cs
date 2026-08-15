using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>
    /// A logical human seat's permanent GameServer client. Physical endpoints can disappear and
    /// rebind without replacing this instance; the temporary AI is only selected on a new decision.
    /// </summary>
    public sealed class StableSeatController : IPlayerClient, INetworkDecisionClient, IDirectActionAuthorizer
    {
        private readonly int _playerId;
        private readonly RemotePlayerClient _remote;
        private readonly SimpleAIClient _temporaryAi;
        private readonly Func<bool> _isOnline;
        private readonly Action<DecisionControllerKind> _controllerChanged;
        private readonly SeatDecisionControlLatch _latch = new SeatDecisionControlLatch();
        private NetworkDecisionContext _activeDecision;
        private bool _temporaryAiOwnsDecision;
        private bool _permanentAi;
        private GameServer _server;

        public StableSeatController(int playerId, SeatMessageStream messageStream, GameSession session,
            Func<bool> isOnline, Action<DecisionControllerKind> controllerChanged)
        {
            _playerId = playerId;
            _remote = new RemotePlayerClient(playerId, messageStream, session);
            _temporaryAi = new SimpleAIClient(playerId, null);
            _isOnline = isOnline ?? throw new ArgumentNullException(nameof(isOnline));
            _controllerChanged = controllerChanged;
        }

        public int PlayerId => _playerId;
        public bool IsAiControllingActiveDecision => _temporaryAiOwnsDecision && _activeDecision != null;

        public CancellationToken TurnCancellationToken
        {
            get => _remote.TurnCancellationToken;
            set
            {
                _remote.TurnCancellationToken = value;
                _temporaryAi.TurnCancellationToken = value;
            }
        }

        public void SetServer(GameServer server)
        {
            _server = server;
            _temporaryAi.SetServer(server);
        }

        public void SetSession(GameSession session) => _remote.SetSession(session);

        public void SetPermanentAi()
        {
            _permanentAi = true;
            _temporaryAiOwnsDecision = true;
            _controllerChanged?.Invoke(DecisionControllerKind.AI);
        }

        public bool IsHumanSubmissionAllowed(long decisionId) => !_permanentAi && _latch.IsHumanSubmissionAllowed(decisionId);

        public bool CanSubmitDirectAction(NetworkDecisionContext activeDecision) =>
            _temporaryAiOwnsDecision && activeDecision != null && _activeDecision?.DecisionId == activeDecision.DecisionId;

        public void SetActiveDecision(NetworkDecisionContext decision)
        {
            _activeDecision = decision;
            _remote.SetActiveDecision(decision);
            if (decision == null || !Participates(decision)) return;

            var controller = _latch.OpenDecision(decision.DecisionId, !_permanentAi && _isOnline());
            _temporaryAiOwnsDecision = controller == DecisionControllerKind.AI;
            _controllerChanged?.Invoke(controller);
            if (_temporaryAiOwnsDecision) SynchronizeTemporaryAi();
        }

        public void CloseDecision(long decisionId) => _latch.CloseDecision(decisionId);
        public void MarkOffline() => _latch.MarkOffline();
        public void MarkOnline() => _latch.MarkOnline();

        public void OnGameStart(List<TileData> startingHand) => _remote.OnGameStart(startingHand);
        public void OnPlayerDrawn(int playerId) => _remote.OnPlayerDrawn(playerId);
        public void OnTurnWithoutDraw() => _remote.OnTurnWithoutDraw();
        public void OnWallCountChanged(int remainingCount) => _remote.OnWallCountChanged(remainingCount);

        public void OnTileDrawn(TileData drawnTile, bool isKongReplacementDraw)
        {
            _remote.OnTileDrawn(drawnTile, isKongReplacementDraw);
            if (_temporaryAiOwnsDecision) _temporaryAi.OnTileDrawn(drawnTile, isKongReplacementDraw);
        }

        public void OnOtherPlayerDiscarded(int playerId, TileData discardedTile)
        {
            _remote.OnOtherPlayerDiscarded(playerId, discardedTile);
            if (_temporaryAiOwnsDecision) _temporaryAi.OnOtherPlayerDiscarded(playerId, discardedTile);
        }

        public void OnAddedKongDeclared(int playerId, TileData targetTile)
        {
            _remote.OnAddedKongDeclared(playerId, targetTile);
            if (_temporaryAiOwnsDecision) _temporaryAi.OnAddedKongDeclared(playerId, targetTile);
        }

        public void OnActionResolved(int playerId, ClientActionType actionType, TileData targetTile, int[] chiCombinations = null)
        {
            _remote.OnActionResolved(playerId, actionType, targetTile, chiCombinations);
            if (_temporaryAiOwnsDecision) _temporaryAi.OnActionResolved(playerId, actionType, targetTile, chiCombinations);
        }

        public void OnDrawGame() => _remote.OnDrawGame();
        public void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown) =>
            _remote.OnPlayerWin(playerId, totalFan, fanDetails, isSelfDraw, winKind, loserId,
                winningHand, talentFanBreakdown);
        public void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex)
        {
            _remote.OnRoundStart(roundNumber, prevalentWind, seatWind, dealerIndex);
            _temporaryAi.OnRoundStart(roundNumber, prevalentWind, seatWind, dealerIndex);
        }
        public void OnSessionEnd(int[] finalScores) => _remote.OnSessionEnd(finalScores);

        public void OnTimeout(TileData autoDiscardedTile)
        {
            _remote.OnTimeout(autoDiscardedTile);
            if (_temporaryAiOwnsDecision) _temporaryAi.OnTimeout(autoDiscardedTile);
        }

        public void OnTalentInfo(ScoringOptions scoringOptions)
        {
            _remote.OnTalentInfo(scoringOptions);
            _temporaryAi.OnTalentInfo(scoringOptions);
        }
        public void OnPeekWallTiles(List<TileData> topTiles) => _remote.OnPeekWallTiles(topTiles);

        private bool Participates(NetworkDecisionContext decision)
        {
            return decision.Phase == NetworkDecisionPhase.MainTurn
                ? decision.ActingSeatIndex == _playerId
                : decision.EligibleSeats.Contains(_playerId);
        }

        private void SynchronizeTemporaryAi()
        {
            if (_server == null) return;
            _temporaryAi.RestoreAuthoritativeState(
                _server.GetHandSnapshot(_playerId),
                _server.GetMeldSnapshot(_playerId),
                _server.GetScoringOptionsSnapshot(_playerId));
        }
    }
}
