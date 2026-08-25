using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("bide_the_tide", "候潮", "本家本局提交至少6次弃牌后，合法胡牌最终结算额外+2番。",
        TalentTier.Small, 4, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class BideTheTideTalent : TalentRule
    {
        private const string DiscardCountKey = "discard_count";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex == context.OwnerSeatIndex
                && context.Facts.ActionType == ClientActionType.Discard)
            {
                context.State.IncrementCounter(DiscardCountKey, TalentStateScope.Round);
            }
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetCounter(DiscardCountKey, TalentStateScope.Round) >= 6 ? 2 : 0;

        public override int GetSnapshotPrivateValue(TalentRuntimeState state) =>
            System.Math.Min(6, state.GetCounter(DiscardCountKey, TalentStateScope.Round));
    }
}
