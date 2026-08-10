using System;
using System.Threading;

namespace MahjongGame.Core.Network
{
    public enum GameRoundCompletionKind
    {
        Win,
        Draw,
        Aborted
    }

    public sealed class GameRoundCompletion
    {
        public GameRoundCompletionKind Kind { get; }
        public Exception Error { get; }

        internal GameRoundCompletion(GameRoundCompletionKind kind, Exception error)
        {
            Kind = kind;
            Error = error;
        }
    }

    /// <summary>Admits only the first terminal result emitted by one GameServer round.</summary>
    public sealed class GameRoundCompletionLatch
    {
        private int _isCompleted;

        public bool TryComplete(
            GameRoundCompletionKind kind,
            Exception error,
            out GameRoundCompletion completion)
        {
            if (Interlocked.CompareExchange(ref _isCompleted, 1, 0) != 0)
            {
                completion = null;
                return false;
            }

            completion = new GameRoundCompletion(kind, error);
            return true;
        }
    }
}
