using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.UI
{
    public static class ResultHandLayoutPolicy
    {
        public const float MinTileWidth = 32f;
        public const float MaxTileWidth = 52f;
        public const float InterTileGap = 2f;
        public const float SectionGap = 10f;
        public const float TileAspectRatio = 360f / 272f;

        public static int CountVisibleTiles(WinningHandSnapshot hand)
        {
            if (hand == null) return 0;

            int concealedCount = hand.concealedTiles?.Count(tile => tile != null && tile.isValid) ?? 0;
            int winningCount = hand.winningTile != null && hand.winningTile.isValid ? 1 : 0;
            int meldCount = hand.melds?.Where(meld => meld != null)
                .Sum(meld => meld.tiles?.Count(tile => tile != null && tile.isValid) ?? 0) ?? 0;
            return concealedCount + winningCount + meldCount;
        }

        public static float CalculateTileWidth(float availableWidth, int visibleTileCount, int sectionGapCount)
        {
            if (visibleTileCount <= 0) return 0f;

            float interTileWidth = Math.Max(0, visibleTileCount - 1) * InterTileGap;
            float sectionWidth = Math.Max(0, sectionGapCount) * SectionGap;
            float rawWidth = (availableWidth - interTileWidth - sectionWidth) / visibleTileCount;
            return Math.Max(MinTileWidth, Math.Min(MaxTileWidth, rawWidth));
        }

        public static bool ShouldUseTileBack(MeldType meldType, int tileIndex, int tileCount)
        {
            return meldType == MeldType.Kan_Concealed
                && tileCount > 1
                && (tileIndex == 0 || tileIndex == tileCount - 1);
        }
    }
}
