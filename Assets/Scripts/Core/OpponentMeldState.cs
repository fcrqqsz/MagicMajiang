using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core
{
    /// <summary>Client-side meld model represented by one opponent view.</summary>
    public sealed class OpponentMeldState
    {
        private readonly List<Meld> _melds = new List<Meld>();

        public IReadOnlyList<Meld> Melds => _melds;

        public void Clear()
        {
            _melds.Clear();
        }

        public void Replace(IEnumerable<Meld> melds)
        {
            _melds.Clear();
            foreach (var meld in melds ?? Enumerable.Empty<Meld>())
            {
                var clone = CloneMeld(meld);
                if (clone != null) _melds.Add(clone);
            }
        }

        public bool TryApply(MeldType type, IEnumerable<TileData> meldTiles)
        {
            var tiles = (meldTiles ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .ToList();
            if (tiles.Count == 0) return false;

            if (type == MeldType.Kan_Added)
            {
                var targetTile = tiles[0];
                var matchingPon = _melds.FirstOrDefault(meld => meld.Type == MeldType.Pon
                    && meld.FirstTile != null
                    && meld.FirstTile.TileSuit == targetTile.TileSuit
                    && meld.FirstTile.Value == targetTile.Value);
                if (matchingPon == null) return false;

                matchingPon.Type = MeldType.Kan_Added;
                matchingPon.Tiles.Add(targetTile);
                return true;
            }

            _melds.Add(new Meld(type, tiles, tiles[0].OriginalOwnerID,
                type == MeldType.Kan_Concealed));
            return true;
        }

        private static Meld CloneMeld(Meld meld)
        {
            if (meld?.Tiles == null) return null;
            var tiles = meld.Tiles.Where(tile => tile != null).ToList();
            if (tiles.Count == 0) return null;
            return new Meld(meld.Type, tiles, meld.SourcePlayerID, meld.IsConcealed);
        }
    }
}
