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
        /// <summary>Session-only UI selection; reconnect tickets never replace it.</summary>
        public ClientServerEnvironment SelectedServerEnvironment { get; private set; }
        private ClientSceneNavigation _sceneNavigation;
        private Task _lobbyNavigationTask;
        private int _recoveryRequestVersion;
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
                SelectedServerEnvironment = ClientServerStartupPolicy.InitialEnvironment;
                RoomService = new ClientRoomService(ClientServerEndpointPolicy.Resolve(SelectedServerEnvironment));
                RoomService.RoomReady += HandleRoomReady;
                RoomService.RoomJoined += HandleRoomJoined;
                RoomService.RoomClosed += HandleRoomClosed;
                RoomService.ReconnectSnapshotApplied += HandleReconnectSnapshotApplied;
                RoomService.RecoveryProgressChanged += HandleRecoveryProgressChanged;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void HandleRoomReady()
        {
            if (RoomService?.HasRoom != true || IsLeavingForLobby) return;
            _ = ObserveNavigationAsync(NavigateToSceneAsync(SceneNames.Game));
        }

        private void HandleRoomJoined(RoomJoinedMessage joined)
        {
            if (!IsLeavingForLobby) _lobbyNavigationTask = null;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            _sceneNavigation?.Invalidate();
            if (RoomService != null)
            {
                RoomService.RoomReady -= HandleRoomReady;
                RoomService.RoomJoined -= HandleRoomJoined;
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
            _ = ObserveNavigationAsync(LoadSceneAdditiveAsync(SceneNames.Login));
        }

        private void Update()
        {
            AttachLoadingScreen();
            RoomService?.Tick(Time.unscaledTime);
        }

        /// <summary>Switches the session-only online/local selection and starts its Hello handshake.</summary>
        public bool SelectServerEnvironment(ClientServerEnvironment environment, string username)
        {
            if (RoomService == null) return false;
            string address = ClientServerEndpointPolicy.Resolve(environment);
            return ClientServerEnvironmentSelectionPolicy.TrySwitch(
                SelectedServerEnvironment,
                environment,
                () => RoomService.TrySwitchServer(address, LoginUsernamePolicy.Normalize(username)),
                selectedEnvironment => SelectedServerEnvironment = selectedEnvironment);
        }

        /// <summary>Restores a matching saved room first; otherwise starts the selected server connection immediately.</summary>
        public bool ConnectAfterLogin(string username)
        {
            if (RoomService == null) return false;
            string normalizedUsername = LoginUsernamePolicy.Normalize(username);
            bool reconnectStarted = RoomService.ReconnectSavedRoom(normalizedUsername);
            if (!ClientServerStartupPolicy.ShouldConnectSelectedServerAfterLogin(reconnectStarted)) return true;
            return RoomService.TryReconnectSelectedServer(normalizedUsername);
        }

        private void HandleRecoveryProgressChanged(ClientRecoveryProgress progress)
        {
            AttachLoadingScreen();
            if (progress == null) return;
            if (progress.Stage == ClientRecoveryStage.Restored) return;
            _loadingScreen?.ShowReconnect(progress);
            if (progress.Stage == ClientRecoveryStage.TerminalFailure)
                _ = ObserveNavigationAsync(NavigateToLobbyAsync(false));
        }

        private async void HandleReconnectSnapshotApplied(RoomGameSnapshot snapshot)
        {
            if (snapshot == null || RoomService?.HasRoom != true || IsLeavingForLobby) return;
            int requestVersion = ++_recoveryRequestVersion;
            int presentationVersion = RoomService.RecoveryPresentationVersion;
            try
            {
                var target = ClientRecoverySceneRoutingPolicy.GetTarget((RoomState)snapshot.roomState);
                if (target == ClientRecoverySceneTarget.None)
                {
                    await NavigateToLobbyAsync(false);
                    return;
                }
                string targetScene = target == ClientRecoverySceneTarget.Game ? SceneNames.Game : SceneNames.MainLobby;
                Task navigation = NavigateToSceneAsync(targetScene);
                int generation = SceneNavigation.Generation;
                await navigation;
                if (generation != SceneNavigation.Generation || requestVersion != _recoveryRequestVersion
                    || RoomService?.HasRoom != true || IsLeavingForLobby) return;
                if (target == ClientRecoverySceneTarget.Game)
                    GameManager.Instance?.ApplyNetworkRecoverySnapshot(snapshot, presentationVersion);
                AttachLoadingScreen();
                _loadingScreen?.HideReconnect();
            }
            catch (Exception error)
            {
                Debug.LogError($"Failed to route room recovery: {error.Message}");
            }
        }

        private bool IsLeavingForLobby => _lobbyNavigationTask != null && !_lobbyNavigationTask.IsCompleted;

        private ClientSceneNavigation SceneNavigation => _sceneNavigation ?? (_sceneNavigation = new ClientSceneNavigation(
            new[] { SceneNames.Login, SceneNames.MainLobby, SceneNames.Game },
            scene => SceneManager.GetSceneByName(scene).isLoaded,
            LoadSceneCoreAsync, SetActiveSceneIfLoaded, UnloadSceneAsync));

        /// <summary>One completion boundary for battle-menu, result, and reconnect exits.</summary>
        public Task LeaveBattleToLobbyAsync() => NavigateToLobbyAsync(true);

        private Task NavigateToLobbyAsync(bool leaveRoom)
        {
            if (_lobbyNavigationTask != null && !_lobbyNavigationTask.IsFaulted
                && (!_lobbyNavigationTask.IsCompleted || RoomService?.HasRoom != true)) return _lobbyNavigationTask;
            SceneNavigation.Invalidate();
            _recoveryRequestVersion++;
            var completion = new TaskCompletionSource<bool>();
            _lobbyNavigationTask = completion.Task;
            _ = CompleteLobbyNavigationAsync(leaveRoom, completion);
            return completion.Task;
        }

        private async Task CompleteLobbyNavigationAsync(bool leaveRoom, TaskCompletionSource<bool> completion)
        {
            try
            {
                if (leaveRoom && RoomService != null) await RoomService.LeaveRoomForLobbyAsync();
                await NavigateToSceneAsync(SceneNames.MainLobby);
                AttachLoadingScreen();
                _loadingScreen?.HideReconnect();
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
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

        private void HandleReconnectLeaveRequested()
        {
            _ = ObserveNavigationAsync(LeaveBattleToLobbyAsync());
        }

        private void HandleRoomClosed(string reason)
        {
            Debug.LogWarning($"[NetworkManager] Room closed: {reason}");
            _ = ObserveNavigationAsync(NavigateToLobbyAsync(false));
        }

        public async Task ReturnToPersistentFlowAsync()
        {
            // A Game scene already importing when exit started may run its Awake guard.
            // Its fallback must join the authoritative lobby route instead of replacing it.
            if (IsLeavingForLobby)
            {
                await _lobbyNavigationTask;
                return;
            }
            if (!SceneManager.GetSceneByName(SceneNames.Persistent).isLoaded)
            {
                SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
                return;
            }

            await NavigateToSceneAsync(SceneNames.Login);
        }

        public Task LoadSceneAdditiveAsync(string sceneName) => NavigateToSceneAsync(sceneName);

        private async Task NavigateToSceneAsync(string sceneName)
        {
            LoadingScreenController.Instance?.Show();
            Task navigation = SceneNavigation.NavigateAsync(sceneName);
            int generation = SceneNavigation.Generation;
            try
            {
                await navigation;
            }
            finally
            {
                if (generation == SceneNavigation.Generation) LoadingScreenController.Instance?.Hide();
            }
        }

        private static async Task LoadSceneCoreAsync(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation == null) throw new InvalidOperationException($"Could not load scene '{sceneName}'.");
            while (!operation.isDone) await Task.Yield();
        }

        public async Task UnloadSceneAsync(string sceneName)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(sceneName);
            if (operation == null && SceneManager.GetSceneByName(sceneName).isLoaded)
                throw new InvalidOperationException($"Could not unload scene '{sceneName}'.");
            if (operation != null)
                while (!operation.isDone) await Task.Yield();
        }

        public Task LoadSceneAndUnloadCurrentAsync(string sceneToLoad, string sceneToUnload) => NavigateToSceneAsync(sceneToLoad);

        private static async Task ObserveNavigationAsync(Task navigation)
        {
            try { await navigation; }
            catch (Exception error) { Debug.LogError($"Failed scene navigation: {error.Message}"); }
        }

        private static void SetActiveSceneIfLoaded(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
                SceneManager.SetActiveScene(scene);
        }
    }
}
