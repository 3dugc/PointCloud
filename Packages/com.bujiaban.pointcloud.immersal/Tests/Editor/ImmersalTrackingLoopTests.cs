using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using global::Immersal;
using global::Immersal.XR;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    /// <summary>
    /// Runs the production backend's acquisition, confirmation, maintenance,
    /// smoothing and recovery loops. Only camera/native matching/conversion are
    /// scripted: this is not another implementation of the tracking state machine.
    /// No native map is loaded, and these tests do not establish device accuracy.
    /// </summary>
    public sealed class ImmersalTrackingLoopTests
    {
        private Harness _harness;

        [UnitySetUp]
        public IEnumerator EnterEmptyPlayMode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
        }

        [UnityTearDown]
        public IEnumerator DrainAndLeavePlayMode()
        {
            if (_harness != null)
            {
                _harness.CancelAndReleaseSolver();
                double deadline = Time.realtimeSinceStartupAsDouble + 3d;
                while (_harness.Operation != null && !_harness.Operation.IsCompleted &&
                       Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                bool drained = _harness.Operation == null || _harness.Operation.IsCompleted;
                _harness.Dispose();
                _harness = null;
                Assert.That(drained, Is.True, "The production tracking loop did not drain during cleanup.");
            }
            if (UnityEditor.EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator FullSampleCountReportsRemainingRequirementWithoutClaimingSuccess()
        {
            var settings = FastSettings();
            settings.MinimumStrongLocalizationConfidence = 200;
            _harness = new Harness(settings);
            PointCloudLocalizationProgress latest = default;
            Task<Pose?> scan = _harness.StartInitialScan(new InlineProgress(p => latest = p));

            yield return Until(() => latest.StableSampleCount >= settings.RequiredStableSamples);
            Assert.That(latest.Stage, Is.EqualTo(PointCloudLocalizationStage.Confirming));
            Assert.That(latest.IsConfirmed, Is.False);
            Assert.That(latest.ConfirmationRequirement, Is.EqualTo(PointCloudConfirmationRequirement.StrongerMatch));
            Assert.That(scan.IsCompleted, Is.False, "A full counter cannot publish a pose while quality evidence is missing.");
            _harness.Cancellation.Cancel();
            yield return Until(() => scan.IsCompleted, allowCancellation: true);
            Assert.That(scan.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator RetainedEvidenceReportsTheActualSdkQualityDetailAndCameraFailure()
        {
            var settings = FastSettings();
            settings.MinimumStrongLocalizationConfidence = 200;
            _harness = new Harness(settings);
            PointCloudLocalizationProgress latest = default;
            Task<Pose?> scan = _harness.StartInitialScan(new InlineProgress(p => latest = p));
            yield return Until(() => latest.StableSampleCount >= settings.RequiredStableSamples);

            _harness.Method.Success = false;
            yield return Until(() => latest.ConfirmationRequirement == PointCloudConfirmationRequirement.NoMapMatch);
            Assert.That(latest.StableSampleCount, Is.GreaterThan(0));
            Assert.That(latest.IsConfirmed, Is.False);

            _harness.Method.Success = true;
            _harness.Method.Confidence = 40;
            yield return Until(() => latest.ConfirmationRequirement == PointCloudConfirmationRequirement.StrongerMatch);
            Assert.That(latest.StableSampleCount, Is.GreaterThan(0));
            Assert.That(latest.IsConfirmed, Is.False);

            _harness.Platform.HasDetail = false;
            yield return Until(() => latest.ConfirmationRequirement == PointCloudConfirmationRequirement.InsufficientVisualDetail);
            Assert.That(latest.Stage, Is.EqualTo(PointCloudLocalizationStage.NeedMoreVisualDetail));
            Assert.That(latest.StableSampleCount, Is.GreaterThan(0));
            Assert.That(latest.IsConfirmed, Is.False);

            _harness.Platform.HasFrame = false;
            yield return Until(() => latest.ConfirmationRequirement == PointCloudConfirmationRequirement.CameraUnavailable);
            Assert.That(latest.StableSampleCount, Is.GreaterThan(0));
            Assert.That(latest.IsConfirmed, Is.False);
            Assert.That(scan.IsCompleted, Is.False);

            _harness.Cancellation.Cancel();
            yield return Until(() => scan.IsCompleted, allowCancellation: true);
            Assert.That(scan.IsCanceled, Is.True);
            Assert.That(_harness.Platform.AllImagesReleased, Is.True);
        }

        [UnityTest]
        public IEnumerator InitialTrackingResetImmediatelyReportsZeroAndRecoveryStartsWithOneFreshObservation()
        {
            var settings = FastSettings();
            settings.MinimumStrongLocalizationConfidence = 200;
            _harness = new Harness(settings);
            PointCloudLocalizationProgress latest = default;
            Task<Pose?> scan = _harness.StartInitialScan(new InlineProgress(p => latest = p));
            yield return Until(() => latest.StableSampleCount >= 3);

            _harness.Tracking.Invalidate("test_reset_before_initial_confirmation");
            // Assert synchronously: no later frame is allowed to be responsible
            // for notifying the consumer that the old observations were lost.
            Assert.That(latest.Stage, Is.EqualTo(PointCloudLocalizationStage.TrackingLost));
            Assert.That(latest.ConfirmationRequirement, Is.EqualTo(PointCloudConfirmationRequirement.TrackingRecovery));
            Assert.That(latest.StableSampleCount, Is.Zero);
            Assert.That(latest.AttemptCount, Is.Zero);
            Assert.That(latest.IsConfirmed, Is.False);
            yield return Until(() => scan.IsCompleted);
            Assert.That(scan.Result.HasValue, Is.False);

            PointCloudLocalizationProgress? firstRecovered = null;
            _harness.Tracking.ObservePlatformStatus(1);
            // RunImmersalAsync likewise restarts ScanUntilStableAsync after a
            // replaced gate returns null. No synthetic observations are inserted.
            Task<Pose?> recoveredScan = _harness.StartInitialScan(new InlineProgress(p =>
            {
                if (!firstRecovered.HasValue && p.Stage == PointCloudLocalizationStage.Confirming)
                    firstRecovered = p;
            }));
            yield return Until(() => firstRecovered.HasValue);
            Assert.That(firstRecovered.Value.StableSampleCount, Is.EqualTo(1));
            Assert.That(firstRecovered.Value.AttemptCount, Is.EqualTo(1));
            Assert.That(firstRecovered.Value.IsConfirmed, Is.False);
            Assert.That(_harness.Maintenance.HasPose, Is.False);

            _harness.Cancellation.Cancel();
            yield return Until(() => recoveredScan.IsCompleted, allowCancellation: true);
            Assert.That(recoveredScan.IsCanceled, Is.True);
            Assert.That(_harness.Platform.AllImagesReleased, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedImageAndConflictingPoseReportDifferentRequirements()
        {
            var settings = FastSettings();
            settings.MinimumStrongLocalizationConfidence = 200;
            _harness = new Harness(settings);
            PointCloudLocalizationProgress latest = default;
            Task<Pose?> scan = _harness.StartInitialScan(new InlineProgress(p => latest = p));
            yield return Until(() => latest.StableSampleCount >= settings.RequiredStableSamples);

            int repeatedImageReports = _harness.ProgressReports.Count;
            // Camera movement continues, but its analyzed image signature
            // duplicates the first view and cannot supply another vote.
            _harness.Platform.ImageViewOverride = 0;
            yield return Until(() => _harness.ProgressReports.FindIndex(repeatedImageReports,
                p => p.ConfirmationRequirement == PointCloudConfirmationRequirement.DifferentView) >= 0);
            Assert.That(scan.IsCompleted, Is.False);

            int conflictingPoseReports = _harness.ProgressReports.Count;
            _harness.Platform.ImageViewOverride = null;
            _harness.Method.Candidate = _ => At(.2f);
            yield return Until(() => _harness.ProgressReports.FindIndex(conflictingPoseReports,
                p => p.ConfirmationRequirement == PointCloudConfirmationRequirement.PoseDisagreement) >= 0);
            Assert.That(_harness.ProgressReports.Exists(p => p.IsConfirmed), Is.False);
            Assert.That(scan.IsCompleted, Is.False);

            _harness.Cancellation.Cancel();
            yield return Until(() => scan.IsCompleted, allowCancellation: true);
            Assert.That(scan.IsCanceled, Is.True);
        }

        [UnityTest]
        public IEnumerator ConfirmationProgressResetCannotReturnPoseFromReplacedGate()
        {
            _harness = new Harness();
            _harness.Method.Candidate = _ => At(.3f);
            bool resetDuringConfirmation = false;
            Task<Pose?> scan = _harness.StartInitialScan(new InlineProgress(progress =>
            {
                if (resetDuringConfirmation ||
                    !progress.IsConfirmed)
                    return;
                resetDuringConfirmation = true;
                _harness.Tracking.Invalidate("test_reset_inside_confirmation_progress_callback");
            }));
            yield return Until(() => scan.IsCompleted);

            Assert.That(resetDuringConfirmation, Is.True);
            Assert.That(scan.Result.HasValue, Is.False,
                "A synchronous progress callback replaced the gate; its old confirmed pose is no longer valid.");
            Assert.That(_harness.Maintenance.HasPose, Is.False);
            Assert.That(_harness.Platform.AllImagesReleased, Is.True);
        }

        [UnityTest]
        public IEnumerator SmoothingPoseCallbackResetReportsInterruptionInsteadOfApplied()
        {
            _harness = new Harness();
            Task<bool> smoothing = _harness.StartSmoothing(At(.08f), _ =>
                _harness.Tracking.Invalidate("test_reset_inside_smoothing_pose_callback"));
            yield return Until(() => smoothing.IsCompleted);

            Assert.That(smoothing.Result, Is.False,
                "A consumer reset during onPose interrupts the correction; it must not be reported as applied.");
            Assert.That(_harness.Maintenance.Reacquiring, Is.True);
            Assert.That(_harness.Maintenance.IsSmoothing, Is.False);
            Assert.That(_harness.GateIsTemporal, Is.False);
            Assert.That(_harness.Stages, Does.Contain(PointCloudLocalizationStage.TrackingLost));
            Assert.That(_harness.Poses.Count, Is.EqualTo(1));
            yield return null;
            yield return null;
            Assert.That(_harness.Poses.Count, Is.EqualTo(1),
                "No further smoothing poses may be published after the reset.");
        }

        [UnityTest]
        public IEnumerator MaintenanceAcquiresAndSolvesAgainForTwoActualPoseCorrections()
        {
            _harness = new Harness();
            _harness.Platform.VaryViews = false;
            _harness.Method.Candidate = _ => At(_harness.LastPose.position.x < .079f ? .08f : .16f);
            _harness.StartMaintenance();

            yield return Until(() => !_harness.Maintenance.IsSmoothing &&
                _harness.LastPose.position.x >= .1599f);

            Assert.That(_harness.Method.Calls, Is.GreaterThanOrEqualTo(10),
                "Both corrections must be backed by fresh five-frame confirmations.");
            Assert.That(_harness.Platform.Captures, Is.EqualTo(_harness.Method.Calls));
            Assert.That(_harness.Updater.Conversions, Is.EqualTo(_harness.Method.Calls));
            Assert.That(_harness.Poses.Count, Is.GreaterThan(2),
                "The actual smoother must publish intermediate poses.");
            Assert.That(_harness.Poses.Exists(p => p.position.x > 0 && p.position.x < .08f), Is.True);
            Assert.That(_harness.LastPose.position.x, Is.EqualTo(.16f).Within(.0001f));
            Assert.That(_harness.Operation.IsCompleted, Is.False);

            int callbacks = _harness.Poses.Count;
            _harness.Cancellation.Cancel();
            yield return Until(() => _harness.Operation.IsCompleted, allowCancellation: true);
            Assert.That(_harness.Operation.IsCanceled, Is.True);
            yield return null;
            yield return null;
            Assert.That(_harness.Poses.Count, Is.EqualTo(callbacks));
            Assert.That(_harness.Platform.AllImagesReleased, Is.True);
        }

        [UnityTest]
        public IEnumerator ResetDuringPendingMaintenanceSolveImmediatelyUsesInitialGate()
        {
            _harness = new Harness();
            _harness.Method.HoldFirstSolve = true;
            _harness.Method.Candidate = call => At(call % 2 == 0 ? 1f : 1.06f);
            _harness.StartMaintenance();
            yield return Until(() => _harness.Method.Calls == 1);
            Assert.That(_harness.Method.TemporalGateAtSolve[0], Is.True);

            _harness.Tracking.Invalidate("test_reset_during_native_solve");
            _harness.Method.ReleaseFirstSolve();
            yield return Until(() => _harness.Poses.Count != 0);

            Assert.That(_harness.Method.TemporalGateAtSolve[1], Is.False,
                "The next solve must use acquisition, without first confirming the old maintenance gate.");
            Assert.That(_harness.Method.TemporalGateAtSolve.FindAll(temporal => temporal).Count,
                Is.EqualTo(1));
            Assert.That(_harness.LastPose.position.x, Is.InRange(.9999f, 1.0601f));
            Assert.That(_harness.Stages, Does.Contain(PointCloudLocalizationStage.TrackingLost));
            Assert.That(_harness.Stages, Does.Contain(PointCloudLocalizationStage.Reacquiring));
            Assert.That(_harness.ProgressReports.Exists(p =>
                p.Stage == PointCloudLocalizationStage.Reacquiring &&
                p.StableSampleCount > 0 && !p.IsConfirmed), Is.True,
                "Reacquisition must report intermediate observation counts instead of leaving the UI at zero.");
            Assert.That(_harness.Stages[_harness.Stages.Count - 1],
                Is.EqualTo(PointCloudLocalizationStage.Tracking));
            // The 0/.06 m alternating spread fits iOS acquisition's .075 m,
            // but its .05 m maintenance gate can never collect five of seven.
            Assert.That(_harness.Method.Calls, Is.LessThanOrEqualTo(9));
        }

        [UnityTest]
        public IEnumerator TwoConsistentLargeCorrectionsRequireNewViewsBeforeReacquisition()
        {
            _harness = new Harness();
            _harness.Platform.VaryViews = false;
            _harness.Method.Candidate = _ => At(1f);
            _harness.StartMaintenance();
            yield return Until(() => _harness.Method.Calls >= 15);

            Assert.That(_harness.Method.TemporalGateAtSolve.GetRange(0, 10),
                Is.All.True, "Two complete independent maintenance windows must precede reacquisition.");
            Assert.That(_harness.Method.TemporalGateAtSolve[10], Is.False);
            Assert.That(_harness.Poses, Is.Empty,
                "A consistent one-metre offset must not bypass the initial gate's viewpoint requirement.");
            Assert.That(_harness.Stages, Does.Contain(PointCloudLocalizationStage.Reacquiring));

            _harness.Platform.VaryViews = true;
            yield return Until(() => _harness.Poses.Count != 0);
            Assert.That(_harness.LastPose.position.x, Is.EqualTo(1f).Within(.0001f));
            Assert.That(_harness.Poses.Count, Is.EqualTo(1));
            Assert.That(_harness.Stages[_harness.Stages.Count - 1],
                Is.EqualTo(PointCloudLocalizationStage.Tracking));
        }

        [UnityTest]
        public IEnumerator CancellationDrainsLateNativeResultWithoutPublishingPose()
        {
            _harness = new Harness();
            _harness.Method.HoldFirstSolve = true;
            _harness.Method.Candidate = _ => At(.08f);
            _harness.StartMaintenance();
            yield return Until(() => _harness.Method.Calls == 1);

            _harness.Cancellation.Cancel();
            yield return null;
            Assert.That(_harness.Operation.IsCompleted, Is.False,
                "The real backend must wait for a solver already executing native work.");
            _harness.Method.ReleaseFirstSolve();
            yield return Until(() => _harness.Operation.IsCompleted, allowCancellation: true);

            Assert.That(_harness.Operation.IsCanceled, Is.True);
            Assert.That(_harness.Poses, Is.Empty);
            Assert.That(_harness.Updater.Conversions, Is.Zero);
            Assert.That(_harness.Platform.AllImagesReleased, Is.True);
            int reports = _harness.Stages.Count;
            yield return null;
            yield return null;
            Assert.That(_harness.Poses, Is.Empty);
            Assert.That(_harness.Stages.Count, Is.EqualTo(reports));
        }

        [UnityTest]
        public IEnumerator VerifiedPoseExpiresWhileNativeSolveIsStillPending()
        {
            var settings = FastSettings();
            settings.MaintenanceObservationAgeSeconds = .25d;
            _harness = new Harness(settings);
            _harness.Method.HoldFirstSolve = true;
            _harness.StartMaintenance();
            yield return Until(() => _harness.Method.Calls == 1);
            yield return Until(() => _harness.Stages.Contains(PointCloudLocalizationStage.TrackingLost));

            Assert.That(_harness.Operation.IsCompleted, Is.False);
            Assert.That(_harness.Poses, Is.Empty);
            Assert.That(_harness.Stages, Does.Contain(PointCloudLocalizationStage.Reacquiring));
            Assert.That(_harness.Method.Calls, Is.EqualTo(1),
                "Expiry must be observed by the production Update even with no new SDK result.");
        }

        private IEnumerator Until(Func<bool> condition, bool allowCancellation = false)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 6d;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                Assert.That(_harness.Operation.IsFaulted, Is.False,
                    _harness.Operation.Exception?.ToString());
                if (!allowCancellation)
                    Assert.That(_harness.Operation.IsCanceled, Is.False);
                yield return null;
            }
            Assert.That(condition(), Is.True,
                $"Production loop timed out: captures={_harness.Platform.Captures}, " +
                $"solves={_harness.Method.Calls}, callbacks={_harness.Poses.Count}.");
        }

        private static Pose At(float x) => new Pose(new Vector3(x, 0, 0), Quaternion.identity);

        private static ImmersalLocalizationProfile.Settings FastSettings()
        {
            // Keep the iOS acquisition/maintenance sample counts, window sizes,
            // geometric thresholds and view requirements. Timing fields keep
            // the test bounded; this does not measure on-device performance.
            return new ImmersalLocalizationProfile.Settings
            {
                RequiredStableSamples = 5,
                MaximumObservationWindow = 9,
                MaxPositionDrift = .075f,
                MaxRotationDriftDegrees = 3f,
                MinimumStrongMatches = 2,
                MinimumViewRotationDelta = 4f,
                MinimumFrameSignatureDistance = 10,
                MinimumConfirmationRotationSpan = 12f,
                MinimumConfirmationSeconds = .04d,
                MinimumAttemptIntervalSeconds = .025d,
                MaximumObservationAgeSeconds = 10d,
                MaximumEvidenceGapSeconds = 5d,
                MaintenanceRequiredStableSamples = 5,
                MaintenanceMaximumObservationWindow = 7,
                MaintenanceMaxPositionDrift = .05f,
                MaintenanceMaxRotationDriftDegrees = 2f,
                MaintenanceMinimumConfirmationSeconds = .04d,
                MaintenanceAttemptIntervalSeconds = .025d,
                MaintenanceObservationAgeSeconds = 10d,
                MaintenanceEvidenceGapSeconds = 5d,
                CorrectionSmoothingSeconds = .08f
            };
        }

        private sealed class Harness : IDisposable
        {
            private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
            private const int MapId = 194873;
            private readonly GameObject _host;
            private readonly IDictionary _registeredMaps;
            internal readonly ImmersalPointCloudBackend Backend;
            internal readonly ScriptedPlatform Platform;
            internal readonly ScriptedMethod Method;
            internal readonly LoopSceneUpdater Updater;
            internal readonly ImmersalTrackingMonitor Tracking;
            internal readonly ImmersalMaintenanceState Maintenance;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly List<Pose> Poses = new List<Pose>();
            internal readonly List<PointCloudLocalizationStage> Stages = new List<PointCloudLocalizationStage>();
            internal readonly List<PointCloudLocalizationProgress> ProgressReports = new List<PointCloudLocalizationProgress>();
            internal Task Operation;
            internal Pose LastPose => Poses.Count == 0 ? Pose.identity : Poses[Poses.Count - 1];

            internal Harness(ImmersalLocalizationProfile.Settings settings = null)
            {
                _host = new GameObject("Real Immersal tracking loop / scripted native boundary");
                _host.SetActive(false);
                Backend = _host.AddComponent<ImmersalPointCloudBackend>();
                var sdk = _host.GetComponent<ImmersalSDK>();
                Set(sdk, "m_InitializeAutomatically", false);
                _host.GetComponent<ImmersalSession>().enabled = false;
                _host.GetComponent<Localizer>().enabled = false;
                Updater = _host.AddComponent<LoopSceneUpdater>();
                var analyzer = _host.AddComponent<LoopTrackingAnalyzer>();
                Set(sdk, "m_SceneUpdater", Updater);
                Set(sdk, "m_TrackingAnalyzer", analyzer);

                var mapObject = new GameObject("Registered test map, never loaded into native SDK");
                mapObject.transform.SetParent(_host.transform, false);
                XRMap map = mapObject.AddComponent<XRMap>();
                Set(map, "m_MapId", MapId);
                Type sinkType = typeof(ImmersalPointCloudBackend).GetNestedType("RawPoseSink", BindingFlags.NonPublic);
                var sink = (ISceneUpdateable)Activator.CreateInstance(sinkType, Private, null,
                    new object[] { mapObject.transform, (Action)(() => Tracking?.Invalidate("test_scene_reset")) }, null);
                _registeredMaps = (IDictionary)typeof(MapManager).GetField("m_MapEntries",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(_registeredMaps.Count, Is.Zero, "Tracking loop tests need an isolated empty scene.");
                MapManager.RegisterMap(map, sink);

                Platform = new ScriptedPlatform();
                Method = new ScriptedMethod { TemporalGate = () => GateIsTemporal, MapId = MapId };
                Set(Backend, "_sdk", sdk);
                Set(Backend, "_runtimeMap", map);
                Set(Backend, "_poseSink", sink);
                Set(Backend, "_platform", Platform);
                Set(Backend, "_localizationMethod", Method);
                Set(Backend, "_ownsPlatformLifecycle", false);
                Set(Backend, "_enableDiagnosticLogging", false);
                var policy = (settings ?? FastSettings()).CopyValidated();
                Set(Backend, "_policy", policy);
                Maintenance = new ImmersalMaintenanceState(policy, false);
                Set(Backend, "_maintenance", Maintenance);
                Set(Backend, "_operationProgress", new InlineProgress(p =>
                {
                    Stages.Add(p.Stage);
                    ProgressReports.Add(p);
                }));
                Set(Backend, "_operationCancellationToken", Cancellation.Token);
                Tracking = new ImmersalTrackingMonitor(Platform, reason => Invoke("ResetTrackingEvidence", reason));
                Tracking.ObservePlatformStatus(1);
                Set(Backend, "_trackingMonitor", Tracking);
                _host.SetActive(true);
            }

            internal bool GateIsTemporal
            {
                get
                {
                    object gate = typeof(ImmersalPointCloudBackend).GetProperty("_confirmationGate", Private).GetValue(Backend);
                    return (bool)gate.GetType().GetProperty("UsesTemporalObservations", Private).GetValue(gate);
                }
            }

            internal void StartMaintenance()
            {
                Operation = (Task)Invoke("MaintainPoseAsync", 1, Pose.identity,
                    (Action<Pose>)(pose => Poses.Add(pose)), Cancellation.Token);
            }

            internal Task<Pose?> StartInitialScan(IProgress<PointCloudLocalizationProgress> progress)
            {
                var combined = new InlineProgress(p =>
                {
                    Stages.Add(p.Stage);
                    ProgressReports.Add(p);
                    progress?.Report(p);
                });
                Set(Backend, "_operationProgress", combined);
                var scan = (Task<Pose?>)Invoke("ScanUntilStableAsync", 1, Cancellation.Token,
                    combined, .025d, null);
                Operation = scan;
                return scan;
            }

            internal Task<bool> StartSmoothing(Pose candidate, Action<Pose> onPose)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                Maintenance.Accept(Pose.identity, now);
                Maintenance.Accept(candidate, now);
                var smoothing = (Task<bool>)Invoke("SmoothCorrectionAsync", (Action<Pose>)(pose =>
                {
                    Poses.Add(pose);
                    onPose(pose);
                }), Cancellation.Token);
                Operation = smoothing;
                return smoothing;
            }

            internal void CancelAndReleaseSolver()
            {
                Cancellation.Cancel();
                Method.ReleaseFirstSolve();
            }

            public void Dispose()
            {
                Tracking.Dispose();
                // The map was never native-loaded. Remove this registry entry
                // before SDK.OnDestroy, which otherwise calls native FreeMap.
                _registeredMaps.Remove(MapId);
                Object.DestroyImmediate(_host);
                Cancellation.Dispose();
            }

            private object Invoke(string name, params object[] arguments) =>
                typeof(ImmersalPointCloudBackend).GetMethod(name, Private).Invoke(Backend, arguments);

            private static void Set(object target, string name, object value) =>
                target.GetType().GetField(name, Private).SetValue(target, value);
        }

        private sealed class InlineProgress : IProgress<PointCloudLocalizationProgress>
        {
            private readonly Action<PointCloudLocalizationProgress> _report;
            internal InlineProgress(Action<PointCloudLocalizationProgress> report) { _report = report; }
            public void Report(PointCloudLocalizationProgress value) => _report(value);
        }

        private sealed class ScriptedPlatform : IPlatformSupport
        {
            private readonly List<SimpleImageData> _images = new List<SimpleImageData>();
            internal int Captures;
            internal bool VaryViews = true;
            internal bool HasDetail = true;
            internal bool HasFrame = true;
            internal int? ImageViewOverride;
            internal bool AllImagesReleased => _images.TrueForAll(image => image.UnmanagedDataPointer == IntPtr.Zero);

            public Task<IPlatformUpdateResult> UpdatePlatform()
            {
                int view = VaryViews ? Captures % 8 : 0;
                Captures++;
                if (!HasFrame)
                    return Task.FromResult<IPlatformUpdateResult>(new SimplePlatformUpdateResult
                    {
                        Success = false, CameraData = null,
                        Status = new SimplePlatformStatus { TrackingQuality = 1 }
                    });
                var image = new SimpleImageData(HasDetail
                    ? DetailedFrame(ImageViewOverride ?? view) : new byte[96 * 72]);
                _images.Add(image);
                var camera = new CameraData(image)
                {
                    Width = 96, Height = 72, Channels = 1,
                    Format = CameraDataFormat.SingleChannel,
                    Intrinsics = new Vector4(100, 100, 48, 36),
                    CameraPositionOnCapture = new Vector3(view * .06f, 1.5f, 0),
                    CameraRotationOnCapture = Quaternion.Euler(0, view * 8f, 0),
                    ScreenOrientation = Quaternion.identity,
                    Distortion = Array.Empty<double>()
                };
                return Task.FromResult<IPlatformUpdateResult>(new SimplePlatformUpdateResult
                {
                    Success = true, CameraData = camera,
                    Status = new SimplePlatformStatus { TrackingQuality = 1 }
                });
            }

            public Task<IPlatformUpdateResult> UpdatePlatform(IPlatformConfiguration configuration) => UpdatePlatform();
            public Task<IPlatformConfigureResult> ConfigurePlatform() =>
                Task.FromResult<IPlatformConfigureResult>(new SimplePlatformConfigureResult { Success = true });
            public Task<IPlatformConfigureResult> ConfigurePlatform(IPlatformConfiguration configuration) => ConfigurePlatform();
            public Task StopAndCleanUp() => Task.CompletedTask;

            private static byte[] DetailedFrame(int view)
            {
                var bytes = new byte[96 * 72];
                new System.Random(8127).NextBytes(bytes);
                // Put a descending row into an otherwise ascending 8x8 dHash.
                // Its actual analyzed signature has 16 bits of separation from
                // every other view, while the image retains distributed corners.
                for (int row = 0; row < 8; row++)
                {
                    int y = (row + 1) * 72 / 9;
                    for (int column = 0; column <= 8; column++)
                    {
                        int x = column * 95 / 8;
                        byte value = (byte)(row == view ? 240 - column * 28 : 16 + column * 28);
                        for (int dy = 0; dy <= 1; dy++)
                            for (int dx = 0; dx <= 1 && x + dx < 96; dx++)
                                bytes[(y + dy) * 96 + x + dx] = value;
                    }
                }
                return bytes;
            }
        }

        private sealed class ScriptedMethod : ILocalizationMethod
        {
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            internal int MapId;
            internal int Calls;
            internal bool HoldFirstSolve;
            internal bool Success = true;
            internal int Confidence = 100;
            internal Func<int, Pose> Candidate = _ => Pose.identity;
            internal Func<bool> TemporalGate;
            internal readonly List<bool> TemporalGateAtSolve = new List<bool>();
            public ConfigurationMode ConfigurationMode => ConfigurationMode.Always;
            public IMapOption[] MapOptions => Array.Empty<IMapOption>();

            public async Task<ILocalizationResult> Localize(ICameraData cameraData, CancellationToken token)
            {
                using IImageData lease = cameraData.GetImageData();
                int call = ++Calls;
                TemporalGateAtSolve.Add(TemporalGate());
                Pose pose = Candidate(call);
                bool success = Success;
                int confidence = Confidence;
                // Like a native solve already executing, this await deliberately
                // ignores cancellation. Production must drain and reject it.
                if (HoldFirstSolve && call == 1) await _release.Task;
                return new ScriptedResult(MapId, pose, success, confidence);
            }

            internal void ReleaseFirstSolve() => _release.TrySetResult(true);
            public Task<bool> Configure(ILocalizationMethodConfiguration configuration) => Task.FromResult(true);
            public Task StopAndCleanUp() => Task.CompletedTask;
            public Task OnMapRegistered(XRMap map) => Task.CompletedTask;
        }

        private sealed class ScriptedResult : ILocalizationResult
        {
            internal readonly Pose MapPose;
            private readonly int _confidence;
            internal ScriptedResult(int mapId, Pose pose, bool success, int confidence)
            {
                MapId = mapId;
                MapPose = pose;
                Success = success;
                _confidence = confidence;
            }
            public bool Success { get; }
            public int MapId { get; }
            public LocalizeInfo LocalizeInfo => new LocalizeInfo
            {
                mapId = MapId, confidence = _confidence, rmse = .5d,
                position = MapPose.position, rotation = MapPose.rotation
            };
        }

        private sealed class LoopSceneUpdater : MonoBehaviour, ISceneUpdater
        {
            internal int Conversions;
            public Task UpdateScene(MapEntry entry, ICameraData camera, ILocalizationResult result)
            {
                Conversions++;
                Pose pose = ((ScriptedResult)result).MapPose;
                return entry.SceneParent.SceneUpdate(new SceneUpdateData
                {
                    Pose = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one),
                    CameraData = camera, MapEntry = entry, LocalizeInfo = result.LocalizeInfo
                });
            }
        }

        private sealed class LoopTrackingAnalyzer : MonoBehaviour, ITrackingAnalyzer
        {
            public ITrackingStatus TrackingStatus => null;
            public void Analyze(IPlatformStatus platform, ILocalizationResults results) { }
            public void Reset() { }
        }
    }
}
