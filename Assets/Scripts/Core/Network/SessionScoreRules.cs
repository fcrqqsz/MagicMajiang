using System;

namespace MahjongGame.Core.Network
{
    public enum SessionEndReason
    {
        None = 0,
        ScheduledRoundsCompleted = 1,
        ScoreDepleted = 2,
        Aborted = 3
    }

    public static class SessionScoreRules
    {
        public static int GetInitialScore(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Single: return 50;
                case GameMode.EastOnly: return 100;
                case GameMode.HalfGame: return 150;
                case GameMode.FullGame: return 200;
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown game mode.");
            }
        }
    }
}
