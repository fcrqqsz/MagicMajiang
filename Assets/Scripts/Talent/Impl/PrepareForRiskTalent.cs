using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("prepare_for_risk", "未雨绸缪", "每小局首次主决策选择防自摸或防放铳；满足保险条件时，本家返还8分门槛分。",
        TalentTier.Medium, 12, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class PrepareForRiskTalent : TalentRule
    {
        private const string ChosenKey = "chosen";
        private const string ModeKey = "mode";
        private const int SelfDrawMode = 1;
        private const int RonMode = 2;
        private const int RefundScore = 8;

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (!context.IsFirstMainDecisionOfRound
                || context.State.GetFlag(ChosenKey, TalentStateScope.Round))
            {
                return;
            }

            output.Add(new TalentActionOption
            {
                TalentId = Id,
                AiPriority = 100,
                Choice = new TalentChoiceSet(
                    TalentChoiceKind.Mode,
                    "选择未雨绸缪模式",
                    "ron",
                    new[]
                    {
                        new TalentChoiceOption("self_draw", "防自摸", SelfDrawMode),
                        new TalentChoiceOption("ron", "防放铳", RonMode)
                    })
            });
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (!context.IsFirstMainDecisionOfRound
                || context.State.GetFlag(ChosenKey, TalentStateScope.Round))
            {
                return TalentActionResult.Reject(TalentActionErrorCodes.NotAvailable);
            }
            if (!TryParseMode(request?.ChoiceId, out int mode))
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidChoice);

            context.State.SetFlag(ChosenKey, true, TalentStateScope.Round);
            context.State.SetCounter(ModeKey, mode, TalentStateScope.Round);
            return TalentActionResult.Success(
                effectApplied: true,
                publicStateEventType: "prepare_for_risk_mode",
                publicStateValue: mode);
        }

        public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
        {
            if (!context.State.GetFlag(ChosenKey, TalentStateScope.Round)
                || outcome.IsAborted
                || outcome.IsDraw
                || !outcome.WinnerSeatIndex.HasValue)
            {
                return;
            }

            int winner = outcome.WinnerSeatIndex.Value;
            if (winner == context.OwnerSeatIndex) return;

            bool hasDiscarder = outcome.DiscarderSeatIndex.HasValue;
            int discarder = hasDiscarder ? outcome.DiscarderSeatIndex.Value : -1;
            bool uninvolvedThirdPartyRon = hasDiscarder
                                           && discarder != context.OwnerSeatIndex
                                           && discarder != winner
                                           && winner != context.OwnerSeatIndex;
            bool protectsOtherSelfDraw = context.State.GetCounter(ModeKey, TalentStateScope.Round) == SelfDrawMode
                                         && !hasDiscarder;
            bool protectsOwnRonLoss = context.State.GetCounter(ModeKey, TalentStateScope.Round) == RonMode
                                      && hasDiscarder
                                      && discarder == context.OwnerSeatIndex;
            if (!uninvolvedThirdPartyRon && !protectsOtherSelfDraw && !protectsOwnRonLoss)
                return;

            context.ApplyScoreDelta(RefundScore, "prepare_for_risk_refund");
        }

        private static bool TryParseMode(string choiceId, out int mode)
        {
            switch (choiceId)
            {
                case "self_draw": mode = SelfDrawMode; return true;
                case "ron": mode = RonMode; return true;
                default: mode = 0; return false;
            }
        }
    }
}
