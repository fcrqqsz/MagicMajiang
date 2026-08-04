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
        public TalentStateScope StateScope { get; set; } = TalentStateScope.Round;
        public TalentActivationWindow ActivationWindow { get; set; } = TalentActivationWindow.None;
        public TalentRevealPolicy RevealPolicy { get; set; } = TalentRevealPolicy.HiddenUntilPublicEffect;
        public TalentSideboardPolicy SideboardPolicy { get; set; } = TalentSideboardPolicy.Flexible;

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
