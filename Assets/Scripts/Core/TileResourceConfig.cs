using UnityEngine;
using System.Collections.Generic;

namespace MahjongGame.Core
{
    [CreateAssetMenu(fileName = "MainTileConfig", menuName = "Mahjong/Tile Config")]
    public class TileResourceConfig : ScriptableObject
    {
        // [优化] 使用单一数组存储所有 34 种牌的图片
        // 索引映射遵循 MahjongLogic.GetTileIndex:
        // 0-8: 万, 9-17: 筒, 18-26: 索, 27-30: 东南西北, 31-33: 中发白
        public Sprite[] allTileSprites = new Sprite[34];

        // 兼容性保留（可选，建议在编辑器脚本迁移后移除）
        [HideInInspector] public List<Sprite> manTiles;
        [HideInInspector] public List<Sprite> pinTiles;
        [HideInInspector] public List<Sprite> souTiles;
        [HideInInspector] public List<Sprite> windTiles;
        [HideInInspector] public List<Sprite> dragonTiles;

        /// <summary>
        /// 根据 TileData 获取对应的 Sprite
        /// [优化] 时间复杂度从 O(N) 降为 O(1)
        /// </summary>
        public Sprite GetSprite(TileData data)
        {
            if (data == null) return null;
            
            int idx = MahjongLogic.GetTileIndex(data);
            
            if (idx >= 0 && idx < allTileSprites.Length)
            {
                return allTileSprites[idx];
            }
            
            Debug.LogWarning($"[TileConfig] 索引越界: {data.TileSuit}_{data.Value} -> Index: {idx}");
            return null;
        }
    }
}