using System.IO;
using UnityEngine;
using SuperMajiang.Network.Data;

namespace SuperMajiang.Systems
{
    public class ProfileManager : MonoBehaviour
    {
        public static ProfileManager Instance { get; private set; }

        public PlayerProfile CurrentProfile { get; private set; }
        
        private string profilePath;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                profilePath = Path.Combine(Application.persistentDataPath, "profile.json");
                LoadProfile();
            }
            else
            {
                Destroy(gameObject);
            }
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
