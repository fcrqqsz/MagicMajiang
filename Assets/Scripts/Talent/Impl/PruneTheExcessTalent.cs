using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("prune_the_excess", "去芜", "本家本局第3次打出幺九牌或字牌后发动；本局合法胡牌最终结算额外+3番。",
        TalentTier.Small, 6, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class PruneTheExcessTalent : TalentRule
    {
        private const string QualifyingDiscardCountKey = "qualifying_discard_count";
        private const string TriggeredKey = "triggered";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex
                || context.Facts.ActionType != ClientActionType.Discard
                || !IsQualifying(context.Facts.TargetTile))
            {
                return;
            }

            int count = context.State.IncrementCounter(
                QualifyingDiscardCountKey,
                TalentStateScope.Round);
            if (count >= 3 && !context.State.GetFlag(TriggeredKey, TalentStateScope.Round))
            {
                context.State.SetFlag(TriggeredKey, true, TalentStateScope.Round);
                context.EmitPublic("prune_the_excess_triggered", 1);
            }
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(TriggeredKey, TalentStateScope.Round) ? 3 : 0;

        private static bool IsQualifying(TalentTileFacts tile)
        {
            if (tile == null) return false;
            if (tile.Suit == Suit.Wind || tile.Suit == Suit.Dragon) return true;
            return (tile.Suit == Suit.Man || tile.Suit == Suit.Pin || tile.Suit == Suit.Sou)
                   && (tile.Value == 1 || tile.Value == 9);
        }
    }
}
