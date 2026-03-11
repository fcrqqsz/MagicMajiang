using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace MahjongGame.Core
{
    /// <summary>
    /// 玩家的牌库配置
    /// 负责定义“我要带几张什么牌”，并计算异化值
    /// </summary>
    [System.Serializable]
    public class DeckConfig : ISerializationCallbackReceiver
    {
        // 存储每种牌的数量： Key="Suit_Value", Value=Count
        // 例如: "Man_1" -> 3 (带了3张一万)
        private Dictionary<string, int> _cardCounts = new Dictionary<string, int>();

        [SerializeField, HideInInspector]
        private List<string> _keys = new List<string>();
        [SerializeField, HideInInspector]
        private List<int> _values = new List<int>();

        // 缓存计算出的异化值
        public int AlienationScore { get; private set; } = 0;

        // 设定标准总数常量
        private const int TARGET_TOTAL_COUNT = 34;

        public DeckConfig()
        {
            // 初始化默认为空
            Clear();
        }

        public void OnBeforeSerialize()
        {
            _keys.Clear();
            _values.Clear();
            foreach (var kvp in _cardCounts)
            {
                _keys.Add(kvp.Key);
                _values.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            _cardCounts.Clear();
            for (int i = 0; i < Mathf.Min(_keys.Count, _values.Count); i++)
            {
                _cardCounts.Add(_keys[i], _values[i]);
            }
        }

        public void Clear()
        {
            _cardCounts.Clear();
            AlienationScore = 0;
        }

        /// <summary>
        /// 设置某张牌的数量
        /// </summary>
        public void SetCardCount(Suit suit, int value, int count)
        {
            string key = GetKey(suit, value);
            if (count < 0) count = 0;
            _cardCounts[key] = count;
        }

        /// <summary>
        /// 获取某张牌的数量
        /// </summary>
        public int GetCardCount(Suit suit, int value)
        {
            string key = GetKey(suit, value);
            return _cardCounts.ContainsKey(key) ? _cardCounts[key] : 0;
        }

        private string GetKey(Suit suit, int value) => $"{suit}_{value}";

        /// <summary>
        /// 校验：牌库是否合法 (必须严格等于34张)
        /// </summary>
        public bool IsValid()
        {
            int total = _cardCounts.Values.Sum();
            if (total != TARGET_TOTAL_COUNT)
            {
                Debug.LogWarning($"牌库非法！当前总数: {total}, 目标: {TARGET_TOTAL_COUNT}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 修正后的异化值算法
        /// </summary>
        public void CalculateAlienationScore()
        {
            int score = 0;

            // 1. 定义各花色的标准总数
            Dictionary<Suit, int> suitStdTotals = new Dictionary<Suit, int>()
            {
                { Suit.Man, 9 },    // 1-9
                { Suit.Pin, 9 },    // 1-9
                { Suit.Sou, 9 },    // 1-9
                { Suit.Wind, 4 },   // 东南西北
                { Suit.Dragon, 3 }  // 中发白
            };

            // 用于统计当前牌库的各花色实际总数
            Dictionary<Suit, int> suitActualTotals = new Dictionary<Suit, int>()
            {
                { Suit.Man, 0 }, { Suit.Pin, 0 }, { Suit.Sou, 0 },
                { Suit.Wind, 0 }, { Suit.Dragon, 0 }
            };

            // --- 循环 A: 遍历所有可能的 34 种标准牌型 ---
            foreach (Suit suit in System.Enum.GetValues(typeof(Suit)))
            {
                int maxVal = (suit == Suit.Wind) ? 4 : (suit == Suit.Dragon ? 3 : 9);

                for (int val = 1; val <= maxVal; val++)
                {
                    int actual = GetCardCount(suit, val);
                    int standard = 1; // 标准每种1张

                    // 规则 1: 单种牌数量偏差 (每种牌与1的差值绝对值)
                    // 例如：34张东风 -> |34 - 1| = 33 分
                    // 例如：0张南风 -> |0 - 1| = 1 分
                    score += Mathf.Abs(actual - standard);

                    // 累计该花色的实际总数
                    suitActualTotals[suit] += actual;
                }
            }

            // --- 循环 B: 计算各花色总数量偏差 ---
            foreach (Suit suit in System.Enum.GetValues(typeof(Suit)))
            {
                int actualTotal = suitActualTotals[suit];
                int stdTotal = suitStdTotals[suit];

                // 规则 2: 花色总数偏差
                // 例如：风牌实际34张，标准4张 -> |34 - 4| = 30 分
                int diff = Mathf.Abs(actualTotal - stdTotal);
                score += diff;
            }

            this.AlienationScore = score;
        }

        /// <summary>
        /// 辅助：生成标准牌库 (异化值为0)
        /// </summary>
        public static DeckConfig CreateStandard()
        {
            DeckConfig config = new DeckConfig();
            
            // 数牌 1-9
            for (int i = 1; i <= 9; i++)
            {
                config.SetCardCount(Suit.Man, i, 1);
                config.SetCardCount(Suit.Pin, i, 1);
                config.SetCardCount(Suit.Sou, i, 1);
            }
            // 风牌 1-4 (东南西北)
            for (int i = 1; i <= 4; i++) config.SetCardCount(Suit.Wind, i, 1);
            // 箭牌 1-3 (中发白)
            for (int i = 1; i <= 3; i++) config.SetCardCount(Suit.Dragon, i, 1);

            config.CalculateAlienationScore();
            return config;
        }
        
        /// <summary>
        /// 将配置转换为实际的 TileData 列表
        /// </summary>
        public List<TileData> GenerateTiles(int ownerId)
        {
            List<TileData> list = new List<TileData>();
            foreach (var kvp in _cardCounts)
            {
                // 解析 Key: "Suit_Value"
                string[] parts = kvp.Key.Split('_');
                Suit s = (Suit)System.Enum.Parse(typeof(Suit), parts[0]);
                int v = int.Parse(parts[1]);
                int count = kvp.Value;

                for (int i = 0; i < count; i++)
                {
                    list.Add(new TileData(s, v, ownerId));
                }
            }
            return list;
        }
    }
}