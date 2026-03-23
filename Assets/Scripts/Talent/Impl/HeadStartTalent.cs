using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("head_start", "快人一步", "你的番数+2（降低起胡门槛）",
        TalentTier.Medium, 12)]
    public class HeadStartTalent : TalentRule
    {
        public const int BonusFanValue = 2;
    }
}
