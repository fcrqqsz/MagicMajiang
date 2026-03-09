using UnityEngine;
using UnityEngine.SceneManagement;
using MahjongGame.Core;

namespace MahjongGame.Systems
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

            if (scene.name == SceneNames.Game)
            {
                PersistentCamera.enabled = false;
            }
            else if (scene.name == SceneNames.Login || scene.name == SceneNames.MainLobby)
            {
                PersistentCamera.enabled = true;
            }
        }
    }
}
