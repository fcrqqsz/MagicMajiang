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

        public abstract bool Check(FanContext ctx);
    }
}