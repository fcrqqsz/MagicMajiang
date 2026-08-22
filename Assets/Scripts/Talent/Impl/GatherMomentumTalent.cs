using System;
using System.Collections.Generic;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("gather_momentum", "乘势", "每次吃、碰、明杠、暗杠、加杠成功时积攒1层【势】（最多3层，跨小局保留）。自己的摸牌出牌阶段可主动消耗全部【势】进入强化状态（每小局限1次）；进入强化后，本小局若达成起胡番数胡牌，每消耗1层【势】最终结算额外+8番。",
        TalentTier.Large, 26, TalentPhase.Scoring,
        StateScope = TalentStateScope.Match,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class GatherMomentumTalent : TalentRule, IPublicChargeTalent
    {
        private const string ChargeKey = "momentum";
        private const string ArmedKey = "armed";
        private const string ArmedChargeKey = "armed_charge";

        public override void OnActionCommitted(TalentActionCommittedContext context)
        {
            if (context.Facts.ActorSeatIndex != context.OwnerSeatIndex) return;

            switch (context.Facts.ActionType)
            {
                case ClientActionType.Chi:
                case ClientActionType.Pon:
                case ClientActionType.MingGan:
                case ClientActionType.AnGan:
                case ClientActionType.JiaGang:
                    break;
                default:
                    return;
            }

            int current = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
            if (current >= 3) return;

            int next = Math.Min(3, current + 1);
            context.SetPublicCounter(ChargeKey, next, TalentStateScope.Match);
        }

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn
                || context.State.GetCounter(ChargeKey, TalentStateScope.Match) < 1
                || context.State.GetFlag(ArmedKey, TalentStateScope.Round))
            {
                return;
            }

            output.Add(new TalentActionOption { TalentId = Id, AiPriority = 200 });
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn
                || context.State.GetFlag(ArmedKey, TalentStateScope.Round))
            {
                return TalentActionResult.NotSupported();
            }
            int consumedLayers = context.State.GetCounter(ChargeKey, TalentStateScope.Match);
            if (consumedLayers <= 0)
                return TalentActionResult.Reject(TalentActionErrorCodes.InsufficientResource);

            context.State.SetCounter(
                ArmedChargeKey,
                consumedLayers,
                TalentStateScope.Round);
            context.SetPublicCounter(ChargeKey, 0, TalentStateScope.Match);
            context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
            context.EmitPublic("armed", 1);
            return TalentActionResult.Success(effectApplied: true);
        }

        public override int GetPostLegalFanBonus(TalentWinContext context) =>
            context.State.GetFlag(ArmedKey, TalentStateScope.Round)
                ? context.State.GetCounter(ArmedChargeKey, TalentStateScope.Round) * 8
                : 0;

        public override void OnAcceptedWin(TalentWinContext context)
        {
            if (!context.State.GetFlag(ArmedKey, TalentStateScope.Round)) return;
            context.State.SetFlag(ArmedKey, false, TalentStateScope.Round);
            context.State.SetCounter(ArmedChargeKey, 0, TalentStateScope.Round);
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
