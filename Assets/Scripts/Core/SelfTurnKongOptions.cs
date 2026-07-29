using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core
{
    /// <summary>Legal concealed- and added-kong targets available during a self turn.</summary>
    public sealed class SelfTurnKongOptions
    {
        public IReadOnlyList<TileData> AnGangTargets { get; }
        public IReadOnlyList<TileData> JiaGangTargets { get; }
        public bool HasAny => AnGangTargets.Count > 0 || JiaGangTargets.Count > 0;

        public SelfTurnKongOptions(IEnumerable<TileData> anGangTargets, IEnumerable<TileData> jiaGangTargets)
        {
            AnGangTargets = (anGangTargets ?? Enumerable.Empty<TileData>()).ToList().AsReadOnly();
            JiaGangTargets = (jiaGangTargets ?? Enumerable.Empty<TileData>()).ToList().AsReadOnly();
        }
    }

    public static class SelfTurnKongResolver
    {
        public static SelfTurnKongOptions Resolve(IEnumerable<TileData> hand, IEnumerable<Meld> melds)
        {
            var handTiles = (hand ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .ToList();

            var anGangTargets = handTiles
                .GroupBy(tile => new { tile.TileSuit, tile.Value })
                .Where(group => group.Count() >= 4)
                .Select(group => group.First())
                .ToList();

            var jiaGangTargets = (melds ?? Enumerable.Empty<Meld>())
                .Where(meld => meld != null && meld.Type == MeldType.Pon && meld.FirstTile != null)
                .Select(meld => meld.FirstTile)
                .Where(ponTile => handTiles.Any(handTile => handTile.TileSuit == ponTile.TileSuit && handTile.Value == ponTile.Value))
                .GroupBy(tile => new { tile.TileSuit, tile.Value })
                .Select(group => handTiles.First(handTile => handTile.TileSuit == group.Key.TileSuit && handTile.Value == group.Key.Value))
                .ToList();

            return new SelfTurnKongOptions(anGangTargets, jiaGangTargets);
        }
    }
}
