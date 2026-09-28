using NUnit.Framework;
using UnityEngine;
using static Bujiaban.PointCloud.Tests.LocalizationConfirmationGateTests;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class LocalizationGateDiagnosticsTests
    {
        [Test]
        public void TraceDistinguishesSimilarImagesFromPoseDisagreement()
        {
            var gate = Gate();
            Put(gate, 0, 1, mapX: 0);
            var result = gate.Register(Camera(0, 6), Signature(0), true, Map(),
                90, true, true, 2, 2);
            Assert.That(result.Trace.currentSupportsGroup, Is.False);
            Assert.That(result.Trace.currentImageConflicts, Is.EqualTo(1));
            Assert.That(result.Trace.currentViewConflicts, Is.Zero);
            Assert.That(result.Trace.currentPositionConflicts, Is.Zero);
            Assert.That(result.Trace.currentRotationConflicts, Is.Zero);
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.DifferentView));
        }

        [Test]
        public void TraceReportsActualLocalConflictWithoutCallingItImageSimilarity()
        {
            var gate = Gate();
            Put(gate, 0, 1, mapX: 0);
            var result = Put(gate, 1, 2, mapX: 1);
            Assert.That(result.Trace.currentSupportsGroup, Is.False);
            Assert.That(result.Trace.currentPositionConflicts, Is.EqualTo(1));
            Assert.That(result.Trace.currentMaxLocalPositionDelta, Is.EqualTo(1).Within(.0001f));
            Assert.That(result.Trace.currentImageConflicts, Is.Zero);
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.PoseDisagreement));
        }

        [Test]
        public void TraceSeparatesFarOriginMovementFromLocalAlignment()
        {
            var gate = Gate();
            Vector3 remoteOrigin = new Vector3(0, 0, 8);
            gate.Register(Camera(0, 0), Signature(0), true,
                new Pose(remoteOrigin, Quaternion.identity), 90, true, true, 1, 1);
            Quaternion rotation = Quaternion.Euler(0, 1.8f, 0);
            var result = gate.Register(Camera(.24f, 6), Signature(1), true,
                new Pose(rotation * remoteOrigin, rotation), 90, true, true, 2, 2);
            Assert.That(result.StableCount, Is.EqualTo(2));
            Assert.That(result.Trace.currentMaxOriginPositionDelta, Is.GreaterThan(.25f));
            Assert.That(result.Trace.currentMaxLocalPositionDelta, Is.LessThan(.008f));
            Assert.That(result.Trace.currentPositionConflicts, Is.Zero);
        }

        [Test]
        public void PendingRequirementRemainsAvailableWithDiagnosticsDisabled()
        {
            var gate = Gate(false);
            for (int i = 0; i < 6; i++) Put(gate, i, i + 1, strong: false);
            Assert.That(gate.StableCount, Is.EqualTo(6));
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.StrongerMatch));
            gate.AdvanceTime(9);
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.EvidenceExpired));
            gate.Reset();
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.CollectingSamples));
            for (int i = 0; i < 5; i++) Put(gate, i, i + 1);
            Assert.That(gate.PendingRequirement, Is.EqualTo(PointCloudConfirmationRequirement.None));
        }

        [Test]
        public void FailureTraceCannotAttachContextToOldPose()
        {
            var gate = Gate();
            Put(gate, 0, 1, context: false);
            var result = Put(gate, 0, 2, accepted: false, context: true);
            Assert.That(result.Trace.contextAdded, Is.False);
            Assert.That(result.Trace.retained[0].contextAttemptId, Is.Zero);
            Assert.That(result.Trace.retained[0].poseAttemptId, Is.EqualTo(1));
            Assert.That(result.Trace.retained[0].poseAgeSeconds, Is.EqualTo(1));
            Assert.That(result.Trace.poseAction, Is.EqualTo("miss_preserves_unexpired_evidence"));
        }

        [Test]
        public void TraceSeparatesAnchorCameraFromLatestAcceptedCamera()
        {
            var gate = Gate();
            Put(gate, 0, 1, accepted: false);
            var result = Put(gate, 0, 2, x: .01f);
            var sample = result.Trace.retained[0];
            Assert.That(sample.ageSeconds, Is.EqualTo(1));
            Assert.That(sample.poseAgeSeconds, Is.Zero);
            Assert.That(sample.poseCameraPose, Is.Not.EqualTo(sample.cameraPose));
        }

        [Test]
        public void ExpiryTraceIncludesOldPoseSourceAndReason()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            gate.AdvanceTime(4);
            Assert.That(gate.LastExpiryTrace.removed[0].poseAttemptId, Is.EqualTo(1));
            Assert.That(gate.LastExpiryTrace.removed[0].poseAgeSeconds, Is.EqualTo(3));
            Assert.That(gate.LastExpiryTrace.removed[0].removalReason, Is.EqualTo("evidence_gap_expired"));
        }

        [Test]
        public void ConfirmationIdentifiesCurrentEvidenceDurationAndSelectedMedoid()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1);
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.Trace.stableObservationIds, Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
            Assert.That(result.Trace.medoidObservationId, Is.EqualTo(3));
            Assert.That(result.Trace.strongMatchCount, Is.EqualTo(5));
            Assert.That(result.Trace.durationSeconds, Is.EqualTo(4));
            Assert.That(result.Trace.currentSupportsGroup, Is.True);
        }

        [Test]
        public void DiagnosticsOffAndOnHaveIdenticalBehaviorIncludingTime()
        {
            var plain = Gate(false);
            var traced = Gate(true);
            for (int i = 0; i < 7; i++)
            {
                var a = Put(plain, i, i + 1);
                var b = Put(traced, i, i + 1);
                Assert.That(a.IsConfirmed, Is.EqualTo(b.IsConfirmed));
                Assert.That(a.ConfirmedPose, Is.EqualTo(b.ConfirmedPose));
                Assert.That(a.HasCurrentPose, Is.EqualTo(b.HasCurrentPose));
                Assert.That(a.CurrentPose, Is.EqualTo(b.CurrentPose));
                Assert.That(a.HasStablePose, Is.EqualTo(b.HasStablePose));
                Assert.That(a.StablePose, Is.EqualTo(b.StablePose));
                Assert.That(a.StableCount, Is.EqualTo(b.StableCount));
                Assert.That(a.Trace, Is.Null);
            }
            Assert.That(plain.AdvanceTime(11), Is.EqualTo(traced.AdvanceTime(11)));
            Assert.That(plain.StableCount, Is.EqualTo(traced.StableCount));
        }
    }
}
