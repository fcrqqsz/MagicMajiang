using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("set_the_tone", "定调", "每小局首次主决策选择万、饼或条；本家以所选数牌胡牌时，最终结算额外+4番。",
        TalentTier.Medium, 12, TalentPhase.Scoring,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class SetTheToneTalent : TalentRule
    {
        private const string ChosenKey = "chosen";
        private const string SuitKey = "suit";

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
                Choice = CreateSuitChoice()
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
            if (!TryParseSuit(request?.ChoiceId, out Suit suit))
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidChoice);

            context.State.SetFlag(ChosenKey, true, TalentStateScope.Round);
            context.State.SetCounter(SuitKey, (int)suit, TalentStateScope.Round);
            return TalentActionResult.Success(
                effectApplied: true,
                publicStateEventType: "set_the_tone_suit",
                publicStateValue: (int)suit + 1);
        }

        public override int GetPostLegalFanBonus(TalentWinContext context)
        {
            if (!context.State.GetFlag(ChosenKey, TalentStateScope.Round)
                || context.Facts.WinningTile == null)
            {
                return 0;
            }

            Suit selectedSuit = (Suit)context.State.GetCounter(SuitKey, TalentStateScope.Round);
            return IsNumberedSuit(selectedSuit)
                   && context.Facts.WinningTile.Suit == selectedSuit
                ? 4
                : 0;
        }

        private static TalentChoiceSet CreateSuitChoice() =>
            new TalentChoiceSet(
                TalentChoiceKind.Suit,
                "选择定调花色",
                "man",
                new[]
                {
                    new TalentChoiceOption("man", "万", (int)Suit.Man),
                    new TalentChoiceOption("pin", "饼", (int)Suit.Pin),
                    new TalentChoiceOption("sou", "条", (int)Suit.Sou)
                });

        private static bool TryParseSuit(string choiceId, out Suit suit)
        {
            switch (choiceId)
            {
                case "man": suit = Suit.Man; return true;
                case "pin": suit = Suit.Pin; return true;
                case "sou": suit = Suit.Sou; return true;
                default: suit = Suit.Man; return false;
            }
        }

        private static bool IsNumberedSuit(Suit suit) =>
            suit == Suit.Man || suit == Suit.Pin || suit == Suit.Sou;
    }
}
