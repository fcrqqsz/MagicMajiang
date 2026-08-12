using System;
using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("sheathed_edge", "藏锋", "未获胜积攒锋，消耗3层令本局下次合法胡牌+16番。",
        TalentTier.Large, 28, TalentPhase.Scoring,
        StateScope = TalentStateScope.Match,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
        SideboardPolicy = TalentSideboardPolicy.MainOnly)]
    public sealed class SheathedEdgeTalent : TalentRule, IPublicChargeTalent
    {
        private const string ChargeKey = "edge";
        private const string ArmedKey = "armed";

        public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
        {
            if (outcome.IsAborted || outcome.WinnerSeatIndex == context.OwnerSeatIndex) return;
            int current = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
            context.SetPublicCounter(
                ChargeKey,
                Math.Min(3, current + 1),
                TalentStateScope.Match);
        }

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (!context.IsFirstMainDecisionOfRound
                || context.State.GetCounter(ChargeKey, TalentStateScope.Match) < 3
                || context.State.GetFlag(ArmedKey, TalentStateScope.Round))
            {
                return;
            }

            output.Add(new TalentActionOption { TalentId = Id });
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (!context.IsFirstMainDecisionOfRound
                || context.State.GetFlag(ArmedKey, TalentStateScope.Round))
            {
                return TalentActionResult.NotSupported();
            }
            if (context.State.GetCounter(ChargeKey, TalentStateScope.Match) < 3)
                return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);

            context.SetPublicCounter(ChargeKey, 0, TalentStateScope.Match);
            context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
            context.EmitPublic("armed", 1);
            return TalentActionResult.Success(effectApplied: true);
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(ArmedKey, TalentStateScope.Round) ? 16 : 0;

        public override void OnAcceptedWin(TalentWinContext context)
        {
            if (!context.State.GetFlag(ArmedKey, TalentStateScope.Round)) return;
            context.State.SetFlag(ArmedKey, false, TalentStateScope.Round);
            context.EmitPublic("armed_consumed", 1);
        }

        public int GetCurrentCharge(TalentRuntimeState state) =>
            state.GetCounter(ChargeKey, TalentStateScope.Match);

        public override int GetSnapshotPrivateValue(TalentRuntimeState state) =>
            GetCurrentCharge(state);

        public bool TryReduceCharge(TalentRuntimeState state, int amount)
        {
            int current = GetCurrentCharge(state);
            if (amount <= 0 || current <= 0) return false;
            state.SetCounter(
                ChargeKey,
                Math.Max(0, current - amount),
                TalentStateScope.Match);
            return true;
        }
    }
}
