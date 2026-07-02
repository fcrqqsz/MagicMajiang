using System;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Interfaces;

namespace MahjongGame.Core.Services
{
    public class WallService : IWallService
    {
        private List<TileData> _wallTiles = new List<TileData>();
        private Random _random;

        public WallService(int seed = -1)
        {
            if (seed == -1)
            {
                _random = new Random();
            }
            else
            {
                _random = new Random(seed);
            }
        }

        public void BuildWall(List<DeckConfig> playerConfigs)
        {
            _wallTiles.Clear();

            int playerId = 0;
            foreach (var config in playerConfigs)
            {
                // TODO: 假设 DeckConfig 有 GenerateTiles 方法。
                // 暂时需要保证 DeckConfig 可在逻辑层访问，如果它继承自 ScriptableObject 需要重构。
                // 如果它已经是 ScriptableObject，我们在服务端怎么创建？
                // 这个稍微复杂，我们回头再处理 DeckConfig，目前保持方法签名和 DeckManager 一致。
                if (config != null)
                {
                    List<TileData> playerTiles = config.GenerateTiles(playerId);
                    _wallTiles.AddRange(playerTiles);
                }
                playerId++;
            }
        }

        public List<TileData> GetWallTiles() => _wallTiles;

        public void ShuffleWall()
        {
            for (int i = 0; i < _wallTiles.Count; i++)
            {
                TileData temp = _wallTiles[i];
                int randomIndex = _random.Next(i, _wallTiles.Count);
                _wallTiles[i] = _wallTiles[randomIndex];
                _wallTiles[randomIndex] = temp;
            }
        }

        public TileData DrawTile()
        {
            if (_wallTiles.Count == 0)
            {
                return null;
            }

            TileData drawnTile = _wallTiles[0];
            _wallTiles.RemoveAt(0);
            return drawnTile;
        }

        public List<TileData> PeekTopTiles(int count)
        {
            int actual = Math.Min(count, _wallTiles.Count);
            return _wallTiles.GetRange(0, actual);
        }

        public int RemainingCount => _wallTiles.Count;
    }
}
