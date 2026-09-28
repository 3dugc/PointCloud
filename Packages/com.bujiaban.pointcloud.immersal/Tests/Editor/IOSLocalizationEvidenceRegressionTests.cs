using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class IOSLocalizationEvidenceRegressionTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [TestCase(100, 1.127, true, true)]
        [TestCase(82, 1.059, true, true)]
        [TestCase(77, 1.059, true, false)]
        [TestCase(49, 1.059, false, false)]
        [TestCase(100, 1.201, false, false)]
        [TestCase(100, double.NaN, false, false)]
        public void ActualIOSAssetClassifiesQualityWithoutDiscardingHighConfidenceAtValidRmse(
            int confidence, double rmse, bool reliable, bool strong)
        {
            var policy = LoadActualIOSProfile();
            Assert.That(policy.MinimumStrongMatches, Is.EqualTo(1));
            Assert.That(policy.MinimumStrongLocalizationConfidence, Is.EqualTo(80));
            Assert.That(ImmersalPosePolicy.IsReliable(policy, confidence, rmse), Is.EqualTo(reliable));
            Assert.That(ImmersalPosePolicy.IsStrong(policy, confidence, rmse), Is.EqualTo(strong));
        }

        [Test]
        public void FourViewsSurviveEightSecondMissGapThenExpireIndividuallyAtTheirOriginalTwentySecondAges()
        {
            var policy = LoadActualIOSProfile();
            Assert.That(policy.MaximumObservationAgeSeconds, Is.EqualTo(20));
            Assert.That(policy.MaximumEvidenceGapSeconds, Is.EqualTo(policy.MaximumObservationAgeSeconds));
            object gate = CreateGate(policy);
            for (int view = 0; view < 4; view++)
                Register(gate, view, view + 1, view, true, false);
            Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(4));

            Assert.That(Advance(gate, 11), Is.False, "Eight seconds after the latest accepted pose must not erase four fresh views.");
            object miss = Register(gate, 0, 5, 11, false, false);
            Assert.That(Member<int>(miss, "StableCount"), Is.EqualTo(4));
            Assert.That(Member<bool>(miss, "IsConfirmed"), Is.False);
            object trace = Member<object>(miss, "Trace");
            Assert.That(Member<string>(trace, "poseAction"), Is.EqualTo("miss_preserves_unexpired_evidence"));
            Array retained = Member<Array>(trace, "retained");
            Assert.That(Member<long>(retained.GetValue(0), "poseAttemptId"), Is.EqualTo(1));
            Assert.That(Member<double>(retained.GetValue(0), "poseAgeSeconds"), Is.EqualTo(11));

            Register(gate, 0, 6, 19.99, false, false);
            Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(4));
            for (int view = 0; view < 4; view++)
            {
                Assert.That(Advance(gate, 20 + view), Is.True);
                Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(3 - view),
                    "Misses must not rejuvenate an accepted pose; each view expires at its own source timestamp.");
                object expiry = Member<object>(gate, "LastExpiryTrace");
                Array removed = Member<Array>(expiry, "removed");
                Assert.That(removed.Length, Is.EqualTo(1));
                Assert.That(Member<long>(removed.GetValue(0), "poseAttemptId"), Is.EqualTo(view + 1));
                Assert.That(Member<double>(removed.GetValue(0), "poseAgeSeconds"), Is.EqualTo(20));
                if (view < 3)
                    Assert.That(Member<string>(removed.GetValue(0), "removalReason"), Is.EqualTo("observation_age_expired"));
            }
        }

        [TestCase(true, 1, 4)]
        [TestCase(false, 5, 0)]
        public void ResultCrossingOldGapDeadlineReportsOneDirectlyButCurrentProfileRetainsEvidence(
            bool useOldGap, int expectedCount, int expectedRemoved)
        {
            var policy = LoadActualIOSProfile();
            if (useOldGap) policy.MaximumEvidenceGapSeconds = 8;
            object gate = CreateGate(policy);
            for (int view = 0; view < 4; view++)
                Register(gate, view, view + 1, view, true, false);
            Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(4));

            // Mirrors acquisition starting before the old deadline, then the
            // native result returning after it. Only Register's final count is
            // reported to the caller; its internal empty state is not a report.
            Assert.That(Advance(gate, 10.9), Is.False);
            object result = Register(gate, 4, 5, 11.1, true, false);
            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(expectedCount));
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.False,
                "Retaining five ordinary observations must not bypass the strong-match requirement.");
            Array removed = Member<Array>(Member<object>(result, "Trace"), "removed");
            Assert.That(removed.Length, Is.EqualTo(expectedRemoved));
            foreach (object sample in removed)
                Assert.That(Member<string>(sample, "removalReason"), Is.EqualTo("evidence_gap_expired"));
        }

        [Test]
        public void ExpiringThreeOlderViewsCanLegitimatelyLeaveOneFreshView()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            Register(gate, 0, 1, 0, true, false);
            Register(gate, 1, 2, 1, true, false);
            Register(gate, 2, 3, 2, true, false);
            Register(gate, 3, 4, 10, true, false);
            Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(4));

            // If checks resume after several deadlines, three genuinely old
            // samples expire together. The remaining sample must stay valid.
            Assert.That(Advance(gate, 22), Is.True);
            Assert.That(Member<int>(gate, "StableCount"), Is.EqualTo(1));
            object trace = Member<object>(gate, "LastExpiryTrace");
            Array removed = Member<Array>(trace, "removed");
            Assert.That(removed.Length, Is.EqualTo(3));
            foreach (object sample in removed)
                Assert.That(Member<string>(sample, "removalReason"), Is.EqualTo("observation_age_expired"));
            Array retained = Member<Array>(trace, "retained");
            Assert.That(retained.Length, Is.EqualTo(1));
            Assert.That(Member<long>(retained.GetValue(0), "poseAttemptId"), Is.EqualTo(4));
        }

        [Test]
        public void RepresentativeLoggedQualityPairsConfirmFiveSyntheticViewsWithTwoStrongMatches()
        {
            // These confidence/RMSE pairs appeared in the supplied device log.
            // Camera poses, signatures, timestamps and map poses below are
            // deliberately synthetic: this is a gate regression, not image replay.
            int[] confidence = { 63, 67, 100, 64, 82 };
            double[] rmse = { 1.096, 1.093, 1.127, 1.024, 1.059 };
            var policy = LoadActualIOSProfile();
            object gate = CreateGate(policy);
            object result = null;
            for (int view = 0; view < confidence.Length; view++)
            {
                bool reliable = ImmersalPosePolicy.IsReliable(policy, confidence[view], rmse[view]);
                bool strong = reliable && ImmersalPosePolicy.IsStrong(policy, confidence[view], rmse[view]);
                result = Register(gate, view, view + 1, view, reliable, strong);
                if (view < 4)
                    Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
            }
            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<int>(Member<object>(result, "Trace"), "strongMatchCount"), Is.EqualTo(2));
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.True);
        }

        [Test]
        public void ActualIOSPolicyKeepsFiveViewsAndQualityGeometryAndDurationThresholds()
        {
            var policy = LoadActualIOSProfile();
            Assert.That(policy.MinimumStrongMatches, Is.EqualTo(1));
            Assert.That(policy.RequiredStableSamples, Is.EqualTo(5));
            Assert.That(policy.MinimumLocalizationConfidence, Is.EqualTo(50));
            Assert.That(policy.MinimumStrongLocalizationConfidence, Is.EqualTo(80));
            Assert.That(policy.MaximumLocalizationRmse, Is.EqualTo(1.2));
            Assert.That(policy.MaximumStrongLocalizationRmse, Is.EqualTo(1.2));
            Assert.That(policy.MaxPositionDrift, Is.EqualTo(.075f));
            Assert.That(policy.MaxRotationDriftDegrees, Is.EqualTo(3));
            Assert.That(policy.MinimumViewPositionDelta, Is.EqualTo(.05f));
            Assert.That(policy.MinimumViewRotationDelta, Is.EqualTo(4));
            Assert.That(policy.MinimumFrameSignatureDistance, Is.EqualTo(10));
            Assert.That(policy.MinimumConfirmationPositionSpan, Is.EqualTo(.15f));
            Assert.That(policy.MinimumConfirmationRotationSpan, Is.EqualTo(12));
            Assert.That(policy.MinimumContextMatches, Is.EqualTo(2));
            Assert.That(policy.MinimumConfirmationSeconds, Is.EqualTo(3));
        }

        [Test]
        public void LatestLoggedQualityPairsConfirmFiveSyntheticViewsWithExactlyOneStrongMatch()
        {
            // Only confidence/RMSE pairs come from the 63ff2915 device log
            // (attempts 29, 24, 26, 21, 31). Poses, view signatures and times
            // are synthetic and deliberately satisfy the other gate conditions.
            int[] confidence = { 71, 61, 58, 73, 83 };
            double[] rmse = { 1.149, 1.128, 1.118, 1.119, 1.133 };
            var policy = LoadActualIOSProfile();
            object gate = CreateGate(policy);
            object result = null;
            for (int view = 0; view < confidence.Length; view++)
            {
                bool reliable = ImmersalPosePolicy.IsReliable(policy, confidence[view], rmse[view]);
                bool strong = reliable && ImmersalPosePolicy.IsStrong(policy, confidence[view], rmse[view]);
                Assert.That(reliable, Is.True);
                result = Register(gate, view, view + 1, view, reliable, strong,
                    qualityScore: confidence[view] - (float)rmse[view]);
                if (view < 4)
                    Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
            }

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<int>(Member<object>(result, "Trace"), "strongMatchCount"), Is.EqualTo(1));
            Assert.That(Member<bool>(result, "HasViewDiversity"), Is.True);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.True);
        }

        [Test]
        public void FourStrongViewsCannotReplaceTheRequiredFifthObservation()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            object result = null;
            for (int view = 0; view < 4; view++)
                result = Register(gate, view, view + 1, view, true, true);

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(4));
            Assert.That(Member<bool>(result, "HasStrongMatch"), Is.True);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
        }

        [Test]
        public void FiveConsistentViewsWithoutAnyStrongMatchRemainUnconfirmed()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            object result = null;
            for (int view = 0; view < 5; view++)
                result = Register(gate, view, view + 1, view, true, false);

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<bool>(result, "HasViewDiversity"), Is.True);
            Assert.That(Member<int>(Member<object>(result, "Trace"), "strongMatchCount"), Is.Zero);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
            Assert.That(Member<PointCloudConfirmationRequirement>(gate, "PendingRequirement"),
                Is.EqualTo(PointCloudConfirmationRequirement.StrongerMatch));
        }

        [Test]
        public void CurrentMissCannotConfirmFiveRetainedViewsWithOneStrongMatch()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            ConfirmWithOneStrongMatch(gate);
            object miss = Register(gate, 4, 6, 5, false, false);

            Assert.That(Member<int>(miss, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<bool>(miss, "HasStrongMatch"), Is.True);
            Assert.That(Member<bool>(miss, "IsConfirmed"), Is.False);
            Assert.That(Member<PointCloudConfirmationRequirement>(gate, "PendingRequirement"),
                Is.EqualTo(PointCloudConfirmationRequirement.CurrentMatch));
        }

        [Test]
        public void CurrentConflictingPoseCannotBorrowFiveRetainedViewsWithOneStrongMatch()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            ConfirmWithOneStrongMatch(gate);
            object conflict = Register(gate, 5, 6, 5, true, true,
                new Pose(new Vector3(.08f, 0, 0), Quaternion.identity));

            Assert.That(Member<int>(conflict, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<bool>(conflict, "HasStrongMatch"), Is.True);
            Assert.That(Member<bool>(conflict, "IsConfirmed"), Is.False);
            Assert.That(Member<int>(Member<object>(conflict, "Trace"), "currentPositionConflicts"), Is.EqualTo(5));
            Assert.That(Member<PointCloudConfirmationRequirement>(gate, "PendingRequirement"),
                Is.EqualTo(PointCloudConfirmationRequirement.PoseDisagreement));
        }

        [Test]
        public void RepeatedInitialViewCannotManufactureFiveVotesEvenWithStrongMatches()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            object result = null;
            for (int attempt = 1; attempt <= 5; attempt++)
                result = Register(gate, 0, attempt, attempt, true, true);

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(1));
            Assert.That(Member<int>(result, "ObservationCount"), Is.EqualTo(1));
            Assert.That(Member<bool>(result, "HasStrongMatch"), Is.True);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
        }

        [Test]
        public void FiveViewsWithOneStrongMatchStillNeedThreeSecondsOfEvidence()
        {
            object gate = CreateGate(LoadActualIOSProfile());
            object result = null;
            for (int view = 0; view < 5; view++)
                result = Register(gate, view, view + 1, view * .5d, true, view == 4);

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(5));
            Assert.That(Member<bool>(result, "HasStrongMatch"), Is.True);
            Assert.That(Member<bool>(result, "HasViewDiversity"), Is.True);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
            Assert.That(Member<PointCloudConfirmationRequirement>(gate, "PendingRequirement"),
                Is.EqualTo(PointCloudConfirmationRequirement.ObservationDuration));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MaintenanceAfterInitialConfirmationNeedsThreeFreshVotesAndItsOwnStrongMatch(bool hasFreshStrong)
        {
            var policy = LoadActualIOSProfile();
            Assert.That(policy.MaintenanceRequiredStableSamples, Is.EqualTo(3));
            var maintenance = new ImmersalMaintenanceState(policy, true);
            object initialGate = Member<object>(maintenance, "Gate");
            object initial = ConfirmWithOneStrongMatch(initialGate);
            Assert.That(maintenance.Accept(Member<Pose>(initial, "ConfirmedPose"), 4), Is.EqualTo("initial"));
            object gate = Member<object>(maintenance, "Gate");
            Assert.That(gate, Is.Not.SameAs(initialGate));
            Assert.That(Member<int>(gate, "StableCount"), Is.Zero);

            object result = null;
            for (int capture = 0; capture < 3; capture++)
            {
                // Ordinary maintenance permits the same view, while capture
                // times remain separated by the actual acquisition cadence.
                result = Register(gate, 0, capture + 6,
                    5 + capture * policy.MaintenanceAttemptIntervalSeconds,
                    true, hasFreshStrong && capture == 0);
                if (capture < 2)
                    Assert.That(Member<bool>(result, "IsConfirmed"), Is.False);
            }

            Assert.That(Member<int>(result, "StableCount"), Is.EqualTo(3));
            Assert.That(Member<int>(Member<object>(result, "Trace"), "strongMatchCount"),
                Is.EqualTo(hasFreshStrong ? 1 : 0));
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.EqualTo(hasFreshStrong),
                "The initial strong observation must not carry over into the fresh maintenance gate.");
        }

        private static object ConfirmWithOneStrongMatch(object gate)
        {
            object result = null;
            for (int view = 0; view < 5; view++)
                result = Register(gate, view, view + 1, view, true, view == 4);
            Assert.That(Member<bool>(result, "IsConfirmed"), Is.True);
            Assert.That(Member<int>(Member<object>(result, "Trace"), "strongMatchCount"), Is.EqualTo(1));
            return result;
        }

        private static ImmersalLocalizationProfile.Settings LoadActualIOSProfile()
        {
            foreach (string guid in AssetDatabase.FindAssets("iOSLocalizationProfile t:ImmersalLocalizationProfile"))
            {
                var profile = AssetDatabase.LoadAssetAtPath<ImmersalLocalizationProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null && profile.name == "iOSLocalizationProfile") return profile.CreateSnapshot();
            }
            Assert.Ignore("This device-asset regression requires the real iOS project profile or its Minimal distribution asset.");
            return null;
        }

        private static object CreateGate(ImmersalLocalizationProfile.Settings policy)
        {
            // Core's internal gate stays outside the public API and does not
            // need a new friend assembly just for this platform-asset test.
            MethodInfo factory = typeof(ImmersalPosePolicy).GetMethod("CreateInitialConfirmationGate",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(factory, Is.Not.Null);
            return factory.Invoke(null, new object[] { policy, true });
        }

        private static object Register(object gate, int view, long attempt, double time, bool reliable, bool strong,
            Pose? mapPose = null, float? qualityScore = null)
        {
            Pose camera = new Pose(Vector3.up * 1.5f, Quaternion.Euler(0, view * 8, 0));
            return gate.GetType().GetMethod("Register", Members).Invoke(gate, new object[]
            {
                camera, 255UL << (view * 8), reliable, mapPose ?? Pose.identity,
                qualityScore ?? (strong ? 90f : 60f), strong, true, attempt, time
            });
        }

        private static bool Advance(object gate, double time) =>
            (bool)gate.GetType().GetMethod("AdvanceTime", Members).Invoke(gate, new object[] { time });

        private static T Member<T>(object value, string name)
        {
            PropertyInfo property = value.GetType().GetProperty(name, Members);
            return (T)(property != null ? property.GetValue(value) : value.GetType().GetField(name, Members).GetValue(value));
        }
    }
}
