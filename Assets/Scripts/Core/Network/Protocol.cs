using System.Collections.Generic;

namespace MahjongGame.Core.Network
{
    /// <summary>
    /// 玩家提交给服务端的动作类型
    /// </summary>
    public enum ClientActionType
    {
        Skip,       // 过
        Discard,    // 出牌
        Chi,        // 吃
        Pon,        // 碰
        MingGan,    // 明杠
        AnGan,      // 暗杠
        JiaGang,    // 加杠
        Hu          // 胡
    }

    /// <summary>
    /// 客户端向服务端发送的意图请求
    /// </summary>
    public class ClientAction
    {
        public int PlayerId { get; private set; }
        public ClientActionType ActionType { get; private set; }
        public TileData TargetTile { get; private set; }
        
        // 补充数据
        public int[] ChiCombinations { get; private set; } // 吃的组合
        public int TotalFan { get; private set; } // 如果是胡牌，客户端算出的番数（服务端可以验证或直接信任）
        public List<string> FanDetails { get; private set; } // 胡牌细节

        public ClientAction(int playerId, ClientActionType type, TileData target = null, int[] chiCombinations = null)
        {
            PlayerId = playerId;
            ActionType = type;
            TargetTile = target;
            ChiCombinations = chiCombinations;
        }

        public void SetHuDetails(int totalFan, List<string> fanDetails)
        {
            TotalFan = totalFan;
            FanDetails = fanDetails;
        }

        public static ClientAction Skip(int playerId) => new ClientAction(playerId, ClientActionType.Skip);
        public static ClientAction Discard(int playerId, TileData tile) => new ClientAction(playerId, ClientActionType.Discard, tile);
    }
}
