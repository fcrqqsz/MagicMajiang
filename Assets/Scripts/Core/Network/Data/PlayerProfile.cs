using System.Collections.Generic;

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
    }

    [System.Serializable]
    public class ProfileSettings
    {
        public float MasterVolume = 1.0f;
        public float MusicVolume = 1.0f;
        public float SFXVolume = 1.0f;
        public bool DebugMode = false;
    }
}
