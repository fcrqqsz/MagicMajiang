using System.Collections.Generic;
using System.Threading;
using MahjongGame.Core;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Agents
{
    /// <summary>
    /// 胖客户端接口（定义接收服务端事件的方法）
    /// </summary>
    public interface IPlayerClient
    {
        int PlayerId { get; }

        /// <summary>
        /// 服务端在每个等待阶段前设置，客户端用于取消正在执行的 async 操作
        /// </summary>
        CancellationToken TurnCancellationToken { get; set; }

        /// <summary>
        /// 游戏开始时，服务端同步初始手牌给客户端
        /// </summary>
        void OnGameStart(List<TileData> startingHand);

        /// <summary>
        /// 回合开始，自己摸牌
        /// </summary>
        void OnTileDrawn(TileData drawnTile);

        /// <summary>
        /// 广播：某人摸牌了（用于对手表现层增加暗牌）
        /// </summary>
        void OnPlayerDrawn(int playerId);

        /// <summary>吃、碰后由服务端确认的直接出牌回合。</summary>
        void OnTurnWithoutDraw();

        /// <summary>服务端权威牌山余量同步。</summary>
        void OnWallCountChanged(int remainingCount);

        /// <summary>
        /// 广播：其他玩家打出了一张牌
        /// </summary>
        /// <param name="playerId">打牌的玩家ID</param>
        /// <param name="discardedTile">打出的牌</param>
        void OnOtherPlayerDiscarded(int playerId, TileData discardedTile);

        /// <summary>
        /// 广播：某人声明加杠，其他玩家可在该牌真正落副露前选择胡或过。
        /// </summary>
        void OnAddedKongDeclared(int playerId, TileData targetTile);

        /// <summary>
        /// 广播：某人执行了某个动作（比如碰、杠、吃）
        /// 用于客户端同步表现层的副露动画、更新其余牌的数量等
        /// </summary>
        void OnActionResolved(int playerId, Network.ClientActionType actionType, TileData targetTile, int[] chiCombinations = null);
        
        /// <summary>
        /// 广播：流局
        /// </summary>
        void OnDrawGame();

        /// <summary>
        /// 广播：有人胡牌
        /// </summary>
        void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw,
            WinKind winKind, int loserId, WinningHandSnapshot winningHand,
            TalentFanBreakdownMessage talentFanBreakdown);

        /// <summary>
        /// 新一局开始时，服务端同步圈风/门风/东家信息
        /// </summary>
        void OnRoundStart(int roundNumber, WindDirection prevalentWind, WindDirection seatWind, int dealerIndex);

        /// <summary>
        /// 整个对战结束，广播最终分数
        /// </summary>
        void OnSessionEnd(int[] finalScores);

        /// <summary>
        /// 服务端超时自动处理后，通知客户端清理UI状态并同步手牌
        /// </summary>
        /// <param name="autoDiscardedTile">服务端自动出的牌，null表示响应超时（跳过）</param>
        void OnTimeout(TileData autoDiscardedTile);

        /// <summary>
        /// 通知客户端天赋带来的算番加成信息
        /// </summary>
        void OnTalentInfo(ScoringOptions scoringOptions);

        /// <summary>
        /// 窥探天赋：通知牌山顶部几张牌
        /// </summary>
        void OnPeekWallTiles(List<TileData> topTiles);
    }
}
