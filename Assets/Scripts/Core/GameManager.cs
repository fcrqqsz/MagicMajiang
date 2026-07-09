using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MahjongGame.Systems;
using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Data;
using MahjongGame.Core.Network.Messages;
using MahjongGame.Core.Agents;
using MahjongGame.Talents;
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

        [Header("Network Mode")]
        public bool isNetworkMode = false;
        public bool isServer = false;
        public string serverAddress = "ws://127.0.0.1:9876/game";

        // 多局对战状态
        public GameSession Session { get; private set; }
        private GameServer _currentServer;
        private List<IPlayerClient> _clients;
        private MahjongGame.Core.Network.RemoteServerProxy _currentClientProxy;
        private DeckConfig _hostConfig;
        private bool _isWaitingForReady = false;

        // 事件委托缓存，用于正确取消订阅
        private System.Action<int, float> _onTurnStartedHandler;
        private System.Action _onTurnEndedHandler;

        void Awake()
        {
            Instance = this;
            // 强制在主线程初始化 MainThreadDispatcher，避免 WebSocket 子线程访问时触发 Unity 线程安全报错
            var dispatcher = MahjongGame.Core.Network.Transport.MainThreadDispatcher.Instance;
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

            if (_currentClientProxy != null)
            {
                _currentClientProxy.Cleanup();
                _currentClientProxy = null;
            }

            var wsc = MahjongGame.Core.Network.Transport.WebSocketClient.Instance;
            if (wsc != null)
            {
                wsc.OnConnected -= HandleClientConnectedToServer;
            }

            MahjongGame.Core.Network.Transport.GameEndpoint.OnMessageReceived -= HandleServerMessage;
            MahjongGame.Core.Network.Transport.GameEndpoint.OnClientConnected -= HandleClientConnected;
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

            // 初始资金天赋：对战开始时加分
            var talentConfigs = BuildTalentConfigs();
            if (talentConfigs.TryGetValue(0, out var p0Config))
            {
                if (p0Config.GetAllEquippedIds().Contains("starting_capital"))
                {
                    Session.Scores[0] += Talents.Impl.StartingCapitalTalent.BonusScore;
                    Debug.Log($"<color=yellow>[天赋触发] 初始资金: 玩家0 初始分数+{Talents.Impl.StartingCapitalTalent.BonusScore}</color>");
                }
            }

            StartNextRound();
        }

        private Dictionary<int, TalentSlotConfig> BuildTalentConfigs()
        {
            var talentConfigs = new Dictionary<int, TalentSlotConfig>();
            if (ProfileManager.Instance?.CurrentProfile != null)
            {
                var profile = ProfileManager.Instance.CurrentProfile;
                int idx = profile.SelectedDeckIndex;
                if (profile.SavedDecks.Count > 0)
                {
                    if (idx < 0 || idx >= profile.SavedDecks.Count) idx = 0;
                    talentConfigs[0] = profile.SavedDecks[idx].Talents ?? new TalentSlotConfig();
                }
                else
                {
                    talentConfigs[0] = new TalentSlotConfig();
                }
            }
            else
            {
                talentConfigs[0] = new TalentSlotConfig();
            }
            for (int i = 1; i < 4; i++)
                talentConfigs[i] = new TalentSlotConfig();
            return talentConfigs;
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

            if (isNetworkMode)
            {
                if (isServer)
                {
                    // 确保 WebSocketService 已经被动态创建并挂载到场景中
                    var wss = MahjongGame.Core.Network.Transport.WebSocketService.Instance;
                    if (wss == null)
                    {
                        var go = new GameObject("WebSocketService");
                        wss = go.AddComponent<MahjongGame.Core.Network.Transport.WebSocketService>();
                    }
                    wss.StartServer();

                    // 创建服务端
                    _currentServer = new GameServer(DeckManager.Instance);
                    _currentServer.ActionTimeoutMs = (int)(actionTimeout * 1000);
                    _currentServer.ResponseTimeoutMs = (int)(responseTimeout * 1000);

                    // 监听局结束事件
                    _currentServer.OnRoundFinished += OnRoundFinished;

                    // 注册消息派发
                    MahjongGame.Core.Network.Transport.GameEndpoint.OnMessageReceived -= HandleServerMessage;
                    MahjongGame.Core.Network.Transport.GameEndpoint.OnMessageReceived += HandleServerMessage;

                    _isWaitingForReady = true;

                    // 如果长连接上已经有客户端连接，复用该连接并等待 Ready 准备消息
                    if (_clients != null && _clients.Count > 0 && _clients.Any(c => c is RemotePlayerClient))
                    {
                        Debug.Log("[GameServer] 检测到已在线的远程客户端连接，清除旧 AI，等待客户端 Ready 以启动对局...");
                        
                        // 剔除旧局绑定的 AI，等收到 Ready 消息后再重新装配
                        _clients.RemoveAll(c => c is SimpleAIClient);
                        foreach (var remoteClient in _clients.OfType<RemotePlayerClient>())
                        {
                            remoteClient.SetSession(Session);
                        }
                    }
                    else
                    {
                        _clients = new List<IPlayerClient>();
                        
                        MahjongGame.Core.Network.Transport.GameEndpoint.OnClientConnected -= HandleClientConnected;
                        MahjongGame.Core.Network.Transport.GameEndpoint.OnClientConnected += HandleClientConnected;

                        Debug.Log("[GameServer] 等待远程客户端连接以开始第一局游戏...");
                    }
                }
                else
                {
                    // 确保 WebSocketClient 已经动态创建并挂载到场景中
                    var wsc = MahjongGame.Core.Network.Transport.WebSocketClient.Instance;
                    if (wsc == null)
                    {
                        var go = new GameObject("WebSocketClient");
                        wsc = go.AddComponent<MahjongGame.Core.Network.Transport.WebSocketClient>();
                    }

                    // HUD: 更新局信息
                    if (GameHUDController.Instance != null)
                    {
                        GameHUDController.Instance.UpdateRoundInfo(Session);
                    }

                    // 【长连接复用逻辑】如果长连接保持 Open，直接复用并发送 Ready 准备消息
                    if (wsc.ReadyState == WebSocketSharp.WebSocketState.Open)
                    {
                        Debug.Log("[GameClient] 复用已是 Open 状态的 WebSocket 长连接，准备新小局...");

                        var localPlayer = new LocalPlayerClient(0, null, playerHandController);
                        _clients = new List<IPlayerClient> { localPlayer };

                        if (_currentClientProxy != null)
                        {
                            _currentClientProxy.SetLocalClient(localPlayer);
                        }
                        else
                        {
                            var proxy = new MahjongGame.Core.Network.RemoteServerProxy(localPlayer);
                            _currentClientProxy = proxy;
                        }
                        localPlayer.SetServer(_currentClientProxy);

                        // 发送 Ready 开启对局
                        string readyJson = MessageSerializer.Serialize("Ready", 0, new DrawGameMessage());
                        wsc.SendNetworkMessage(readyJson);
                        Debug.Log("[GameClient] 长连接复用，发送 Ready 准备消息。");
                    }
                    else
                    {
                        // 首次建立连接或连接已断开失效，清理并重新连接
                        if (_currentClientProxy != null)
                        {
                            _currentClientProxy.Cleanup();
                            _currentClientProxy = null;
                        }

                        _clients = new List<IPlayerClient>();
                        var localPlayer = new LocalPlayerClient(0, null, playerHandController);
                        _clients.Add(localPlayer);

                        var proxy = new MahjongGame.Core.Network.RemoteServerProxy(localPlayer);
                        _currentClientProxy = proxy;
                        localPlayer.SetServer(proxy);

                        // 订阅连接成功事件：一旦长连接握手成功，自动发送 Ready 开始第一局
                        wsc.OnConnected -= HandleClientConnectedToServer;
                        wsc.OnConnected += HandleClientConnectedToServer;

                        wsc.Connect(serverAddress);
                        Debug.Log($"[GameClient] 正在连接到服务端 {serverAddress}...");
                    }
                }
            }
            else
            {
                // 单机模式
                _currentServer = new GameServer(DeckManager.Instance);
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

                // 构建天赋配置
                var talentConfigs = BuildTalentConfigs();

                // 启动
                _currentServer.StartGame(_clients, allConfigs, Session, talentConfigs);
            }
        }

        private void HandleClientConnected(string connId, MahjongGame.Core.Network.Transport.GameEndpoint endpoint)
        {
            if (_clients == null || _clients.Count == 0)
            {
                Debug.Log($"[GameServer] 客户端 {connId} 连接成功，等待客户端 Ready 以启动对局...");

                _clients = new List<IPlayerClient>();
                var remotePlayer = new RemotePlayerClient(0, endpoint, Session);
                _clients.Add(remotePlayer);
            }
        }

        private void HandleServerMessage(string connId, string json, MahjongGame.Core.Network.Transport.GameEndpoint endpoint)
        {
            var envelope = MessageSerializer.DeserializeEnvelope(json);
            if (envelope != null)
            {
                if (envelope.type == "Ready")
                {
                    if (!_isWaitingForReady)
                    {
                        Debug.LogWarning($"[GameServer] 收到客户端 {connId} 的 Ready 准备就绪包，但当前服务器未处于等待准备状态，忽略。");
                        return;
                    }
                    _isWaitingForReady = false;

                    Debug.Log($"[GameServer] 收到客户端 {connId} 的 Ready 准备就绪包，开启发牌游戏流程！");
                    
                    // 重新为当前小局装配 AI
                    _clients.RemoveAll(c => c is SimpleAIClient);
                    _clients.Add(new SimpleAIClient(1, _currentServer));
                    _clients.Add(new SimpleAIClient(2, _currentServer));
                    _clients.Add(new SimpleAIClient(3, _currentServer));

                    List<DeckConfig> allConfigs = new List<DeckConfig>();
                    allConfigs.Add(_hostConfig);
                    allConfigs.Add(DeckConfig.CreateStandard());
                    allConfigs.Add(DeckConfig.CreateStandard());
                    allConfigs.Add(DeckConfig.CreateStandard());

                    var talentConfigs = BuildTalentConfigs();

                    // 启动游戏循环，发牌并开启对局
                    _currentServer.StartGame(_clients, allConfigs, Session, talentConfigs);
                }
                else if (envelope.type == "Action")
                {
                    var msg = MessageSerializer.DeserializePayload<ClientActionMessage>(envelope.data);
                    if (_clients != null && _currentServer != null)
                    {
                        var remoteClient = _clients.OfType<RemotePlayerClient>().FirstOrDefault(c => c.Endpoint == endpoint);
                        if (remoteClient != null)
                        {
                            var action = new ClientAction(remoteClient.PlayerId, (ClientActionType)msg.actionType, msg.targetTile?.ToTileData(), msg.chiCombinations);
                            action.SetHuDetails(msg.totalFan, msg.fanDetails != null ? new List<string>(msg.fanDetails) : null);
                            _currentServer.SubmitAction(action);
                        }
                    }
                }
            }
        }

        private void HandleClientConnectedToServer()
        {
            if (MahjongGame.Core.Network.Transport.WebSocketClient.Instance != null)
            {
                MahjongGame.Core.Network.Transport.WebSocketClient.Instance.OnConnected -= HandleClientConnectedToServer;
                string readyJson = MessageSerializer.Serialize("Ready", 0, new DrawGameMessage());
                MahjongGame.Core.Network.Transport.WebSocketClient.Instance.SendNetworkMessage(readyJson);
                Debug.Log("[GameClient] 长连接握手成功，发送 Ready 准备消息。");
            }
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

            // 如果是网络服务端的模式，在当前小局结束后，服务器自动准备下一局并等待客户端 Ready
            if (isNetworkMode && isServer)
            {
                if (Session != null && !Session.IsSessionOver())
                {
                    Debug.Log("[GameServer] 当前小局结束，服务器自动进入下一局准备状态，等待客户端 Ready...");
                    StartNextRound();
                }
                else
                {
                    Debug.Log("[GameServer] 对战 Session 已由服务器判定结束，不再开启下一局。");
                }
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
