using System;

namespace MahjongGame.Core.Network
{
    /// <summary>Applies the dedicated server's score snapshot to a client-side session.</summary>
    public static class SessionScorePolicy
    {
        public static bool ApplyAuthoritativeScores(GameSession session, int[] scores)
        {
            if (session?.Scores == null || scores == null || scores.Length == 0) return false;

            int count = Math.Min(session.Scores.Length, scores.Length);
            for (int i = 0; i < count; i++) session.Scores[i] = scores[i];
            return true;
        }
    }
}
