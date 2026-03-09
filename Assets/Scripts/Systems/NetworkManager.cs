using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using MahjongGame.Core;
using MahjongGame.Core.Network.Interfaces;
using MahjongGame.Core.Network.Mock;

namespace MahjongGame.Systems
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        public IAuthService AuthService { get; private set; }
        public IMatchmakingService MatchmakingService { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);

                // Initialize Mock Services
                AuthService = new MockAuthService();
                MatchmakingService = new MockMatchmakingService();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // Auto load login scene additively
            _ = LoadSceneAdditiveAsync(SceneNames.Login);
        }

        public async Task LoadSceneAdditiveAsync(string sceneName)
        {
            try
            {
                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Show();

                AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

                while (!asyncLoad.isDone)
                {
                    await Task.Yield();
                }

                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Hide();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load scene '{sceneName}': {e.Message}");
                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Hide();
            }
        }

        public async Task UnloadSceneAsync(string sceneName)
        {
            try
            {
                AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
                if (asyncUnload != null)
                {
                    while (!asyncUnload.isDone)
                    {
                        await Task.Yield();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to unload scene '{sceneName}': {e.Message}");
            }
        }

        public async Task LoadSceneAndUnloadCurrentAsync(string sceneToLoad, string sceneToUnload)
        {
            try
            {
                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Show();

                AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Additive);
                while (!asyncLoad.isDone)
                {
                    await Task.Yield();
                }

                AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneToUnload);
                if (asyncUnload != null)
                {
                    while (!asyncUnload.isDone)
                    {
                        await Task.Yield();
                    }
                }

                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Hide();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed scene transition '{sceneToLoad}' <- '{sceneToUnload}': {e.Message}");
                if (LoadingScreenController.Instance != null)
                    LoadingScreenController.Instance.Hide();
            }
        }
    }
}
