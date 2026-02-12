using System.Collections.Generic;

namespace MahjongGame.Core.Fan
{
    /// <summary>
    /// 听牌类型定义
    /// </summary>
    public enum WaitType
    {
        Unknown,
        Single,     // 单钓 (1番)
        Closed,     // 坎张 (1番)
        Edge,       // 边张 (1番)
        TwoSided,   // 两面
        Other       // 其他
    }

    /// <summary>
    /// 手牌拆解结果
    /// </summary>
    public class HandDecomposition
    {
        public List<Meld> AllMelds = new List<Meld>(); // 包含副露和立牌拆出的面子
        public List<TileData> Pair = new List<TileData>(); // 雀头
    }

    /// <summary>
    /// 算番所需的全部上下文信息
    /// </summary>
    public class FanContext
    {
        public List<TileData> HandTiles; // 立牌 (原始)
        public List<Meld> FixedMelds;    // 副露 (原始)
        public TileData WinningTile;     // 胡的那张牌
        public bool IsSelfDraw;          // 是否自摸
        public int[] HandCounts;         // 频率数组 (缓存优化)
        
        // --- 核心结构 ---
        public HandDecomposition Decomposition; // 当前采用的拆解方案

        // --- 动作状态 ---
        public bool IsKongWin;           // 杠上开花 (8番)
        public bool IsRobKongWin;        // 抢杠胡 (8番)
        public bool IsLastTileWin;       // 和绝张 (4番)
        public WaitType Wait;            // 听牌类型

        // 场况信息
        public Suit RoundWind; // 圈风
        public Suit SeatWind;  // 门风

        public FanContext(List<TileData> hand, List<Meld> melds, TileData winTile, bool selfDraw, Suit round, Suit seat, HandDecomposition decomposition = null)
        {
            this.HandTiles = new List<TileData>(hand);
            this.FixedMelds = melds;
            this.WinningTile = winTile;
            this.IsSelfDraw = selfDraw;
            this.RoundWind = round;
            this.SeatWind = seat;
            this.Wait = WaitType.Unknown;
            this.Decomposition = decomposition;

            // 预先计算好频率数组
            this.HandCounts = MahjongLogic.ConvertToFrequencyArray(hand);
            if (!IsSelfDraw && winTile != null) 
            {
                this.HandCounts[MahjongLogic.GetTileIndex(winTile)]++;
            }
        }
    }
}