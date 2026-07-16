namespace MahjongGame.Core.Network
{
    /// <summary>Defines whether a human may enter the pre-match ready state.</summary>
    public static class RoomReadyPolicy
    {
        public static bool CanMarkMatchReady(bool aiFill, int humanCount)
        {
            return aiFill || humanCount >= 4;
        }
    }
}
