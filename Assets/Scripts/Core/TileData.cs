using System;
using UnityEditor.Build;

namespace MahjongGame.Core
{
    /// <summary>
    /// 纯数据类，逻辑层使用。
    /// 类似于 C++ 中的 struct TileData
    /// </summary>
    [Serializable] // 允许在 Inspector 或 JSON 中序列化
    public class TileData
    {
        public string ID;           // 唯一ID (GUID)，用于追溯具体哪一张牌
        public Suit TileSuit;       // 花色
        public int Value;           // 数值 (1-9). 字牌的话，1=东, 2=南... 1=中, 2=发...
        
        // --- 你的特色玩法核心数据 ---
        
        // 原始归属者：谁把这张牌带入游戏的？(用于结算“谁的牌坑了谁”)
        public int OriginalOwnerID; 
        
        // 标签系统：用于Roguelike天赋。
        // 例如：天赋让这张牌变成“爆炸牌”，我们不改类结构，而是加Tag
        // 类似于 C++ 的 std::set<string> tags
        public bool IsModified; 
        public string SpecialEffectID; // 对应的天赋效果ID

        public TileType TileType => (TileSuit == Suit.Wind || TileSuit == Suit.Dragon) ? TileType.Word : TileType.Number;

        public TileData(Suit suit, int value, int ownerID)
        {
            this.ID = Guid.NewGuid().ToString();
            this.TileSuit = suit;
            this.Value = value;
            this.OriginalOwnerID = ownerID;
            this.IsModified = false;
        }

        public string GetName(bool print = false)
        {
            string tileName = GetSuitName();

            if (this.TileSuit != Suit.Wind && this.TileSuit != Suit.Dragon)
            {
                if (print)
                {
                    tileName = $"{this.Value}\n{tileName}";
                } else
                {
                    tileName = $"{this.Value}{tileName}";
                }
            }
            return tileName;
        }

        public string GetSuitName()
        {
            string suitName = "";
            switch (this.TileSuit)
            {
                case Suit.Man: suitName = "万"; break;
                case Suit.Pin: suitName = "筒"; break;
                case Suit.Sou: suitName = "条"; break;
                case Suit.Wind: suitName = GetWindName(this.Value); break;
                case Suit.Dragon: suitName = GetDragonName(this.Value); break;
            }
            return suitName;
        }

        // 辅助函数：获取字牌名称
        private string GetWindName(int val) => val switch { 1=>"东", 2=>"南", 3=>"西", 4=>"北", _=>"胡" };
        private string GetDragonName(int val) => val switch { 1=>"中", 2=>"发", 3=>"白", _=>"胡" };


        // 方便调试打印
        public override string ToString()
        {
            return $"{TileSuit}_{Value} (Owner:{OriginalOwnerID})";
        }
    }
}