using System;
using MahjongGame.Core;

internal static class GameTableLayoutPolicyTests
{
    public static void Run(RegressionRunner runner)
    {
        RunCenteredHandTests(runner);
        RunCenteredRiverTests(runner);
        RunSeatRotationTests(runner);
        RunSeatAnchorTests(runner);
        RunSurfaceElevationTests(runner);
        RunFourKongSpanTests(runner);
        RunHudGeometryTests(runner);
        RunCentralHudSafeZoneTests(runner);
        RunWaitStripCapacityTests(runner);
        RunWaitStripDynamicWidthTests(runner);
        RunWaitStripSafeZoneTests(runner);
    }

    private static void RunCenteredHandTests(RegressionRunner runner)
    {
        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(0, 0, 0.84f, false, 0.32f), 0f)
            && NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(1, 0, 0.84f, true, 0.32f), 0f),
            "Table layout regression: empty and one-tile hands must remain at the seat anchor.");

        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(13, 0, 0.84f, false, 0.32f), -5.04f)
            && NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(13, 6, 0.84f, false, 0.32f), 0f)
            && NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(13, 12, 0.84f, false, 0.32f), 5.04f),
            "Table layout regression: a 13-tile hand must stay centered on its seat anchor.");

        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(14, 0, 0.84f, false, 0.32f), -5.46f)
            && NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(14, 13, 0.84f, false, 0.32f), 5.46f),
            "Table layout regression: an even 14-tile hand must have symmetric outer tiles.");

        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(14, 0, 0.84f, true, 0.32f), -5.62f)
            && NearlyEqual(GameTableLayoutPolicy.GetCenteredHandX(14, 13, 0.84f, true, 0.32f), 5.62f),
            "Table layout regression: the drawn-tile gap must be included in hand centering.");
    }

    private static void RunCenteredRiverTests(RegressionRunner runner)
    {
        TableLayoutPoint first = GameTableLayoutPolicy.GetRiverLocalPosition(0, 6, 0.93f, 1.26f);
        TableLayoutPoint sixth = GameTableLayoutPolicy.GetRiverLocalPosition(5, 6, 0.93f, 1.26f);
        TableLayoutPoint seventh = GameTableLayoutPolicy.GetRiverLocalPosition(6, 6, 0.93f, 1.26f);
        TableLayoutPoint fourthRow = GameTableLayoutPolicy.GetRiverLocalPosition(18, 6, 0.93f, 1.26f);

        runner.Check(
            NearlyEqual(first.X, -2.325f) && NearlyEqual(sixth.X, 2.325f)
            && NearlyEqual(first.Z, 0f) && NearlyEqual(sixth.Z, 0f),
            "Table layout regression: six river columns must be symmetric around the river anchor.");
        runner.Check(
            NearlyEqual(seventh.X, -2.325f) && NearlyEqual(seventh.Z, -1.26f)
            && NearlyEqual(fourthRow.Z, -3.78f),
            "Table layout regression: river rows must grow on local negative Z through the fourth row.");
    }

    private static void RunSeatRotationTests(RegressionRunner runner)
    {
        var point = new TableLayoutPoint(2f, 3f);
        TableLayoutPoint bottom = GameTableLayoutPolicy.RotateForSeat(point, 0);
        TableLayoutPoint right = GameTableLayoutPolicy.RotateForSeat(point, 1);
        TableLayoutPoint top = GameTableLayoutPolicy.RotateForSeat(point, 2);
        TableLayoutPoint left = GameTableLayoutPolicy.RotateForSeat(point, 3);

        runner.Check(
            NearlyEqual(bottom.X, 2f) && NearlyEqual(bottom.Z, 3f)
            && NearlyEqual(right.X, -3f) && NearlyEqual(right.Z, 2f)
            && NearlyEqual(top.X, -2f) && NearlyEqual(top.Z, -3f)
            && NearlyEqual(left.X, 3f) && NearlyEqual(left.Z, -2f),
            "Table layout regression: seat rotation must map local layout consistently around the table.");
    }

    private static void RunWaitStripCapacityTests(RegressionRunner runner)
    {
        runner.Check(
            GameTableLayoutPolicy.CanFitWaitItems(820f, 80f, 24f, 13, 52f),
            "Table layout regression: the wait strip must show all thirteen waits without scrolling.");
        runner.Check(
            !GameTableLayoutPolicy.CanFitWaitItems(820f, 80f, 24f, 14, 52f),
            "Table layout regression: the wait strip capacity check must reject content wider than the panel.");
    }

    private static void RunWaitStripDynamicWidthTests(RegressionRunner runner)
    {
        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetWaitHintWidth(1, 54f, 88f, 196f, 790f), 196f)
            && NearlyEqual(GameTableLayoutPolicy.GetWaitHintWidth(5, 54f, 88f, 196f, 790f), 358f)
            && NearlyEqual(GameTableLayoutPolicy.GetWaitHintWidth(13, 54f, 88f, 196f, 790f), 790f),
            "Table layout regression: the wait strip width must grow with one, five, and thirteen wait items.");

        runner.Check(
            NearlyEqual(GameTableLayoutPolicy.GetWaitHintWidth(0, 54f, 88f, 196f, 790f), 196f)
            && NearlyEqual(GameTableLayoutPolicy.GetWaitHintWidth(14, 54f, 88f, 196f, 790f), 790f),
            "Table layout regression: the wait strip width must stay within its minimum and maximum bounds.");
    }

    private static void RunSeatAnchorTests(RegressionRunner runner)
    {
        TableSeatLayout bottom = GameTableLayoutPolicy.GetSeatLayout(0, 10f, 3.15f);
        TableSeatLayout right = GameTableLayoutPolicy.GetSeatLayout(1, 10f, 3.15f);
        TableSeatLayout top = GameTableLayoutPolicy.GetSeatLayout(2, 10f, 3.15f);
        TableSeatLayout left = GameTableLayoutPolicy.GetSeatLayout(3, 10f, 3.15f);

        runner.Check(
            NearlyEqual(bottom.HandRoot.X, 0f) && NearlyEqual(bottom.HandRoot.Z, -10f)
            && NearlyEqual(bottom.RiverRoot.X, 0f) && NearlyEqual(bottom.RiverRoot.Z, -3.15f)
            && NearlyEqual(bottom.YawDegrees, 0f),
            "Table layout regression: the local seat anchors must occupy the bottom side.");
        runner.Check(
            NearlyEqual(right.HandRoot.X, 10f) && NearlyEqual(right.HandRoot.Z, 0f)
            && NearlyEqual(right.RiverRoot.X, 3.15f) && NearlyEqual(right.RiverRoot.Z, 0f)
            && NearlyEqual(right.YawDegrees, -90f),
            "Table layout regression: the right seat anchors must face inward from the right side.");
        runner.Check(
            NearlyEqual(top.HandRoot.X, 0f) && NearlyEqual(top.HandRoot.Z, 10f)
            && NearlyEqual(top.RiverRoot.X, 0f) && NearlyEqual(top.RiverRoot.Z, 3.15f)
            && NearlyEqual(top.YawDegrees, 180f),
            "Table layout regression: the top seat anchors must face inward from the far side.");
        runner.Check(
            NearlyEqual(left.HandRoot.X, -10f) && NearlyEqual(left.HandRoot.Z, 0f)
            && NearlyEqual(left.RiverRoot.X, -3.15f) && NearlyEqual(left.RiverRoot.Z, 0f)
            && NearlyEqual(left.YawDegrees, 90f),
            "Table layout regression: the left seat anchors must face inward from the left side.");
    }

    private static void RunWaitStripSafeZoneTests(RegressionRunner runner)
    {
        runner.Check(
            WaitStripSitsBetweenFourthRiverRowAndHand(1200f, 675f)
            && WaitStripSitsBetweenFourthRiverRowAndHand(1200f, 750f),
            "Table layout regression: the wait strip must remain between the fourth local river row and the hand at 1920x1080 and 960x600.");

        runner.Check(
            ActionBandClearsWaitStrip(675f) && ActionBandClearsWaitStrip(750f),
            "Table layout regression: the compact action band must stay above the wait strip.");
    }

    private static bool WaitStripSitsBetweenFourthRiverRowAndHand(float logicalWidth, float logicalHeight)
    {
        TableVerticalBand waitBand = GameTableLayoutPolicy.GetBottomAnchoredBand(
            logicalHeight,
            0.12f,
            96f);

        TableScreenPoint fourthRiverRow = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(0f, -3.25f - 3f * 1.26f),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);
        TableScreenPoint hand = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(0f, -10f),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);

        return fourthRiverRow.Y < waitBand.Top && waitBand.Bottom < hand.Y;
    }

    private static bool ActionBandClearsWaitStrip(float logicalHeight)
    {
        const float actionBottom = 194f;
        float actionBottomY = logicalHeight - actionBottom;
        TableVerticalBand waitBand = GameTableLayoutPolicy.GetBottomAnchoredBand(
            logicalHeight,
            0.12f,
            96f);
        return actionBottomY < waitBand.Top;
    }

    private static void RunHudGeometryTests(RegressionRunner runner)
    {
        TableVerticalBand hudBand = GameTableLayoutPolicy.GetCenteredVerticalBand(306.45f, 104f);
        runner.Check(
            NearlyEqual(hudBand.Top, 254.45f) && NearlyEqual(hudBand.Bottom, 358.45f),
            "Table layout regression: the readable non-rotated HUD must preserve its full configured height around the center.");

        TableVerticalBand waitBand = GameTableLayoutPolicy.GetBottomAnchoredBand(675f, 0.12f, 96f);
        runner.Check(
            NearlyEqual(waitBand.Top, 498f) && NearlyEqual(waitBand.Bottom, 594f),
            "Table layout regression: percentage-anchored wait strips must resolve against viewport height.");
    }

    private static void RunSurfaceElevationTests(RegressionRunner runner)
    {
        float handRootY = GameTableLayoutPolicy.GetSurfaceAlignedRootY(-0.26f, -0.627f, 0.03f);
        float meldLocalY = GameTableLayoutPolicy.GetSurfaceAlignedChildLocalY(
            handRootY,
            -0.26f,
            -0.24f,
            0f);

        runner.Check(
            NearlyEqual(handRootY, 0.397f)
            && NearlyEqual(handRootY - 0.627f, -0.23f),
            "Table layout regression: an inclined local hand must clear the table instead of intersecting it.");
        runner.Check(
            NearlyEqual(meldLocalY, -0.417f)
            && NearlyEqual(handRootY + meldLocalY - 0.24f, -0.26f),
            "Table layout regression: lifting the hand must not make its flat melds float above the table.");
    }

    private static void RunFourKongSpanTests(RegressionRunner runner)
    {
        TableHorizontalSpan fourKongs = GameTableLayoutPolicy.GetMeldCollectionSpan(
            6.4f,
            4,
            4,
            0.8f,
            0.2f);

        runner.Check(
            NearlyEqual(fourKongs.Min, -7.4f)
            && NearlyEqual(fourKongs.Max, 6f)
            && fourKongs.Min >= -10.8f
            && fourKongs.Max <= 10.8f,
            "Table layout regression: four full-width kongs must remain inside the playable table width.");
    }

    private static void RunCentralHudSafeZoneTests(RegressionRunner runner)
    {
        runner.Check(
            CentralHudFitsBetweenRivers(1200f, 675f)
            && CentralHudFitsBetweenRivers(1200f, 750f),
            "Table layout regression: the compact central HUD must fit between the inner river edges at desktop and web aspect ratios.");
    }

    private static bool CentralHudFitsBetweenRivers(float logicalWidth, float logicalHeight)
    {
        const float riverInnerEdge = 2.65f;
        TableScreenPoint topRiverInner = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(0f, riverInnerEdge),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);
        TableScreenPoint bottomRiverInner = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(0f, -riverInnerEdge),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);
        TableVerticalBand hudY = GameTableLayoutPolicy.GetCenteredVerticalBand(
            logicalHeight * 0.454f,
            104f);
        TableScreenPoint leftRiverInner = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(-riverInnerEdge, 0f),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);
        TableScreenPoint rightRiverInner = GameTableLayoutPolicy.ProjectGroundPoint(
            new TableLayoutPoint(riverInnerEdge, 0f),
            15f,
            -17f,
            44.1449f,
            50f,
            logicalWidth,
            logicalHeight);
        TableHorizontalSpan hudX = GameTableLayoutPolicy.GetCenteredHorizontalSpan(
            logicalWidth * 0.5f,
            164f);
        return hudY.Top >= topRiverInner.Y
               && hudY.Bottom <= bottomRiverInner.Y
               && hudX.Min >= leftRiverInner.X
               && hudX.Max <= rightRiverInner.X;
    }

    private static bool NearlyEqual(float left, float right) =>
        Math.Abs(left - right) < 0.0001f;
}
