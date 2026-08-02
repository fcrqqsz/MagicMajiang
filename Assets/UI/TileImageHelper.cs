using MahjongGame.Core;

namespace MahjongGame.UI
{
    public static class TileImageHelper
    {
        public static string GetTileBackImagePath()
        {
            return "Art/FlatTile/flat_back";
        }

        public static string GetTileImagePath(Suit suit, int value)
        {
            string prefix = "Art/FlatTile/f";
            string suffix = "";
            string valueStr = value.ToString();

            switch (suit)
            {
                case Suit.Man: suffix = "m"; break;
                case Suit.Pin: suffix = "p"; break;
                case Suit.Sou: suffix = "s"; break;
                case Suit.Wind: suffix = "z"; break;
                case Suit.Dragon:
                    suffix = "z";
                    if (value == 1) valueStr = "7"; // 中
                    else if (value == 2) valueStr = "6"; // 发
                    else if (value == 3) valueStr = "5"; // 白
                    break;
            }

            return $"{prefix}{valueStr}{suffix}";
        }

        public static string GetTileImagePath(TileData tile)
        {
            return GetTileImagePath(tile.TileSuit, tile.Value);
        }
    }
}
