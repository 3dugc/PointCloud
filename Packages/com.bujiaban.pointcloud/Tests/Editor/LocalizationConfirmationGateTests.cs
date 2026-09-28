using System;
using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class LocalizationConfirmationGateTests
    {
        internal static LocalizationConfirmationGate Gate(bool trace = true,
            double age = 10, double gap = 3) =>
            new LocalizationConfirmationGate(5, 9, .05f, 5f, 12, .05f, 2f, .15f, 20f,
                trace, minimumStrongMatches: 3, minimumContextMatches: 2,
                minimumConfirmationSeconds: 4, maximumObservationAgeSeconds: age,
                maximumEvidenceGapSeconds: gap);

        internal static Pose Camera(float x, float yaw)
        {
            double half = yaw * Math.PI / 360;
            return new Pose(new Vector3(x, 0, 0),
                new Quaternion(0, (float)Math.Sin(half), 0, (float)Math.Cos(half)));
        }

        internal static Pose Map(float x = 0) => new Pose(new Vector3(x, 0, 0), Quaternion.identity);
        internal static ulong Signature(int i) => 255UL << (i * 8);
        internal static LocalizationGateResult Put(LocalizationConfirmationGate gate, int i,
            double time, bool accepted = true, bool strong = true, bool context = true,
            float? x = null, float? yaw = null, float? mapX = null) =>
            gate.Register(Camera(x ?? 0, yaw ?? i * 6), Signature(i), accepted,
                Map(mapX ?? i * .004f), strong ? 90 : 60, strong && accepted,
                context, i + 1, time);

        [TestCase(0, 9)]
        [TestCase(13, 13)]
        [TestCase(5, 4)]
        [TestCase(5, 13)]
        [TestCase(5, 31)]
        [TestCase(5, 32)]
        [TestCase(5, 64)]
        public void UnsupportedWindowIsRejectedBeforeAnySearch(int required, int window)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LocalizationConfirmationGate(required, window,
                    .05f, 5f, 12, .05f, 2f, .15f, 20f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MaximumWindowConfirmsAndEvictsWithOrWithoutDiagnostics(bool trace)
        {
            int maximum = LocalizationConfirmationGate.MaximumSupportedObservations;
            var gate = new LocalizationConfirmationGate(maximum, maximum,
                .05f, 5f, 1, .05f, 2f, .15f, 20f, trace,
                maximumObservationAgeSeconds: 100, maximumEvidenceGapSeconds: 3);
            LocalizationGateResult result = default;
            for (int i = 0; i <= maximum; i++)
            {
                result = gate.Register(Camera(i * .1f, i * 6f), 1UL << i,
                    true, Map(), 90, true, true, i + 1, i);
            }

            Assert.That(result.ObservationCount, Is.EqualTo(maximum));
            Assert.That(result.StableCount, Is.EqualTo(maximum));
            Assert.That(result.IsConfirmed, Is.True);
            if (trace)
                Assert.That(result.Trace.removed[0].removalReason,
                    Is.EqualTo("window_removes_oldest_observation"));
            else
                Assert.That(result.Trace, Is.Null);
        }

        [Test]
        public void FiveStrongContextViewsOverFourSecondsConfirmWithoutWalking()
        {
            var gate = Gate();
            for (int i = 0; i < 4; i++) Assert.That(Put(gate, i, i + 1).IsConfirmed, Is.False);
            var result = Put(gate, 4, 5);
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.Trace.confirmationPath, Is.EqualTo("context_and_rotation"));
            Assert.That(result.Trace.durationSeconds, Is.EqualTo(4));
            Assert.That(result.ConfirmedPose.position.x, Is.EqualTo(.008f).Within(.0001f));
        }

        [Test]
        public void StablePoseIsAvailableBeforeFinalConfirmation()
        {
            var gate = new LocalizationConfirmationGate(3, 5, 0, 0, 0,
                .05f, 2f, 0, 0, minimumStrongMatches: 1,
                minimumContextMatches: 1, minimumConfirmationSeconds: 1,
                maximumObservationAgeSeconds: 5, maximumEvidenceGapSeconds: 3,
                minimumIndependentObservationSeconds: .25);
            Pose candidate = Map(.08f);
            LocalizationGateResult first = gate.Register(Pose.identity, 1, true,
                candidate, 90, true, true, 1, 0);

            Assert.That(first.IsConfirmed, Is.False);
            Assert.That(first.HasStablePose, Is.True);
            Assert.That(first.StablePose.position.x, Is.EqualTo(.08f).Within(.0001f));
            Assert.That(first.StableCount, Is.EqualTo(1));
        }

        [Test]
        public void FiveFreshPosesInABurstDoNotConfirm()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, 1 + i * .1);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.blockReason, Is.EqualTo("insufficient_confirmation_duration"));
        }

        [Test]
        public void FiveSamplesNeedAtLeastThreeStrongMatches()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, strong: i < 2);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.HasStrongMatch, Is.False);
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.strongMatchCount, Is.EqualTo(2));
        }

        [Test]
        public void ThreeStrongMatchesAmongFiveCanConfirm()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, strong: i < 3);
            Assert.That(result.IsConfirmed, Is.True);
        }

        [Test]
        public void FloorTranslationAloneNeverConfirms()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++)
                result = Put(gate, i, i + 1, context: false, x: i * .1f, yaw: 0);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.blockReason, Is.EqualTo("insufficient_view_diversity"));
        }

        [Test]
        public void FloorRotationAloneNeverConfirms()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, context: false);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void FloorCanConfirmWithBothTranslationAndRotationAndStrongFreshEvidence()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++)
                result = Put(gate, i, i + 1, context: false, x: i * .05f);
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.HasContextView, Is.False);
            Assert.That(result.Trace.confirmationPath, Is.EqualTo("translation_and_rotation"));
        }

        [Test]
        public void OneContextFrameCannotUnlockStationaryConfirmation()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, context: i == 0);
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.contextMatchCount, Is.EqualTo(1));
        }

        [Test]
        public void TwoAcceptedContextFramesCanUnlockStationaryConfirmation()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, context: i < 2);
            Assert.That(result.IsConfirmed, Is.True);
        }

        [Test]
        public void SameObservationDoesNotAccumulateVotes()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 10; i++)
                result = gate.Register(Camera(0, 0), Signature(0), true, Map(), 90, true, true, i + 1, i + 1);
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.ObservationCount, Is.EqualTo(1));
        }

        [Test]
        public void DifferentCameraWithSameImageIsRetainedButCannotAddStableVote()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            var result = gate.Register(Camera(.2f, 30), Signature(0), true, Map(), 90, true, true, 2, 2);
            Assert.That(result.IsNewObservation, Is.True);
            Assert.That(result.ObservationCount, Is.EqualTo(2));
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void DifferentImageWithoutDifferentCameraCannotAddVote()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            var result = gate.Register(Camera(0, 0), Signature(1), true, Map(), 90, true, true, 2, 2);
            Assert.That(result.IsNewObservation, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(1));
        }

        [Test]
        public void ShortFailureAtAnotherViewPreservesCandidateButCannotConfirm()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            Put(gate, 1, 2);
            var result = Put(gate, 2, 2.5, accepted: false);
            Assert.That(result.StableCount, Is.EqualTo(2));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void ThreeSecondsWithoutAcceptedEvidenceClearsCandidate()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            Put(gate, 1, 2);
            Assert.That(gate.AdvanceTime(4.99), Is.False);
            Assert.That(gate.AdvanceTime(5), Is.True);
            Assert.That(gate.StableCount, Is.Zero);
            Assert.That(gate.LastExpiryTrace.removed, Has.Length.EqualTo(2));
            Assert.That(gate.LastExpiryTrace.removed[0].removalReason, Is.EqualTo("evidence_gap_expired"));
        }

        [Test]
        public void NewSuccessAfterLongGapStartsAgainAtOne()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            Put(gate, 1, 2);
            var result = Put(gate, 2, 20);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.removed, Has.Length.EqualTo(2));
        }

        [Test]
        public void OldVoteExpiresEvenWhileOtherViewsKeepSucceeding()
        {
            var gate = Gate(age: 5, gap: 3);
            Put(gate, 0, 1);
            Put(gate, 1, 2);
            Put(gate, 2, 3);
            Put(gate, 3, 4);
            Put(gate, 4, 5);
            var result = Put(gate, 5, 6);
            Assert.That(result.Trace.removed, Has.Length.EqualTo(1));
            Assert.That(result.Trace.removed[0].poseAttemptId, Is.EqualTo(1));
            Assert.That(result.Trace.removed[0].removalReason, Is.EqualTo("observation_age_expired"));
        }

        [Test]
        public void LowerScoringContradictoryResultReplacesOldPose()
        {
            var gate = Gate();
            gate.Register(Camera(0, 0), Signature(0), true, Map(), 100, true, true, 1, 1);
            var result = gate.Register(Camera(0, 0), Signature(0), true, Map(5), 60, false, false, 2, 2);
            Assert.That(result.Trace.retained[0].poseAttemptId, Is.EqualTo(2));
            Assert.That(result.Trace.retained[0].hasStrongMatch, Is.False);
            Assert.That(result.Trace.retained[0].hasContext, Is.False);
            Assert.That(result.Trace.poseAction, Is.EqualTo("replaced_with_latest_pose"));
        }

        [Test]
        public void FailedSameViewPreservesOldPoseWithoutBorrowingContextOrRefreshingAge()
        {
            var gate = Gate();
            Put(gate, 0, 1, context: false);
            var result = gate.Register(Camera(0, 0), Signature(0), false, Map(), 0, false, true, 2, 2);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.retained[0].hasMapPose, Is.True);
            Assert.That(result.Trace.retained[0].poseAttemptId, Is.EqualTo(1));
            Assert.That(result.Trace.retained[0].poseAgeSeconds, Is.EqualTo(1));
            Assert.That(result.Trace.retained[0].hasContext, Is.False);
            Assert.That(result.Trace.contextAdded, Is.False);
            Assert.That(result.Trace.poseAction, Is.EqualTo("miss_preserves_unexpired_evidence"));
        }

        [Test]
        public void FailedCurrentFrameCannotReturnAnExistingConfirmedGroup()
        {
            var gate = Gate();
            for (int i = 0; i < 5; i++) Put(gate, i, i + 1);
            var result = Put(gate, 5, 5.5, accepted: false);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.Trace.currentSupportsGroup, Is.False);
        }

        [Test]
        public void CurrentOutlierCannotReturnAnOlderStableGroup()
        {
            var gate = Gate();
            for (int i = 0; i < 5; i++) Put(gate, i, i + 1);
            var result = Put(gate, 5, 5.5, mapX: 5);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void ReplacementWithDuplicateImageCannotCountTwoVotes()
        {
            var gate = Gate();
            Put(gate, 0, 1);
            Put(gate, 1, 2);
            // A fresh result from view 1 replaces that view's evidence, but its
            // image now matches view 0 and cannot provide a second stable vote.
            var result = gate.Register(Camera(0, 6), Signature(0), true, Map(), 95, true, true, 3, 3);
            Assert.That(result.StableCount, Is.EqualTo(1));
        }

        [Test]
        public void WindowEvictsUnmatchedViewsBeforeAcceptedEvidence()
        {
            var gate = Gate(age: 100, gap: 100);
            Put(gate, 0, 0);
            LocalizationGateResult result = default;
            for (int i = 1; i <= 9; i++)
                result = gate.Register(Camera(i, i * 10), (ulong)i * 0x9e3779b97f4a7c15UL,
                    false, Map(), 0, false, false, i + 1, i * .1);
            Assert.That(result.ObservationCount, Is.LessThanOrEqualTo(9));
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.Trace.retained[0].poseAttemptId, Is.EqualTo(1));
            Assert.That(result.Trace.removed[0].removalReason, Is.EqualTo("window_removes_unmatched_observation"));
        }

        [Test]
        public void InconsistentMapPositionsCannotFormFiveVotes()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++) result = Put(gate, i, i + 1, mapX: i * .2f);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void InconsistentMapRotationsCannotFormFiveVotes()
        {
            var gate = Gate();
            LocalizationGateResult result = default;
            for (int i = 0; i < 5; i++)
                result = gate.Register(Camera(0, i * 6), Signature(i), true,
                    Camera(0, i * 3), 90, true, true, i + 1, i + 1);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(result.IsConfirmed, Is.False);
        }

        [Test]
        public void ResetClearsCandidateClockAndIds()
        {
            var gate = Gate();
            Put(gate, 0, 100);
            gate.Reset();
            var result = Put(gate, 0, 1);
            Assert.That(result.StableCount, Is.EqualTo(1));
            Assert.That(gate.TotalDistinctObservationCount, Is.EqualTo(1));
        }

        [Test]
        public void ClockMustBeFiniteAndMonotonic()
        {
            var gate = Gate();
            Put(gate, 0, 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => gate.AdvanceTime(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => gate.AdvanceTime(double.NaN));
        }
    }
}
