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

            // 临时存储所有命中的番种及其次数 (简化元组声明以提高兼容性)
            var matchedResults = new List<(FanRule Rule, int Count)>();
            // 存储被排斥的 ID 集合
            var excludedIds = new HashSet<string>();

            // 2. 第一轮判定：找出所有命中的番种，并收集排斥 ID
            foreach (var rule in rules)
            {
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

            // 3. 第二轮过滤与求和：剔除掉在排斥集中的番种
            foreach (var result in matchedResults)
            {
                if (excludedIds.Contains(result.Rule.Id))
                {
                    // Debug.Log($"[Fan] 番种 {result.Rule.Name} 被排斥，不计分");
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