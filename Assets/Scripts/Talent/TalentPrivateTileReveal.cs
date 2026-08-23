using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    public sealed class TalentPrivateTileReveal
    {
        private readonly TileData[] _tiles;

        public string TalentId { get; }
        public int ViewerSeatIndex { get; }
        public int TargetSeatIndex { get; }
        public int RoundNumber { get; }
        public IReadOnlyList<TileData> Tiles => _tiles.Select(CopyTile).ToArray();

        public TalentPrivateTileReveal(
            string talentId,
            int viewerSeatIndex,
            int targetSeatIndex,
            int roundNumber,
            IEnumerable<TileData> tiles)
        {
            TalentId = talentId ?? string.Empty;
            ViewerSeatIndex = viewerSeatIndex;
            TargetSeatIndex = targetSeatIndex;
            RoundNumber = roundNumber;
            _tiles = (tiles ?? Enumerable.Empty<TileData>())
                .Where(t => t != null)
                .Select(CopyTile)
                .ToArray();
        }

        public TalentPrivateTileReveal CreateDetachedCopy() =>
            new TalentPrivateTileReveal(TalentId, ViewerSeatIndex, TargetSeatIndex, RoundNumber, _tiles);

        private static TileData CopyTile(TileData tile) => new TileData(tile.TileSuit, tile.Value, 0)
        {
            ID = string.Empty,
            IsModified = tile.IsModified,
            SpecialEffectID = null
        };
    }
}
