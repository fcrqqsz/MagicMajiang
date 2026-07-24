namespace MahjongGame.Core.Network
{
    /// <summary>Fixed reconnect backoff and terminal-error classification for the UI recovery loop.</summary>
    public static class ClientReconnectRetryPolicy
    {
        private static readonly int[] RetryDelaysSeconds = { 0, 1, 2, 4, 8, 10 };

        /// <summary>Returns the delay before the zero-based reconnect attempt.</summary>
        public static int GetDelaySeconds(int attemptIndex)
        {
            if (attemptIndex <= 0) return RetryDelaysSeconds[0];
            return RetryDelaysSeconds[attemptIndex < RetryDelaysSeconds.Length
                ? attemptIndex
                : RetryDelaysSeconds.Length - 1];
        }

        /// <summary>
        /// The server can never make these failures recoverable for this ticket. Other errors,
        /// including a briefly still-online development identity, are retried until the user leaves.
        /// </summary>
        public static bool ShouldRetryAfterError(string errorCode)
        {
            return errorCode != NetworkErrorCodes.RoomNotFound
                && errorCode != NetworkErrorCodes.SeatExpired
                && errorCode != NetworkErrorCodes.StreamMismatch
                && errorCode != NetworkErrorCodes.ProtocolMismatch;
        }
    }
}
