using System;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("redirect_force", "化劲", "每小局首次受到削减公开充能效果时，使该效果无效，并使本小局本家胡牌额外+4番。",
        TalentTier.Medium, 12, TalentPhase.ActionValidation, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class RedirectForceTalent : TalentRule, IPublicChargeDefenseTalent
    {
        private const string ConsumedKey = "consumed";
        private const string ArmedKey = "armed";

        public override int Priority => 10;

        public override bool TryBlockNegativeEffect(
            TalentNegativeEffectContext context,
            TalentNegativeEffect effect)
        {
            if (!string.Equals(effect.EffectType, TalentNegativeEffectTypes.ReducePublicChargeLayer, StringComparison.Ordinal))
                return false;

            if (context.State.GetFlag(ConsumedKey, TalentStateScope.Round))
                return false;

            context.State.SetFlag(ConsumedKey, true, TalentStateScope.Round);
            context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
            context.Reveal("blocked_negative_effect", 0);
            return true;
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(ArmedKey, TalentStateScope.Round)
                ? 4
                : 0;

        public override void OnAcceptedWin(TalentWinContext context)
        {
            if (!context.State.GetFlag(ArmedKey, TalentStateScope.Round)) return;

            context.State.SetFlag(ArmedKey, false, TalentStateScope.Round);
            context.EmitPublic("armed_consumed", 1);
        }

        public override int GetSnapshotPrivateValue(TalentRuntimeState state) =>
            state.IsActive && !state.GetFlag(ConsumedKey, TalentStateScope.Round) ? 1 : 0;
    }
}
