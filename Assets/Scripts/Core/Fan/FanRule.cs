namespace MahjongGame.Core.Fan
{
    public abstract class FanRule
    {
        // 基础信息 (由 Attribute 注入或重写)
        public string Id { get; set; }
        public virtual string Name { get; }
        public virtual string Description { get; }
        
        // 动态分值系统
        private int _baseFanValue;
        private int _bonusFanValue; // 天赋带来的加成

        public int FanValue => _baseFanValue + _bonusFanValue;

        // 初始化
        public void Initialize(string id, int defaultFan)
        {
            Id = id;
            _baseFanValue = defaultFan;
            _bonusFanValue = 0;
        }

        // 天赋修改接口
        public void ApplyModifier(int delta)
        {
            _bonusFanValue += delta;
        }

        /// <summary>
        /// 优先级：数值越大越先判定 (用于排斥逻辑)
        /// </summary>
        public virtual int Priority => 0;

        /// <summary>
        /// 如果此番种达成，将排斥（不再计算）以下 ID 的番种
        /// </summary>
        public virtual string[] ExcludedRuleIds => new string[0];

        /// <summary>
        /// 计算该番种触发了多少次
        /// </summary>
        /// <returns>0 表示未触发，>0 表示触发次数</returns>
        public abstract int GetMatchCount(FanContext ctx);
    }
}