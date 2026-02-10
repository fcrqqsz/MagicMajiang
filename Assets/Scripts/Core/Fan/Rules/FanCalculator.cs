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
            if (rule.Check(ctx))
            {
                totalFan += rule.FanValue;
                fanNames.Add($"{rule.Name}({rule.FanValue})");
            }
        }
        return totalFan;
    }
    }
}