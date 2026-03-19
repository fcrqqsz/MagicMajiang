using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public class TalentContext
    {
        public int CurrentPlayerId;
        public int TalentOwnerId;
        public List<TileData> WallTiles;       // WallBuilding 阶段可变引用
        public ServerGameState GameState;       // 读取快照
        public GameSession Session;
        public DeckConfig OwnerDeckConfig;

        public bool IsOwnersTurn => CurrentPlayerId == TalentOwnerId;
    }
}
