using System;

namespace MahjongGame.Core.Network
{
    /// <summary>Pure startup ordering for the session-only server selection.</summary>
    public static class ClientServerStartupPolicy
    {
        public static ClientServerEnvironment InitialEnvironment => ClientServerEnvironment.Online;

        /// <summary>A saved-room recovery owns the socket until it resolves, so it suppresses selected-server startup.</summary>
        public static bool ShouldConnectSelectedServerAfterLogin(bool reconnectStarted) => !reconnectStarted;
    }

    /// <summary>Keeps session server selection visible to synchronous diagnostics emitted by a switch attempt.</summary>
    public static class ClientServerEnvironmentSelectionPolicy
    {
        public static bool TrySwitch(ClientServerEnvironment current, ClientServerEnvironment requested,
            Func<bool> trySwitch, Action<ClientServerEnvironment> applySelection)
        {
            if (trySwitch == null || applySelection == null) return false;

            applySelection(requested);
            if (trySwitch()) return true;

            applySelection(current);
            return false;
        }
    }
}
