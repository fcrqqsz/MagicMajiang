using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("call_the_mark", "点将", "每小局1次，摸牌出牌阶段可指定一名其他玩家并公开目标；本家下一次提交吃/碰/明杠若来自该目标，合法胡牌最终结算额外+6番；若来自其他玩家则本局奖励失效。",
        TalentTier.Medium, 14, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class CallTheMarkTalent : TalentRule
    {
        private const string UsedThisRoundKey = "used_this_round";
        private const string TargetSeatKey = "target_seat";
        private const string MarkPendingKey = "mark_pending";
        private const string MarkSuccessKey = "mark_success";
        private const string MarkFailedKey = "mark_failed";

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn
                || context.State.GetFlag(UsedThisRoundKey, TalentStateScope.Round))
            {
                return;
            }

            int kamicha = (context.OwnerSeatIndex + 3) % 4;
            for (int s = 0; s < 4; s++)
            {
                if (s == context.OwnerSeatIndex) continue;

                int priority = (s == kamicha) ? 150 : 100;
                output.Add(new TalentActionOption
                {
                    TalentId = Id,
                    TargetSeatIndex = s,
                    AiPriority = priority
                });
            }
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn)
                return TalentActionResult.NotSupported();
            if (context.State.GetFlag(UsedThisRoundKey, TalentStateScope.Round))
                return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

            if (request == null
                || request.TargetSeatIndex < 0
                || request.TargetSeatIndex > 3
                || request.TargetSeatIndex == context.OwnerSeatIndex)
            {
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidTarget);
            }

            context.State.SetFlag(UsedThisRoundKey, true, TalentStateScope.Round);
            context.State.SetCounter(TargetSeatKey, request.TargetSeatIndex, TalentStateScope.Round);
            context.State.SetFlag(MarkPendingKey, true, TalentStateScope.Round);

            return TalentActionResult.Success(
                effectApplied: true,
                publicStateEventType: "call_the_mark_target",
                publicStateValue: request.TargetSeatIndex + 1);
        }

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex) return;
            if (!context.State.GetFlag(MarkPendingKey, TalentStateScope.Round)) return;

            switch (context.Facts.ActionType)
            {
                case ClientActionType.Chi:
                case ClientActionType.Pon:
                case ClientActionType.MingGan:
                    break;
                default:
                    return;
            }

            context.State.SetFlag(MarkPendingKey, false, TalentStateScope.Round);
            int target = context.State.GetCounter(TargetSeatKey, TalentStateScope.Round);

            if (context.Facts.SourceSeatIndex.HasValue && context.Facts.SourceSeatIndex.Value == target)
            {
                context.State.SetFlag(MarkSuccessKey, true, TalentStateScope.Round);
                context.EmitPublic("call_the_mark_success", target + 1);
            }
            else
            {
                context.State.SetFlag(MarkFailedKey, true, TalentStateScope.Round);
                context.EmitPublic(
                    "call_the_mark_failed",
                    context.Facts.SourceSeatIndex.HasValue
                        ? context.Facts.SourceSeatIndex.Value + 1
                        : 0);
            }
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(MarkSuccessKey, TalentStateScope.Round) ? 6 : 0;

        public override int GetSnapshotPrivateValue(TalentRuntimeState state)
        {
            if (state.GetFlag(MarkSuccessKey, TalentStateScope.Round)) return 6;
            if (state.GetFlag(MarkPendingKey, TalentStateScope.Round))
                return state.GetCounter(TargetSeatKey, TalentStateScope.Round) + 1;
            if (state.GetFlag(UsedThisRoundKey, TalentStateScope.Round)) return -1;
            return 0;
        }

        public override string GetSnapshotPrivateStatusKey(TalentRuntimeState state)
        {
            if (state.GetFlag(MarkSuccessKey, TalentStateScope.Round)) return "success";
            if (state.GetFlag(MarkFailedKey, TalentStateScope.Round)) return "failed";
            if (state.GetFlag(MarkPendingKey, TalentStateScope.Round)) return "pending";
            return null;
        }
    }
}
