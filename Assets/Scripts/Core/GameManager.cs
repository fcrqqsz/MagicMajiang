using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Systems;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Agents;
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

        [Header("Game Mode")]
        public GameMode gameMode = GameMode.Single;

        [Header("Debug")]
        public bool useDebugHand = false;
        public List<TileData> debugHand = new List<TileData>();

        [Header("AI Cheat")]
        public bool forceAIDiscard = false;
        public List<TileData> aiCheatDiscards = new List<TileData>();

        [Header("Timeout")]
        [Tooltip("主回合超时时间(秒)，0表示不超时")]
        public float actionTimeout = 30f;
        [Tooltip("响应收集超时时间(秒)，0表示不超时")]
        public float responseTimeout = 10f;

        // 存储当前局各玩家的牌库配置
        public Dictionary<int, DeckConfig> ActiveConfigs { get; private set; } = new Dictionary<int, DeckConfig>();

        // 多局对战状态
        public GameSession Session { get; private set; }
        private GameServer _currentServer;
        private List<IPlayerClient> _clients;
        private DeckConfig _hostConfig;

        // 事件委托缓存，用于正确取消订阅
        private System.Action<int, float> _onTurnStartedHandler;
        private System.Action _onTurnEndedHandler;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            DeckConfig targetConfig = DeckConfig.CreateStandard();

            // Try to load from ProfileManager using selected deck index
            if (ProfileManager.Instance != null && ProfileManager.Instance.CurrentProfile != null)
            {
                var profile = ProfileManager.Instance.CurrentProfile;
                int idx = profile.SelectedDeckIndex;
                if (profile.SavedDecks.Count > 0)
                {
                    if (idx < 0 || idx >= profile.SavedDecks.Count)
                        idx = 0;
                    targetConfig = profile.SavedDecks[idx].Config;
                }
            }

            StartGameWithConfig(targetConfig);
        }

        void OnDestroy()
        {
            if (_currentServer != null)
            {
                _currentServer.StopGame();
            }
        }

        public OpponentViewController GetOpponentView(int playerId)
        {
            if (playerId == 1) return rightOpponent;
            if (playerId == 2) return topOpponent;
            if (playerId == 3) return leftOpponent;
            return null;
        }

        /// <summary>
        /// 获取指定玩家的异化分
        /// </summary>
        public int GetAlienationScore(int playerId)
        {
            if (ActiveConfigs != null && ActiveConfigs.TryGetValue(playerId, out var config))
            {
                return config.AlienationScore;
            }
            return 0;
        }

        /// <summary>
        /// 由 UI 调用的入口 (兼容旧接口，默认单局模式)
        /// </summary>
        public void StartGameWithConfig(DeckConfig hostConfig)
        {
            GameMode selectedMode = gameMode; // SerializeField fallback
            if (ProfileManager.Instance?.CurrentProfile != null)
            {
                int modeIdx = ProfileManager.Instance.CurrentProfile.Settings.SelectedGameMode;
                if (modeIdx >= 0 && modeIdx <= 3)
                    selectedMode = (GameMode)modeIdx;
            }
            StartSession(selectedMode, hostConfig);
        }

        /// <summary>
        /// 启动多局对战
        /// </summary>
        public void StartSession(GameMode mode, DeckConfig hostConfig)
        {
            Debug.Log($"开始对战: 模式={mode}");
            _hostConfig = hostConfig;
            Session = new GameSession(mode);

            TalentManager.Instance.TriggerGameStart();

            StartNextRound();
        }

        /// <summary>
        /// 启动下一局
        /// </summary>
        public void StartNextRound()
        {
            if (Session == null || Session.IsSessionOver())
            {
                Debug.LogWarning("[GameManager] 对战已结束，无法开始下一局");
                return;
            }

            string scores = $"P0:{Session.Scores[0]} | P1:{Session.Scores[1]} | P2:{Session.Scores[2]} | P3:{Session.Scores[3]}";
            Debug.Log($"<color=yellow>[GameManager] {Session.GetCurrentRoundInfo()} 开始 | 东家:P{Session.DealerIndex}</color>");
            Debug.Log($"<color=cyan>[GameManager] 当前分数 [{scores}]</color>");

            // 构建牌库配置
            List<DeckConfig> allConfigs = new List<DeckConfig>();
            allConfigs.Add(_hostConfig);
            allConfigs.Add(DeckConfig.CreateStandard());
            allConfigs.Add(DeckConfig.CreateStandard());
            allConfigs.Add(DeckConfig.CreateStandard());

            ActiveConfigs.Clear();
            for (int i = 0; i < allConfigs.Count; i++)
            {
                ActiveConfigs[i] = allConfigs[i];
            }

            // 创建服务器和客户端
            _currentServer = new GameServer();
            _currentServer.ActionTimeoutMs = (int)(actionTimeout * 1000);
            _currentServer.ResponseTimeoutMs = (int)(responseTimeout * 1000);
            _clients = new List<IPlayerClient>();

            _clients.Add(new LocalPlayerClient(0, _currentServer, playerHandController));
            _clients.Add(new SimpleAIClient(1, _currentServer));
            _clients.Add(new SimpleAIClient(2, _currentServer));
            _clients.Add(new SimpleAIClient(3, _currentServer));

            // 监听局结束事件
            _currentServer.OnRoundFinished += OnRoundFinished;

            // HUD: 更新局信息 + 监听回合事件
            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.UpdateRoundInfo(Session);

                _onTurnStartedHandler = (playerIdx, timeout) =>
                {
                    GameHUDController.Instance?.StartTimer(timeout, playerIdx);
                };
                _onTurnEndedHandler = () =>
                {
                    GameHUDController.Instance?.StopTimer();
                };
                _currentServer.OnTurnStarted += _onTurnStartedHandler;
                _currentServer.OnTurnEnded += _onTurnEndedHandler;
            }

            // 启动
            _currentServer.StartGame(_clients, allConfigs, Session);
        }

        private void OnRoundFinished()
        {
            if (_currentServer != null)
            {
                _currentServer.OnRoundFinished -= OnRoundFinished;
                _currentServer.OnTurnStarted -= _onTurnStartedHandler;
                _currentServer.OnTurnEnded -= _onTurnEndedHandler;
            }

            // 推进到下一局
            Session.AdvanceRound();

            // HUD: 停止计时 + 更新分数
            if (GameHUDController.Instance != null)
            {
                GameHUDController.Instance.StopTimer();
                GameHUDController.Instance.UpdateScores(Session.Scores);
            }

            // 通知 ResultPanelController 当前对战状态
            if (ResultPanelController.Instance != null)
            {
                ResultPanelController.Instance.SetSessionInfo(Session);
            }
        }

        /// <summary>
        /// 对战结束，广播最终分数
        /// </summary>
        public void EndSession()
        {
            if (Session == null) return;

            Debug.Log($"[GameManager] 对战结束! 最终分数: {string.Join(",", Session.Scores)}");

            if (_clients != null)
            {
                foreach (var client in _clients)
                {
                    client.OnSessionEnd(Session.Scores);
                }
            }
        }
    }
}
