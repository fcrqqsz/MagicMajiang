using System;

namespace MahjongGame.Core.Fan
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class FanRuleAttribute : Attribute
    {
        public string Id { get; private set; }
        public int DefaultFan { get; private set; }
        public string RuleSet { get; private set; } // 例如 "CSM" (国标), "Riichi" (日麻)

        public FanRuleAttribute(string id, int defaultFan, string ruleSet = "CSM")
        {
            Id = id;
            DefaultFan = defaultFan;
            RuleSet = ruleSet;
        }
    }
}