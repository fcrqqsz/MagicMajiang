using System.Collections.Generic;

namespace MahjongGame.Core
{
    public enum MeldType
    {
        Chi,            // 吃 (顺子)
        Pon,            // 碰 (刻子)
        Kan_Exposed,    // 明杠
        Kan_Concealed,  // 暗杠
        Kan_Added       // 加杠 (碰了之后再杠)
    }

    [System.Serializable]
    public class Meld
    {
        public MeldType Type;
        public TileData FirstTile; // 代表牌（如果是吃，则是最小的那张；如果是碰，则是任意一张）
        public List<TileData> Tiles; // 包含的具体牌
        public int SourcePlayerID; // 供牌者（是谁打出来的？用于算分）

        public Meld(MeldType type, List<TileData> tiles, int sourceId)
        {
            this.Type = type;
            this.Tiles = tiles;
            this.SourcePlayerID = sourceId;
            // 简单的排序，方便取最小值
            tiles.Sort((a, b) => a.Value.CompareTo(b.Value));
            this.FirstTile = tiles[0];
        }
        
        // 辅助：获取这组牌的代表ID (用于算法)
        // 假设 Man_1 = 0, Man_9 = 8, Pin_1 = 9 ...
        // 这个转换逻辑稍后在 LogicUtils 里统一写
    }
}