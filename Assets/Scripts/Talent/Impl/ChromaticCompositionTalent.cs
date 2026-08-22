using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core;

namespace MahjongGame.Talents.Impl
{
    [TalentRule("chromatic_composition", "异彩成章", "胡牌时，若牌组中至少有4张异化牌，每张异化牌+3番，最多计算8张。",
        TalentTier.Large, 26, TalentPhase.Scoring,
        StateScope = TalentStateScope.Match,
        RevealPolicy = TalentRevealPolicy.PublicAtMatchStart,
        SideboardPolicy = TalentSideboardPolicy.Flexible)]
    public sealed class ChromaticCompositionTalent : TalentRule
    {
        public override int GetPostLegalFanBonus(TalentWinContext context)
        {
            int modifiedCount = EnumeratePhysicalTiles(context.Facts)
                .Where(tile => tile.IsModified && !string.IsNullOrWhiteSpace(tile.Id))
                .Select(tile => tile.Id)
                .Distinct(StringComparer.Ordinal)
                .Count();
            return modifiedCount >= 4 ? Math.Min(modifiedCount, 8) * 3 : 0;
        }

        private static IEnumerable<TalentTileFacts> EnumeratePhysicalTiles(TalentWinFacts facts)
        {
            foreach (TalentTileFacts tile in facts.ConcealedHandTiles)
                yield return tile;
            foreach (TalentMeldFacts meld in facts.Melds)
            foreach (TalentTileFacts tile in meld.Tiles)
                yield return tile;
            if (facts.WinningTile != null) yield return facts.WinningTile;
        }
    }
}
