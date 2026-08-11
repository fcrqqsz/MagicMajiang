using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("composure", "定心", "每小局首次受到的负面天赋效果无效。",
        TalentTier.Small, 6, TalentPhase.ActionValidation,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect)]
    public sealed class ComposureTalent : TalentRule
    {
        private const string ConsumedKey = "consumed";

        public override bool TryBlockNegativeEffect(
            TalentNegativeEffectContext context,
            TalentNegativeEffect effect)
        {
            if (context.State.GetFlag(ConsumedKey, TalentStateScope.Round)) return false;

            context.State.SetFlag(ConsumedKey, true, TalentStateScope.Round);
            context.Reveal("blocked_negative_effect", 1);
            return true;
        }
    }
}
