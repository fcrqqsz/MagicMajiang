using System;

namespace MahjongGame.Core
{
    public readonly struct TableLayoutPoint
    {
        public TableLayoutPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }
        public float Z { get; }
    }

    public readonly struct TableSeatLayout
    {
        public TableSeatLayout(
            TableLayoutPoint handRoot,
            TableLayoutPoint riverRoot,
            float yawDegrees)
        {
            HandRoot = handRoot;
            RiverRoot = riverRoot;
            YawDegrees = yawDegrees;
        }

        public TableLayoutPoint HandRoot { get; }
        public TableLayoutPoint RiverRoot { get; }
        public float YawDegrees { get; }
    }

    public readonly struct TableScreenPoint
    {
        public TableScreenPoint(float x, float y, float depth)
        {
            X = x;
            Y = y;
            Depth = depth;
        }

        public float X { get; }
        public float Y { get; }
        public float Depth { get; }
    }

    public readonly struct TableVerticalBand
    {
        public TableVerticalBand(float top, float bottom)
        {
            Top = top;
            Bottom = bottom;
        }

        public float Top { get; }
        public float Bottom { get; }
    }

    public readonly struct TableHorizontalSpan
    {
        public TableHorizontalSpan(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }
    }

    public static class GameTableLayoutPolicy
    {
        public static float GetCenteredHandX(
            int tileCount,
            int index,
            float tileStep,
            bool hasDrawGap,
            float drawGap)
        {
            if (tileCount <= 0 || index < 0 || index >= tileCount)
                return 0f;

            hasDrawGap = hasDrawGap && tileCount > 1;
            float totalSpan = (tileCount - 1) * tileStep + (hasDrawGap ? drawGap : 0f);
            float x = -totalSpan * 0.5f + index * tileStep;
            if (hasDrawGap && index == tileCount - 1)
                x += drawGap;
            return x;
        }

        public static TableLayoutPoint GetRiverLocalPosition(
            int index,
            int tilesPerRow,
            float columnStep,
            float rowStep)
        {
            int row = index / tilesPerRow;
            int column = index % tilesPerRow;
            float centerColumn = (tilesPerRow - 1) * 0.5f;
            return new TableLayoutPoint(
                (column - centerColumn) * columnStep,
                -row * rowStep);
        }

        public static TableLayoutPoint RotateForSeat(TableLayoutPoint point, int seatIndex)
        {
            switch (seatIndex)
            {
                case 1:
                    return new TableLayoutPoint(-point.Z, point.X);
                case 2:
                    return new TableLayoutPoint(-point.X, -point.Z);
                case 3:
                    return new TableLayoutPoint(point.Z, -point.X);
                default:
                    return point;
            }
        }

        public static TableSeatLayout GetSeatLayout(
            int seatIndex,
            float handDistance,
            float riverDistance)
        {
            TableLayoutPoint handRoot = RotateForSeat(
                new TableLayoutPoint(0f, -handDistance),
                seatIndex);
            TableLayoutPoint riverRoot = RotateForSeat(
                new TableLayoutPoint(0f, -riverDistance),
                seatIndex);
            float yawDegrees;
            switch (seatIndex)
            {
                case 1:
                    yawDegrees = -90f;
                    break;
                case 2:
                    yawDegrees = 180f;
                    break;
                case 3:
                    yawDegrees = 90f;
                    break;
                default:
                    yawDegrees = 0f;
                    break;
            }

            return new TableSeatLayout(handRoot, riverRoot, yawDegrees);
        }

        public static bool CanFitWaitItems(
            float panelWidth,
            float titleWidth,
            float horizontalPadding,
            int itemCount,
            float itemWidth)
        {
            float availableWidth = panelWidth - titleWidth - horizontalPadding * 2f;
            return itemCount * itemWidth <= availableWidth;
        }

        public static float GetWaitHintWidth(
            int itemCount,
            float itemWidth,
            float fixedWidth,
            float minimumWidth,
            float maximumWidth)
        {
            float resolvedMinimum = Math.Max(0f, minimumWidth);
            float resolvedMaximum = Math.Max(resolvedMinimum, maximumWidth);
            float contentWidth = Math.Max(0f, fixedWidth)
                                 + Math.Max(0, itemCount) * Math.Max(0f, itemWidth);
            return Math.Min(resolvedMaximum, Math.Max(resolvedMinimum, contentWidth));
        }

        public static float GetRotatedSquareHalfExtent(float sideLength) =>
            Math.Max(0f, sideLength) * 0.70710678f;

        public static float GetSurfaceAlignedRootY(
            float surfaceY,
            float objectMinimumY,
            float clearance) =>
            surfaceY - objectMinimumY + Math.Max(0f, clearance);

        public static float GetSurfaceAlignedChildLocalY(
            float parentWorldY,
            float surfaceY,
            float objectMinimumY,
            float clearance) =>
            GetSurfaceAlignedRootY(surfaceY, objectMinimumY, clearance) - parentWorldY;

        public static TableHorizontalSpan GetMeldCollectionSpan(
            float anchorX,
            int meldCount,
            int visualTilesPerMeld,
            float tileWidth,
            float meldGap)
        {
            if (meldCount <= 0 || visualTilesPerMeld <= 0 || tileWidth <= 0f)
                return new TableHorizontalSpan(anchorX, anchorX);

            float currentOffset = 0f;
            float minCenter = float.MaxValue;
            float maxCenter = float.MinValue;
            for (int meldIndex = 0; meldIndex < meldCount; meldIndex++)
            {
                float startX = currentOffset - visualTilesPerMeld * tileWidth;
                minCenter = Math.Min(minCenter, startX);
                maxCenter = Math.Max(
                    maxCenter,
                    startX + (visualTilesPerMeld - 1) * tileWidth);
                currentOffset = startX - Math.Max(0f, meldGap);
            }

            float halfTile = tileWidth * 0.5f;
            return new TableHorizontalSpan(
                anchorX + minCenter - halfTile,
                anchorX + maxCenter + halfTile);
        }

        public static TableVerticalBand GetCenteredVerticalBand(float centerY, float height)
        {
            float halfHeight = Math.Max(0f, height) * 0.5f;
            return new TableVerticalBand(centerY - halfHeight, centerY + halfHeight);
        }

        public static TableHorizontalSpan GetCenteredHorizontalSpan(float centerX, float width)
        {
            float halfWidth = Math.Max(0f, width) * 0.5f;
            return new TableHorizontalSpan(centerX - halfWidth, centerX + halfWidth);
        }

        public static TableVerticalBand GetBottomAnchoredBand(
            float logicalHeight,
            float bottomRatio,
            float bandHeight)
        {
            float resolvedHeight = Math.Max(0f, logicalHeight);
            float resolvedBottom = resolvedHeight * Math.Max(0f, bottomRatio);
            float resolvedBandHeight = Math.Max(0f, bandHeight);
            float bottom = resolvedHeight - resolvedBottom;
            return new TableVerticalBand(bottom - resolvedBandHeight, bottom);
        }

        public static TableScreenPoint ProjectGroundPoint(
            TableLayoutPoint point,
            float cameraHeight,
            float cameraZ,
            float cameraPitchDegrees,
            float verticalFieldOfViewDegrees,
            float logicalWidth,
            float logicalHeight)
        {
            double pitch = cameraPitchDegrees * Math.PI / 180d;
            double relativeY = -cameraHeight;
            double relativeZ = point.Z - cameraZ;
            double cameraY = relativeY * Math.Cos(pitch) + relativeZ * Math.Sin(pitch);
            double depth = -relativeY * Math.Sin(pitch) + relativeZ * Math.Cos(pitch);
            double halfVerticalSpan = Math.Tan(verticalFieldOfViewDegrees * Math.PI / 360d);
            double aspect = logicalWidth / logicalHeight;

            float screenX = (float)(logicalWidth * (
                0.5d + point.X / (2d * depth * halfVerticalSpan * aspect)));
            float screenY = (float)(logicalHeight * (
                0.5d - cameraY / (2d * depth * halfVerticalSpan)));
            return new TableScreenPoint(screenX, screenY, (float)depth);
        }
    }
}
