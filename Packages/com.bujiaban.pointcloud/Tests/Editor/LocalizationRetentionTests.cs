using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class LocalizationRetentionTests
    {
        private static LocalizationConfirmationGate Gate(bool temporal = false) =>
            new LocalizationConfirmationGate(3, 9, temporal ? 0 : .05f, temporal ? 0 : 3,
                temporal ? 0 : 8, .1f, 4, temporal ? 0 : .15f, temporal ? 0 : 12,
                true, 2, 2, 2.5, 25, 10,
                minimumIndependentObservationSeconds: temporal ? 1 : 0);

        private static LocalizationGateResult Put(LocalizationConfirmationGate gate,
            long attempt, double time, float x, float yaw, ulong signature, bool success = true, float mapX = 0) =>
            gate.Register(new Pose(new Vector3(x, 0, 0), Quaternion.Euler(0, yaw, 0)), signature,
                success, new Pose(new Vector3(mapX, 0, 0), Quaternion.identity), 100,
                success, true, attempt, time);

        [Test]
        public void StationaryMaintenanceConfirmsButInitialLocalizationDoesNot()
        {
            foreach (bool temporal in new[] { false, true })
            {
                var gate = Gate(temporal);
                Put(gate, 1, 0, 0, 0, 255);
                Put(gate, 2, 2, 0, 0, 255);
                var result = Put(gate, 3, 4, 0, 0, 255);
                Assert.That(result.IsConfirmed, Is.EqualTo(temporal));
            }
        }

        [Test]
        public void SameAttemptOrBurstCannotFillMaintenanceVotes()
        {
            var gate = Gate(true);
            for (int i = 0; i < 8; i++) Put(gate, 1, i * 2, 0, 0, 255);
            Assert.That(gate.StableCount, Is.EqualTo(1));
            gate = Gate(true);
            for (int i = 0; i < 8; i++) Put(gate, i + 1, i * .01, 0, 0, 255);
            Assert.That(gate.StableCount, Is.EqualTo(1));
        }

        [Test]
        public void ShortMissDoesNotEraseOrRefreshOldEvidenceAndCannotConfirm()
        {
            var gate = Gate();
            Put(gate, 1, 0, 0, 0, 255);
            Put(gate, 2, 1, 0, 8, 255UL << 8);
            var miss = Put(gate, 3, 5, 0, 8, 255UL << 8, false);
            Assert.That(miss.StableCount, Is.EqualTo(2));
            Assert.That(miss.IsConfirmed, Is.False);
            Assert.That(miss.Trace.poseAction, Is.EqualTo("miss_preserves_unexpired_evidence"));
            Assert.That(Put(gate, 4, 6, .5f, 45, 255UL << 16).IsConfirmed, Is.True);
            gate.AdvanceTime(17);
            Assert.That(gate.StableCount, Is.Zero, "Misses must not extend evidence forever");
        }

        [Test]
        public void LargeViewChangeWithSimilarImageDoesNotOverwriteEarlierPose()
        {
            var gate = Gate();
            Put(gate, 1, 0, 0, 0, 255);
            var moved = Put(gate, 2, 1, 1, 60, 255, false);
            Assert.That(moved.IsNewObservation, Is.True);
            Assert.That(moved.StableCount, Is.EqualTo(1));
            Assert.That(moved.IsConfirmed, Is.False);
        }

        [Test]
        public void ManyUnmatchedViewsCannotEvictUsefulEvidenceBeforeItsAgeLimit()
        {
            var gate = Gate();
            Put(gate, 1, 0, 0, 0, 255);
            Put(gate, 2, 1, 0, 8, 255UL << 8);
            for (int i = 0; i < 15; i++)
                Put(gate, i + 3, 2 + i * .2, 1 + i, 45, 255UL << 16, false);
            Assert.That(gate.StableCount, Is.EqualTo(2));
            Assert.That(Put(gate, 20, 6, .5f, 45, 255UL << 24).IsConfirmed, Is.True);
        }

        [Test]
        public void OlderTighterClusterDoesNotHideANewSufficientCluster()
        {
            var gate = Gate();
            Put(gate, 1, 0, 0, 0, 255);
            Put(gate, 2, 1, 0, 8, 255UL << 8);
            Put(gate, 3, 3, 0, 16, 255UL << 16);
            Put(gate, 4, 4, 1, 24, 255UL << 24, true, 1);
            Put(gate, 5, 5, 1, 32, 255UL << 32, true, 1.01f);
            var latest = Put(gate, 6, 7, 1, 40, 255UL << 40, true, 1.02f);
            Assert.That(latest.IsConfirmed, Is.True);
            Assert.That(latest.ConfirmedPose.position.x, Is.GreaterThan(.9f));
            Assert.That(latest.StableCount, Is.EqualTo(3), "Conflicting clusters must never be combined");
        }

        [Test]
        public void ContradictoryReturnedPoseStillReplacesAndDoesNotBorrowOldConfirmation()
        {
            var gate = Gate();
            Put(gate, 1, 0, 0, 0, 255);
            Put(gate, 2, 1, 0, 8, 255UL << 8);
            Put(gate, 3, 2, 0, 8, 255UL << 8, true, 1);
            Assert.That(Put(gate, 4, 3, .5f, 45, 255UL << 16).IsConfirmed, Is.False);
        }
    }
}
