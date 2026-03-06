using UnityEngine;
using UnityEngine.SceneManagement;

namespace SuperMajiang.Systems
{
    public class CameraManager : MonoBehaviour
    {
        public static CameraManager Instance { get; private set; }

        [Tooltip("The main camera that exists in the Persistent scene.")]
        public Camera PersistentCamera;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (PersistentCamera == null) return;

            // Define which scenes need the persistent camera and which have their own
            if (scene.name == "03_Game")
            {
                // Game scene has its own 3D camera, so we disable the persistent one
                // to prevent rendering overlap and save performance.
                PersistentCamera.enabled = false;
            }
            else if (scene.name == "01_Login" || scene.name == "02_MainLobby")
            {
                // UI only scenes need the persistent camera to render the background and UI
                PersistentCamera.enabled = true;
            }
        }
    }
}
