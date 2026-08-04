using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using MahjongGame.Core;
using MahjongGame.Core.Network.Interfaces;
using MahjongGame.Core.Network.Mock;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Systems
{
    public class NetworkManager : MonoBehaviour
    {
        public static NetworkManager Instance { get; private set; }

        public IAuthService AuthService { get; private set; }
        public IMatchmakingService MatchmakingService { get; private set; }
        public ClientRoomService RoomService { get; private set; }
        private bool _isRoutingRecoverySnapshot;
        private RoomGameSnapshot _queuedRecoverySnapshot;
        private LoadingScreenController _loadingScreen;

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
                RoomService.ReconnectSnapshotApplied += HandleReconnectSnapshotApplied;
                RoomService.RecoveryProgressChanged += HandleRecoveryProgressChanged;
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
                RoomService.ReconnectSnapshotApplied -= HandleReconnectSnapshotApplied;
                RoomService.RecoveryProgressChanged -= HandleRecoveryProgressChanged;
                RoomService.Dispose();
            }
            DetachLoadingScreen();
        }

        private void Start()
        {
            AttachLoadingScreen();
            // Auto load login scene additively
            _ = LoadSceneAdditiveAsync(SceneNames.Login);
        }

        private void Update()
        {
            AttachLoadingScreen();
            RoomService?.Tick(Time.unscaledTime);
        }

        private void HandleRecoveryProgressChanged(ClientRecoveryProgress progress)
        {
            AttachLoadingScreen();
            if (progress == null) return;
            if (progress.Stage == ClientRecoveryStage.Restored) return;
            _loadingScreen?.ShowReconnect(progress);
            if (progress.Stage == ClientRecoveryStage.TerminalFailure)
                _ = RouteTerminalRecoveryToLobbyAsync();
        }

        private async void HandleReconnectSnapshotApplied(RoomGameSnapshot snapshot)
        {
            if (snapshot == null) return;
            _queuedRecoverySnapshot = snapshot;
            if (_isRoutingRecoverySnapshot) return;

            _isRoutingRecoverySnapshot = true;
            try
            {
                while (_queuedRecoverySnapshot != null)
                {
                    var pending = _queuedRecoverySnapshot;
                    _queuedRecoverySnapshot = null;
                    await RouteRecoverySnapshotAsync(pending);
                }
            }
            finally
            {
                _isRoutingRecoverySnapshot = false;
            }
        }

        private async Task RouteRecoverySnapshotAsync(RoomGameSnapshot snapshot)
        {
            var target = ClientRecoverySceneRoutingPolicy.GetTarget((RoomState)snapshot.roomState);
            switch (target)
            {
                case ClientRecoverySceneTarget.Lobby:
                    await EnsureRecoverySceneAsync(SceneNames.MainLobby);
                    break;
                case ClientRecoverySceneTarget.Game:
                    await EnsureRecoverySceneAsync(SceneNames.Game);
                    GameManager.Instance?.ApplyNetworkRecoverySnapshot(snapshot, RoomService?.RecoveryPresentationVersion ?? 0);
                    break;
                default:
                    await RouteTerminalRecoveryToLobbyAsync();
                    return;
            }

            AttachLoadingScreen();
            _loadingScreen?.HideReconnect();
        }

        private async Task RouteTerminalRecoveryToLobbyAsync()
        {
            var game = SceneManager.GetSceneByName(SceneNames.Game);
            if (!game.isLoaded) return;
            await EnsureRecoverySceneAsync(SceneNames.MainLobby);
        }

        private async Task EnsureRecoverySceneAsync(string targetScene)
        {
            if (!SceneManager.GetSceneByName(targetScene).isLoaded)
                await LoadSceneAdditiveAsync(targetScene);

            SetActiveSceneIfLoaded(targetScene);
            foreach (var sceneName in new[] { SceneNames.Login, SceneNames.MainLobby, SceneNames.Game })
            {
                if (sceneName == targetScene) continue;
                if (SceneManager.GetSceneByName(sceneName).isLoaded)
                    await UnloadSceneAsync(sceneName);
            }
        }

        private void AttachLoadingScreen()
        {
            if (_loadingScreen == LoadingScreenController.Instance) return;
            DetachLoadingScreen();
            _loadingScreen = LoadingScreenController.Instance;
            if (_loadingScreen != null)
                _loadingScreen.ReconnectLeaveRequested += HandleReconnectLeaveRequested;
        }

        private void DetachLoadingScreen()
        {
            if (_loadingScreen != null)
                _loadingScreen.ReconnectLeaveRequested -= HandleReconnectLeaveRequested;
            _loadingScreen = null;
        }

        private async void HandleReconnectLeaveRequested()
        {
            RoomService?.LeaveRoomOrAbandonRecovery();
            AttachLoadingScreen();
            _loadingScreen?.HideReconnect();
            await EnsureRecoverySceneAsync(SceneNames.MainLobby);
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

        public async Task ReturnToPersistentFlowAsync()
        {
            if (!SceneManager.GetSceneByName(SceneNames.Persistent).isLoaded)
            {
                SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
                return;
            }

            await EnsureRecoverySceneAsync(SceneNames.Login);
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
