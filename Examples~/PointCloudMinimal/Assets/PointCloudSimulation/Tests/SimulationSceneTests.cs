using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Bujiaban.PointCloud.Simulation.Tests
{
    public sealed class SimulationSceneTests
    {
        [Test]
        public void BothPlatformProfilesPassDeterministicScenarios()
        {
            string report = SimulationChecks.RunAll(
                SimulationChecks.LoadProfile("Rokid"), SimulationChecks.LoadProfile("iOS"));
            Assert.That(report, Does.Contain("LIMITATION reproduced"));
            Assert.That(report, Does.Contain("shared-state: stationary reacquisition blocked"));
            Assert.That(report, Does.Contain("shared-state: 6cm recovery"));
            Assert.That(report, Does.Contain("shared-state: verification expiry"));
            Assert.That(report, Does.Contain("shared-state: origin reset interrupts smoothing"));
            Debug.Log(report);
        }

        [UnityTest]
        public IEnumerator IndependentSceneReplacesAndStopsRealFacadeOperations()
        {
            // Unity Test Runner backs up and restores the user's scenes.
            EditorSceneManager.OpenScene(SimulationChecks.ScenePath);
            yield return new EnterPlayMode();
            PointCloudSimulationScene scene = null;
            try
            {
                yield return null;
                scene = Object.FindAnyObjectByType<PointCloudSimulationScene>();
                Assert.That(scene, Is.Not.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(SimulationChecks.ScenePath));
                scene.Backend.Speed = 10;
                scene.StartSimulation(0, 0);
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!(scene.Backend.Model?.HasPose ?? false) && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(scene.Backend.Model.HasPose, Is.True, "Independent scene did not produce a first pose");
                var first = scene.Backend.Model;
                scene.StartSimulation(1, 0);
                scene.StartSimulation(1, 0);
                deadline = Time.realtimeSinceStartupAsDouble + 10;
                while ((ReferenceEquals(scene.Backend.Model, first) || !scene.Backend.Model.HasPose) &&
                    Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(scene.Backend.Model, Is.Not.SameAs(first));
                Assert.That(scene.Backend.Model.HasPose, Is.True);
                Assert.That(first.Running, Is.False);
                Assert.That(scene.Backend.MaximumConcurrent, Is.EqualTo(1));
                scene.StopSimulation();
                deadline = Time.realtimeSinceStartupAsDouble + 5;
                while (scene.Backend.Concurrent != 0 && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(scene.Backend.Released, Is.EqualTo(scene.Backend.Started));
                int updates = scene.Backend.Model.PoseUpdates;
                yield return null;
                yield return null;
                Assert.That(scene.Backend.Model.PoseUpdates, Is.EqualTo(updates));
            }
            finally
            {
                if (scene != null) scene.StopSimulation();
            }
            yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [UnityTest]
        public IEnumerator QuantitativeSceneRendersPhysicalCorrectionsAndFreezesWhenPausedOrStopped()
        {
            EditorSceneManager.OpenScene(SimulationChecks.ScenePath);
            yield return new EnterPlayMode();
            PointCloudSimulationScene scene = null;
            try
            {
                yield return null;
                scene = Object.FindAnyObjectByType<PointCloudSimulationScene>();
                Assert.That(scene, Is.Not.Null);
                Transform corrected = scene.transform.Find("Confirmed Pose");
                Transform uncorrected = scene.transform.Find("Without Continuous Correction");
                Transform observer = scene.transform.Find("Synthetic Camera");
                Assert.That(corrected, Is.Not.Null);
                Assert.That(uncorrected, Is.Not.Null);
                Assert.That(observer, Is.Not.Null);
                scene.Backend.Speed = 10;
                for (int platform = 0; platform < 2; platform++)
                {
                    SimulationModel previous = scene.Backend.Model;
                    scene.StartExperiment(platform, DriftScenario.Manual);
                    double deadline = Time.realtimeSinceStartupAsDouble + 15;
                    while ((ReferenceEquals(scene.Backend.Model, previous) ||
                        !(scene.Backend.Model?.Experiment?.HasBaseline ?? false)) &&
                        Time.realtimeSinceStartupAsDouble < deadline)
                        yield return null;
                    SimulationModel model = scene.Backend.Model;
                    Assert.That(model, Is.Not.SameAs(previous));
                    Assert.That(model?.Experiment?.HasBaseline, Is.True);
                    CorrectionExperiment experiment = model.Experiment;
                    scene.Backend.Paused = true;
                    yield return null;
                    yield return null;
                    Assert.That(corrected.gameObject.activeInHierarchy, Is.True);
                    Assert.That(uncorrected.gameObject.activeInHierarchy, Is.True);

                    for (int injection = 0; injection < 2; injection++)
                    {
                        int corrections = model.Corrections;
                        experiment.InjectDrift(8, 3);
                        double pausedAt = model.Clock;
                        int samples = experiment.Samples.Count;
                        int updates = model.PoseUpdates;
                        yield return null;
                        yield return null;
                        Assert.That(Vector3.Distance(observer.position, new Vector3(0, 1.5f, -2)),
                            Is.LessThan(.0001f), "Tracking drift must not move the physical observer.");
                        Assert.That(HorizontalDistance(corrected), Is.EqualTo(.08f).Within(.0005f),
                            "The real facade-delivered pose must move physically when the tracking frame drifts.");
                        Assert.That(Quaternion.Angle(corrected.rotation, Quaternion.identity),
                            Is.EqualTo(3).Within(.05f));
                        Assert.That(HorizontalDistance(uncorrected), Is.EqualTo(.08f * (injection + 1)).Within(.0005f));
                        Assert.That(Quaternion.Angle(uncorrected.rotation, Quaternion.identity),
                            Is.EqualTo(3 * (injection + 1)).Within(.05f));
                        Pose pausedCorrected = new Pose(corrected.position, corrected.rotation);
                        Pose pausedUncorrected = new Pose(uncorrected.position, uncorrected.rotation);
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        Assert.That(model.Clock, Is.EqualTo(pausedAt));
                        Assert.That(experiment.Samples.Count, Is.EqualTo(samples));
                        Assert.That(model.PoseUpdates, Is.EqualTo(updates));
                        AssertDisplayPose(corrected, pausedCorrected);
                        AssertDisplayPose(uncorrected, pausedUncorrected);

                        scene.Backend.Paused = false;
                        deadline = Time.realtimeSinceStartupAsDouble + 15;
                        while ((experiment.RecoverySeconds < 0 || model.Corrections == corrections) &&
                            Time.realtimeSinceStartupAsDouble < deadline)
                            yield return null;
                        Assert.That(experiment.RecoverySeconds, Is.GreaterThan(2));
                        Assert.That(model.Corrections, Is.EqualTo(corrections + 1));
                        scene.Backend.Paused = true;
                        // Scene.Update may precede Backend.Update in the completion frame.
                        yield return null;
                        yield return null;
                        Assert.That(HorizontalDistance(corrected), Is.LessThan(.001f),
                            "The displayed physical object must return to truth through facade pose callbacks.");
                        Assert.That(Quaternion.Angle(corrected.rotation, Quaternion.identity), Is.LessThan(.05f));
                        Assert.That(HorizontalDistance(uncorrected), Is.EqualTo(.08f * (injection + 1)).Within(.0005f));
                        Assert.That(Quaternion.Angle(uncorrected.rotation, Quaternion.identity),
                            Is.EqualTo(3 * (injection + 1)).Within(.05f));
                    }

                    Pose stoppedCorrected = new Pose(corrected.position, corrected.rotation);
                    Pose stoppedUncorrected = new Pose(uncorrected.position, uncorrected.rotation);
                    scene.StopSimulation();
                    deadline = Time.realtimeSinceStartupAsDouble + 5;
                    while (scene.Backend.Concurrent != 0 && Time.realtimeSinceStartupAsDouble < deadline)
                        yield return null;
                    Assert.That(scene.Backend.Concurrent, Is.Zero);
                    Assert.That(scene.Backend.Released, Is.EqualTo(scene.Backend.Started));
                    Assert.That(model.Running, Is.False);
                    int finalUpdates = model.PoseUpdates;
                    int finalSamples = experiment.Samples.Count;
                    double stoppedAt = model.Clock;
                    double recoverySeconds = experiment.RecoverySeconds;
                    scene.Backend.Paused = false;
                    for (int frame = 0; frame < 5; frame++) yield return null;
                    Assert.That(model.Clock, Is.EqualTo(stoppedAt));
                    Assert.That(experiment.Samples.Count, Is.EqualTo(finalSamples));
                    Assert.That(model.PoseUpdates, Is.EqualTo(finalUpdates));
                    Assert.That(experiment.RecoverySeconds, Is.EqualTo(recoverySeconds));
                    Assert.That(corrected.gameObject.activeInHierarchy, Is.True);
                    Assert.That(uncorrected.gameObject.activeInHierarchy, Is.True);
                    AssertDisplayPose(corrected, stoppedCorrected);
                    AssertDisplayPose(uncorrected, stoppedUncorrected);
                }
                Assert.That(scene.Backend.MaximumConcurrent, Is.EqualTo(1));
            }
            finally
            {
                if (scene != null) scene.StopSimulation();
            }
            yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static float HorizontalDistance(Transform target) =>
            new Vector2(target.position.x, target.position.z).magnitude;

        private static void AssertDisplayPose(Transform target, Pose expected)
        {
            Assert.That(Vector3.Distance(target.position, expected.position), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(target.rotation, expected.rotation), Is.LessThan(.03f));
        }

        [UnityTearDown]
        public IEnumerator ExitPlayModeAfterFailure()
        {
            if (UnityEditor.EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }
    }
}
