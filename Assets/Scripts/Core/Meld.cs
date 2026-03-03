using System.Collections.Generic;

namespace MahjongGame.Core
{
    public enum MeldType
    {
        Chi,            // 吃 (顺子)
        Pon,            // 碰 (刻子)
        Kan_Exposed,    // 明杠
        Kan_Concealed,  // 暗杠
        Kan_Added,      // 加杠 (碰了之后再杠)
        Knitted         // 组合龙的特殊顺子 (如 147, 258, 369)
    }

    [System.Serializable]
    public class Meld
    {
        public MeldType Type;
        public TileData FirstTile; // 代表牌（如果是吃，则是最小的那张；如果是碰，则是任意一张）
        public List<TileData> Tiles; // 包含的具体牌
        public int SourcePlayerID; // 供牌者（是谁打出来的？用于算分）
        public bool IsConcealed;   // 是否为暗面 (暗杠、或者立牌中拆解出来的面子)

        public bool IsPungOrKong => Type == MeldType.Pon || Type == MeldType.Kan_Exposed || Type == MeldType.Kan_Concealed || Type == MeldType.Kan_Added;
        public bool IsChow => Type == MeldType.Chi;
        public bool IsKong => Type == MeldType.Kan_Exposed || Type == MeldType.Kan_Concealed || Type == MeldType.Kan_Added;

        public Meld(MeldType type, List<TileData> tiles, int sourceId, bool isConcealed = false)
        {
            this.Type = type;
            this.Tiles = tiles;
            this.SourcePlayerID = sourceId;
            this.IsConcealed = isConcealed;
            // 简单的排序，方便取最小值
            tiles.Sort((a, b) => a.Value.CompareTo(b.Value));
            this.FirstTile = tiles[0];
        }
    }
}