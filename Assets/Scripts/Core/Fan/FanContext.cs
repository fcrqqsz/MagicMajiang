using System.Collections.Generic;

namespace MahjongGame.Core.Fan
{
    /// <summary>
    /// 算番所需的全部上下文信息
    /// </summary>
    public class FanContext
    {
        public List<TileData> HandTiles; // 立牌
        public List<Meld> Melds;         // 副露
        public TileData WinningTile;     // 胡的那张牌
        public bool IsSelfDraw;          // 是否自摸
        public int[] HandCounts;         // 频率数组 (缓存优化)
        
        // 场况信息
        public Suit RoundWind; // 圈风
        public Suit SeatWind;  // 门风

        public FanContext(List<TileData> hand, List<Meld> melds, TileData winTile, bool selfDraw, Suit round, Suit seat)
        {
            this.HandTiles = new List<TileData>(hand);
            this.Melds = melds;
            this.WinningTile = winTile;
            this.IsSelfDraw = selfDraw;
            this.RoundWind = round;
            this.SeatWind = seat;

            // 预先计算好频率数组，方便各个番种直接查
            // 注意：算番时，要把胡的那张牌算进手牌里
            this.HandCounts = MahjongLogic.ConvertToFrequencyArray(hand);
            if (!IsSelfDraw && winTile != null) // 如果是点炮，把这张牌加进去算
            {
                this.HandCounts[MahjongLogic.GetTileIndex(winTile)]++;
            }
        }
    }
}