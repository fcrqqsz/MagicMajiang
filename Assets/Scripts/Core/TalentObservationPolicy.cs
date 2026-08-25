using System;

namespace MahjongGame.Core
{
    public enum TalentObservationMode
    {
        None,
        AlienatedTiles,
        TerminalOrHonorTiles
    }

    public sealed class TalentObservationState
    {
        public string ActiveTalentId { get; private set; }
        public TalentObservationMode ActiveMode { get; private set; }

        public void Toggle(string talentId)
        {
            TalentObservationMode mode = TalentObservationPolicy.ResolveMode(talentId);
            if (mode == TalentObservationMode.None)
            {
                ResetForRoundBoundary();
                return;
            }

            if (string.Equals(ActiveTalentId, talentId, StringComparison.Ordinal))
            {
                ResetForRoundBoundary();
                return;
            }

            ActiveTalentId = talentId;
            ActiveMode = mode;
        }

        public void ResetForRoundBoundary()
        {
            ActiveTalentId = null;
            ActiveMode = TalentObservationMode.None;
        }
    }

    public static class TalentObservationPolicy
    {
        public static TalentObservationMode ResolveMode(string talentId) => talentId switch
        {
            "chromatic_composition" => TalentObservationMode.AlienatedTiles,
            "fading_color" => TalentObservationMode.AlienatedTiles,
            "prune_the_excess" => TalentObservationMode.TerminalOrHonorTiles,
            _ => TalentObservationMode.None
        };

        public static bool IsInspectable(string talentId) =>
            ResolveMode(talentId) != TalentObservationMode.None;

        public static bool Matches(TalentObservationMode mode, TileData tile)
        {
            if (tile == null) return false;
            return mode switch
            {
                TalentObservationMode.AlienatedTiles => tile.IsModified,
                TalentObservationMode.TerminalOrHonorTiles => IsTerminalOrHonor(tile),
                _ => false
            };
        }

        private static bool IsTerminalOrHonor(TileData tile)
        {
            if (tile.TileSuit == Suit.Wind || tile.TileSuit == Suit.Dragon) return true;
            return (tile.TileSuit == Suit.Man || tile.TileSuit == Suit.Pin || tile.TileSuit == Suit.Sou)
                   && (tile.Value == 1 || tile.Value == 9);
        }
    }
}
