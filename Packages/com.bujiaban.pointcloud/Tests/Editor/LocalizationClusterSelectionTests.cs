using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class LocalizationClusterSelectionTests
    {
        // Initial-gate values from the shipped iOS and Rokid profiles. Keep this
        // core regression independent of the vendor package and project assets.
        private static LocalizationConfirmationGate Gate(bool rokid) =>
            new LocalizationConfirmationGate(5, 9, .05f, rokid ? 3 : 4,
                rokid ? 8 : 10, rokid ? .1f : .075f, rokid ? 4 : 3, .15f, 12,
                true, 2, 2, rokid ? 2.5 : 3, rokid ? 25 : 20, rokid ? 10 : 8);

        private static LocalizationGateResult Put(LocalizationConfirmationGate gate,
            int view, int attempt, double time, float mapX, bool strong, bool accepted = true) =>
            gate.Register(new Pose(Vector3.zero, Quaternion.Euler(0, view * 6, 0)),
                255UL << (view * 8), accepted,
                new Pose(new Vector3(mapX, 0, 0), Quaternion.identity),
                strong ? 100 : 60, strong && accepted, true, attempt, time);

        private static LocalizationGateResult SharedGroups(bool rokid, bool extraWeakViews,
            bool currentStrong = true, bool currentAccepted = true)
        {
            var gate = Gate(rokid);
            // G is strong at -6 cm. A-D are weak at 0 cm, E-F weak at +6 cm.
            // A-D fit either cluster; E-F cannot fit G under either profile.
            Assert.That(Put(gate, 0, 1, 0, -.06f, true).IsConfirmed, Is.False);
            for (int view = 1; view <= 4; view++)
                Assert.That(Put(gate, view, view + 1, view * .75, 0, false).IsConfirmed, Is.False);
            if (extraWeakViews)
                for (int view = 5; view <= 6; view++)
                    Assert.That(Put(gate, view, view + 1, view * .75, .06f, false).IsConfirmed, Is.False);

            // A fresh result from A supplies the second strong match. G+A-D
            // satisfy every gate, while the larger A-F group has only one strong.
            return Put(gate, 1, 8, 5.25, 0, currentStrong, currentAccepted);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FiveViewsWithTwoStrongMatchesConfirm(bool rokid)
        {
            var result = SharedGroups(rokid, false);
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.Trace.strongMatchCount, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LargerWeakGroupCannotHideQualifiedGroupSharingTheCurrentView(bool rokid)
        {
            var result = SharedGroups(rokid, true);
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.StableCount, Is.EqualTo(5));
            Assert.That(result.Trace.strongMatchCount, Is.EqualTo(2));
            Assert.That(result.Trace.currentSupportsGroup, Is.True);
            Assert.That(result.Trace.stableObservationIds, Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NoQualifiedGroupKeepsTheLargestGroupForDiagnostics(bool rokid)
        {
            var result = SharedGroups(rokid, true, currentStrong: false);
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(6));
            Assert.That(result.Trace.stableObservationIds, Is.EqualTo(new long[] { 2, 3, 4, 5, 6, 7 }));
            Assert.That(result.Trace.blockReason, Is.EqualTo("insufficient_strong_matches"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CurrentMissCannotSupplyTheMissingStrongMatch(bool rokid)
        {
            var result = SharedGroups(rokid, true, currentAccepted: false);
            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(6));
            Assert.That(result.Trace.currentSupportsGroup, Is.False);
            Assert.That(result.Trace.blockReason, Is.EqualTo("current_result_not_in_stable_group"));
        }
    }
}
