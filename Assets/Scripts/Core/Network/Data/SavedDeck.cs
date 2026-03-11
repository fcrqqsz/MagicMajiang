using System.Collections.Generic;

namespace MahjongGame.Core.Network.Data
{
    [System.Serializable]
    public class SavedDeck
    {
        public string DeckId;
        public string DeckName;
        public int AlienationScore;
        public DeckConfig Config;
    }
}
