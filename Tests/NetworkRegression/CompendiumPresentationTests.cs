using System;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Fan;
using MahjongGame.Talents;

internal static class CompendiumPresentationTests
{
    public static void Run(RegressionRunner runner)
    {
        // 1. 国标番种完整性校验 (当前实现 80 种)
        var rules = FanRuleRegistry.Instance.ActiveRules;
        runner.Check(rules != null, "ActiveRules should not be null");
        runner.Check(rules != null && rules.Count == 80, $"Expected 80 registered MCR fan rules in codebase, got {rules?.Count}");

        if (rules != null)
        {
            foreach (var rule in rules)
            {
                runner.Check(!string.IsNullOrWhiteSpace(rule.Id), $"Rule ID should not be empty: {rule.GetType().Name}");
                runner.Check(!string.IsNullOrWhiteSpace(rule.Name), $"Rule Name should not be empty for {rule.Id}");
                runner.Check(rule.FanValue > 0, $"Rule FanValue should be positive for {rule.Id}, got {rule.FanValue}");
            }

            // 2. 番种分段过滤逻辑校验
            var topRules = rules.Where(r => r.FanValue >= 48).ToList();
            var highRules = rules.Where(r => r.FanValue >= 16 && r.FanValue < 48).ToList();
            var midRules = rules.Where(r => r.FanValue >= 6 && r.FanValue < 16).ToList();
            var lowRules = rules.Where(r => r.FanValue < 6).ToList();

            int totalCategorized = topRules.Count + highRules.Count + midRules.Count + lowRules.Count;
            runner.Check(totalCategorized == 80, $"Total categorized rules should be 80, got {totalCategorized}");

            // 88/64/48番顶级大番: 7 (88番) + 6 (64番) + 2 (48番) = 15 种
            runner.Check(topRules.Count == 15, $"Expected 15 top tier rules (48+), got {topRules.Count}");
            runner.Check(topRules.Any(r => r.Name == "大四喜" && r.FanValue == 88), "Top rules must include 大四喜");
            runner.Check(topRules.Any(r => r.Name == "十三幺" && r.FanValue == 88), "Top rules must include 十三幺");
            runner.Check(topRules.Any(r => r.Name == "一色双龙会" && r.FanValue == 64), "Top rules must include 一色双龙会");
            runner.Check(topRules.Any(r => r.Name == "一色四同顺" && r.FanValue == 48), "Top rules must include 一色四同顺");

            // 16~32番高番种: 3 (32番) + 9 (24番) + 5 (16番) = 17 种
            runner.Check(highRules.Count == 17, $"Expected 17 high tier rules (16-32), got {highRules.Count}");
            runner.Check(highRules.Any(r => r.Name == "七对子" && r.FanValue == 24), "High rules must include 七对子");
            runner.Check(highRules.Any(r => r.Name == "清龙" && r.FanValue == 16), "High rules must include 清龙");

            // 6~12番中番种: 5 (12番) + 10 (8番) + 7 (6番) = 22 种
            runner.Check(midRules.Count == 22, $"Expected 22 mid tier rules (6-12), got {midRules.Count}");
            runner.Check(midRules.Any(r => r.Name == "碰碰和" && r.FanValue == 6), "Mid rules must include 碰碰和");

            // 1~4番小番种: 4 (4番) + 11 (2番) + 11 (1番) = 26 种
            runner.Check(lowRules.Count == 26, $"Expected 26 low tier rules (1-4), got {lowRules.Count}");
            runner.Check(lowRules.Any(r => r.Name == "自摸" && r.FanValue == 1), "Low rules must include 自摸");

            // 3. 番种模糊搜索逻辑校验
            // 搜索 "龙"
            var dragonRules = rules.Where(r => (r.Name != null && r.Name.Contains("龙")) || (r.Description != null && r.Description.Contains("龙"))).ToList();
            runner.Check(dragonRules.Count >= 4, $"Expected at least 4 dragon-related rules, got {dragonRules.Count}");
            runner.Check(dragonRules.Any(r => r.Name == "清龙"), "Search '龙' must include 清龙");
            runner.Check(dragonRules.Any(r => r.Name == "花龙"), "Search '龙' must include 花龙");
            runner.Check(dragonRules.Any(r => r.Name == "组合龙"), "Search '龙' must include 组合龙");

            // 搜索 "自摸"
            var selfDrawRules = rules.Where(r => (r.Name != null && r.Name.Contains("自摸")) || (r.Description != null && r.Description.Contains("自摸"))).ToList();
            runner.Check(selfDrawRules.Any(r => r.Name == "自摸"), "Search '自摸' must match 自摸");
            runner.Check(selfDrawRules.Any(r => r.Name == "不求人"), "Search '自摸' must match 不求人 by description");
        }

        // 4. 天赋 9 大核心玩法规则完整性与品阶消耗精准校验
        var registry = TalentRegistry.Instance;
        var ids = registry.GetAllIds();
        runner.Check(ids != null, "Talent IDs should not be null");

        var expectedTalents = new[]
        {
            new { Id = "sheathed_edge", Name = "藏锋", Tier = TalentTier.Large, Cost = 28 },
            new { Id = "midas_touch", Name = "点金手", Tier = TalentTier.Medium, Cost = 15 },
            new { Id = "dragon_ascent", Name = "如龙", Tier = TalentTier.Medium, Cost = 15 },
            new { Id = "head_start", Name = "快人一步", Tier = TalentTier.Medium, Cost = 12 },
            new { Id = "interception", Name = "截流", Tier = TalentTier.Small, Cost = 8 },
            new { Id = "composure", Name = "定心", Tier = TalentTier.Small, Cost = 6 },
            new { Id = "starting_capital", Name = "初始资金", Tier = TalentTier.Small, Cost = 5 },
            new { Id = "peek", Name = "窥探", Tier = TalentTier.Small, Cost = 5 },
            new { Id = "draw_reward", Name = "厚积", Tier = TalentTier.Small, Cost = 3 }
        };

        foreach (var expected in expectedTalents)
        {
            runner.Check(registry.HasTalent(expected.Id), $"Registry must contain talent {expected.Id}");
            runner.Check(registry.GetDisplayName(expected.Id) == expected.Name,
                $"Talent {expected.Id} name mismatch: expected {expected.Name}, got {registry.GetDisplayName(expected.Id)}");
            runner.Check(registry.GetTier(expected.Id) == expected.Tier,
                $"Talent {expected.Id} tier mismatch: expected {expected.Tier}, got {registry.GetTier(expected.Id)}");
            runner.Check(registry.GetCost(expected.Id) == expected.Cost,
                $"Talent {expected.Id} cost mismatch: expected {expected.Cost}, got {registry.GetCost(expected.Id)}");
            runner.Check(!string.IsNullOrWhiteSpace(registry.GetDescription(expected.Id)),
                $"Talent {expected.Id} description must not be empty");
        }

        // 5. 天赋品阶与主动筛选逻辑校验
        var majorTalents = expectedTalents.Where(t => t.Tier == TalentTier.Large).ToList();
        var mediumTalents = expectedTalents.Where(t => t.Tier == TalentTier.Medium).ToList();
        var minorTalents = expectedTalents.Where(t => t.Tier == TalentTier.Small).ToList();
        var activeTalents = expectedTalents.Where(t => registry.GetMetadata(t.Id).ActivationWindow != TalentActivationWindow.None).ToList();

        runner.Check(majorTalents.Count == 1, $"Expected 1 Large production talent (藏锋), got {majorTalents.Count}");
        runner.Check(majorTalents.Any(t => t.Id == "sheathed_edge"), "Large talent must be sheathed_edge");

        runner.Check(mediumTalents.Count == 3, $"Expected 3 Medium production talents, got {mediumTalents.Count}");
        runner.Check(mediumTalents.Any(t => t.Id == "midas_touch"), "Medium talents must include midas_touch");
        runner.Check(mediumTalents.Any(t => t.Id == "dragon_ascent"), "Medium talents must include dragon_ascent");
        runner.Check(mediumTalents.Any(t => t.Id == "head_start"), "Medium talents must include head_start");

        runner.Check(minorTalents.Count == 5, $"Expected 5 Small production talents, got {minorTalents.Count}");
        runner.Check(minorTalents.Any(t => t.Id == "interception"), "Small talents must include interception");
        runner.Check(minorTalents.Any(t => t.Id == "composure"), "Small talents must include composure");
        runner.Check(minorTalents.Any(t => t.Id == "starting_capital"), "Small talents must include starting_capital");
        runner.Check(minorTalents.Any(t => t.Id == "peek"), "Small talents must include peek");
        runner.Check(minorTalents.Any(t => t.Id == "draw_reward"), "Small talents must include draw_reward");

        // 主动技能（截流）
        runner.Check(activeTalents.Any(t => t.Id == "interception"), "Active talents must include interception");
    }
}
