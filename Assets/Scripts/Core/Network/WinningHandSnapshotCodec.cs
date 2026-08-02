using System;
using System.Collections.Generic;
using System.Linq;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.Core.Network
{
    /// <summary>Creates and owns the canonical copy/normalization rules for result-hand snapshots.</summary>
    public static class WinningHandSnapshotCodec
    {
        public static WinningHandSnapshot Create(
            IEnumerable<TileData> concealedHand,
            IEnumerable<Meld> melds,
            TileData winningTile,
            bool isSelfDraw)
        {
            var normalizedHand = (concealedHand ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .ToList();

            if (isSelfDraw && winningTile != null)
            {
                int winningIndex = FindWinningTileIndex(normalizedHand, winningTile);
                if (winningIndex >= 0) normalizedHand.RemoveAt(winningIndex);
            }

            return Normalize(new WinningHandSnapshot
            {
                concealedTiles = normalizedHand.Select(ToSimpleTile).ToArray(),
                winningTile = ToSimpleTile(winningTile),
                melds = (melds ?? Enumerable.Empty<Meld>())
                    .Where(meld => meld != null)
                    .Select(ToSnapshotMeld)
                    .ToArray()
            });
        }

        public static WinningHandSnapshot Clone(WinningHandSnapshot source)
        {
            if (source == null) return null;
            return new WinningHandSnapshot
            {
                concealedTiles = (source.concealedTiles ?? Array.Empty<SimpleTileData>())
                    .Select(CloneTile).ToArray(),
                winningTile = CloneTile(source.winningTile),
                melds = (source.melds ?? Array.Empty<SnapshotMeld>())
                    .Select(CloneMeld).ToArray()
            };
        }

        public static WinningHandSnapshot Normalize(WinningHandSnapshot source)
        {
            if (source == null) return null;

            var clone = Clone(source);
            clone.concealedTiles = clone.concealedTiles.Where(IsValidTile).ToArray();
            if (!IsValidTile(clone.winningTile)) clone.winningTile = null;
            clone.melds = clone.melds
                .Where(meld => meld != null && IsKnownMeldType(meld.meldType))
                .Select(NormalizeMeld)
                .Where(HasExpectedTileCount)
                .ToArray();
            return clone;
        }

        public static bool TryValidate(WinningHandSnapshot snapshot, out string error)
        {
            if (snapshot == null)
            {
                error = "Winning hand snapshot is missing.";
                return false;
            }

            if (!IsValidTile(snapshot.winningTile))
            {
                error = "Winning tile is missing or invalid.";
                return false;
            }

            if (snapshot.concealedTiles == null || snapshot.concealedTiles.Any(tile => !IsValidTile(tile)))
            {
                error = "Concealed tiles contain a missing or invalid tile.";
                return false;
            }

            if (snapshot.melds == null)
            {
                error = "Meld collection is missing.";
                return false;
            }

            foreach (var meld in snapshot.melds)
            {
                if (!TryValidateMeld(meld, out error)) return false;
            }

            error = null;
            return true;
        }

        private static int FindWinningTileIndex(List<TileData> hand, TileData winningTile)
        {
            if (!string.IsNullOrEmpty(winningTile.ID))
            {
                int exactIndex = hand.FindIndex(tile =>
                    !string.IsNullOrEmpty(tile.ID)
                    && string.Equals(tile.ID, winningTile.ID, StringComparison.Ordinal));
                if (exactIndex >= 0) return exactIndex;
            }

            return hand.FindLastIndex(tile =>
                tile.TileSuit == winningTile.TileSuit && tile.Value == winningTile.Value);
        }

        private static SnapshotMeld ToSnapshotMeld(Meld meld)
        {
            return new SnapshotMeld
            {
                meldType = (int)meld.Type,
                sourceSeatIndex = meld.SourcePlayerID,
                isConcealed = meld.IsConcealed,
                tileCount = meld.Tiles?.Count ?? 0,
                tiles = (meld.Tiles ?? new List<TileData>())
                    .Where(tile => tile != null)
                    .Select(ToSimpleTile)
                    .ToArray()
            };
        }

        private static SimpleTileData ToSimpleTile(TileData tile)
        {
            return tile == null ? null : new SimpleTileData(tile);
        }

        private static SimpleTileData CloneTile(SimpleTileData tile)
        {
            return tile == null ? null : new SimpleTileData
            {
                suit = tile.suit,
                value = tile.value,
                ownerId = tile.ownerId,
                isValid = tile.isValid
            };
        }

        private static SnapshotMeld CloneMeld(SnapshotMeld meld)
        {
            return meld == null ? null : new SnapshotMeld
            {
                meldType = meld.meldType,
                sourceSeatIndex = meld.sourceSeatIndex,
                isConcealed = meld.isConcealed,
                tileCount = meld.tileCount,
                tiles = (meld.tiles ?? Array.Empty<SimpleTileData>()).Select(CloneTile).ToArray()
            };
        }

        private static SnapshotMeld NormalizeMeld(SnapshotMeld meld)
        {
            meld.tiles = (meld.tiles ?? Array.Empty<SimpleTileData>()).Where(IsValidTile).ToArray();
            meld.tileCount = meld.tiles.Length;
            meld.isConcealed = (MeldType)meld.meldType == MeldType.Kan_Concealed;
            if (meld.sourceSeatIndex < -1 || meld.sourceSeatIndex > 3) meld.sourceSeatIndex = -1;
            return meld;
        }

        private static bool TryValidateMeld(SnapshotMeld meld, out string error)
        {
            if (meld == null || !IsKnownMeldType(meld.meldType))
            {
                error = "Meld is missing or has an unknown type.";
                return false;
            }

            if (meld.sourceSeatIndex < -1 || meld.sourceSeatIndex > 3)
            {
                error = "Meld source seat is outside the supported range.";
                return false;
            }

            if (meld.tiles == null || meld.tiles.Any(tile => !IsValidTile(tile)))
            {
                error = "Meld contains a missing or invalid tile.";
                return false;
            }

            if (!HasExpectedTileCount(meld) || meld.tileCount != meld.tiles.Length)
            {
                error = "Meld tile count does not match its type or tile array.";
                return false;
            }

            bool expectedConcealed = (MeldType)meld.meldType == MeldType.Kan_Concealed;
            if (meld.isConcealed != expectedConcealed)
            {
                error = "Meld concealed state does not match its type.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsValidTile(SimpleTileData tile)
        {
            if (tile == null || !tile.isValid || tile.suit < (int)Suit.Man || tile.suit > (int)Suit.Dragon)
                return false;

            switch ((Suit)tile.suit)
            {
                case Suit.Man:
                case Suit.Pin:
                case Suit.Sou:
                    return tile.value >= 1 && tile.value <= 9;
                case Suit.Wind:
                    return tile.value >= 1 && tile.value <= 4;
                case Suit.Dragon:
                    return tile.value >= 1 && tile.value <= 3;
                default:
                    return false;
            }
        }

        private static bool IsKnownMeldType(int meldType)
        {
            return meldType >= (int)MeldType.Chi && meldType <= (int)MeldType.Knitted;
        }

        private static bool HasExpectedTileCount(SnapshotMeld meld)
        {
            if (meld?.tiles == null || !IsKnownMeldType(meld.meldType)) return false;
            int expectedCount = IsKong((MeldType)meld.meldType) ? 4 : 3;
            return meld.tiles.Length == expectedCount;
        }

        private static bool IsKong(MeldType meldType)
        {
            return meldType == MeldType.Kan_Exposed
                || meldType == MeldType.Kan_Concealed
                || meldType == MeldType.Kan_Added;
        }
    }
}
