using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("follow_the_trail", "循迹", "为每个对手记录最近两次弃牌花色；荣和时若放铳者前一张弃牌花色与胡牌张同为数牌（万/饼/条）且花色相同，最终结算额外+4番。",
        TalentTier.Small, 8, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class FollowTheTrailTalent : TalentRule
    {
        public override TalentScope Scope => TalentScope.Global;

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActionType != ClientActionType.Discard) return;

            int actor = context.Facts.ActorSeatIndex;
            if (actor == context.OwnerSeatIndex || actor < 0 || actor > 3) return;
            if (context.Facts.TargetTile == null) return;

            Suit suit = context.Facts.TargetTile.Suit;
            string hasCurrKey = $"has_curr_{actor}";
            string hasPrevKey = $"has_prev_{actor}";
            string currKey = $"curr_{actor}";
            string prevKey = $"prev_{actor}";

            if (context.State.GetFlag(hasCurrKey, TalentStateScope.Round))
            {
                int existingCurr = context.State.GetCounter(currKey, TalentStateScope.Round);
                context.State.SetCounter(prevKey, existingCurr, TalentStateScope.Round);
                context.State.SetFlag(hasPrevKey, true, TalentStateScope.Round);
            }

            context.State.SetCounter(currKey, (int)suit, TalentStateScope.Round);
            context.State.SetFlag(hasCurrKey, true, TalentStateScope.Round);
        }

        public override int GetPostLegalFanBonus(TalentWinContext context)
        {
            if (context.CurrentSeatIndex != context.OwnerSeatIndex) return 0;
            if (context.Facts.IsSelfDraw) return 0;
            if (!context.Facts.DiscarderSeatIndex.HasValue) return 0;

            int discarder = context.Facts.DiscarderSeatIndex.Value;
            if (discarder == context.OwnerSeatIndex || discarder < 0 || discarder > 3) return 0;
            if (context.Facts.WinningTile == null) return 0;

            Suit winningSuit = context.Facts.WinningTile.Suit;
            if (winningSuit != Suit.Man && winningSuit != Suit.Pin && winningSuit != Suit.Sou) return 0;

            string historyPrefix = context.Facts.IsRobKong ? "curr" : "prev";
            if (!context.State.GetFlag($"has_{historyPrefix}_{discarder}", TalentStateScope.Round)) return 0;

            Suit previousDiscardSuit = (Suit)context.State.GetCounter(
                $"{historyPrefix}_{discarder}",
                TalentStateScope.Round);
            if (previousDiscardSuit != Suit.Man
                && previousDiscardSuit != Suit.Pin
                && previousDiscardSuit != Suit.Sou)
            {
                return 0;
            }

            return previousDiscardSuit == winningSuit ? 4 : 0;
        }
    }
}
