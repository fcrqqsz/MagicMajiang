using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("starting_capital", "初始资金", "对战开始时，初始分数+30",
        TalentTier.Small, 5,
        StateScope = TalentStateScope.Match,
        RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
        SideboardPolicy = TalentSideboardPolicy.MainOnlyLocked)]
    public class StartingCapitalTalent : TalentRule
    {
        public const int BonusScore = 30;
    }
}
