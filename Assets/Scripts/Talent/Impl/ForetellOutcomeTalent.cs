using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("foretell_outcome", "预判", "每小局首次主决策选择自摸或荣和；本家以所选方式合法胡牌时，最终结算额外+3番。",
        TalentTier.Small, 6, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class ForetellOutcomeTalent : TalentRule
    {
        private const string ChosenKey = "chosen";
        private const string ModeKey = "mode";
        private const int SelfDrawMode = 1;
        private const int RonMode = 2;

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
                    "选择预判方式",
                    "self_draw",
                    new[]
                    {
                        new TalentChoiceOption("self_draw", "自摸", SelfDrawMode),
                        new TalentChoiceOption("ron", "荣和", RonMode)
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
                publicStateEventType: "foretell_outcome_mode",
                publicStateValue: mode);
        }

        public override int GetPostLegalFanBonus(TalentWinContext context)
        {
            if (!context.State.GetFlag(ChosenKey, TalentStateScope.Round)) return 0;

            int selectedMode = context.State.GetCounter(ModeKey, TalentStateScope.Round);
            bool matched = selectedMode == SelfDrawMode
                ? context.Facts.IsSelfDraw
                : selectedMode == RonMode && !context.Facts.IsSelfDraw;
            return matched ? 3 : 0;
        }

        public override string GetSnapshotPrivateStatusKey(TalentRuntimeState state)
        {
            if (!state.GetFlag(ChosenKey, TalentStateScope.Round)) return null;
            return state.GetCounter(ModeKey, TalentStateScope.Round) == SelfDrawMode
                ? "self_draw"
                : "ron";
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
