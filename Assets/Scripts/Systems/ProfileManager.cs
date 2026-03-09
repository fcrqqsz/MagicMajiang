using System.IO;
using UnityEngine;
using MahjongGame.Core.Network.Data;

namespace MahjongGame.Systems
{
    public class ProfileManager
    {
        private static ProfileManager _instance;
        public static ProfileManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new ProfileManager();
                return _instance;
            }
        }

        public PlayerProfile CurrentProfile { get; private set; }

        private readonly string profilePath;

        private ProfileManager()
        {
            profilePath = Path.Combine(Application.persistentDataPath, "profile.json");
            LoadProfile();
        }

        public void LoadProfile()
        {
            if (File.Exists(profilePath))
            {
                try
                {
                    string json = File.ReadAllText(profilePath);
                    CurrentProfile = JsonUtility.FromJson<PlayerProfile>(json);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to load profile: {e.Message}");
                    CreateNewProfile();
                }
            }
            else
            {
                CreateNewProfile();
            }
        }

        private void CreateNewProfile()
        {
            CurrentProfile = new PlayerProfile
            {
                UID = System.Guid.NewGuid().ToString(),
                Nickname = "Guest_" + Random.Range(1000, 9999).ToString()
            };
            SaveProfile();
        }

        public void SaveProfile()
        {
            try
            {
                string json = JsonUtility.ToJson(CurrentProfile, true);
                File.WriteAllText(profilePath, json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save profile: {e.Message}");
            }
        }
    }
}
