using System.Collections.Generic;

namespace MahjongGame.Core.Network.Data
{
    [System.Serializable]
    public class PlayerProfile
    {
        public string UID;
        public string Nickname;
        public ProfileSettings Settings = new ProfileSettings();
        public List<SavedDeck> SavedDecks = new List<SavedDeck>();
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
