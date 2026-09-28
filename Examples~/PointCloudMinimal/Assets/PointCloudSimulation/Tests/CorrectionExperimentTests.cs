using System;
using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation.Tests
{
    public sealed class CorrectionExperimentTests
    {
        [Test]
        public void TranslatedRotatedTrackingFrameHasAnalyticPhysicalCoordinates()
        {
            var trackingFromWorld = new Pose(new Vector3(2, .4f, -1), Quaternion.Euler(0, 37, 0));
            var physicalPose = new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, -14, 0));
            float sine = Mathf.Sin(37 * Mathf.Deg2Rad);
            float cosine = Mathf.Cos(37 * Mathf.Deg2Rad);

            Pose observed = CorrectionExperiment.Compose(trackingFromWorld, physicalPose);

            AssertPosition(observed.position,
                new Vector3(2 + cosine + 3 * sine, 2.4f, -1 - sine + 3 * cosine));
            Assert.That(Quaternion.Angle(observed.rotation, Quaternion.Euler(0, 23, 0)), Is.LessThan(.03f));
            Pose inverse = CorrectionExperiment.Inverse(trackingFromWorld);
            AssertPosition(inverse.position, new Vector3(-2 * cosine - sine, -.4f, -2 * sine + cosine));
            Pose restored = CorrectionExperiment.Compose(inverse, observed);
            AssertPosition(restored.position, physicalPose.position);
            Assert.That(Quaternion.Angle(restored.rotation, physicalPose.rotation), Is.LessThan(.03f));
        }

        [Test]
        public void ChangingPublishedOutputCannotChangeSyntheticMeasurementAtTheSamePhysicalTime()
        {
            foreach (DriftScenario scenario in new[] { DriftScenario.Noisy, DriftScenario.PersistentWrong })
            {
                var first = new CorrectionExperiment(scenario);
                var second = new CorrectionExperiment(scenario);
                first.Record(0, true, Pose.identity, PointCloudLocalizationStage.Tracking);
                second.Record(0, true, Pose.identity, PointCloudLocalizationStage.Tracking);
                first.InjectDrift(8, 3);
                second.InjectDrift(8, 3);
                first.Advance(3);
                second.Advance(3);
                first.Record(3, true, Pose.identity, PointCloudLocalizationStage.Tracking);
                second.Record(3, true, new Pose(new Vector3(5, 2, -4), Quaternion.Euler(0, 80, 0)),
                    PointCloudLocalizationStage.Reacquiring);

                first.Observe(2.75); // One driver may request an extra frame while another is smoothing.
                Pose firstObservation = first.Observe(3);
                Pose secondObservation = second.Observe(3);

                AssertPosition(firstObservation.position, secondObservation.position);
                Assert.That(Quaternion.Angle(firstObservation.rotation, secondObservation.rotation), Is.LessThan(.03));
                AssertPosition(first.CameraPose(3).position, second.CameraPose(3).position);
                Assert.That(Quaternion.Angle(first.CameraPose(3).rotation, second.CameraPose(3).rotation), Is.LessThan(.03));
                Assert.That(first.FrameSignature(3), Is.EqualTo(second.FrameSignature(3)));
                Assert.That(Mathf.Abs(first.PositionErrorCm - second.PositionErrorCm), Is.GreaterThan(100),
                    "The compared outputs must actually differ while their synthetic inputs stay identical.");
            }
        }

        [TestCase("iOS")]
        [TestCase("Rokid")]
        public void SyntheticObservationChangesWithTrackingFrameWhilePublishedPoseIsStillUnchanged(string platform)
        {
            var experiment = new CorrectionExperiment(DriftScenario.Manual);
            SimulationModel model = Start(platform, experiment);
            Pose previousOutput = model.Pose;
            int updates = model.PoseUpdates;
            int attempts = model.Attempts;
            int samples = experiment.Samples.Count;

            experiment.InjectDrift(200, 37);

            Assert.That(experiment.Samples.Count, Is.EqualTo(samples), "Injection must not invent another SDK sample.");
            Assert.That(experiment.PositionErrorCm, Is.EqualTo(200).Within(.03));
            Assert.That(experiment.RotationErrorDegrees, Is.EqualTo(37).Within(.03));
            Until(model, () => model.Attempts > attempts, 5);
            Assert.That(model.PoseUpdates, Is.EqualTo(updates), "One new observation cannot confirm a correction.");
            AssertPosition(model.Pose.position, previousOutput.position);
            AssertPosition(model.Candidate.position, new Vector3(2, 0, 0));
            Assert.That(Quaternion.Angle(model.Candidate.rotation, Quaternion.Euler(0, 37, 0)), Is.LessThan(.03));
            Assert.That(Vector3.Distance(model.Candidate.position, model.Pose.position), Is.GreaterThan(1.9f),
                "The synthetic SDK candidate must be generated independently of the current output.");
            AssertPosition(experiment.CandidateWorldPose.position, Vector3.zero);
            Assert.That(Quaternion.Angle(experiment.CandidateWorldPose.rotation, Quaternion.identity), Is.LessThan(.03));
            AssertPosition(experiment.CorrectedWorldPose.position,
                new Vector3(-2 * Mathf.Cos(37 * Mathf.Deg2Rad), 0, -2 * Mathf.Sin(37 * Mathf.Deg2Rad)));
            model.Stop();
        }

        [TestCase("iOS")]
        [TestCase("Rokid")]
        public void TwoDriftInjectionsProduceRealPhysicalErrorAndSeparateGradualRecoveries(string platform)
        {
            var experiment = new CorrectionExperiment(DriftScenario.Manual);
            SimulationModel model = Start(platform, experiment);
            Pose fixedReference = model.Pose;
            int corrections = model.Corrections;
            var trace = new System.Text.StringBuilder();
            model.OnEvent += message => trace.AppendLine(message +
                $" pose={model.Pose.position.x:0.000}m confirmations={model.Confirmations}");
            for (int injection = 0; injection < 2; injection++)
            {
                double injectedAt = model.Clock;
                experiment.InjectDrift(8, 3);
                Assert.That(experiment.PositionErrorCm, Is.EqualTo(8).Within(.05));
                Assert.That(experiment.RotationErrorDegrees, Is.EqualTo(3).Within(.03));
                Assert.That(experiment.RecoverySeconds, Is.LessThan(0));
                if (injection == 0)
                {
                    Assert.That(experiment.UncorrectedPositionErrorCm, Is.EqualTo(8).Within(.05));
                    Assert.That(experiment.UncorrectedRotationErrorDegrees, Is.EqualTo(3).Within(.03));
                }

                bool observedPartialCorrection = false;
                bool observedPreConfirmationMovement = false;
                int confirmations = model.Confirmations;
                for (int step = 0; step < 900 && experiment.RecoverySeconds < 0; step++)
                {
                    model.Step(.05);
                    Pose worldOutput = IndependentWorldPose(experiment.TrackingFromWorld, model.Pose);
                    Pose worldReference = IndependentWorldPose(experiment.TrackingFromWorld, fixedReference);
                    Assert.That(experiment.PositionErrorCm, Is.EqualTo(worldOutput.position.magnitude * 100).Within(.005));
                    Assert.That(experiment.UncorrectedPositionErrorCm,
                        Is.EqualTo(worldReference.position.magnitude * 100).Within(.005));
                    Assert.That(experiment.RotationErrorDegrees,
                        Is.EqualTo(Quaternion.Angle(worldOutput.rotation, Quaternion.identity)).Within(.03));
                    observedPartialCorrection |= model.Smoothing && experiment.PositionErrorCm > .1f &&
                        experiment.PositionErrorCm < 7.9f;
                    observedPreConfirmationMovement |= model.Confirmations == confirmations &&
                        experiment.PositionErrorCm < 7.9f;
                }

                Assert.That(observedPartialCorrection, Is.True, "The object must visibly pass through intermediate corrections.");
                Assert.That(observedPreConfirmationMovement, Is.True,
                    "Maintenance must begin bounded movement before the final stable result arrives.\n" + trace);
                Assert.That(model.Corrections, Is.EqualTo(++corrections));
                Assert.That(experiment.RecoverySeconds, Is.GreaterThan(2));
                Assert.That(experiment.RecoverySeconds, Is.LessThanOrEqualTo(model.Clock - injectedAt + .05));
                Assert.That(experiment.PositionErrorCm, Is.LessThanOrEqualTo(1));
                Assert.That(experiment.RotationErrorDegrees, Is.LessThanOrEqualTo(.5));
                Assert.That(experiment.UncorrectedPositionErrorCm, Is.GreaterThanOrEqualTo(7.9));
                Assert.That(experiment.UncorrectedRotationErrorDegrees, Is.GreaterThanOrEqualTo(2.9));
            }
            Assert.That(experiment.UncorrectedPositionErrorCm, Is.GreaterThan(15.8));
            model.Stop();
        }

        [TestCase("iOS")]
        [TestCase("Rokid")]
        public void PendingRecoveryCannotCompleteWhileTrackingIsInvalidOrAfterStop(string platform)
        {
            var experiment = new CorrectionExperiment(DriftScenario.Manual);
            Assert.That(experiment.RecoverySeconds, Is.LessThan(0));
            Assert.That(float.IsNaN(experiment.PositionErrorCm), Is.True);
            Assert.Throws<InvalidOperationException>(() => experiment.InjectDrift(8, 3));
            SimulationModel model = Start(platform, experiment);
            experiment.InjectDrift(8, 3);
            model.SetTracking(false);
            int updates = model.PoseUpdates;
            for (int i = 0; i < 700; i++) model.Step(.05);
            Assert.That(model.Stage, Is.EqualTo(PointCloudLocalizationStage.TrackingLost));
            Assert.That(experiment.RecoverySeconds, Is.LessThan(0));
            Assert.That(experiment.PositionErrorCm, Is.EqualTo(8).Within(.05));
            Assert.That(model.PoseUpdates, Is.EqualTo(updates));

            model.Stop();
            int sampleCount = experiment.Samples.Count;
            double stoppedAt = model.Clock;
            float rms = experiment.PositionRmsCm;
            model.SetTracking(true);
            for (int i = 0; i < 100; i++) model.Step(1);
            Assert.That(experiment.Samples.Count, Is.EqualTo(sampleCount));
            Assert.That(model.Clock, Is.EqualTo(stoppedAt));
            Assert.That(experiment.PositionRmsCm, Is.EqualTo(rms));
            Assert.That(experiment.RecoverySeconds, Is.LessThan(0));
            Assert.That(model.PoseUpdates, Is.EqualTo(updates));
        }

        [Test]
        public void BothPlatformProfilesPassQuantitativeComparisonsAndExposePersistentWrongMatchLimitation()
        {
            string report = CorrectionExperimentChecks.RunAll(
                SimulationChecks.LoadProfile("Rokid"), SimulationChecks.LoadProfile("iOS"));
            Assert.That(report, Does.Contain("step 2"));
            Assert.That(report, Does.Contain("Noisy"));
            Assert.That(report, Does.Contain("LIMITATION reproduced:"));
            Assert.That(report, Does.Contain("loss and stop"));
            Debug.Log(report);
        }

        private static SimulationModel Start(string platform, CorrectionExperiment experiment)
        {
            var profile = SimulationChecks.LoadProfile(platform);
            Assert.That(profile, Is.Not.Null);
            var model = new SimulationModel(profile, experiment);
            Until(model, () => experiment.HasBaseline, 25);
            Assert.That(model.HasPose, Is.True);
            Assert.That(experiment.PositionErrorCm, Is.LessThan(.01));
            return model;
        }

        private static void Until(SimulationModel model, Func<bool> condition, double seconds)
        {
            for (int i = 0; i < (int)Math.Ceiling(seconds / .05) && !condition(); i++) model.Step(.05);
            Assert.That(condition(), Is.True, "The expected production-state transition did not happen before the deadline.");
        }

        private static Pose IndependentWorldPose(Pose trackingFromWorld, Pose trackedObject)
        {
            Quaternion worldFromTracking = Quaternion.Inverse(trackingFromWorld.rotation);
            return new Pose(worldFromTracking * (trackedObject.position - trackingFromWorld.position),
                worldFromTracking * trackedObject.rotation);
        }

        private static void AssertPosition(Vector3 actual, Vector3 expected) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
    }
}
