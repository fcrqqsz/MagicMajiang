using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("travel_light", "轻装上阵", "起手牌发完后，将起手牌中所有数牌1变为2、9变为8。",
        TalentTier.Medium, 16, TalentPhase.InitialHandCompleted,
        StateScope = TalentStateScope.Round,
        RevealPolicy = TalentRevealPolicy.HiddenUntilPublicEffect,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class TravelLightTalent : TalentRule
    {
        public override TalentScope Scope => TalentScope.Global;

        public override void OnInitialHandCompleted(TalentInitialHandContext context)
        {
            foreach (TalentTileFacts tile in context.Facts.Tiles)
            {
                if (!IsSuited(tile.Suit) || (tile.Value != 1 && tile.Value != 9)) continue;
                context.TryTransformTile(
                    tile.Id,
                    tile.Suit,
                    tile.Value == 1 ? 2 : 8);
            }
        }

        private static bool IsSuited(Suit suit) =>
            suit == Suit.Man || suit == Suit.Pin || suit == Suit.Sou;
    }
}
