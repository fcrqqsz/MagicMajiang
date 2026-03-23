using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("peek", "窥探", "每局开始时可以看到牌山顶部4张牌",
        TalentTier.Small, 5)]
    public class PeekTalent : TalentRule
    {
        public const int PeekCount = 4;
    }
}
