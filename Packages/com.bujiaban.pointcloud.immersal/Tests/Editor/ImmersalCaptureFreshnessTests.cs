using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalCaptureFreshnessTests
    {
        [Test]
        public void RepeatedOlderAndInvalidCapturesAreRejectedUntilReset()
        {
            var freshness = new ImmersalCaptureFreshness();
            Assert.That(freshness.TryAccept(10), Is.True);
            Assert.That(freshness.TryAccept(10), Is.False);
            Assert.That(freshness.TryAccept(9), Is.False);
            Assert.That(freshness.TryAccept(double.NaN), Is.False);
            Assert.That(freshness.TryAccept(double.PositiveInfinity), Is.False);
            Assert.That(freshness.TryAccept(11), Is.True);
            freshness.Reset();
            Assert.That(freshness.TryAccept(1), Is.True);
        }
    }
}
