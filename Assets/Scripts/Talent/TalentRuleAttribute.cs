using System;
using MahjongGame.Core;

namespace MahjongGame.Talents
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class TalentRuleAttribute : Attribute
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public TalentTier Tier { get; }
        public int AlienationCost { get; }
        public TalentPhase[] Phases { get; }

        public TalentRuleAttribute(string id, string displayName, string description,
            TalentTier tier, int alienationCost, params TalentPhase[] phases)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Tier = tier;
            AlienationCost = alienationCost;
            Phases = phases;
        }
    }
}
