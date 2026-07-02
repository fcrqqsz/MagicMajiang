using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Core.Interfaces
{
    public interface IWallService
    {
        void BuildWall(List<DeckConfig> playerConfigs);
        List<TileData> GetWallTiles();
        void ShuffleWall();
        TileData DrawTile();
        List<TileData> PeekTopTiles(int count);
        int RemainingCount { get; }
    }
}
