namespace MahjongGame.Core.Network
{
    /// <summary>
    /// A reconnect always establishes a fresh authoritative baseline. Mahjong table state is
    /// compact, and this avoids relying on a partial in-process projection for recovery.
    /// </summary>
    public static class ClientReconnectRecoveryPolicy
    {
        public static bool ShouldUseCachedProjection() => false;
    }
}
