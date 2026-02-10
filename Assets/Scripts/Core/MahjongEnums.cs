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
}