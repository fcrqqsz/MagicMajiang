using System.Collections.Generic;
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

        // 从 Registry 获取当前所有已加载的规则
        var rules = FanRuleRegistry.Instance.ActiveRules;

        foreach (var rule in rules)
        {
            int count = rule.GetMatchCount(ctx);
            if (count > 0)
            {
                int subTotal = rule.FanValue * count;
                totalFan += subTotal;

                string displayName = count > 1 ? $"{rule.Name} x{count}" : rule.Name;
                fanNames.Add($"{displayName}({subTotal})");
            }
        }
        return totalFan;
    }
    }
}