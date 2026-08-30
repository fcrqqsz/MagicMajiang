using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Core.Network.Data
{
    [System.Serializable]
    public class PlayerProfile
    {
        public string UID;
        public string Nickname;
        public ProfileSettings Settings = new ProfileSettings();
        public int SelectedDeckIndex;
        public List<SavedDeck> SavedDecks = new List<SavedDeck>();
        // Future Proofing: Gacha & Cosmetics
        public List<string> Inventory = new List<string>();
        // Note: Unity's JsonUtility does not natively support Dictionary. 
        // We will use two lists for keys and values for simple serialization if needed, or a custom class.
        // For now, simple list of unlocked cosmetics is enough.
        public List<string> UnlockedCosmetics = new List<string>();

        public void Normalize()
        {
            Settings ??= new ProfileSettings();
            Settings.Normalize();
            SavedDecks ??= new List<SavedDeck>();
            foreach (SavedDeck deck in SavedDecks)
            {
                deck?.Normalize();
            }
        }
    }

    [System.Serializable]
    public class ProfileSettings
    {
        public int SelectedGameMode = 0; // 0=Single, 1=EastOnly, 2=HalfGame, 3=FullGame
        public void Normalize() { }
    }
}
