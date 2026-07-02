using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Core;
using MahjongGame.Core.Interfaces;
using MahjongGame.Core.Services;

namespace MahjongGame.Systems
{
    public class DeckManager : MonoBehaviour, IWallService
    {
        // 单例模式 (Singleton)，方便全局访问
        public static DeckManager Instance { get; private set; }

        [Header("Assets")]
        public TileResourceConfig tileConfig;

        private IWallService _wallService;

        private void Awake()
        {
            if (Instance == null) 
            {
                Instance = this;
                _wallService = new WallService();
            }
            else 
            {
                Destroy(gameObject);
            }
        }

        public void BuildWall(List<DeckConfig> playerConfigs)
        {
            _wallService.BuildWall(playerConfigs);
            Debug.Log($"总牌山构建完成，共 {_wallService.RemainingCount} 张牌。");
        }

        public List<TileData> GetWallTiles() => _wallService.GetWallTiles();

        public void ShuffleWall()
        {
            _wallService.ShuffleWall();
            Debug.Log("洗牌完成");
        }

        public TileData DrawTile()
        {
            TileData drawnTile = _wallService.DrawTile();
            if (drawnTile == null)
            {
                Debug.LogWarning("流局！牌山已空。");
            }
            return drawnTile;
        }

        public List<TileData> PeekTopTiles(int count)
        {
            return _wallService.PeekTopTiles(count);
        }

        public int RemainingCount => _wallService.RemainingCount;
    }
}