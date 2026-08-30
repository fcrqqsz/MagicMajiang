using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("peek", "窥探", "每局开始时可以看到牌山顶部4张牌；这些牌进入其他玩家暗手后会持续显示。明牌排序仅用于整理信息，不代表真实手牌位置。",
        TalentTier.Small, 5,
        RevealPolicy = TalentRevealPolicy.OwnerOnly)]
    public class PeekTalent : TalentRule
    {
        public const int PeekCount = 4;

        public override int GetRoundStartPeekCount(TalentRoundContext context) => PeekCount;
    }
}
