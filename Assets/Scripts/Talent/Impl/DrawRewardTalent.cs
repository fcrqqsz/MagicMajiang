using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("draw_reward", "厚积", "流局时获得+5分",
        TalentTier.Small, 3)]
    public class DrawRewardTalent : TalentRule
    {
        public const int DrawBonus = 5;

        public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
        {
            if (outcome.IsDraw)
                context.ApplyScoreDelta(DrawBonus, "draw_reward");
        }
    }
}
