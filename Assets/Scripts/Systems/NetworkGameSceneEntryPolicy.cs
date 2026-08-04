namespace MahjongGame.Systems
{
    public enum NetworkGameSceneEntryDecision
    {
        InitializeNetworkClient,
        ReturnToPersistent
    }

    public static class NetworkGameSceneEntryPolicy
    {
        public static NetworkGameSceneEntryDecision Decide(
            bool hasNetworkManager,
            bool hasRoomService,
            bool hasRoom)
        {
            return hasNetworkManager && hasRoomService && hasRoom
                ? NetworkGameSceneEntryDecision.InitializeNetworkClient
                : NetworkGameSceneEntryDecision.ReturnToPersistent;
        }
    }
}
