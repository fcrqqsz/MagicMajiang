using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core
{
    public struct AllowedActions
    {
        public bool CanHu;
        public bool CanPon;
        public bool CanGan; // 明杠
        public bool CanChiLeft;   // 吃左边 (例如打3，我有12)
        public bool CanChiMiddle; // 吃中间 (例如打3，我有24)
        public bool CanChiRight;  // 吃右边 (例如打3，我有45)
        
        public bool HasAction => CanHu || CanPon || CanGan || CanChiLeft || CanChiMiddle || CanChiRight;
    }

    public static class ActionValidator
    {
        /// <summary>
        /// 当别人打出一张牌时，检查我能做什么
        /// </summary>
        public static AllowedActions CheckActions(List<TileData> myHand, List<Meld> myMelds, TileData discardedTile, bool isNextPlayer)
        {
            AllowedActions actions = new AllowedActions();
            int[] handCounts = MahjongLogic.ConvertToFrequencyArray(myHand);
            int targetIdx = MahjongLogic.GetTileIndex(discardedTile);

            // 1. 检查胡 (点炮)
            // 将这张牌临时加入判定
            if (MahjongLogic.IsWin(myHand, myMelds, discardedTile))
            {
                actions.CanHu = true;
            }

            // 2. 检查碰 (手里有 >= 2张)
            if (handCounts[targetIdx] >= 2)
            {
                actions.CanPon = true;
            }

            // 3. 检查明杠 (手里有 3张)
            if (handCounts[targetIdx] >= 3)
            {
                actions.CanGan = true;
            }

            // 4. 检查吃 (只能吃上家的牌)
            if (isNextPlayer && discardedTile.TileSuit != Suit.Wind && discardedTile.TileSuit != Suit.Dragon)
            {
                // 左吃: target是3 (idx 2)，我有 1,2 (idx 0,1)
                if (targetIdx >= 2 && handCounts[targetIdx - 1] > 0 && handCounts[targetIdx - 2] > 0)
                    actions.CanChiLeft = true;

                // 中吃: target是2 (idx 1)，我有 1,3 (idx 0,2)
                if (targetIdx >= 1 && targetIdx <= 32 && handCounts[targetIdx - 1] > 0 && handCounts[targetIdx + 1] > 0)
                    actions.CanChiMiddle = true;

                // 右吃: target是1 (idx 0)，我有 2,3 (idx 1,2)
                if (targetIdx <= 31 && handCounts[targetIdx + 1] > 0 && handCounts[targetIdx + 2] > 0)
                    actions.CanChiRight = true;
            }

            return actions;
        }

        /// <summary>
        /// [新增] 检查自己摸牌后的操作权限 (自摸胡、暗杠、加杠)
        /// </summary>
        /// <param name="myHand">手牌数据</param>
        /// <param name="myMelds">已有的副露</param>
        /// <param name="drawnTile">刚摸到的那张牌</param>
        public static AllowedActions CheckSelfActions(List<TileData> myHand, List<Meld> myMelds, TileData drawnTile)
        {
            AllowedActions actions = new AllowedActions();

            // 1. 检查自摸胡 (Tsumo)
            // 注意：isSelfDraw = true
            // 我们这里只做成胡判定，是否满足番数由 MahjongLogic 内部控制 (8番起胡)
            int fan;
            List<string> details;
            
            // 为了判定，我们需要把 handTile 和 drawnTile 结合，或者 MahjongLogic 已经能处理 hand 包含 drawn 的情况
            // 假设 myHand 已经包含了 drawnTile (TurnManager 里是先 Draw 后 Check)
            // 那么 CheckWinWithFan 的 winningTile 参数只是用来指明哪张是胡的牌
            if (MahjongLogic.CheckWinWithFan(myHand, myMelds, drawnTile, true, out fan, out details))
            {
                actions.CanHu = true;
            }

            // 2. 检查暗杠 (An Gang)
            // 手里有 4 张一样的牌
            // 简单的判定：手牌里同花色同数值的计数 >= 4
            // (更严谨的做法是调用 HandController.GetAnGanOptions，这里做快速预检)
            var anGanGroups = myHand.GroupBy(t => new { t.TileSuit, t.Value }).Where(g => g.Count() == 4);
            if (anGanGroups.Any())
            {
                actions.CanGan = true;
            }

            // 3. 检查加杠 (Jia Gang / Added Kan)
            // 条件：已有 Pon 的副露，且手牌里（包括刚摸的）有一张相同的牌
            foreach (var meld in myMelds)
            {
                if (meld.Type == MeldType.Pon)
                {
                    // 检查手牌里是否有和这个碰(Pon)一样的牌
                    // Pon 的代表牌通常是 FirstTile
                    bool hasMatchingTile = myHand.Any(t => t.TileSuit == meld.FirstTile.TileSuit && t.Value == meld.FirstTile.Value);
                    
                    if (hasMatchingTile)
                    {
                        actions.CanGan = true;
                        // 注意：AllowedActions 只有一个 CanGan，UI 上显示的都是 [杠]
                        // 具体是暗杠还是加杠，点击后由 HandController 区分
                        break; 
                    }
                }
            }

            return actions;
        }

        /// <summary>
        /// 获取所有能吃某张牌的组合
        /// </summary>
        /// <param name="myHand">我的手牌</param>
        /// <param name="target">目标牌</param>
        /// <returns>返回 List<int[]>，每个数组包含两个整数，代表用来吃的那两张牌的 Value</returns>
        public static List<int[]> GetChiCombinations(List<TileData> myHand, TileData target)
        {
            List<int[]> combos = new List<int[]>();
            
            // 字牌不能吃
            if (target.TileSuit == Suit.Wind || target.TileSuit == Suit.Dragon) return combos;

            int val = target.Value;
            
            // 获取手里该花色所有【不重复】的数值
            var distinctValues = myHand
                .Where(t => t.TileSuit == target.TileSuit)
                .Select(t => t.Value)
                .Distinct()
                .ToHashSet();

            // 检查三种吃法
            if (val > 2 && distinctValues.Contains(val - 2) && distinctValues.Contains(val - 1))
                combos.Add(new int[] { val - 2, val - 1 });
            if (val > 1 && val < 9 && distinctValues.Contains(val - 1) && distinctValues.Contains(val + 1))
                combos.Add(new int[] { val - 1, val + 1 });
            if (val < 8 && distinctValues.Contains(val + 1) && distinctValues.Contains(val + 2))
                combos.Add(new int[] { val + 1, val + 2 });

            return combos;
        }
    }
}