using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class LocalizationCoordinateInvariantTests
    {
        // The production iOS initial-confirmation thresholds. These tests use
        // the real gate; synthetic poses isolate coordinate-system behavior.
        private static LocalizationConfirmationGate InitialGate() =>
            new LocalizationConfirmationGate(5, 9, .05f, 4f, 10, .075f, 3f, .15f, 12f,
                captureDiagnostics: true, minimumStrongMatches: 2, minimumContextMatches: 2,
                minimumConfirmationSeconds: 3, maximumObservationAgeSeconds: 20,
                maximumEvidenceGapSeconds: 20);

        private static LocalizationConfirmationGate PairGate() =>
            new LocalizationConfirmationGate(2, 2, 0, 0, 0, .075f, 3f, 0, 0,
                captureDiagnostics: true, minimumStrongMatches: 1, minimumContextMatches: 1,
                minimumConfirmationSeconds: 1, maximumObservationAgeSeconds: 20, maximumEvidenceGapSeconds: 20,
                minimumIndependentObservationSeconds: .1);

        private static Pose Yaw(float degrees, float x = 0) =>
            new Pose(new Vector3(x, 0, 0), Quaternion.Euler(0, degrees, 0));

        // Coordinate changes are composed independently of the gate's distance
        // implementation: left multiplication changes tracking coordinates;
        // right multiplication relabels the map coordinates.
        private static Pose Compose(Pose a, Pose b) =>
            new Pose(a.position + a.rotation * b.position, a.rotation * b.rotation);

        private static LocalizationGateResult InitialResult(Pose[] candidates,
            Pose mapCoordinates, Pose trackingCoordinates)
        {
            var gate = InitialGate();
            LocalizationGateResult result = default;
            for (int i = 0; i < candidates.Length; i++)
            {
                Pose camera = Compose(trackingCoordinates, Yaw(i * 6f, i * .06f));
                Pose candidate = Compose(trackingCoordinates, Compose(candidates[i], mapCoordinates));
                result = gate.Register(camera, 255UL << (i * 8), true, candidate,
                    90, true, true, i + 1, i * .75d);
            }
            return result;
        }

        private static LocalizationGateResult PairResult(Pose first, Pose second,
            Vector3 firstCamera, Vector3 secondCamera)
        {
            var gate = PairGate();
            gate.Register(new Pose(firstCamera, Quaternion.identity), 1, true,
                first, 90, true, true, 1, 0);
            return gate.Register(new Pose(secondCamera, Quaternion.identity), 2, true,
                second, 90, true, true, 2, 1);
        }

        private static Pose[] LocalAngularNoise() => new[]
        {
            Pose.identity, Pose.identity, Pose.identity, Pose.identity, Yaw(2)
        };

        private static void AssertSameDecision(LocalizationGateResult expected,
            LocalizationGateResult actual)
        {
            Assert.That(actual.IsConfirmed, Is.EqualTo(expected.IsConfirmed));
            Assert.That(actual.StableCount, Is.EqualTo(expected.StableCount));
            Assert.That(actual.Trace.stableObservationIds,
                Is.EqualTo(expected.Trace.stableObservationIds));
            Assert.That(actual.Trace.medoidObservationId,
                Is.EqualTo(expected.Trace.medoidObservationId));
        }

        private static void AssertPose(Pose expected, Pose actual)
        {
            Assert.That(Vector3.Distance(expected.position, actual.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(expected.rotation, actual.rotation), Is.LessThan(.05f));
        }

        [Test]
        public void MapCoordinateTranslationAndRotationPreserveConfirmationAndOutput()
        {
            Pose[] candidates = LocalAngularNoise();
            Pose relabel = new Pose(new Vector3(0, .4f, 8), Quaternion.Euler(11, 37, -8));
            LocalizationGateResult original = InitialResult(candidates, Pose.identity, Pose.identity);
            LocalizationGateResult relabeled = InitialResult(candidates, relabel, Pose.identity);

            Assert.That(original.IsConfirmed, Is.True);
            AssertSameDecision(original, relabeled);
            AssertPose(Compose(original.ConfirmedPose, relabel), relabeled.ConfirmedPose);
        }

        [Test]
        public void TrackingWorldRigidTransformPreservesConfirmationAndOutput()
        {
            Pose[] candidates = LocalAngularNoise();
            Pose changedWorld = new Pose(new Vector3(2, .4f, -1), Quaternion.Euler(11, 37, -8));
            LocalizationGateResult original = InitialResult(candidates, Pose.identity, Pose.identity);
            LocalizationGateResult transformed = InitialResult(candidates, Pose.identity, changedWorld);

            Assert.That(original.IsConfirmed, Is.True);
            AssertSameDecision(original, transformed);
            AssertPose(Compose(changedWorld, original.ConfirmedPose), transformed.ConfirmedPose);
        }

        [Test]
        public void FarMapOriginDoesNotAmplifyLocalTwoDegreeNoiseIntoMissingFifthVote()
        {
            // Moving only the map origin 8 m makes its positions differ by
            // 2 * 8 * sin(1 degree) = 27.92385 cm. At the observed camera
            // endpoints (0..24 cm), disagreement is at most 0.83772 cm.
            Pose relabel = new Pose(new Vector3(0, 0, 8), Quaternion.identity);
            LocalizationGateResult result = InitialResult(LocalAngularNoise(), relabel, Pose.identity);

            Assert.That(result.StableCount, Is.EqualTo(5),
                "Relabeling a distant map origin must not remove a locally consistent fifth observation.");
            Assert.That(result.IsConfirmed, Is.True);
            Assert.That(result.HasStrongMatch, Is.True);
            Assert.That(result.HasViewDiversity, Is.True);
        }

        [Test]
        public void MapRelabelingPreservesMedoidWhenAllCandidatesAlreadyPass()
        {
            // All pairs fit the old origin-position bound both before and
            // after relabeling. Its old ranking nevertheless chose observation
            // 4 before and 2 after, so eligibility alone is insufficient.
            Pose[] candidates =
            {
                Yaw(-.2f, -.007f), Yaw(-.1f, .009f), Yaw(0, -.011f),
                Yaw(.1f, -.007f), Yaw(.2f, -.003f)
            };
            Pose relabel = new Pose(new Vector3(0, 0, 8), Quaternion.Euler(0, 37, 0));
            LocalizationGateResult original = InitialResult(candidates, Pose.identity, Pose.identity);
            LocalizationGateResult relabeled = InitialResult(candidates, relabel, Pose.identity);

            Assert.That(original.IsConfirmed, Is.True);
            Assert.That(relabeled.IsConfirmed, Is.True);
            AssertSameDecision(original, relabeled);
            AssertPose(Compose(original.ConfirmedPose, relabel), relabeled.ConfirmedPose);
        }

        [TestCase(.074f, true)]
        [TestCase(.08f, false)]
        public void ActualLocalTranslationStillUsesSevenPointFiveCentimeterLimit(float meters, bool expected)
        {
            LocalizationGateResult result = PairResult(Pose.identity, Yaw(0, meters),
                new Vector3(-1, .5f, 2), new Vector3(1, .5f, 2));

            Assert.That(result.IsConfirmed, Is.EqualTo(expected));
            Assert.That(result.StableCount, Is.EqualTo(expected ? 2 : 1));
        }

        [Test]
        public void RotationBeyondThreeDegreesIsRejectedEvenAtZeroPositionDisagreement()
        {
            LocalizationGateResult result = PairResult(Pose.identity, Yaw(3.2f),
                Vector3.zero, Vector3.zero);

            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(1));
        }

        [Test]
        public void OppositeCameraEndpointsCannotCancelTheirErrorAtTheMidpoint()
        {
            // The midpoint is zero and therefore has zero error. At each
            // endpoint the 2.8-degree rotation disagrees by 7.8183 cm, while
            // still satisfying the independent 3-degree rotation threshold.
            LocalizationGateResult result = PairResult(Pose.identity, Yaw(2.8f),
                new Vector3(-1.6f, 0, 0), new Vector3(1.6f, 0, 0));

            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EitherCameraEndpointCanExposeExcessiveDisagreement(bool reverseEndpoints)
        {
            // One endpoint has zero disagreement and the other 7.8183 cm;
            // the midpoint is only 3.9091 cm. Checking either one fixed
            // endpoint, or their midpoint, is insufficient.
            Vector3 near = Vector3.zero;
            Vector3 far = new Vector3(1.6f, 0, 0);
            LocalizationGateResult result = PairResult(Pose.identity, Yaw(2.8f),
                reverseEndpoints ? far : near, reverseEndpoints ? near : far);

            Assert.That(result.IsConfirmed, Is.False);
            Assert.That(result.StableCount, Is.EqualTo(1));
        }
    }
}
