namespace MahjongGame.Core.Network
{
    /// <summary>Scene target selected only after a full authoritative recovery snapshot arrives.</summary>
    public enum ClientRecoverySceneTarget
    {
        None,
        Lobby,
        Game
    }

    public static class ClientRecoverySceneRoutingPolicy
    {
        public static ClientRecoverySceneTarget GetTarget(RoomState state)
        {
            switch (state)
            {
                case RoomState.WaitingForPlayers:
                case RoomState.WaitingForMatchReady:
                    return ClientRecoverySceneTarget.Lobby;
                case RoomState.LoadingGameScene:
                case RoomState.InRound:
                case RoomState.WaitingForNextRound:
                case RoomState.WaitingForSideboard:
                case RoomState.SessionCompleted:
                    return ClientRecoverySceneTarget.Game;
                default:
                    return ClientRecoverySceneTarget.None;
            }
        }
    }
}
