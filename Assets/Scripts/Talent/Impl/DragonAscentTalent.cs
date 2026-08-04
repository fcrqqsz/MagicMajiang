using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("dragon_ascent", "如龙", "清龙判定允许一张牌\u00b11大小浮动",
        TalentTier.Medium, 15)]
    public class DragonAscentTalent : TalentRule
    {
        public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options)
        {
            options.RelaxedPureStraight = true;
        }
    }
}
