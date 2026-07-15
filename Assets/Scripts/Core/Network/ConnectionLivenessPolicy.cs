using System;

namespace MahjongGame.Core.Network
{
    /// <summary>Single timeout policy used by the room server for inactive websocket connections.</summary>
    public static class ConnectionLivenessPolicy
    {
        public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

        public static bool IsExpired(DateTime lastActivityUtc, DateTime utcNow)
        {
            return utcNow - lastActivityUtc >= HeartbeatTimeout;
        }
    }
}
