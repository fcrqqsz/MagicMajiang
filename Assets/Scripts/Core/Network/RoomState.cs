namespace MahjongGame.Core.Network
{
    public enum RoomState
    {
        WaitingForPlayers,
        WaitingForMatchReady,
        LoadingGameScene,
        InRound,
        WaitingForNextRound,
        Closed
    }
}
