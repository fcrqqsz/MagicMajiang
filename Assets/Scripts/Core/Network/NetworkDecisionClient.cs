namespace MahjongGame.Core.Network
{
    public interface INetworkDecisionClient
    {
        void SetActiveDecision(NetworkDecisionContext decision);
        void CloseDecision(long decisionId);
    }

    public interface IDirectActionAuthorizer
    {
        bool CanSubmitDirectAction(NetworkDecisionContext activeDecision);
    }
}
