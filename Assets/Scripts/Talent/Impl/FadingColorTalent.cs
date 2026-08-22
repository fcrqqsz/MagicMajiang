using System;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("fading_color", "褪色", "每小局本家首次提交打出异化牌时积攒1点【墨】（最多2点，跨小局保留）。自己的摸牌出牌阶段可主动消耗1点【墨】，削减指定对手1点公开充能（【锋】/【势】/【墨】等）。",
        TalentTier.Small, 8, TalentPhase.ActionValidation,
        StateScope = TalentStateScope.Match,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class FadingColorTalent : TalentRule, IPublicChargeTalent, IPublicChargeControlTalent
    {
        private const string ChargeKey = "ink";
        private const string RoundChargedFlag = "charged_this_round";
        private const string UsedDecisionKey = "used_decision";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex) return;
            if (context.Facts.ActionType != ClientActionType.Discard) return;
            if (context.Facts.TargetTile == null || !context.Facts.TargetTile.IsModified) return;

            if (context.State.GetFlag(RoundChargedFlag, TalentStateScope.Round)) return;
            context.State.SetFlag(RoundChargedFlag, true, TalentStateScope.Round);

            int current = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
            if (current < 2)
            {
                int next = Math.Min(2, current + 1);
                context.RevealWithPublicCounter(ChargeKey, next, TalentStateScope.Match);
            }
        }

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn
                || context.State.GetCounter(ChargeKey, TalentStateScope.Match) < 1
                || context.State.GetToken(UsedDecisionKey, TalentStateScope.Round) == context.DecisionId)
            {
                return;
            }

            foreach (PublicChargeTarget target in context.GetPublicChargeTargets())
            {
                output.Add(new TalentActionOption
                {
                    TalentId = Id,
                    AiPriority = 100,
                    TargetSeatIndex = target.OwnerSeatIndex,
                    TargetTalentId = target.TalentId,
                    TargetPublicCharge = target.CurrentCharge
                });
            }
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn)
                return TalentActionResult.NotSupported();

            int current = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
            if (current <= 0)
                return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);

            if (context.State.GetToken(UsedDecisionKey, TalentStateScope.Round) == context.DecisionId)
                return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

            PublicChargeTarget target = context.ResolvePublicChargeTarget(request);
            if (target == null)
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);

            int remaining = current - 1;
            context.State.SetCounter(ChargeKey, remaining, TalentStateScope.Match);
            context.State.SetToken(UsedDecisionKey, context.DecisionId, TalentStateScope.Round);
            context.RevealWithPublicCounter(ChargeKey, remaining, TalentStateScope.Match);

            TalentNegativeEffectResult effectResult = context.ApplyNegativeEffect(new TalentNegativeEffect(
                context.OwnerSeatIndex,
                Id,
                target.OwnerSeatIndex,
                target.TalentId,
                TalentNegativeEffectTypes.ReducePublicChargeLayer));

            return TalentActionResult.Success(
                effectApplied: effectResult.WasApplied,
                publicStateEventType: "ink_changed",
                publicStateValue: remaining);
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
