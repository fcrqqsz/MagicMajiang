using System;

namespace MahjongGame.Core.Network
{
    /// <summary>Single timeout policy used by the room server for inactive websocket connections.</summary>
    public static class ConnectionLivenessPolicy
    {
        public const int DefaultHeartbeatTimeoutSeconds = 10;
        public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(DefaultHeartbeatTimeoutSeconds);

        public static bool IsExpired(DateTime lastActivityUtc, DateTime utcNow)
        {
            return IsExpired(lastActivityUtc, utcNow, HeartbeatTimeout);
        }

        public static bool IsExpired(DateTime lastActivityUtc, DateTime utcNow, TimeSpan heartbeatTimeout) =>
            utcNow - lastActivityUtc >= heartbeatTimeout;

        public static bool IsClientAcknowledgementExpired(float lastAcknowledgementTime, float now) =>
            now - lastAcknowledgementTime >= DefaultHeartbeatTimeoutSeconds;
    }
}
