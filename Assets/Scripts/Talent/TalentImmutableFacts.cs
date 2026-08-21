using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MahjongGame.Core;
using MahjongGame.Core.Network;

namespace MahjongGame.Talents
{
    public sealed class TalentTileFacts
    {
        public Suit Suit { get; }
        public int Value { get; }
        public string Id { get; }
        public int OriginalOwnerId { get; }
        public bool IsModified { get; }
        public string SpecialEffectId { get; }

        internal TalentTileFacts(TileData tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            Suit = tile.TileSuit;
            Value = tile.Value;
            Id = tile.ID;
            OriginalOwnerId = tile.OriginalOwnerID;
            IsModified = tile.IsModified;
            SpecialEffectId = tile.SpecialEffectID;
        }

        public static TalentTileFacts FromTile(TileData tile) => new TalentTileFacts(tile);
    }

    public sealed class TalentMeldFacts
    {
        private readonly ReadOnlyCollection<TalentTileFacts> _tiles;

        public MeldType Type { get; }
        public int SourceSeatIndex { get; }
        public bool IsConcealed { get; }
        public IReadOnlyList<TalentTileFacts> Tiles => _tiles;

        internal TalentMeldFacts(Meld meld)
        {
            if (meld == null) throw new ArgumentNullException(nameof(meld));
            Type = meld.Type;
            SourceSeatIndex = meld.SourcePlayerID;
            IsConcealed = meld.IsConcealed;
            _tiles = Array.AsReadOnly((meld.Tiles ?? new List<TileData>())
                .Where(tile => tile != null)
                .Select(tile => new TalentTileFacts(tile))
                .ToArray());
        }
    }

    public sealed class TalentInitialHandFacts
    {
        private readonly ReadOnlyCollection<TalentTileFacts> _tiles;

        public int OwnerSeatIndex { get; }
        public int RoundNumber { get; }
        public IReadOnlyList<TalentTileFacts> Tiles => _tiles;

        internal TalentInitialHandFacts(
            GameSession session,
            int ownerSeatIndex,
            IEnumerable<TileData> tiles)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (ownerSeatIndex < 0 || ownerSeatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(ownerSeatIndex));

            OwnerSeatIndex = ownerSeatIndex;
            RoundNumber = session.TotalRoundsPlayed + 1;
            _tiles = Array.AsReadOnly((tiles ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .Select(tile => new TalentTileFacts(tile))
                .ToArray());
        }
    }

    public sealed class TalentWinFacts
    {
        private readonly ReadOnlyCollection<TalentTileFacts> _concealedHandTiles;
        private readonly ReadOnlyCollection<TalentMeldFacts> _melds;

        public int WinnerSeatIndex { get; }
        public int? DiscarderSeatIndex { get; }
        public IReadOnlyList<TalentTileFacts> ConcealedHandTiles => _concealedHandTiles;
        public IReadOnlyList<TalentMeldFacts> Melds => _melds;
        public TalentTileFacts WinningTile { get; }
        public bool IsSelfDraw { get; }
        public bool IsRobKong { get; }
        public bool IsKongReplacement { get; }
        public WindDirection RoundWind { get; }
        public WindDirection SeatWind { get; }
        public int RoundNumber { get; }

        private TalentWinFacts(
            GameSession session,
            int winnerSeatIndex,
            int? discarderSeatIndex,
            IEnumerable<TileData> concealedHandTiles,
            IEnumerable<Meld> melds,
            TileData winningTile,
            bool isSelfDraw,
            bool isRobKong,
            bool isKongReplacement)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (winnerSeatIndex < 0 || winnerSeatIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(winnerSeatIndex));
            if (discarderSeatIndex.HasValue
                && (discarderSeatIndex.Value < 0 || discarderSeatIndex.Value > 3))
            {
                throw new ArgumentOutOfRangeException(nameof(discarderSeatIndex));
            }
            if (winningTile == null) throw new ArgumentNullException(nameof(winningTile));

            WinnerSeatIndex = winnerSeatIndex;
            DiscarderSeatIndex = discarderSeatIndex;
            _concealedHandTiles = Array.AsReadOnly((concealedHandTiles ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .Select(tile => new TalentTileFacts(tile))
                .ToArray());
            _melds = Array.AsReadOnly((melds ?? Enumerable.Empty<Meld>())
                .Where(meld => meld != null)
                .Select(meld => new TalentMeldFacts(meld))
                .ToArray());
            WinningTile = new TalentTileFacts(winningTile);
            IsSelfDraw = isSelfDraw;
            IsRobKong = isRobKong;
            IsKongReplacement = isKongReplacement;
            RoundWind = session.PrevalentWind;
            SeatWind = session.GetSeatWind(winnerSeatIndex);
            RoundNumber = session.TotalRoundsPlayed + 1;
        }

        internal static TalentWinFacts Create(
            GameSession session,
            int winnerSeatIndex,
            int? discarderSeatIndex,
            IEnumerable<TileData> concealedHandTiles,
            IEnumerable<Meld> melds,
            TileData winningTile,
            bool isSelfDraw,
            bool isRobKong,
            bool isKongReplacement) => new TalentWinFacts(
                session,
                winnerSeatIndex,
                discarderSeatIndex,
                concealedHandTiles,
                melds,
                winningTile,
                isSelfDraw,
                isRobKong,
                isKongReplacement);
    }
}
