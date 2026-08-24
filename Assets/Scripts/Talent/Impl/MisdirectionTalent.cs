using System.Collections.Generic;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("misdirection", "障眼法", "每小局1次，在自己的摸牌出牌阶段装备；下一次弃牌按万→饼→条、东→南→西→北→中→发→白→东变化。",
        TalentTier.Small, 8, TalentPhase.OnDiscard,
        StateScope = TalentStateScope.Round,
        ActivationWindow = TalentActivationWindow.MainTurn,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class MisdirectionTalent : TalentRule
    {
        private const string UsedKey = "used";
        private const string ArmedKey = "armed";

        public override void GetAvailableActions(
            TalentActionQueryContext context,
            List<TalentActionOption> output)
        {
            if (context.RequiredWindow == TalentActivationWindow.MainTurn
                && !context.State.GetFlag(UsedKey, TalentStateScope.Round))
            {
                output.Add(new TalentActionOption
                {
                    TalentId = Id,
                    AiPriority = 50
                });
            }
        }

        public override TalentActionResult TryActivate(
            TalentActivationContext context,
            TalentActionRequest request)
        {
            if (context.RequiredWindow != TalentActivationWindow.MainTurn)
                return TalentActionResult.NotSupported();
            if (context.State.GetFlag(UsedKey, TalentStateScope.Round))
                return TalentActionResult.Reject(TalentActionErrorCodes.AlreadyUsedThisTurn);

            context.State.SetFlag(UsedKey, true, TalentStateScope.Round);
            context.State.SetFlag(ArmedKey, true, TalentStateScope.Round);
            return TalentActionResult.Success(effectApplied: true);
        }

        public override TileData OnDiscard(TalentContext context, TileData tile)
        {
            if (tile == null
                || !context.IsOwnersTurn
                || !context.State.GetFlag(ArmedKey, TalentStateScope.Round))
            {
                return tile;
            }

            Transform(tile);
            tile.IsModified = true;
            tile.SpecialEffectID = Id;
            context.State.SetFlag(ArmedKey, false, TalentStateScope.Round);
            return tile;
        }

        public override void OnRoundEnded(TalentRoundContext context, TalentRoundOutcome outcome)
        {
            context.State.SetFlag(ArmedKey, false, TalentStateScope.Round);
        }

        private static void Transform(TileData tile)
        {
            switch (tile.TileSuit)
            {
                case Suit.Man:
                    tile.TileSuit = Suit.Pin;
                    return;
                case Suit.Pin:
                    tile.TileSuit = Suit.Sou;
                    return;
                case Suit.Sou:
                    tile.TileSuit = Suit.Man;
                    return;
                case Suit.Wind:
                    if (tile.Value >= 1 && tile.Value <= 3)
                    {
                        tile.Value++;
                        return;
                    }
                    if (tile.Value == 4)
                    {
                        tile.TileSuit = Suit.Dragon;
                        tile.Value = 1;
                    }
                    return;
                case Suit.Dragon:
                    if (tile.Value >= 1 && tile.Value <= 2)
                    {
                        tile.Value++;
                        return;
                    }
                    if (tile.Value == 3)
                    {
                        tile.TileSuit = Suit.Wind;
                        tile.Value = 1;
                    }
                    return;
            }
        }
    }
}
