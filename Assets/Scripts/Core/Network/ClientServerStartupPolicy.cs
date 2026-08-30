namespace MahjongGame.Core.Network
{
    /// <summary>Pure startup ordering for the session-only server selection.</summary>
    public static class ClientServerStartupPolicy
    {
        public static ClientServerEnvironment InitialEnvironment => ClientServerEnvironment.Online;

        /// <summary>A saved-room recovery owns the socket until it resolves, so it suppresses selected-server startup.</summary>
        public static bool ShouldConnectSelectedServerAfterLogin(bool reconnectStarted) => !reconnectStarted;
    }
}
