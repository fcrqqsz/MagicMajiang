using UnityEngine;
using UnityEngine.SceneManagement;
using MahjongGame.Systems;
using MahjongGame.Core.Agents;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;
using MahjongGame.UI;

namespace MahjongGame.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        [Header("Settings")]
        public HandController playerHandController;

        [Header("Opponent Views")]
        public OpponentViewController rightOpponent;
        public OpponentViewController topOpponent;
        public OpponentViewController leftOpponent;

        [Header("Timeout")]
        [Tooltip("主回合超时时间(秒)，0表示不超时")]
        public float actionTimeout = 30f;
        [Tooltip("响应收集超时时间(秒)，0表示不超时")]
        public float responseTimeout = 10f;

        public GameSession Session { get; private set; }

        private ClientRoomService _roomService;
        private int _localSeatIndex = -1;
        private LocalPlayerClient _localPlayer;
        private RemoteServerProxy _currentClientProxy;
        private int _lastRecoveryPresentationVersion = -1;

        private void Awake()
        {
            Instance = this;
            // 强制在主线程初始化 MainThreadDispatcher，避免 WebSocket 子线程访问时触发 Unity 线程安全报错
            var dispatcher = MahjongGame.Core.Network.Transport.MainThreadDispatcher.Instance;
        }

        private void Start()
        {
            NetworkManager networkManager = NetworkManager.Instance;
            ClientRoomService roomService = networkManager?.RoomService;
            NetworkGameSceneEntryDecision decision = NetworkGameSceneEntryPolicy.Decide(
                networkManager != null,
                roomService != null,
                roomService?.HasRoom == true);

            if (decision != NetworkGameSceneEntryDecision.InitializeNetworkClient)
            {
                Debug.LogError("[GameManager] MissingNetworkRoomForGameScene");
                ReturnToPersistentFlow();
                return;
            }

            _roomService = roomService;
            _localSeatIndex = roomService.SeatIndex;
            Session = new GameSession(roomService.GameMode);
            InitializeNetworkClient();
        }

        private async void ReturnToPersistentFlow()
        {
            if (NetworkManager.Instance != null)
            {
                await NetworkManager.Instance.ReturnToPersistentFlowAsync();
                return;
            }

            SceneManager.LoadScene(SceneNames.Persistent, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (_currentClientProxy != null)
            {
                _currentClientProxy.Cleanup();
                _currentClientProxy = null;
            }
        }

        public OpponentViewController GetOpponentView(int playerId)
        {
            if (_localSeatIndex < 0) return null;
            int relativeSeat = (playerId - _localSeatIndex + 4) % 4;
            if (relativeSeat == 1) return rightOpponent;
            if (relativeSeat == 2) return topOpponent;
            if (relativeSeat == 3) return leftOpponent;
            return null;
        }

        public void StartNextRound()
        {
            if (_roomService?.HasRoom != true)
            {
                Debug.LogWarning("[GameManager] Cannot ready next round without a room.");
                return;
            }
            _roomService.SendReady(ReadyPhase.NextRound);
        }

        private void InitializeNetworkClient()
        {
            _localPlayer = new LocalPlayerClient(_localSeatIndex, null, playerHandController);
            _currentClientProxy?.Cleanup();
            _currentClientProxy = new RemoteServerProxy(_localPlayer, _roomService);
            _localPlayer.SetServer(_currentClientProxy);
            GameHUDController.Instance?.UpdateRoundInfo(Session);
            var recoveredSnapshot = _roomService.GameState.Snapshot;
            if (recoveredSnapshot != null)
                ApplyNetworkRecoverySnapshot(recoveredSnapshot, _roomService.RecoveryPresentationVersion);

            if (_roomService.RoomState == RoomState.LoadingGameScene)
                _roomService.SendReady(ReadyPhase.GameSceneLoaded);
        }

        /// <summary>Applies one E2 projection to the Unity presentation after all stale input has been cancelled.</summary>
        public void ApplyNetworkRecoverySnapshot(RoomGameSnapshot snapshot, int recoveryPresentationVersion = 0)
        {
            if (snapshot == null) return;
            if (recoveryPresentationVersion > 0 && recoveryPresentationVersion == _lastRecoveryPresentationVersion) return;
            if (recoveryPresentationVersion > 0) _lastRecoveryPresentationVersion = recoveryPresentationVersion;

            if (Session == null || Session.Mode != (GameMode)snapshot.gameMode)
                Session = new GameSession((GameMode)snapshot.gameMode);

            Session.Mode = (GameMode)snapshot.gameMode;
            Session.PrevalentWind = (WindDirection)snapshot.prevalentWind;
            Session.DealerIndex = Mathf.Clamp(snapshot.dealerIndex, 0, 3);
            Session.RestoreRoundProgress(snapshot.roundNumber, snapshot.result?.isSessionOver == true);
            SessionScorePolicy.ApplyAuthoritativeScores(Session, snapshot.scores);
            if (snapshot.result != null)
            {
                Session.LastWinnerId = snapshot.result.winnerId;
                Session.LastFanCount = snapshot.result.fanCount;
                Session.LastIsSelfDraw = snapshot.result.isSelfDraw;
                Session.LastLoserId = snapshot.result.loserId;
            }

            _localPlayer?.RestoreFromSnapshot(snapshot);
            _currentClientProxy?.ApplyCurrentTalentRecoveryProjection();
            GameHUDController.Instance?.ApplyRecoverySnapshot(snapshot, Session);
            SideboardPanelController.Instance?.ApplyRecoverySnapshot(snapshot.sideboard);
            ResultPanelController.Instance?.ApplyRecoveryResult(snapshot, Session);
        }
    }
}
