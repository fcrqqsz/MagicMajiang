using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Core;
using System.Linq; // 类似于 C++ 的 STL Algorithms (sort, find, where)

namespace MahjongGame.Systems
{
    public class DeckManager : MonoBehaviour
    {
        // 单例模式 (Singleton)，方便全局访问
        public static DeckManager Instance { get; private set; }

        [Header("Assets")]
        public TileResourceConfig tileConfig;

        // 这里的 List 类似于 C++ std::vector<TileData>
        // 存储当前牌山中剩余的牌
        private List<TileData> _wallTiles = new List<TileData>();

        // 岭上牌 (补花/杠用)
        private List<TileData> _deadWallTiles = new List<TileData>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        /// <summary>
        /// [重构] 核心流程：根据配置构建牌山
        /// </summary>
        /// <param name="playerConfigs">每个玩家的牌库配置 (Index即为PlayerID)</param>
        public void BuildWall(List<DeckConfig> playerConfigs)
        {
            _wallTiles.Clear();

            int playerId = 0;
            foreach (var config in playerConfigs)
            {
                // 1. 生成实际的牌数据
                List<TileData> playerTiles = config.GenerateTiles(playerId);
                
                // 2. 可以在这里打印日志，查看该玩家的异化程度
                Debug.Log($"玩家 {playerId} 牌库生成完毕. " +
                          $"卡牌数: {playerTiles.Count}, " +
                          $"异化值: <color=red>{config.AlienationScore}</color>");

                // 3. 混入总牌山
                _wallTiles.AddRange(playerTiles);
                
                playerId++;
            }

            Debug.Log($"总牌山构建完成，共 {_wallTiles.Count} 张牌。");
        }

        /// <summary>
        /// 获取牌山列表引用（供天赋系统在洗牌前修改）
        /// </summary>
        public List<TileData> GetWallTiles() => _wallTiles;

        /// <summary>
        /// Fisher-Yates 洗牌算法
        /// </summary>
        public void ShuffleWall()
        {
            // Unity 的 Random Range 是左闭右开 [min, max)
            for (int i = 0; i < _wallTiles.Count; i++)
            {
                TileData temp = _wallTiles[i];
                int randomIndex = Random.Range(i, _wallTiles.Count);
                _wallTiles[i] = _wallTiles[randomIndex];
                _wallTiles[randomIndex] = temp;
            }
            
            Debug.Log("洗牌完成");
        }

        /// <summary>
        /// 摸牌逻辑
        /// </summary>
        public TileData DrawTile()
        {
            if (_wallTiles.Count == 0)
            {
                Debug.LogWarning("流局！牌山已空。");
                return null;
            }

            // 类似于 C++ vector::pop_back()，但在麻将中通常从前端摸
            // 列表移除首元素开销较大 (O(N))，优化方案是用索引指针或 LinkedList
            // 但考虑到麻将最多144张，List的性能完全足够。
            TileData drawnTile = _wallTiles[0];
            _wallTiles.RemoveAt(0);
            
            return drawnTile;
        }

        /// <summary>
        /// 窥探牌山顶部 n 张牌（不移除）
        /// </summary>
        public List<TileData> PeekTopTiles(int count)
        {
            int actual = Mathf.Min(count, _wallTiles.Count);
            return _wallTiles.GetRange(0, actual);
        }

        // 获取剩余张数
        public int RemainingCount => _wallTiles.Count;
    }
}