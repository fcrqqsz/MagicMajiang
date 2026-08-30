using MahjongGame.Core;
using MahjongGame.Core.Network;

internal static class OpponentKnownTileDisplayPolicyTests
{
    public static void Run(RegressionRunner runner)
    {
        KnownFacesKeepFaceUpOrientationAcrossDrawAndDiscard(runner);

        OpponentKnownTileDisplay display = OpponentKnownTileDisplayPolicy.Build(5, new[]
        {
            new PrivateKnownTileFace(Suit.Sou, 7, false),
            new PrivateKnownTileFace(Suit.Man, 3, true),
            new PrivateKnownTileFace(Suit.Man, 3, false),
            new PrivateKnownTileFace(Suit.Man, 3, false)
        });

        runner.Check(display.UnknownTileCount == 1
                     && display.KnownTiles.Count == 4,
            "opponent display keeps total concealed count while replacing known backs with faces");
        runner.Check(display.KnownTiles.Select(tile => (tile.Suit, tile.Value, tile.IsModified)).SequenceEqual(new[]
            {
                (Suit.Man, 3, true),
                (Suit.Man, 3, false),
                (Suit.Man, 3, false),
                (Suit.Sou, 7, false)
            }),
            "opponent known faces use stable suit/value sorting and retain duplicate tiles");

        OpponentKnownTileDisplay malformed = OpponentKnownTileDisplayPolicy.Build(2, new[]
        {
            new PrivateKnownTileFace(Suit.Pin, 9, false),
            new PrivateKnownTileFace(Suit.Pin, 8, false),
            new PrivateKnownTileFace(Suit.Pin, 7, false)
        });
        OpponentKnownTileDisplay negative = OpponentKnownTileDisplayPolicy.Build(-4, null);

        runner.Check(malformed.UnknownTileCount == 0 && malformed.KnownTiles.Count == 2,
            "opponent display clamps excessive private knowledge to the authoritative concealed count");
        runner.Check(negative.UnknownTileCount == 0 && negative.KnownTiles.Count == 0,
            "opponent display never produces negative tile counts from malformed snapshots");
    }

    private static void KnownFacesKeepFaceUpOrientationAcrossDrawAndDiscard(RegressionRunner runner)
    {
        var known = new[]
        {
            new PrivateKnownTileFace(Suit.Man, 2, false),
            new PrivateKnownTileFace(Suit.Pin, 7, true),
            new PrivateKnownTileFace(Suit.Sou, 4, false),
            new PrivateKnownTileFace(Suit.Wind, 1, false)
        };

        OpponentKnownTileDisplay afterReveal = OpponentKnownTileDisplayPolicy.Build(13, known);
        OpponentKnownTileDisplay afterDraw = OpponentKnownTileDisplayPolicy.Build(14, known);
        OpponentKnownTileDisplay afterDiscard = OpponentKnownTileDisplayPolicy.Build(13, known);

        runner.Check(afterReveal.GetVisualKindAt(8) == OpponentConcealedTileVisualKind.Back
                     && afterReveal.GetVisualKindAt(9) == OpponentConcealedTileVisualKind.KnownFace
                     && afterDraw.GetVisualKindAt(9) == OpponentConcealedTileVisualKind.Back
                     && afterDraw.GetVisualKindAt(10) == OpponentConcealedTileVisualKind.KnownFace
                     && afterDiscard.GetVisualKindAt(9) == OpponentConcealedTileVisualKind.KnownFace,
            "known opponent faces remain the face-up tail while authoritative draw/discard counts change");
        runner.Check(OpponentKnownTileVisualPolicy.GetLocalYaw(15f, OpponentConcealedTileVisualKind.Back) == 15f
                     && OpponentKnownTileVisualPolicy.GetLocalYaw(15f, OpponentConcealedTileVisualKind.KnownFace) == 195f,
            "known opponent faces rotate 180 degrees from the concealed back orientation toward the local observer");
    }
}
