using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("suit_convergence", "归色", "每小局首次主决策选择一种花色；接下来摸到的前2张其他花色数牌变为该花色。",
        TalentTier.Small, 8,
        TalentPhase.InitialHandCompleted,
        TalentPhase.OnDraw,
        TalentPhase.ActionValidation,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class SuitConvergenceTalent : TalentRule
    {
        private const string DefaultSuitKey = "default_suit";
        private const string TargetSuitKey = "target_suit";
        private const string RemainingKey = "remaining";
        private const string ChosenKey = "chosen";

        public override void OnInitialHandCompleted(TalentInitialHandContext context)
        {
            Suit defaultSuit = new[] { Suit.Man, Suit.Pin, Suit.Sou }
                .Select((suit, index) => new
                {
                    Suit = suit,
                    Index = index,
                    Count = context.Facts.Tiles.Count(tile => tile.Suit == suit)
                })
                .OrderByDescending(candidate => candidate.Count)
                .ThenBy(candidate => candidate.Index)
                .First()
                .Suit;
            context.State.SetCounter(DefaultSuitKey, (int)defaultSuit, TalentStateScope.Round);
        }

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (!context.IsFirstMainDecisionOfRound
                || context.State.GetFlag(ChosenKey, TalentStateScope.Round))
            {
                return;
            }

            Suit defaultSuit = (Suit)context.State.GetCounter(DefaultSuitKey, TalentStateScope.Round);
            output.Add(new TalentActionOption
            {
                TalentId = Id,
                AiPriority = 300,
                Choice = CreateSuitChoice(defaultSuit)
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
            if (!TryParseSuit(request?.ChoiceId, out Suit targetSuit))
                return TalentActionResult.Reject(TalentActionErrorCodes.InvalidChoice);

            context.State.SetCounter(TargetSuitKey, (int)targetSuit, TalentStateScope.Round);
            context.State.SetCounter(RemainingKey, 2, TalentStateScope.Round);
            context.State.SetFlag(ChosenKey, true, TalentStateScope.Round);
            return TalentActionResult.Success(
                effectApplied: true,
                publicStateEventType: GetPublicEventType(targetSuit),
                publicStateValue: 2);
        }

        public override TileData OnDraw(TalentContext context, TileData tile)
        {
            if (tile == null
                || !context.IsOwnersTurn
                || !context.State.GetFlag(ChosenKey, TalentStateScope.Round))
            {
                return tile;
            }

            int remaining = context.State.GetCounter(RemainingKey, TalentStateScope.Round);
            Suit targetSuit = (Suit)context.State.GetCounter(TargetSuitKey, TalentStateScope.Round);
            if (remaining <= 0 || !IsSuited(tile.TileSuit) || tile.TileSuit == targetSuit)
                return tile;

            tile.TileSuit = targetSuit;
            tile.IsModified = true;
            tile.SpecialEffectID = Id;
            remaining--;
            context.State.SetCounter(RemainingKey, remaining, TalentStateScope.Round);
            context.EmitPublic(GetPublicEventType(targetSuit), remaining);
            return tile;
        }

        public override int GetSnapshotPrivateValue(TalentRuntimeState state) =>
            state.GetCounter(RemainingKey, TalentStateScope.Round);

        public override string GetSnapshotPrivateStatusKey(TalentRuntimeState state)
        {
            if (!state.GetFlag(ChosenKey, TalentStateScope.Round)) return null;
            return GetChoiceId((Suit)state.GetCounter(TargetSuitKey, TalentStateScope.Round));
        }

        private static TalentChoiceSet CreateSuitChoice(Suit defaultSuit) =>
            new TalentChoiceSet(
                TalentChoiceKind.Suit,
                "选择归色花色",
                GetChoiceId(defaultSuit),
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

        private static string GetChoiceId(Suit suit) => suit switch
        {
            Suit.Pin => "pin",
            Suit.Sou => "sou",
            _ => "man"
        };

        private static string GetPublicEventType(Suit suit) => suit switch
        {
            Suit.Pin => "suit_convergence_pin",
            Suit.Sou => "suit_convergence_sou",
            _ => "suit_convergence_man"
        };

        private static bool IsSuited(Suit suit) =>
            suit == Suit.Man || suit == Suit.Pin || suit == Suit.Sou;
    }
}
