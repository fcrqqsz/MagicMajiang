using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Fan.Rules;
using UnityEngine;

namespace MahjongGame.Core.Fan
{
    public class FanCalculator
    {
        public int CalculateTotalFan(FanContext ctx, out List<string> fanNames)
        {
            int totalFan = 0;
            fanNames = new List<string>();

            // 1. 获取所有规则并按优先级从高到低排序
            var rules = FanRuleRegistry.Instance.ActiveRules
                .OrderByDescending(r => r.Priority)
                .ToList();

            // 临时存储所有命中的番种及其次数
            var matchedResults = new List<(FanRule Rule, int Count)>();
            // 存储被排斥的 ID 集合
            var excludedIds = new HashSet<string>();

            // 2. 第一轮判定：找出所有命中的番种，并收集排斥 ID
            foreach (var rule in rules)
            {
                // 跳过“无番和”的初步判定，留到最后
                if (rule.Id == "no_points_win") continue;

                int count = rule.GetMatchCount(ctx);
                if (count > 0)
                {
                    matchedResults.Add((rule, count));
                    
                    // 如果命中了，收集它排斥的番种
                    foreach (var exId in rule.ExcludedRuleIds)
                    {
                        excludedIds.Add(exId);
                    }
                }
            }

            // 3. 第二轮过滤：剔除掉在排斥集中的番种
            var finalMatched = matchedResults.Where(r => !excludedIds.Contains(r.Rule.Id)).ToList();

            // 4. 特殊处理：无番和 (8番)
            // 如果除了自摸(1番)以外，没有任何番种命中，则计无番和
            // 注意：国标规则中，无番和是指不计入除自摸外的任何番种。
            if (finalMatched.Count == 0 || (finalMatched.Count == 1 && finalMatched[0].Rule.Id == "self_draw"))
            {
                var noPointsRule = FanRuleRegistry.Instance.ActiveRules.FirstOrDefault(r => r.Id == "no_points_win");
                if (noPointsRule != null)
                {
                    finalMatched.Add((noPointsRule, 1));
                }
            }

            // 5. 汇总求和
            foreach (var result in finalMatched)
            {
                if (excludedIds.Contains(result.Rule.Id))
                {
                    continue;
                }

                int subTotal = result.Rule.FanValue * result.Count;
                totalFan += subTotal;

                string displayName = result.Count > 1 ? $"{result.Rule.Name} x{result.Count}" : result.Rule.Name;
                fanNames.Add($"{displayName}({subTotal})");
            }

            return totalFan;
        }
    }
}