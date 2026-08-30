using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Network
{
    public sealed class PrivateKnownTileFace
    {
        public Suit Suit { get; }
        public int Value { get; }
        public bool IsModified { get; }

        public PrivateKnownTileFace(Suit suit, int value, bool isModified)
        {
            Suit = suit;
            Value = value;
            IsModified = isModified;
        }
    }

    public sealed class PrivateKnownHandProjection
    {
        public int TargetSeatIndex { get; }
        public IReadOnlyList<PrivateKnownTileFace> Tiles { get; }

        public PrivateKnownHandProjection(int targetSeatIndex, IEnumerable<PrivateKnownTileFace> tiles)
        {
            TargetSeatIndex = targetSeatIndex;
            Tiles = (tiles ?? Enumerable.Empty<PrivateKnownTileFace>()).ToArray();
        }
    }

    public sealed class PrivateKnownTilesProjection
    {
        public int ViewerSeatIndex { get; }
        public IReadOnlyList<PrivateKnownHandProjection> Hands { get; }

        public PrivateKnownTilesProjection(int viewerSeatIndex, IEnumerable<PrivateKnownHandProjection> hands)
        {
            ViewerSeatIndex = viewerSeatIndex;
            Hands = (hands ?? Enumerable.Empty<PrivateKnownHandProjection>()).ToArray();
        }
    }

    /// <summary>
    /// Server-only per-viewer memory of privately observed physical tiles. Public projections
    /// deliberately contain no physical identity, owner provenance, or internal effect id.
    /// </summary>
    public sealed class PrivateTileKnowledgeTracker
    {
        private sealed class KnownEntry
        {
            public string InstanceId;
            public Suit Suit;
            public int Value;
            public bool IsModified;
            public long Sequence;
        }

        private readonly int _seatCount;
        private readonly Dictionary<int, List<KnownEntry>> _wallByViewer = new Dictionary<int, List<KnownEntry>>();
        private readonly Dictionary<int, Dictionary<int, List<KnownEntry>>> _handsByViewer =
            new Dictionary<int, Dictionary<int, List<KnownEntry>>>();
        private long _nextSequence;

        public PrivateTileKnowledgeTracker(int seatCount)
        {
            if (seatCount <= 0) throw new ArgumentOutOfRangeException(nameof(seatCount));
            _seatCount = seatCount;
        }

        public void ObserveWallTiles(int viewerSeatIndex, IEnumerable<TileData> tiles)
        {
            ValidateSeat(viewerSeatIndex, nameof(viewerSeatIndex));
            List<KnownEntry> wall = GetOrCreateWall(viewerSeatIndex);
            foreach (TileData tile in tiles ?? Enumerable.Empty<TileData>())
                Upsert(wall, tile);
        }

        public void ObserveConcealedHand(
            int viewerSeatIndex,
            int targetSeatIndex,
            IEnumerable<TileData> tiles)
        {
            ValidateSeat(viewerSeatIndex, nameof(viewerSeatIndex));
            ValidateSeat(targetSeatIndex, nameof(targetSeatIndex));
            if (viewerSeatIndex == targetSeatIndex) return;

            List<KnownEntry> hand = GetOrCreateHand(viewerSeatIndex, targetSeatIndex);
            foreach (TileData tile in tiles ?? Enumerable.Empty<TileData>())
                Upsert(hand, tile);
        }

        public IReadOnlyList<int> ProcessDraw(
            int targetSeatIndex,
            TileData wallTileBeforeTalents,
            TileData drawnTileAfterTalents)
        {
            ValidateSeat(targetSeatIndex, nameof(targetSeatIndex));
            var changedViewers = new List<int>();
            if (wallTileBeforeTalents == null || string.IsNullOrWhiteSpace(wallTileBeforeTalents.ID))
                return changedViewers;

            foreach (int viewerSeatIndex in _wallByViewer.Keys.ToArray())
            {
                List<KnownEntry> wall = _wallByViewer[viewerSeatIndex];
                int index = wall.FindIndex(entry => SamePhysicalTile(entry, wallTileBeforeTalents));
                if (index < 0) continue;

                wall.RemoveAt(index);
                if (viewerSeatIndex != targetSeatIndex
                    && SameVisibleFace(wallTileBeforeTalents, drawnTileAfterTalents))
                {
                    Upsert(GetOrCreateHand(viewerSeatIndex, targetSeatIndex), drawnTileAfterTalents);
                }
                changedViewers.Add(viewerSeatIndex);
            }

            return changedViewers;
        }

        public IReadOnlyList<int> ProcessConcealedTilesBecamePublic(
            int targetSeatIndex,
            IEnumerable<TileData> publicTiles)
        {
            ValidateSeat(targetSeatIndex, nameof(targetSeatIndex));
            var changedViewers = new List<int>();
            TileData[] tiles = (publicTiles ?? Enumerable.Empty<TileData>())
                .Where(tile => tile != null)
                .ToArray();
            if (tiles.Length == 0) return changedViewers;

            foreach (var viewerPair in _handsByViewer)
            {
                if (!viewerPair.Value.TryGetValue(targetSeatIndex, out List<KnownEntry> hand)) continue;
                bool changed = false;
                foreach (TileData tile in tiles)
                {
                    int index = hand.FindIndex(entry => SameVisibleFace(entry, tile, requireModifiedMatch: true));
                    if (index < 0)
                        index = hand.FindIndex(entry => SameVisibleFace(entry, tile, requireModifiedMatch: false));
                    if (index < 0) continue;
                    hand.RemoveAt(index);
                    changed = true;
                }
                if (changed) changedViewers.Add(viewerPair.Key);
            }

            return changedViewers;
        }

        public IReadOnlyList<int> ProcessHiddenTileMutation(
            int targetSeatIndex,
            TileData before,
            TileData after)
        {
            ValidateSeat(targetSeatIndex, nameof(targetSeatIndex));
            var changedViewers = new List<int>();
            if (before == null
                || string.IsNullOrWhiteSpace(before.ID)
                || SameVisibleFace(before, after))
            {
                return changedViewers;
            }

            foreach (var viewerPair in _handsByViewer)
            {
                if (!viewerPair.Value.TryGetValue(targetSeatIndex, out List<KnownEntry> hand)) continue;
                int removed = hand.RemoveAll(entry => SamePhysicalTile(entry, before));
                if (removed > 0) changedViewers.Add(viewerPair.Key);
            }
            return changedViewers;
        }

        /// <summary>
        /// Atomically handles a concealed tile passing through a hidden mutation pipeline and
        /// then becoming public. A changed physical tile only invalidates its old observation;
        /// its new face must not consume a different known tile that remains concealed.
        /// </summary>
        public IReadOnlyList<int> ProcessHiddenPipelineTileBecamePublic(
            int targetSeatIndex,
            TileData before,
            TileData after)
        {
            return SameVisibleFace(before, after)
                ? ProcessConcealedTilesBecamePublic(targetSeatIndex, new[] { after })
                : ProcessHiddenTileMutation(targetSeatIndex, before, after);
        }

        public IReadOnlyList<TileData> GetObservedWallTiles(int viewerSeatIndex)
        {
            ValidateSeat(viewerSeatIndex, nameof(viewerSeatIndex));
            if (!_wallByViewer.TryGetValue(viewerSeatIndex, out List<KnownEntry> wall))
                return Array.Empty<TileData>();
            return wall.OrderBy(entry => entry.Sequence).Select(ToServerTileCopy).ToArray();
        }

        public PrivateKnownTilesProjection GetProjection(int viewerSeatIndex)
        {
            ValidateSeat(viewerSeatIndex, nameof(viewerSeatIndex));
            if (!_handsByViewer.TryGetValue(viewerSeatIndex, out Dictionary<int, List<KnownEntry>> hands))
                return new PrivateKnownTilesProjection(viewerSeatIndex, Array.Empty<PrivateKnownHandProjection>());

            PrivateKnownHandProjection[] projectedHands = hands
                .Where(pair => pair.Value.Count > 0)
                .OrderBy(pair => pair.Key)
                .Select(pair => new PrivateKnownHandProjection(
                    pair.Key,
                    pair.Value
                        .OrderBy(entry => entry.Suit)
                        .ThenBy(entry => entry.Value)
                        .ThenBy(entry => entry.Sequence)
                        .Select(entry => new PrivateKnownTileFace(entry.Suit, entry.Value, entry.IsModified))))
                .ToArray();
            return new PrivateKnownTilesProjection(viewerSeatIndex, projectedHands);
        }

        public void ClearRound()
        {
            _wallByViewer.Clear();
            _handsByViewer.Clear();
            _nextSequence = 0;
        }

        private List<KnownEntry> GetOrCreateWall(int viewerSeatIndex)
        {
            if (!_wallByViewer.TryGetValue(viewerSeatIndex, out List<KnownEntry> wall))
            {
                wall = new List<KnownEntry>();
                _wallByViewer[viewerSeatIndex] = wall;
            }
            return wall;
        }

        private List<KnownEntry> GetOrCreateHand(int viewerSeatIndex, int targetSeatIndex)
        {
            if (!_handsByViewer.TryGetValue(viewerSeatIndex, out Dictionary<int, List<KnownEntry>> hands))
            {
                hands = new Dictionary<int, List<KnownEntry>>();
                _handsByViewer[viewerSeatIndex] = hands;
            }
            if (!hands.TryGetValue(targetSeatIndex, out List<KnownEntry> hand))
            {
                hand = new List<KnownEntry>();
                hands[targetSeatIndex] = hand;
            }
            return hand;
        }

        private void Upsert(List<KnownEntry> entries, TileData tile)
        {
            if (tile == null || string.IsNullOrWhiteSpace(tile.ID)) return;
            KnownEntry existing = entries.FirstOrDefault(entry => SamePhysicalTile(entry, tile));
            if (existing == null)
            {
                existing = new KnownEntry { InstanceId = tile.ID, Sequence = ++_nextSequence };
                entries.Add(existing);
            }
            existing.Suit = tile.TileSuit;
            existing.Value = tile.Value;
            existing.IsModified = tile.IsModified;
        }

        private static bool SamePhysicalTile(KnownEntry entry, TileData tile) =>
            entry != null
            && tile != null
            && !string.IsNullOrWhiteSpace(entry.InstanceId)
            && string.Equals(entry.InstanceId, tile.ID, StringComparison.Ordinal);

        private static bool SameVisibleFace(TileData first, TileData second) =>
            first != null
            && second != null
            && first.TileSuit == second.TileSuit
            && first.Value == second.Value
            && first.IsModified == second.IsModified;

        private static bool SameVisibleFace(KnownEntry entry, TileData tile, bool requireModifiedMatch) =>
            entry != null
            && tile != null
            && entry.Suit == tile.TileSuit
            && entry.Value == tile.Value
            && (!requireModifiedMatch || entry.IsModified == tile.IsModified);

        private static TileData ToServerTileCopy(KnownEntry entry) => new TileData(entry.Suit, entry.Value, 0)
        {
            ID = entry.InstanceId,
            IsModified = entry.IsModified,
            SpecialEffectID = null
        };

        private void ValidateSeat(int seatIndex, string parameterName)
        {
            if (seatIndex < 0 || seatIndex >= _seatCount)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
