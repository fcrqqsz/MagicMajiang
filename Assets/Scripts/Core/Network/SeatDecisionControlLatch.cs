namespace MahjongGame.Core.Network
{
    /// <summary>
    /// Keeps one seat's decision owner stable until that decision closes. Connection state changes
    /// only influence the next decision boundary.
    /// </summary>
    public sealed class SeatDecisionControlLatch
    {
        private long _activeDecisionId;
        private DecisionControllerKind _controller = DecisionControllerKind.Human;
        private bool _isOnline;

        public DecisionControllerKind OpenDecision(long decisionId, bool isOnline)
        {
            _activeDecisionId = decisionId;
            _isOnline = isOnline;
            _controller = RoomLifecyclePolicy.SelectDecisionController(isOnline, false);
            return _controller;
        }

        public void MarkOffline() => _isOnline = false;
        public void MarkOnline() => _isOnline = true;

        public bool IsHumanSubmissionAllowed(long decisionId) =>
            decisionId == _activeDecisionId && _controller == DecisionControllerKind.Human;

        public void CloseDecision(long decisionId)
        {
            if (decisionId != _activeDecisionId) return;
            _activeDecisionId = 0;
        }
    }
}
