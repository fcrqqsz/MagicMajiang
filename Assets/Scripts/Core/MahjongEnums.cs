namespace MahjongGame.Core
{
    // 麻将花色
    public enum Suit
    {
        Man,    // 万
        Pin,    // 筒
        Sou,    // 条
        Wind,   // 风 (东南西北)
        Dragon  // 箭 (中发白)
    }

    // 这一步是为了方便后续的天赋判断
    public enum TileType
    {
        Number, // 数牌
        Word    // 字牌
    }

    // 风位方向 (值与牌面 Value 对应: 东=1, 南=2, 西=3, 北=4)
    public enum WindDirection
    {
        East = 1,
        South = 2,
        West = 3,
        North = 4
    }

    // 对战模式
    public enum GameMode
    {
        Single,     // 单局
        EastOnly,   // 东风局 (4局)
        HalfGame,   // 半庄 (8局, 东+南)
        FullGame    // 全庄 (16局, 东+南+西+北)
    }

    // 天赋品阶
    public enum TalentTier { Small, Medium, Large }

    // 天赋作用阶段
    public enum TalentPhase
    {
        WallBuilding,     // 牌山构建后、洗牌前
        InitialHandCompleted, // 四席起手牌进入服务端权威状态后
        OnDraw,           // 摸牌时
        OnDiscard,        // 出牌时
        ActionValidation, // 吃碰杠胡判定
        Scoring           // 算番阶段
    }

    // 天赋影响范围
    public enum TalentScope { Self, Global }
}
