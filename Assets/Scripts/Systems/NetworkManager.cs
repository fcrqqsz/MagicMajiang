using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using MahjongGame.Core;
using MahjongGame.Core.Network.Interfaces;
using MahjongGame.Core.Network.Mock;
using MahjongGame.Core.Network;

namespace MahjongGame.Systems
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        public IAuthService AuthService { get; private set; }
        public IMatchmakingService MatchmakingService { get; private set; }
        public ClientRoomService RoomService { get; private set; }

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
                RoomService = new ClientRoomService("ws://127.0.0.1:9876/game");
                RoomService.RoomReady += HandleRoomReady;
                RoomService.RoomClosed += HandleRoomClosed;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private async void HandleRoomReady()
        {
            if (SceneManager.GetSceneByName(SceneNames.Game).isLoaded) return;
            await LoadSceneAndUnloadCurrentAsync(SceneNames.Game, SceneNames.MainLobby);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (RoomService != null)
            {
                RoomService.RoomReady -= HandleRoomReady;
                RoomService.RoomClosed -= HandleRoomClosed;
                RoomService.Dispose();
            }
        }

        private void Start()
        {
            // Auto load login scene additively
            _ = LoadSceneAdditiveAsync(SceneNames.Login);
        }

        private void Update()
        {
            RoomService?.Tick(Time.unscaledTime);
        }

        private async void HandleRoomClosed(string reason)
        {
            Debug.LogWarning($"[NetworkManager] Room closed: {reason}");
            var gameScene = SceneManager.GetSceneByName(SceneNames.Game);
            if (!SceneTransitionPolicy.ShouldUnloadGameScene(gameScene.isLoaded)) return;

            var lobbyScene = SceneManager.GetSceneByName(SceneNames.MainLobby);
            if (lobbyScene.isLoaded)
                await UnloadSceneAsync(SceneNames.Game);
            else
                await LoadSceneAndUnloadCurrentAsync(SceneNames.MainLobby, SceneNames.Game);
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

                SetActiveSceneIfLoaded(sceneName);

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

                SetActiveSceneIfLoaded(sceneToLoad);

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

        private static void SetActiveSceneIfLoaded(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
                SceneManager.SetActiveScene(scene);
        }
    }
}
