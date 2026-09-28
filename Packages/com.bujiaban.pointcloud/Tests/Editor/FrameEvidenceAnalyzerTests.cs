using NUnit.Framework;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class FrameEvidenceAnalyzerTests
    {
        [Test]
        public void FlatFrame_IsRejected()
        {
            byte[] pixels = CreateFrame(160, 120, (_, _) => 128);

            bool analyzed = FrameEvidenceAnalyzer.TryAnalyze(
                pixels,
                160,
                120,
                1,
                out FrameEvidence evidence);

            Assert.That(analyzed, Is.True);
            Assert.That(evidence.HasEnoughDetail, Is.False);
            Assert.That(evidence.CornerCount, Is.Zero);
            Assert.That(evidence.DetailedRegionCount, Is.Zero);
        }

        [Test]
        public void SmoothLightingGradient_IsRejected()
        {
            byte[] pixels = CreateFrame(
                160,
                120,
                (x, _) => 70 + x / 2);

            bool analyzed = FrameEvidenceAnalyzer.TryAnalyze(
                pixels,
                160,
                120,
                1,
                out FrameEvidence evidence);

            Assert.That(analyzed, Is.True);
            Assert.That(evidence.Contrast, Is.GreaterThan(10d));
            Assert.That(evidence.HasEnoughDetail, Is.False);
            Assert.That(evidence.CornerCount, Is.Zero);
        }

        [Test]
        public void SingleLongEdge_IsRejected()
        {
            byte[] pixels = CreateFrame(
                160,
                120,
                (x, _) => x < 80 ? 30 : 220);

            bool analyzed = FrameEvidenceAnalyzer.TryAnalyze(
                pixels,
                160,
                120,
                1,
                out FrameEvidence evidence);

            Assert.That(analyzed, Is.True);
            Assert.That(evidence.HasEnoughDetail, Is.False);
            Assert.That(evidence.DetailedRegionCount, Is.LessThan(3));
        }

        [Test]
        public void DistributedFloorMarkings_AreAccepted()
        {
            byte[] pixels = CreateFrame(
                160,
                120,
                (x, y) => IsMarkedRegion(x, y) &&
                          ((x / 3 + y / 3) & 1) == 0
                    ? 230
                    : 35);

            bool analyzed = FrameEvidenceAnalyzer.TryAnalyze(
                pixels,
                160,
                120,
                1,
                out FrameEvidence evidence);

            Assert.That(analyzed, Is.True);
            Assert.That(evidence.HasEnoughDetail, Is.True);
            Assert.That(evidence.CornerCount, Is.GreaterThanOrEqualTo(18));
            Assert.That(evidence.DetailedRegionCount, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void RgbFrame_IsSupported()
        {
            const int width = 160;
            const int height = 120;
            byte[] pixels = new byte[width * height * 3];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte value = (byte)(((x / 4 + y / 4) & 1) == 0
                        ? 230
                        : 25);
                    int index = (y * width + x) * 3;
                    pixels[index] = value;
                    pixels[index + 1] = value;
                    pixels[index + 2] = value;
                }
            }

            Assert.That(
                FrameEvidenceAnalyzer.TryAnalyze(
                    pixels,
                    width,
                    height,
                    3,
                    out FrameEvidence evidence),
                Is.True);
            Assert.That(evidence.HasEnoughDetail, Is.True);
        }

        [Test]
        public void InvalidBuffer_IsRejectedAsInvalidInput()
        {
            Assert.That(
                FrameEvidenceAnalyzer.TryAnalyze(
                    new byte[10],
                    160,
                    120,
                    1,
                    out _),
                Is.False);
        }

        [TestCase("contrast")]
        [TestCase("edges")]
        [TestCase("corners")]
        [TestCase("regions")]
        public void PlatformThresholdsChangeAcceptanceWithoutChangingMeasurements(string threshold)
        {
            byte[] pixels = CreateFrame(160, 120,
                (x, y) => IsMarkedRegion(x, y) && ((x / 3 + y / 3) & 1) == 0 ? 230 : 35);
            Assert.That(FrameEvidenceAnalyzer.TryAnalyze(pixels, 160, 120, 1,
                out FrameEvidence baseline), Is.True);
            Assert.That(baseline.HasEnoughDetail, Is.True);

            Assert.That(FrameEvidenceAnalyzer.TryAnalyze(pixels, 160, 120, 1,
                out FrameEvidence stricter,
                minimumContrast: threshold == "contrast" ? baseline.Contrast + 1 : 10,
                minimumEdgeRatio: threshold == "edges" ? baseline.EdgeRatio + .01 : .018,
                minimumCornerCount: threshold == "corners" ? baseline.CornerCount + 1 : 18,
                minimumDetailedRegions: threshold == "regions" ? baseline.DetailedRegionCount + 1 : 3), Is.True);

            Assert.That(stricter.HasEnoughDetail, Is.False);
            Assert.That(stricter.Contrast, Is.EqualTo(baseline.Contrast));
            Assert.That(stricter.EdgeRatio, Is.EqualTo(baseline.EdgeRatio));
            Assert.That(stricter.CornerCount, Is.EqualTo(baseline.CornerCount));
            Assert.That(stricter.DetailedRegionCount, Is.EqualTo(baseline.DetailedRegionCount));
            Assert.That(stricter.Signature, Is.EqualTo(baseline.Signature));
        }

        [Test]
        public void ThresholdChangeIsNotRetainedByAnotherPlatformCall()
        {
            byte[] pixels = CreateFrame(160, 120,
                (x, y) => ((x / 4 + y / 4) & 1) == 0 ? 230 : 25);
            Assert.That(FrameEvidenceAnalyzer.TryAnalyze(pixels, 160, 120, 1,
                out FrameEvidence stricter, minimumContrast: 127.5d), Is.True);
            Assert.That(stricter.HasEnoughDetail, Is.False);
            Assert.That(FrameEvidenceAnalyzer.TryAnalyze(pixels, 160, 120, 1,
                out FrameEvidence defaults), Is.True);
            Assert.That(defaults.HasEnoughDetail, Is.True);
        }

        private static byte[] CreateFrame(
            int width,
            int height,
            System.Func<int, int, int> valueAt)
        {
            byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = (byte)valueAt(x, y);
            }

            return pixels;
        }

        private static bool IsMarkedRegion(int x, int y)
        {
            return (x >= 10 && x < 50 && y >= 10 && y < 45) ||
                   (x >= 60 && x < 100 && y >= 45 && y < 80) ||
                   (x >= 110 && x < 150 && y >= 75 && y < 110);
        }
    }
}
