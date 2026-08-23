using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("last_stand_formation", "背水阵", "本家提交第2个新公开副露（吃/碰/明杠）后公开发动；发动后当局起胡门槛提高2番，在此门槛下合法胡牌最终结算额外+12番。",
        TalentTier.Medium, 16, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class LastStandFormationTalent : TalentRule
    {
        private const string MeldCountKey = "meld_count";
        private const string TriggeredKey = "triggered";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex) return;

            switch (context.Facts.ActionType)
            {
                case ClientActionType.Chi:
                case ClientActionType.Pon:
                case ClientActionType.MingGan:
                    break;
                default:
                    return;
            }

            int count = context.State.IncrementCounter(MeldCountKey, TalentStateScope.Round, 1);
            if (count >= 2 && !context.State.GetFlag(TriggeredKey, TalentStateScope.Round))
            {
                context.State.SetFlag(TriggeredKey, true, TalentStateScope.Round);
                context.EmitPublic("last_stand_formation_triggered", 1);
            }
        }

        public override void ConfigureScoring(TalentScoringContext context, ScoringOptions options)
        {
            if (context.State.GetFlag(TriggeredKey, TalentStateScope.Round))
            {
                options.MinimumFan += 2;
            }
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(TriggeredKey, TalentStateScope.Round) ? 12 : 0;
    }
}
