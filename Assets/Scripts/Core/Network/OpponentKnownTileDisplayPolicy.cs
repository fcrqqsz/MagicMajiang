using System;
using System.Collections.Generic;
using System.Linq;

namespace MahjongGame.Core.Network
{
    public enum OpponentConcealedTileVisualKind
    {
        Back,
        KnownFace
    }

    public sealed class OpponentKnownTileDisplay
    {
        public int UnknownTileCount { get; }
        public IReadOnlyList<PrivateKnownTileFace> KnownTiles { get; }

        public OpponentKnownTileDisplay(
            int unknownTileCount,
            IEnumerable<PrivateKnownTileFace> knownTiles)
        {
            UnknownTileCount = Math.Max(0, unknownTileCount);
            KnownTiles = (knownTiles ?? Enumerable.Empty<PrivateKnownTileFace>()).ToArray();
        }

        public OpponentConcealedTileVisualKind GetVisualKindAt(int index) =>
            index >= UnknownTileCount && index < UnknownTileCount + KnownTiles.Count
                ? OpponentConcealedTileVisualKind.KnownFace
                : OpponentConcealedTileVisualKind.Back;
    }

    public static class OpponentKnownTileVisualPolicy
    {
        public static float GetLocalYaw(float concealedBackYaw, OpponentConcealedTileVisualKind kind) =>
            kind == OpponentConcealedTileVisualKind.KnownFace
                ? concealedBackYaw + 180f
                : concealedBackYaw;
    }

    /// <summary>
    /// Pure presentation policy: authoritative concealed count remains the total, while known
    /// faces replace that many backs at the sorted tail of an opponent hand.
    /// </summary>
    public static class OpponentKnownTileDisplayPolicy
    {
        public static OpponentKnownTileDisplay Build(
            int concealedTileCount,
            IEnumerable<PrivateKnownTileFace> knownTiles)
        {
            int safeTotal = Math.Max(0, concealedTileCount);
            PrivateKnownTileFace[] sortedKnown = (knownTiles ?? Enumerable.Empty<PrivateKnownTileFace>())
                .Where(tile => tile != null)
                .OrderBy(tile => tile.Suit)
                .ThenBy(tile => tile.Value)
                .Take(safeTotal)
                .ToArray();
            return new OpponentKnownTileDisplay(safeTotal - sortedKnown.Length, sortedKnown);
        }
    }
}
