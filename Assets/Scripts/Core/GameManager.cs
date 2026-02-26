using System.Collections.Generic;
using UnityEngine;
using MahjongGame.Systems;
using MahjongGame.Core; // 确保引用 DeckConfig
using MahjongGame.Core.Network; // 确保引用 Network 命名空间
using MahjongGame.Core.Agents;

namespace MahjongGame.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        [Header("Settings")]
        public HandController playerHandController;

        [Header("Debug")]
        public bool useDebugHand = false;
        public List<TileData> debugHand = new List<TileData>();

        // 模拟玩家数据 (以后会从网络或存档读取)
        // 假设 ID 0 是自己，1,2,3 是 AI/对手
        private int _playerCount = 4;

        // 存储当前局各玩家的牌库配置
        public Dictionary<int, DeckConfig> ActiveConfigs { get; private set; } = new Dictionary<int, DeckConfig>();

        void Awake()
        {
            Instance = this;
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
        /// 由 UI 调用的入口
        /// </summary>
        /// <param name="hostConfig">房主(玩家0)的牌库配置</param>
        public void StartGameWithConfig(DeckConfig hostConfig)
        {
            Debug.Log("接收到玩家配置，正在初始化游戏...");

            List<DeckConfig> allConfigs = new List<DeckConfig>();

            TalentManager.Instance.TriggerGameStart();

            // 1. 添加玩家 0 (UI配置的)
            allConfigs.Add(hostConfig);

            // 2. 添加 3 个 AI (使用标准牌库，或者之后也可以让它们异化)
            allConfigs.Add(DeckConfig.CreateStandard());
            allConfigs.Add(DeckConfig.CreateStandard());
            allConfigs.Add(DeckConfig.CreateStandard());

            ActiveConfigs.Clear();
            for (int i = 0; i < allConfigs.Count; i++)
            {
                ActiveConfigs[i] = allConfigs[i];
            }

            // ====== [新增] 使用全新的 Fat Client, Thin Server 架构 ======
            var server = new GameServer();
            List<IPlayerClient> clients = new List<IPlayerClient>();

            // 玩家 0 是本地玩家
            clients.Add(new LocalPlayerClient(0, server, playerHandController));

            // 玩家 1-3 是胖客户端 AI
            clients.Add(new SimpleAIClient(1, server));
            clients.Add(new SimpleAIClient(2, server));
            clients.Add(new SimpleAIClient(3, server));

            // 启动服务器逻辑（它内部会调用 DeckManager 构建牌山并发牌）
            server.StartGame(clients, allConfigs);
            
            // 注释掉旧的流程
            // MahjongGame.Systems.DeckManager.Instance.BuildWall(allConfigs);
            // DealStartingHand();
            // MahjongGame.Systems.TurnManager.Instance.StartGameLoop();
        }

        // void Start()
        // {
        //     // 游戏入口
        //     StartNewGame();
        // }

        void StartNewGame()
        {
            Debug.Log("=== 游戏开始 ===");
            
            // 1. 收集所有玩家的牌组配置
            // 这里体现了你的“自定义牌河”逻辑
            //var allDecks = CollectPlayerDecks();

            // 1. 收集配置
            var configs = CreateTestDeckConfigs();

            ActiveConfigs.Clear();
            for (int i = 0; i < configs.Count; i++)
            {
                ActiveConfigs[i] = configs[i];
            }

            TalentManager.Instance.TriggerGameStart();

            // 2. 通知 DeckManager 构建牌山
            DeckManager.Instance.BuildWall(configs);

            // 3. 发起手牌 (每人13张)
            // DealStartingHands();
            // 2. 表现层测试：发 13 张牌
            DealStartingHand();
        }

        List<DeckConfig> CreateTestDeckConfigs()
        {
            List<DeckConfig> configs = new List<DeckConfig>();

            // --- 玩家 0: 标准牌库 ---
            DeckConfig p0 = DeckConfig.CreateStandard();
            Debug.Log($"P0 (标准) 异化值: {p0.AlienationScore} (预期: 0)");
            configs.Add(p0);

            // --- P1: 极端测试 (All East Wind) ---
            DeckConfig p1 = new DeckConfig();
            
            // 1. 设置 34 张东风
            p1.SetCardCount(Suit.Wind, 1, 34);
            
            // 2. 确保其他牌都是 0 (新创建的 DeckConfig 默认就是空，但为了严谨不需要额外操作)
            
            // 3. 校验并计算
            if (p1.IsValid())
            {
                p1.CalculateAlienationScore();
                Debug.Log($"<color=cyan>P1 (全东风) 异化值: {p1.AlienationScore} (预期: 126)</color>");
            }
            else
            {
                Debug.LogError("P1 配置牌数不对！");
            }
            
            configs.Add(p1);

            // 补齐 P2, P3 (标准)
            configs.Add(DeckConfig.CreateStandard());
            configs.Add(DeckConfig.CreateStandard());

            return configs;
        }

        /// <summary>
        /// 模拟生成玩家带入的牌
        /// </summary>
        List<List<TileData>> CollectPlayerDecks()
        {
            List<List<TileData>> collected = new List<List<TileData>>();

            for (int playerId = 0; playerId < _playerCount; playerId++)
            {
                List<TileData> playerDeck = new List<TileData>();

                // --- 你的特色逻辑 ---
                // 假设每个玩家必须带入 34 张牌 (标准麻将 136 / 4)
                // 或者按照你的设定：带入 10% 特殊牌，系统补全 90%
                
                // 这里暂时写死：每个人生成 34 张随机牌作为测试
                // for(int i=0; i<34; i++)
                // {
                //     // 简单生成：万子 1-9
                //     playerDeck.Add(new TileData(Suit.Man, (i % 9) + 1, playerId));
                // }

                for(int j=0; j<9; j++)
                {
                    // 简单生成：万子 1-9
                    playerDeck.Add(new TileData(Suit.Man, (j % 9) + 1, playerId));
                }
                for(int j=0; j<9; j++)
                {
                    // 简单生成：饼 1-9
                    playerDeck.Add(new TileData(Suit.Pin, (j % 9) + 1, playerId));
                }
                for(int j=0; j<9; j++)
                {
                    // 简单生成：索 1-9
                    playerDeck.Add(new TileData(Suit.Sou, (j % 9) + 1, playerId));
                }
                for(int j=0; j<4; j++)
                {
                    // 简单生成：风牌
                    playerDeck.Add(new TileData(Suit.Wind, (j % 4) + 1, playerId));
                }
                for(int j=0; j<3; j++)
                {
                    // 简单生成：箭牌
                    playerDeck.Add(new TileData(Suit.Dragon, (j % 3) + 1, playerId));
                }

                Debug.Log($"玩家 {playerId} 带入了 {playerDeck.Count} 张牌。");
                collected.Add(playerDeck);
            }

            return collected;
        }

        void DealStartingHand()
        {
            // 安全检查
            if (playerHandController == null) return;

            playerHandController.ClearHand();

            // --- 调试逻辑：自定义手牌 ---
            if (useDebugHand && debugHand != null && debugHand.Count > 0)
            {
                Debug.Log("<color=yellow>Debug: Using custom starting hand.</color>");
                // 最多发 13 张作为起手
                for (int i = 0; i < Mathf.Min(debugHand.Count, 13); i++)
                {
                    var tile = debugHand[i];
                    // 创建副本避免直接修改 Inspector 引用
                    TileData copy = new TileData(tile.TileSuit, tile.Value, tile.OriginalOwnerID);
                    playerHandController.AddTileDirectly(copy);
                }
            }
            else
            {
                // 正常逻辑：循环调用 13 次 DrawCard
                for (int i = 0; i < 13; i++)
                {
                    playerHandController.DrawCard();
                }
            }

            // 理牌
            playerHandController.SortHand();
        }
    }
}