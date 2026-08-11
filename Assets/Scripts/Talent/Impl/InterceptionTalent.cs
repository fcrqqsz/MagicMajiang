using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("interception", "截流", "整场3次，令一项公开充能天赋减少1层。",
        TalentTier.Small, 8, TalentPhase.ActionValidation,
        StateScope = TalentStateScope.Match,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect)]
    public sealed class InterceptionTalent : TalentRule
    {
        private const string UsesKey = "uses_remaining";
        private const string UsedDecisionKey = "used_decision";

        public override void InitializeMatchState(TalentMatchContext context)
        {
            context.State.SetCounter(UsesKey, 3, TalentStateScope.Match);
        }

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context.State.GetCounter(UsesKey, TalentStateScope.Match) <= 0
                || context.State.GetToken(UsedDecisionKey, TalentStateScope.Round)
                    == context.DecisionId)
            {
                return;
            }

            foreach (PublicChargeTarget target in context.GetPublicChargeTargets())
            {
                output.Add(new TalentActionOption
                {
                    TalentId = Id,
                    TargetSeatIndex = target.OwnerSeatIndex,
                    TargetTalentId = target.TalentId
                });
            }
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            int remaining = context.State.GetCounter(UsesKey, TalentStateScope.Match);
            if (remaining <= 0)
                return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);
            if (context.State.GetToken(UsedDecisionKey, TalentStateScope.Round) == context.DecisionId)
                return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

            PublicChargeTarget target = context.ResolvePublicChargeTarget(request);
            if (target == null)
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);

            context.State.SetCounter(UsesKey, remaining - 1, TalentStateScope.Match);
            context.State.SetToken(UsedDecisionKey, context.DecisionId, TalentStateScope.Round);
            context.RevealWithPublicCounter(UsesKey, remaining - 1, TalentStateScope.Match);
            context.ApplyNegativeEffect(new TalentNegativeEffect(
                context.OwnerSeatIndex,
                Id,
                target.OwnerSeatIndex,
                target.TalentId,
                TalentNegativeEffectTypes.ReducePublicChargeLayer));
            return TalentActionResult.Success();
        }

        public override int GetSnapshotPrivateValue(TalentRuntimeState state) =>
            state.GetCounter(UsesKey, TalentStateScope.Match);
    }
}
