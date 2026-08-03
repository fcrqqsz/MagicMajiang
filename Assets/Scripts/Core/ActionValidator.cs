using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core
{
    public struct AllowedActions
    {
        public bool CanHu;
        public bool CanPon;
        public bool CanMingGan;
        public bool CanAnGan;
        public bool CanJiaGang;
        public bool CanChiLeft;   // 吃左边 (例如打3，我有12)
        public bool CanChiMiddle; // 吃中间 (例如打3，我有24)
        public bool CanChiRight;  // 吃右边 (例如打3，我有45)
        
        public bool HasAction => CanHu || CanPon || CanMingGan || CanAnGan || CanJiaGang
            || CanChiLeft || CanChiMiddle || CanChiRight;
    }

    public static class ActionValidator
    {
        /// <summary>
        /// 当别人打出一张牌时，检查我能做什么
        /// </summary>
        public static AllowedActions CheckActions(List<TileData> myHand, List<Meld> myMelds, TileData discardedTile, bool isNextPlayer, ScoringOptions options = null, WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East)
        {
            AllowedActions actions = new AllowedActions();
            int[] handCounts = MahjongLogic.ConvertToFrequencyArray(myHand);
            int targetIdx = MahjongLogic.GetTileIndex(discardedTile);

            // 1. 检查胡 (点炮) — 含番数校验，番数不够不显示胡按钮
            if (MahjongLogic.CheckWinWithFan(myHand, myMelds, discardedTile, false, out _, out _, roundWind, seatWind, options))
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
                actions.CanMingGan = true;
            }

            // 4. 检查吃 (只能吃上家的牌)。复用实际吃牌组合，避免频率数组跨花色相邻时误判。
            if (isNextPlayer)
            {
                foreach (var combination in GetChiCombinations(myHand, discardedTile))
                {
                    if (combination[1] < discardedTile.Value)
                        actions.CanChiLeft = true;
                    else if (combination[0] < discardedTile.Value)
                        actions.CanChiMiddle = true;
                    else
                        actions.CanChiRight = true;
                }
            }

            return actions;
        }

        /// <summary>
        /// [新增] 检查自己摸牌后的操作权限 (自摸胡、暗杠、加杠)
        /// </summary>
        /// <param name="myHand">手牌数据</param>
        /// <param name="myMelds">已有的副露</param>
        /// <param name="drawnTile">刚摸到的那张牌</param>
        public static AllowedActions CheckSelfActions(List<TileData> myHand, List<Meld> myMelds, TileData drawnTile, ScoringOptions options = null, WindDirection roundWind = WindDirection.East, WindDirection seatWind = WindDirection.East, SelfTurnKongOptions kongOptions = null)
        {
            AllowedActions actions = new AllowedActions();

            // 1. 检查自摸胡 (Tsumo)
            int fan;
            List<string> details;

            if (MahjongLogic.CheckWinWithFan(myHand, myMelds, drawnTile, true, out fan, out details, roundWind, seatWind, options))
            {
                actions.CanHu = true;
            }

            var resolvedKongOptions = kongOptions ?? SelfTurnKongResolver.Resolve(myHand, myMelds);
            actions.CanAnGan = resolvedKongOptions.AnGangTargets.Count > 0;
            actions.CanJiaGang = resolvedKongOptions.JiaGangTargets.Count > 0;

            return actions;
        }

        public static List<TileData> GetConcealedKanOptions(IEnumerable<TileData> hand)
        {
            return SelfTurnKongResolver.Resolve(hand, null).AnGangTargets.ToList();
        }

        public static List<TileData> GetAddedKanOptions(IEnumerable<TileData> hand, IEnumerable<Meld> melds)
        {
            return SelfTurnKongResolver.Resolve(hand, melds).JiaGangTargets.ToList();
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
