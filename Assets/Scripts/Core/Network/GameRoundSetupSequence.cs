using System;

namespace MahjongGame.Core.Network
{
    /// <summary>
    /// Keeps the authoritative wall setup order shared by GameServer and deterministic regressions.
    /// </summary>
    public static class GameRoundSetupSequence
    {
        public static void BuildShuffleDealAndCapturePeek(
            Action buildWall,
            Action applyWallTalents,
            Action shuffleWall,
            Action dealStartingHands,
            Action capturePeek)
        {
            if (buildWall == null) throw new ArgumentNullException(nameof(buildWall));
            if (applyWallTalents == null) throw new ArgumentNullException(nameof(applyWallTalents));
            if (shuffleWall == null) throw new ArgumentNullException(nameof(shuffleWall));
            if (dealStartingHands == null) throw new ArgumentNullException(nameof(dealStartingHands));
            if (capturePeek == null) throw new ArgumentNullException(nameof(capturePeek));

            buildWall();
            applyWallTalents();
            shuffleWall();
            dealStartingHands();
            capturePeek();
        }
    }
}
