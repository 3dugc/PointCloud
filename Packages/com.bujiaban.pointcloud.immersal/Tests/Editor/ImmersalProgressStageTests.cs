using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalProgressStageTests
    {
        [TestCase(0, PointCloudLocalizationStage.Searching)]
        [TestCase(1, PointCloudLocalizationStage.Confirming)]
        [TestCase(2, PointCloudLocalizationStage.Confirming)]
        [TestCase(3, PointCloudLocalizationStage.Confirming)]
        [TestCase(4, PointCloudLocalizationStage.Confirming)]
        public void RetainedVotesStayConfirmingWhileOtherRequirementsArePending(
            int stableCount, PointCloudLocalizationStage expected)
        {
            // Three or more votes must not switch guidance to insufficient
            // detail. The separate gate still decides when a pose can return.
            Assert.That(ImmersalPointCloudBackend.GetProgressStage(stableCount), Is.EqualTo(expected));
        }
    }
}
