using System.Collections.Generic;

namespace SuperMajiang.Network.Data
{
    [System.Serializable]
    public class SavedDeck
    {
        public string DeckId;
        public string DeckName;
        public int AlienationScore;
        // Basic representation of deck tile counts. In reality this could be a list of custom structs.
        // JsonUtility doesn't serialize dictionaries, so we use a list of serializable structs.
        public List<TileCountEntry> TileCounts = new List<TileCountEntry>(); 
    }

    [System.Serializable]
    public struct TileCountEntry
    {
        public int TileID;
        public int Count;
    }
}
