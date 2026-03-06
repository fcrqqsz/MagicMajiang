using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using SuperMajiang.Network.Interfaces;
using SuperMajiang.Network.Mock;

namespace SuperMajiang.Systems
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
            LoadSceneAdditiveAsync("01_Login");
        }

        public async void LoadSceneAdditiveAsync(string sceneName)
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

        public async void UnloadSceneAsync(string sceneName)
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

        public async Task LoadSceneAndUnloadCurrentAsync(string sceneToLoad, string sceneToUnload)
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
    }
}
