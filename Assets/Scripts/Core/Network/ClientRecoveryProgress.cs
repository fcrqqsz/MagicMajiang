namespace MahjongGame.Core.Network
{
    /// <summary>Presentation-safe reconnect lifecycle. It contains no authority or table state.</summary>
    public enum ClientRecoveryStage
    {
        None,
        Connecting,
        Resynchronizing,
        Restored,
        TerminalFailure
    }

    public sealed class ClientRecoveryProgress
    {
        public ClientRecoveryStage Stage { get; }
        public string Message { get; }
        public int Attempt { get; }
        public int RetryDelaySeconds { get; }

        public ClientRecoveryProgress(ClientRecoveryStage stage, string message, int attempt = 0, int retryDelaySeconds = 0)
        {
            Stage = stage;
            Message = message;
            Attempt = attempt;
            RetryDelaySeconds = retryDelaySeconds;
        }
    }
}
