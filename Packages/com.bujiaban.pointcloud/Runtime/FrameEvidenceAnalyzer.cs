using System;

namespace Bujiaban.PointCloud
{
    internal readonly struct FrameEvidence
    {
        internal FrameEvidence(
            bool hasEnoughDetail,
            double contrast,
            double edgeRatio,
            int cornerCount,
            int detailedRegionCount,
            int regionCount,
            ulong signature)
        {
            HasEnoughDetail = hasEnoughDetail;
            Contrast = contrast;
            EdgeRatio = edgeRatio;
            CornerCount = cornerCount;
            DetailedRegionCount = detailedRegionCount;
            RegionCount = regionCount;
            Signature = signature;
        }

        internal bool HasEnoughDetail { get; }
        internal double Contrast { get; }
        internal double EdgeRatio { get; }
        internal int CornerCount { get; }
        internal int DetailedRegionCount { get; }
        internal int RegionCount { get; }
        internal ulong Signature { get; }
    }

    /// <summary>
    /// Measures whether a camera frame contains enough distributed, sharp
    /// corner-like detail for visual localization. It does not classify the
    /// photographed surface or impose a camera direction.
    /// </summary>
    internal static class FrameEvidenceAnalyzer
    {
        private const int SampleColumns = 96;
        private const int SampleRows = 72;
        private const int SampleCapacity = SampleColumns * SampleRows;
        private const int RegionColumns = 4;
        private const int RegionRows = 4;
        internal const int RegionCount = RegionColumns * RegionRows;
        private const int EdgeThreshold = 18;
        private const long CornerResponseThreshold = 2000L;
        internal const double DefaultMinimumContrast = 10d;
        internal const double DefaultMinimumEdgeRatio = 0.018d;
        internal const int DefaultMinimumCornerCount = 18;
        private const int MinimumCornersPerRegion = 3;
        internal const int DefaultMinimumDetailedRegions = 3;

        internal static unsafe bool TryAnalyze(
            byte[] pixels,
            int width,
            int height,
            int channels,
            out FrameEvidence evidence,
            double minimumContrast = DefaultMinimumContrast,
            double minimumEdgeRatio = DefaultMinimumEdgeRatio,
            int minimumCornerCount = DefaultMinimumCornerCount,
            int minimumDetailedRegions = DefaultMinimumDetailedRegions)
        {
            evidence = default;
            if (pixels == null || width < 5 || height < 5 || channels < 1)
                return false;

            long requiredLength = (long)width * height * channels;
            if (requiredLength > pixels.LongLength)
                return false;

            fixed (byte* pointer = pixels)
            {
                return TryAnalyze(
                    (IntPtr)pointer,
                    width,
                    height,
                    channels,
                    out evidence,
                    minimumContrast,
                    minimumEdgeRatio,
                    minimumCornerCount,
                    minimumDetailedRegions);
            }
        }

        internal static unsafe bool TryAnalyze(
            IntPtr pixels,
            int width,
            int height,
            int channels,
            out FrameEvidence evidence,
            double minimumContrast = DefaultMinimumContrast,
            double minimumEdgeRatio = DefaultMinimumEdgeRatio,
            int minimumCornerCount = DefaultMinimumCornerCount,
            int minimumDetailedRegions = DefaultMinimumDetailedRegions)
        {
            evidence = default;
            if (pixels == IntPtr.Zero ||
                width < 5 ||
                height < 5 ||
                channels < 1)
            {
                return false;
            }

            int sampledWidth = Math.Min(width, SampleColumns);
            int sampledHeight = Math.Min(height, SampleRows);
            if (sampledWidth < 5 || sampledHeight < 5)
                return false;

            byte* luminance = stackalloc byte[SampleCapacity];
            Downsample(
                (byte*)pixels.ToPointer(),
                width,
                height,
                channels,
                luminance,
                sampledWidth,
                sampledHeight);

            long sum = 0L;
            long sumSquares = 0L;
            int sampleCount = 0;
            int edgeCount = 0;
            for (int y = 1; y < sampledHeight - 1; y++)
            {
                for (int x = 1; x < sampledWidth - 1; x++)
                {
                    int index = y * sampledWidth + x;
                    int value = luminance[index];
                    sum += value;
                    sumSquares += (long)value * value;
                    sampleCount++;

                    int horizontal = Math.Abs(
                        luminance[index + 1] - luminance[index - 1]);
                    int vertical = Math.Abs(
                        luminance[index + sampledWidth] -
                        luminance[index - sampledWidth]);
                    if (Math.Max(horizontal, vertical) >= EdgeThreshold)
                        edgeCount++;
                }
            }

            if (sampleCount == 0)
                return false;

            int* regionCorners = stackalloc int[RegionCount];
            for (int i = 0; i < RegionCount; i++)
                regionCorners[i] = 0;

            int cornerCount = 0;
            for (int y = 2; y < sampledHeight - 2; y += 2)
            {
                for (int x = 2; x < sampledWidth - 2; x += 2)
                {
                    int index = y * sampledWidth + x;
                    if (!IsCorner(luminance, index, sampledWidth))
                        continue;

                    cornerCount++;
                    int regionX = x * RegionColumns / sampledWidth;
                    int regionY = y * RegionRows / sampledHeight;
                    regionCorners[regionY * RegionColumns + regionX]++;
                }
            }

            int detailedRegionCount = 0;
            for (int i = 0; i < RegionCount; i++)
            {
                if (regionCorners[i] >= MinimumCornersPerRegion)
                    detailedRegionCount++;
            }

            double mean = (double)sum / sampleCount;
            double variance = Math.Max(
                0d,
                (double)sumSquares / sampleCount - mean * mean);
            double contrast = Math.Sqrt(variance);
            double edgeRatio = (double)edgeCount / sampleCount;
            ulong signature = ComputeSignature(
                luminance,
                sampledWidth,
                sampledHeight);
            bool hasEnoughDetail =
                contrast >= minimumContrast &&
                edgeRatio >= minimumEdgeRatio &&
                cornerCount >= minimumCornerCount &&
                detailedRegionCount >= minimumDetailedRegions;

            evidence = new FrameEvidence(
                hasEnoughDetail,
                contrast,
                edgeRatio,
                cornerCount,
                detailedRegionCount,
                RegionCount,
                signature);
            return true;
        }

        private static unsafe bool IsCorner(
            byte* luminance,
            int centerIndex,
            int width)
        {
            long xx = 0L;
            long yy = 0L;
            long xy = 0L;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int index = centerIndex + offsetY * width + offsetX;
                    int gradientX =
                        luminance[index + 1] - luminance[index - 1];
                    int gradientY =
                        luminance[index + width] - luminance[index - width];
                    xx += (long)gradientX * gradientX;
                    yy += (long)gradientY * gradientY;
                    xy += (long)gradientX * gradientY;
                }
            }

            long trace = xx + yy;
            if (trace <= 0L)
                return false;

            long determinant = xx * yy - xy * xy;
            return determinant > 0L &&
                   determinant / trace >= CornerResponseThreshold;
        }

        private static unsafe ulong ComputeSignature(
            byte* luminance,
            int width,
            int height)
        {
            ulong signature = 0UL;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            {
                int sampleY = (y + 1) * height / 9;
                for (int x = 0; x < 8; x++)
                {
                    int leftX = x * (width - 1) / 8;
                    int rightX = (x + 1) * (width - 1) / 8;
                    if (luminance[sampleY * width + leftX] >
                        luminance[sampleY * width + rightX])
                    {
                        signature |= 1UL << bit;
                    }

                    bit++;
                }
            }

            return signature;
        }

        private static unsafe void Downsample(
            byte* source,
            int sourceWidth,
            int sourceHeight,
            int channels,
            byte* destination,
            int destinationWidth,
            int destinationHeight)
        {
            for (int y = 0; y < destinationHeight; y++)
            {
                int sourceY = (int)((long)y * sourceHeight / destinationHeight);
                int nextY = Math.Min(sourceHeight - 1, sourceY + 1);
                for (int x = 0; x < destinationWidth; x++)
                {
                    int sourceX = (int)((long)x * sourceWidth / destinationWidth);
                    int nextX = Math.Min(sourceWidth - 1, sourceX + 1);
                    int value =
                        ReadLuminance(source, sourceWidth, channels, sourceX, sourceY) +
                        ReadLuminance(source, sourceWidth, channels, nextX, sourceY) +
                        ReadLuminance(source, sourceWidth, channels, sourceX, nextY) +
                        ReadLuminance(source, sourceWidth, channels, nextX, nextY);
                    destination[y * destinationWidth + x] = (byte)(value / 4);
                }
            }
        }

        private static unsafe int ReadLuminance(
            byte* source,
            int width,
            int channels,
            int x,
            int y)
        {
            int index = (y * width + x) * channels;
            if (channels == 1)
                return source[index];

            int red = source[index];
            int green = source[index + Math.Min(1, channels - 1)];
            int blue = source[index + Math.Min(2, channels - 1)];
            return (77 * red + 150 * green + 29 * blue) >> 8;
        }
    }
}
