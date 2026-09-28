using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalMaintenanceStateTests
    {
        [Test]
        public void ConsistentMaintenanceResultsMoveBeforeFinalConfirmation()
        {
            var policy = new ImmersalLocalizationProfile.Settings().CopyValidated();
            var state = Started(policy);
            Pose candidate = new Pose(new Vector3(.08f, 0, 0), Quaternion.Euler(0, 3, 0));

            Assert.That(state.Preview(candidate, 1, 3, 1), Is.EqualTo("previewing"));
            Assert.That(state.SmoothingFinalizesCorrection, Is.False);
            state.AdvanceSmoothing(1 + policy.ProvisionalCorrectionSmoothingSeconds);
            Assert.That(state.Pose.position.x, Is.EqualTo(.08f / 3f).Within(.0002f));
            Assert.That(Quaternion.Angle(state.Pose.rotation, Quaternion.identity),
                Is.EqualTo(1f).Within(.02f));

            Assert.That(state.Preview(candidate, 2, 3, 2), Is.EqualTo("previewing"));
            state.AdvanceSmoothing(2 + policy.ProvisionalCorrectionSmoothingSeconds);
            Assert.That(state.Pose.position.x, Is.EqualTo(.08f * 2f / 3f).Within(.0002f));
            Assert.That(Quaternion.Angle(state.Pose.rotation, Quaternion.identity),
                Is.EqualTo(2f).Within(.02f));

            Assert.That(state.Preview(candidate, 3, 3, 3), Is.EqualTo("previewing"));
            state.AdvanceSmoothing(3 + policy.ProvisionalCorrectionSmoothingSeconds);
            Assert.That(state.Pose.position.x, Is.EqualTo(.08f).Within(.0001f));
            Assert.That(Quaternion.Angle(state.Pose.rotation, Quaternion.identity),
                Is.EqualTo(3f).Within(.02f));
            Assert.That(state.Accept(candidate, 4), Is.EqualTo("applied_after_preview"));
            Assert.That(state.IsSmoothing, Is.False,
                "A preview that already reached the confirmed pose must not add an empty final delay.");
        }

        [Test]
        public void UnconfirmedPreviewRollsBackAndLargeCandidateNeverMovesContent()
        {
            var policy = new ImmersalLocalizationProfile.Settings().CopyValidated();
            var state = Started(policy);
            Pose candidate = new Pose(new Vector3(.08f, 0, 0), Quaternion.identity);
            state.Preview(candidate, 1, 3, 1);
            state.AdvanceSmoothing(1 + policy.ProvisionalCorrectionSmoothingSeconds);
            Assert.That(state.Pose.position.x, Is.GreaterThan(.02f));

            Assert.That(state.RollbackPreview(2), Is.True);
            state.AdvanceSmoothing(2 + policy.ProvisionalCorrectionSmoothingSeconds);
            Assert.That(state.Pose.position.x, Is.EqualTo(0).Within(.0001f));

            Pose large = new Pose(Vector3.right, Quaternion.identity);
            Assert.That(state.Preview(large, 1, 3, 3), Is.EqualTo("preview_ignored"));
            Assert.That(state.IsSmoothing, Is.False);
            Assert.That(state.Pose.position.x, Is.EqualTo(0).Within(.0001f));
        }

        [Test]
        public void TrackingInvalidationCancelsPreviewAtLastVerifiedPose()
        {
            var policy = new ImmersalLocalizationProfile.Settings().CopyValidated();
            var state = Started(policy);
            state.Preview(new Pose(Vector3.right * .08f, Quaternion.identity), 1, 3, 1);
            state.AdvanceSmoothing(1.1);
            Assert.That(state.Pose.position.x, Is.GreaterThan(0));

            state.Invalidate(1.1);
            Assert.That(state.Pose.position.x, Is.EqualTo(0).Within(.0001f));
            Assert.That(state.IsSmoothing, Is.False);
            Assert.That(state.Reacquiring, Is.True);
        }

        private static ImmersalMaintenanceState Started(ImmersalLocalizationProfile.Settings policy)
        {
            var state = new ImmersalMaintenanceState(policy, false);
            state.RefreshTracking(0, true);
            Assert.That(state.Accept(Pose.identity, 0), Is.EqualTo("initial"));
            return state;
        }
    }
}
