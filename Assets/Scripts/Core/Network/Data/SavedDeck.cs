using System.Collections.Generic;
using MahjongGame.Talents;

namespace MahjongGame.Core.Network.Data
{
    [System.Serializable]
    public class SavedDeck
    {
        public string DeckId;
        public string DeckName;
        public int AlienationScore;
        public DeckConfig Config;
        public TalentSlotConfig Talents = new TalentSlotConfig();
    }
}
