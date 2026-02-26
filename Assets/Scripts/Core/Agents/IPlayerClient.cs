using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Core.Agents
{
    /// <summary>
    /// 胖客户端接口（定义接收服务端事件的方法）
    /// </summary>
    public interface IPlayerClient
    {
        int PlayerId { get; }

        /// <summary>
        /// 游戏开始时，服务端同步初始手牌给客户端
        /// </summary>
        void OnGameStart(List<TileData> startingHand);

        /// <summary>
        /// 回合开始，自己摸牌
        /// </summary>
        void OnTileDrawn(TileData drawnTile);

        /// <summary>
        /// 广播：其他玩家打出了一张牌
        /// </summary>
        /// <param name="playerId">打牌的玩家ID</param>
        /// <param name="discardedTile">打出的牌</param>
        void OnOtherPlayerDiscarded(int playerId, TileData discardedTile);

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
        void OnPlayerWin(int playerId, int totalFan, List<string> fanDetails, bool isSelfDraw);
    }
}
