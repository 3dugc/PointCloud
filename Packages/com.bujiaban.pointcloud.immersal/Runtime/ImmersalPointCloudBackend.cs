using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using global::Immersal;
using global::Immersal.XR;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ImmersalSDK))]
    [RequireComponent(typeof(ImmersalSession))]
    [RequireComponent(typeof(Localizer))]
    [AddComponentMenu("Bujiaban/Point Cloud/Immersal Backend")]
    public sealed class ImmersalPointCloudBackend : PointCloudBackend
    {
        private const string MapType = "immersal";
        private const string LogPrefix = "[ImmersalPointCloudBackend]";
        private const int FallbackRuntimeMapId = 1;

        [SerializeField, Interface(typeof(ILocalizationMethod))]
        [Tooltip("The one Immersal localization method used for this platform.")]
        private UnityEngine.Object _localizationMethodObject;

        [SerializeField]
        [Tooltip("Enable for adapters owned only by point-cloud localization, such as Rokid raw preview. Disable for a host-owned ARFoundation lifecycle.")]
        private bool _ownsPlatformLifecycle = true;

        [SerializeField]
        [Tooltip("Write bounded local responsibility traces. Does not change localization decisions or capture images.")]
        private bool _enableDiagnosticLogging = true;

        [SerializeField, InspectorName("定位门禁配置")]
        [Tooltip("拖入本平台的 ImmersalLocalizationProfile。留空时保留旧的严格默认值并输出警告；修改在下一轮定位生效。")]
        private ImmersalLocalizationProfile _localizationProfile;

        private ImmersalLocalizationProfile.Settings _policy;
        private string _profileName;
        private bool _usingDefaultProfile;
        private ImmersalLocalizationDiagnostics _diagnostics;
        private long _diagnosticAttemptId;

        private ImmersalSDK _sdk;
        private ImmersalMaintenanceState _maintenance;
        private LocalizationConfirmationGate _confirmationGate => _maintenance?.Gate;
        private ImmersalSession _session;
        private Localizer _localizer;
        private ILocalizationMethod _localizationMethod;
        private RawPoseSink _poseSink;
        private XRMap _runtimeMap;
        private IPlatformSupport _platform;
        private bool _platformConfigured;
        private bool _methodConfigured;
        private int _operationId;
        private int _successfulSampleCount;
        private bool _hasLoggedLocalizationFailure;
        private bool _hasLoggedWeakMatch;
        private bool _hasLoggedDetailedFrame;
        private int _lowDetailFrameCount;
        private readonly ImmersalRuntimeInitialization _runtimeInitialization =
            new ImmersalRuntimeInitialization();
        private ImmersalTrackingMonitor _trackingMonitor;
        private bool _applicationPaused;
        private bool _isMaintaining => _maintenance?.HasPose == true;
        private readonly ImmersalCaptureFreshness _captureFreshness = new ImmersalCaptureFreshness();
        private double _lastMaintenanceHeartbeatAt;
        private double _lastVerifiedAtSeconds => _maintenance?.LastVerifiedAtSeconds ?? -1d;
        private IProgress<PointCloudLocalizationProgress> _operationProgress;
        private CancellationToken _operationCancellationToken;
        private PointCloudLocalizationStage? _lastTrackingStage;
        private double _lastReportedVerification = -1d;

        protected override bool SupportsMapType(string mapType)
        {
            return string.Equals(mapType, MapType, StringComparison.Ordinal);
        }

        protected override Task<Pose> LocalizeCoreAsync(
            string mapType, Stream mapZip, CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress) =>
            ExecuteMapAsync(mapZip, cancellationToken, progress, null);

        protected override Task TrackCoreAsync(
            string mapType, Stream mapZip, Action<Pose> onPose,
            CancellationToken cancellationToken, IProgress<PointCloudLocalizationProgress> progress) =>
            ExecuteMapAsync(mapZip, cancellationToken, progress, onPose);

        private async Task<Pose> ExecuteMapAsync(Stream mapZip, CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress, Action<Pose> onPose)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CapturePolicy();
            var map = await Task.Run(() => ImmersalMapArchiveReader.Read(mapZip, cancellationToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            int operationId = ++_operationId;
            Log(operationId, $"Archive read: name='{map.Name}', bytes={map.Bytes.Length}.");
            try
            {
                return await RunImmersalAsync(map, operationId, cancellationToken, progress, onPose);
            }
            finally
            {
                _diagnostics?.Dispose();
                _diagnostics = null;
            }
        }

        private void CapturePolicy()
        {
            _usingDefaultProfile = _localizationProfile == null;
            _profileName = _usingDefaultProfile ? "Built-in strict defaults" : _localizationProfile.name;
            try
            {
                _policy = _usingDefaultProfile
                    ? new ImmersalLocalizationProfile.Settings().CopyValidated()
                    : _localizationProfile.CreateSnapshot();
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                               exception is InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Invalid Immersal localization profile '{_profileName}': {exception.Message}", exception);
            }

            if (_usingDefaultProfile)
                Debug.LogWarning($"{LogPrefix} No localization profile assigned; using unchanged strict defaults. " +
                    "Assign this platform's profile to tune it independently.", this);
        }

        private async Task<Pose> RunImmersalAsync(
            ImmersalMapArchiveReader.MapData map,
            int operationId,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress,
            Action<Pose> onPose)
        {
            _maintenance = new ImmersalMaintenanceState(_policy, _enableDiagnosticLogging);
            _diagnosticAttemptId = 0;
            _captureFreshness.Reset();
            _lastMaintenanceHeartbeatAt = Time.realtimeSinceStartupAsDouble;
            _lastReportedVerification = -1d;
            _operationProgress = progress;
            _operationCancellationToken = cancellationToken;
            _lastTrackingStage = null;
            Log(operationId, $"Profile='{_profileName}', samples={_policy.RequiredStableSamples}, " +
                $"confidence={_policy.MinimumLocalizationConfidence}, strongConfidence={_policy.MinimumStrongLocalizationConfidence}, " +
                $"strongMatches={_policy.MinimumStrongMatches}.");
            BeginDiagnostics(map, operationId);
            _successfulSampleCount = 0;
            _hasLoggedLocalizationFailure = false;
            _hasLoggedWeakMatch = false;
            _hasLoggedDetailedFrame = false;
            _lowDetailFrameCount = 0;

            Pose result = Pose.identity;
            Exception operationFailure = null;

            try
            {
                ResolveAndValidateDependencies();
                cancellationToken.ThrowIfCancellationRequested();

                await PrepareSdkForOperationAsync(cancellationToken);
                await ConfigureMapAsync(map, cancellationToken);

                // First SDK initialization already configures its platform.
                // Later owned runs re-open only the localization camera.
                if (_ownsPlatformLifecycle && !_platformConfigured)
                    await ConfigurePlatformAsync(cancellationToken);

                _trackingMonitor = new ImmersalTrackingMonitor(_platform, ResetTrackingEvidence);
                _trackingMonitor.SetPaused(_applicationPaused);
                _sdk.OnReset?.AddListener(OnTrackingSpaceReset);
                _session.OnReset?.AddListener(OnTrackingSpaceReset);

                _diagnostics?.Write("runtime_ready", new LocalizationLifecycleTrace
                {
                    phase = "ready", mapId = _runtimeMap.mapId,
                    method = _localizationMethod.GetType().FullName,
                    platform = _platform.GetType().FullName,
                    sceneUpdater = _sdk.SceneUpdater.GetType().FullName,
                    solverType = ImmersalLocalizationDiagnostics.Setting(_localizationMethod, "m_SolverType"),
                    filterRadius = ImmersalLocalizationDiagnostics.Setting(_localizationMethod, "m_FilterRadius")
                });

                ReportProgress(progress, PointCloudLocalizationStage.Searching);
                Pose? initial;
                do
                {
                    initial = await ScanUntilStableAsync(operationId, cancellationToken, progress,
                        _policy.MinimumAttemptIntervalSeconds);
                } while (!initial.HasValue);
                result = initial.Value;
                Log(operationId, $"Stable map-origin pose confirmed: {FormatPose(result)}.");

                if (onPose != null)
                {
                    _maintenance.Accept(result, Time.realtimeSinceStartupAsDouble);
                    onPose(result);
                    ReportTrackingState(_maintenance.Stage);
                    await MaintainPoseAsync(operationId, result, onPose, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                operationFailure = exception;
                _diagnostics?.Error($"localization_operation_last_attempt_{_diagnosticAttemptId}", exception);
            }

            Exception cleanupFailure = await CleanupAsync(operationId);
            if (cleanupFailure != null)
                _diagnostics?.Error("cleanup", cleanupFailure);
            _diagnostics?.Write("operation_end", new LocalizationLifecycleTrace
            {
                phase = "after_cleanup", cancelled = operationFailure is OperationCanceledException,
                failed = (operationFailure != null &&
                    !(operationFailure is OperationCanceledException)) ||
                    cleanupFailure != null,
                willReturnPose = operationFailure == null && cleanupFailure == null,
                pose = operationFailure == null && cleanupFailure == null
                    ? ImmersalLocalizationDiagnostics.Pose(result) : null
            });

            if (operationFailure != null)
            {
                if (cleanupFailure != null)
                    Debug.LogError($"{LogPrefix} Cleanup also failed: {cleanupFailure}", this);
                ExceptionDispatchInfo.Capture(operationFailure).Throw();
                return Pose.identity;
            }

            if (cleanupFailure != null)
                throw new InvalidOperationException(
                    "Immersal localization finished but resource cleanup failed.", cleanupFailure);

            return result;
        }

        private async Task MaintainPoseAsync(
            int operationId, Pose initialPose, Action<Pose> onPose, CancellationToken cancellationToken)
        {
            if (!_maintenance.HasPose)
                _maintenance.Accept(initialPose, Time.realtimeSinceStartupAsDouble);
            ReportTrackingState(_maintenance.Stage);
            Log(operationId, $"Maintenance started: interval={_policy.MaintenanceAttemptIntervalSeconds:0.###}s, " +
                $"samples={_policy.MaintenanceRequiredStableSamples}; new viewpoints required only for reacquisition.");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double delay = _maintenance.NextAttemptAtSeconds - Time.realtimeSinceStartupAsDouble;
                if (delay > 0)
                    await Task.Delay((int)Math.Ceiling(delay * 1000d), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ReportTrackingState(_maintenance.Stage);
                Pose? scanned = await ScanUntilStableAsync(operationId, cancellationToken,
                    _maintenance.Reacquiring ? _operationProgress : null,
                    _maintenance.AttemptIntervalSeconds,
                    (gateResult, token) => PreviewMaintenanceResultAsync(
                        operationId, gateResult, onPose, token));
                if (!scanned.HasValue) continue;

                Pose before = _maintenance.Pose;
                string decision = _maintenance.Accept(scanned.Value, Time.realtimeSinceStartupAsDouble);
                if (decision == "reacquired" || decision == "initial")
                    onPose(_maintenance.Pose);
                ReportTrackingState(_maintenance.Stage);
                if (decision == "smoothing")
                    decision = await SmoothCorrectionAsync(onPose, cancellationToken)
                        ? "applied" : "interrupted_by_tracking_change";
                WriteMaintenanceDecision(decision, before, scanned.Value);
                Log(operationId, $"Maintenance decision={decision}, pose={FormatPose(_maintenance.Pose)}.");
            }
        }

        private async Task<bool> SmoothCorrectionAsync(Action<Pose> onPose, CancellationToken cancellationToken)
        {
            int revision = _maintenance.Revision;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefreshMaintenanceTracking();
                if (_maintenance.Revision != revision) return false;
                if (_maintenance.AdvanceSmoothing(Time.realtimeSinceStartupAsDouble))
                    onPose(_maintenance.Pose);
                if (_maintenance.Revision != revision) return false;
                if (!_maintenance.IsSmoothing) return true;
                await WaitForNextFrameAsync(cancellationToken);
            }
        }

        private async Task PreviewMaintenanceResultAsync(int operationId,
            LocalizationGateResult result, Action<Pose> onPose,
            CancellationToken cancellationToken)
        {
            if (_maintenance.Reacquiring || !_maintenance.HasPose) return;
            Pose before = _maintenance.Pose;
            bool hasPreviewPose = result.HasStablePose || result.HasCurrentPose;
            Pose previewPose = result.HasStablePose ? result.StablePose : result.CurrentPose;
            int previewCount = result.HasStablePose ? result.StableCount : 1;
            string decision = hasPreviewPose
                ? _maintenance.Preview(previewPose, previewCount,
                    _confirmationGate.RequiredSamples, Time.realtimeSinceStartupAsDouble)
                : _maintenance.RollbackPreview(Time.realtimeSinceStartupAsDouble)
                    ? "preview_rollback"
                    : "preview_ignored";
            if (decision != "previewing" && decision != "preview_rollback") return;

            bool completed = await SmoothCorrectionAsync(onPose, cancellationToken);
            string action = completed ? decision : "preview_interrupted_by_tracking_change";
            WriteMaintenanceDecision(action, before,
                hasPreviewPose ? previewPose : before);
            Log(operationId, $"Maintenance decision={action}, " +
                $"stable={result.StableCount}/{_confirmationGate.RequiredSamples}, " +
                $"pose={FormatPose(_maintenance.Pose)}.");
        }

        private void WriteMaintenanceDecision(string action, Pose current, Pose candidate)
        {
            _diagnostics?.Write("maintenance_decision", new MaintenanceDecisionTrace
            {
                action = action,
                currentPose = ImmersalLocalizationDiagnostics.Pose(current),
                candidatePose = ImmersalLocalizationDiagnostics.Pose(candidate),
                positionDelta = Vector3.Distance(current.position, candidate.position),
                rotationDelta = Quaternion.Angle(current.rotation, candidate.rotation)
            });
        }

        private void BeginDiagnostics(ImmersalMapArchiveReader.MapData map, int operationId)
        {
            if (!_enableDiagnosticLogging) return;
            try
            {
                _diagnostics = new ImmersalLocalizationDiagnostics(map.Name, map.Bytes, operationId,
                    new LocalizationPolicyTrace
                    {
                        profileName = _profileName,
                        profileSource = _usingDefaultProfile ? "built_in_defaults" : "asset",
                        samples = _policy.RequiredStableSamples, window = _policy.MaximumObservationWindow,
                        confidence = _policy.MinimumLocalizationConfidence, rmse = _policy.MaximumLocalizationRmse,
                        strongConfidence = _policy.MinimumStrongLocalizationConfidence, strongRmse = _policy.MaximumStrongLocalizationRmse,
                        signatureDistance = _policy.MinimumFrameSignatureDistance,
                        viewPositionDelta = _policy.MinimumViewPositionDelta, viewRotationDelta = _policy.MinimumViewRotationDelta,
                        positionDrift = _policy.MaxPositionDrift, rotationDrift = _policy.MaxRotationDriftDegrees,
                        confirmationPositionSpan = _policy.MinimumConfirmationPositionSpan,
                        confirmationRotationSpan = _policy.MinimumConfirmationRotationSpan,
                        contextDownwardDot = _policy.MaximumDownwardDotForContextView,
                        strongMatches = _policy.MinimumStrongMatches, contextMatches = _policy.MinimumContextMatches,
                        confirmationSeconds = _policy.MinimumConfirmationSeconds,
                        observationAgeSeconds = _policy.MaximumObservationAgeSeconds,
                        evidenceGapSeconds = _policy.MaximumEvidenceGapSeconds,
                        attemptIntervalSeconds = _policy.MinimumAttemptIntervalSeconds,
                        minimumFrameContrast = _policy.MinimumFrameContrast,
                        minimumFrameEdgeRatio = _policy.MinimumFrameEdgeRatio,
                        minimumFrameCornerCount = _policy.MinimumFrameCornerCount,
                        minimumFrameDetailedRegions = _policy.MinimumFrameDetailedRegions,
                        maintenanceSamples = _policy.MaintenanceRequiredStableSamples,
                        maintenanceWindow = _policy.MaintenanceMaximumObservationWindow,
                        maintenanceAttemptIntervalSeconds = _policy.MaintenanceAttemptIntervalSeconds,
                        maintenanceConfirmationSeconds = _policy.MaintenanceMinimumConfirmationSeconds,
                        maintenanceObservationAgeSeconds = _policy.MaintenanceObservationAgeSeconds,
                        maintenanceEvidenceGapSeconds = _policy.MaintenanceEvidenceGapSeconds,
                        maintenancePositionDrift = _policy.MaintenanceMaxPositionDrift,
                        maintenanceRotationDrift = _policy.MaintenanceMaxRotationDriftDegrees,
                        maximumAutomaticPositionCorrection = _policy.MaximumAutomaticPositionCorrection,
                        maximumAutomaticRotationCorrection = _policy.MaximumAutomaticRotationCorrection,
                        positionCorrectionDeadband = _policy.PositionCorrectionDeadband,
                        rotationCorrectionDeadband = _policy.RotationCorrectionDeadband,
                        maximumProvisionalPositionStep = _policy.MaximumProvisionalPositionStep,
                        maximumProvisionalRotationStepDegrees = _policy.MaximumProvisionalRotationStepDegrees,
                        provisionalCorrectionSmoothingSeconds = _policy.ProvisionalCorrectionSmoothingSeconds,
                        correctionSmoothingSeconds = _policy.CorrectionSmoothingSeconds
                    });
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PointCloudDiagnostics] Initialization failed; localization continues: {exception.Message}");
            }
        }

        private void RecordFrame(long attemptId, ICameraData camera, IImageData image,
            int trackingQuality, FrameEvidence evidence, double platformStarted, double platformReturned)
        {
            if (_diagnostics == null) return;
            try
            {
                string captureTimestamp = ImmersalLocalizationDiagnostics.CaptureTimestamp(image, out string timestampStatus);
                _diagnostics.Write("frame", new LocalizationAttemptTrace
                {
                    attemptId = attemptId, unityFrame = Time.frameCount,
                    cameraDataId = ImmersalLocalizationDiagnostics.CameraId(camera),
                    platformStartedAtSeconds = platformStarted,
                    platformReturnedAtSeconds = platformReturned,
                    inspectedAtSeconds = Time.realtimeSinceStartupAsDouble,
                    sourceCaptureTimestamp = captureTimestamp,
                    sourceTimestampStatus = _ownsPlatformLifecycle && captureTimestamp == null
                        ? "if_available_join_platform_log_by_cameraDataId" : timestampStatus,
                    width = camera.Width, height = camera.Height, channels = camera.Channels,
                    trackingQuality = trackingQuality, expectedMapId = _runtimeMap.mapId,
                    imageOrientation = camera.ImageOrientation.ToString(),
                    intrinsics = $"fx={ImmersalLocalizationDiagnostics.Number(camera.Intrinsics.x)}," +
                        $"fy={ImmersalLocalizationDiagnostics.Number(camera.Intrinsics.y)}," +
                        $"cx={ImmersalLocalizationDiagnostics.Number(camera.Intrinsics.z)}," +
                        $"cy={ImmersalLocalizationDiagnostics.Number(camera.Intrinsics.w)}",
                    cameraPose = ImmersalLocalizationDiagnostics.Pose(new Pose(
                        camera.CameraPositionOnCapture, camera.CameraRotationOnCapture)),
                    frameSignature = evidence.Signature.ToString("X16"),
                    enoughDetail = evidence.HasEnoughDetail, contrast = evidence.Contrast,
                    edgeRatio = evidence.EdgeRatio, corners = evidence.CornerCount,
                    detailedRegions = evidence.DetailedRegionCount,
                    skipReason = evidence.HasEnoughDetail ? null : "insufficient_frame_detail_no_sdk_call"
                });
            }
            catch (Exception exception) { _diagnostics.Error("frame_diagnostics", exception); }
        }

        private LocalizationResultTrace RecordSdkResult(long attemptId, ICameraData camera,
            ILocalizationResult result, long sinkRevision, double elapsed)
        {
            if (_diagnostics == null) return null;
            try
            {
                bool success = result?.Success == true;
                LocalizeInfo info = success ? result.LocalizeInfo : default;
                var trace = new LocalizationResultTrace
                {
                    attemptId = attemptId, cameraDataId = ImmersalLocalizationDiagnostics.CameraId(camera),
                    expectedMapId = _runtimeMap.mapId, returnedMapId = result?.MapId ?? 0,
                    sdkSuccess = success, rawInfoAvailable = success,
                    confidence = success ? info.confidence : 0,
                    rmse = success ? ImmersalLocalizationDiagnostics.Number(info.rmse) : null,
                    rawPose = success ? ImmersalLocalizationDiagnostics.Pose(new Pose(info.position, info.rotation)) : null,
                    reliable = success && IsReliableLocalization(info),
                    strong = success && IsReliableLocalization(info) && IsStrongLocalization(info),
                    qualityReason = !success ? "sdk_no_match_or_no_result" :
                        info.confidence < _policy.MinimumLocalizationConfidence ? "confidence_below_minimum" :
                        double.IsNaN(info.rmse) || double.IsInfinity(info.rmse) || info.rmse < 0 ? "invalid_rmse" :
                        info.rmse > _policy.MaximumLocalizationRmse ? "rmse_above_maximum" : "quality_passed",
                    sinkRevisionBefore = sinkRevision, sinkRevisionAfter = sinkRevision,
                    localizationSeconds = elapsed
                };
                _diagnostics.Write("sdk_result", trace);
                return trace;
            }
            catch (Exception exception)
            {
                _diagnostics.Error("sdk_result_diagnostics", exception);
                return null;
            }
        }

        private void ResolveAndValidateDependencies()
        {
            _sdk = GetComponent<ImmersalSDK>();
            if (_sdk == null || !ReferenceEquals(_sdk, ImmersalSDK.Instance))
                throw new InvalidOperationException("The backend must own the scene's single ImmersalSDK.");

            _session = GetComponent<ImmersalSession>();
            _localizer = GetComponent<Localizer>();
            _platform = _sdk.PlatformSupport;

            if (_session == null)
                throw new InvalidOperationException("ImmersalSession is required.");
            if (_localizer == null || !ReferenceEquals(_sdk.Localizer, _localizer))
            {
                throw new InvalidOperationException(
                    "ImmersalSDK must reference the co-located Localizer.");
            }

            _localizationMethod = _localizationMethodObject as ILocalizationMethod;
            if (_localizationMethod == null)
            {
                throw new InvalidOperationException(
                    "Immersal localization method is not assigned.");
            }

            if (!_localizer.AvailableLocalizationMethods.Contains(_localizationMethod))
            {
                throw new InvalidOperationException(
                    "The assigned localization method is not registered with the Immersal Localizer.");
            }

            if (_platform == null)
                throw new InvalidOperationException("Immersal platform support is not configured.");
            if (_sdk.SceneUpdater == null)
                throw new InvalidOperationException("Immersal scene updater is not configured.");
            if (_sdk.TrackingAnalyzer == null)
                throw new InvalidOperationException("Immersal tracking analyzer is not configured.");
            if (_sdk.TrackingAnalyzer is Behaviour trackingAnalyzer &&
                !trackingAnalyzer.isActiveAndEnabled)
            {
                throw new InvalidOperationException(
                    "Immersal tracking analyzer must be active and enabled.");
            }

            _session.AutoStart = false;
            _session.RestartOnReset = false;
        }

        private async Task PrepareSdkForOperationAsync(
            CancellationToken cancellationToken)
        {
            await EnsureSdkReadyAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // ImmersalSession swallows task exceptions. Keep it stopped and run
            // the selected method directly so every failure reaches our caller.
            await _session.StopSession();
            cancellationToken.ThrowIfCancellationRequested();

            await _localizer.StopAndCleanUp();
            cancellationToken.ThrowIfCancellationRequested();

            _sdk.TrackingAnalyzer.Reset();
        }

        private async Task EnsureSdkReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Initialization invokes IPlatformSupport.ConfigurePlatform. Record
            // ownership BEFORE awaiting it, including partial/cancelled startup.
            if (!_sdk.IsReady && _ownsPlatformLifecycle)
                _platformConfigured = true;

            Task initialization = _runtimeInitialization.EnsureReadyAsync(
                () => _sdk.Initialize(), () => _sdk != null && _sdk.IsReady);
            await AwaitWorkWithOwnedPlatformCancellationAsync(initialization, cancellationToken);
        }

        private async Task AwaitWorkWithOwnedPlatformCancellationAsync(
            Task work, CancellationToken cancellationToken)
        {
            if (work == null)
                throw new InvalidOperationException("Immersal SDK work returned no task.");
            if (_ownsPlatformLifecycle && cancellationToken.CanBeCanceled)
            {
                var cancellation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
                {
                    await Task.WhenAny(work, cancellation.Task);
                    if (!work.IsCompleted)
                    {
                        await StopAbandonedOwnedTaskAsync(work);
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
            }
            // Host-owned AR Foundation is never stopped by point-cloud cancellation.
            // In-flight native work must finish before cleanup/replacement.
            await work;
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task ConfigurePlatformAsync(
            CancellationToken cancellationToken)
        {
            // Configure may acquire resources before it reports failure or
            // throws, so every attempted owned configuration needs cleanup.
            _platformConfigured = true;
            Task<IPlatformConfigureResult> configureTask = _platform.ConfigurePlatform();
            await AwaitWorkWithOwnedPlatformCancellationAsync(configureTask, cancellationToken);
            IPlatformConfigureResult result = await configureTask;
            if (result == null)
            {
                throw new InvalidOperationException(
                    "Immersal platform configuration returned no result.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "Immersal platform configuration failed.");
            }
        }

        private async Task ConfigureMapAsync(
            ImmersalMapArchiveReader.MapData map,
            CancellationToken cancellationToken)
        {
            int mapId = ParseMapId(map.Name);
            if (MapManager.TryGetMapEntry(mapId, out _))
            {
                throw new InvalidOperationException(
                    $"Immersal map ID {mapId} is already registered.");
            }

            XRMap runtimeMap = CreateRuntimeMap();
            runtimeMap.SetIdAndName(
                mapId,
                string.IsNullOrWhiteSpace(map.Name)
                    ? $"Runtime Map {mapId}"
                    : map.Name,
                true);
            runtimeMap.LocalizationMethod = _localizationMethod;

            MapLoadingOption loadingOption = null;
            bool needsLocalMap = _localizationMethod.MapOptions?
                .Any(option => option is MapLoadingOption) == true;
            if (needsLocalMap)
            {
                loadingOption = new MapLoadingOption
                {
                    m_SerializedDataSource = (int)MapDataSource.Embed,
                    DownloadVisualizationAtRuntime = false,
                    Bytes = map.Bytes
                };
                runtimeMap.MapOptions = new List<IMapOption> { loadingOption };
            }
            else
            {
                runtimeMap.MapOptions = new List<IMapOption>();
            }

            runtimeMap.Configure();

            try
            {
                DefaultLocalizationMethodConfiguration configuration =
                    new DefaultLocalizationMethodConfiguration
                    {
                        MapsToAdd = new[] { runtimeMap }
                    };
                bool configured = await _localizationMethod.Configure(configuration);
                _methodConfigured = configured;
                cancellationToken.ThrowIfCancellationRequested();
                if (!configured)
                {
                    throw new InvalidOperationException(
                        "Immersal localization method configuration failed.");
                }

                MapManager.RegisterMap(runtimeMap, _poseSink);
                await _localizationMethod.OnMapRegistered(runtimeMap);
                cancellationToken.ThrowIfCancellationRequested();

                bool loaded = await MapManager.LoadMap(runtimeMap);
                cancellationToken.ThrowIfCancellationRequested();
                if (!loaded || !MapManager.TryGetMapEntry(mapId, out _))
                {
                    throw new InvalidOperationException(
                        "Immersal map registration or loading failed.");
                }
            }
            finally
            {
                if (loadingOption != null)
                    loadingOption.Bytes = null;
                map.ClearBytes();
            }
        }

        private async Task<Pose?> ScanUntilStableAsync(
            int operationId,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress,
            double attemptIntervalSeconds,
            Func<LocalizationGateResult, CancellationToken, Task> onProvisional = null)
        {
            LocalizationConfirmationGate scanGate = _confirmationGate;
            double lastAttemptStarted = double.NegativeInfinity;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Never require the obsolete gate to confirm before changing modes.
                if (!ReferenceEquals(scanGate, _confirmationGate))
                    return null;

                // Pace acquisition BEFORE taking an image lease. This is not an
                // operation timeout, and cancellation never waits for the cadence.
                double delay = attemptIntervalSeconds -
                    (Time.realtimeSinceStartupAsDouble - lastAttemptStarted);
                if (delay > 0d)
                    await Task.Delay((int)Math.Ceiling(delay * 1000d), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(scanGate, _confirmationGate))
                    return null;
                lastAttemptStarted = Time.realtimeSinceStartupAsDouble;
                _maintenance.BeginAttempt(lastAttemptStarted);
                if (_isMaintaining && lastAttemptStarted - _lastMaintenanceHeartbeatAt >= 5d)
                {
                    _lastMaintenanceHeartbeatAt = lastAttemptStarted;
                    Log(operationId, $"Maintenance scanning: mode=" +
                        $"{(_confirmationGate.UsesTemporalObservations ? "temporal_consistency" : "reacquisition")}, " +
                        $"stable={_confirmationGate.StableCount}/{_confirmationGate.RequiredSamples}, " +
                        $"attempt={_diagnosticAttemptId}.");
                }
                if (_confirmationGate.AdvanceTime(lastAttemptStarted))
                {
                    _diagnostics?.Expiry(_confirmationGate.LastExpiryTrace);
                    ReportProgress(progress, GetProgressStage(_confirmationGate.StableCount));
                }

                int frameTrackingRevision = _trackingMonitor.Revision;
                double platformStarted = Time.realtimeSinceStartupAsDouble;
                IPlatformUpdateResult platformResult =
                    await UpdatePlatformForOperationAsync(cancellationToken);
                double platformReturned = Time.realtimeSinceStartupAsDouble;
                ICameraData cameraData = platformResult?.CameraData;
                IImageData frameLease = null;
                ILocalizationResult localizationResult = null;
                bool rejectedForLowDetail = false;
                bool rejectedForTracking = false;
                FrameEvidence frameEvidence = default;
                Pose cameraPose = Pose.identity;
                long attemptId = 0;
                LocalizationResultTrace diagnosticResult = null;
                LocalizationResults localizationResults = new LocalizationResults
                {
                    Results = Array.Empty<ILocalizationResult>()
                };

                try
                {
                    // Keep the platform image alive across validation,
                    // localization and scene conversion. The selected
                    // localization method takes one additional reference.
                    frameLease = cameraData?.GetImageData();
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidatePlatformResult(platformResult);
                    _trackingMonitor.ObservePlatformStatus(platformResult.Status.TrackingQuality);

                    if (!ReferenceEquals(scanGate, _confirmationGate) || !_trackingMonitor.CanAccept(frameTrackingRevision))
                    {
                        rejectedForTracking = true;
                        _diagnostics?.Idle(platformResult.Status.TrackingQuality);
                    }
                    else if (platformResult.Success)
                    {
                        // RokidSpatialFrameSource already rejects repeated native timestamps.
                        // AR Foundation can return the same CPU image twice; do not count it twice.
                        if (frameLease is ARFImageData arfImage &&
                            !_captureFreshness.TryAccept(arfImage.Image.timestamp))
                        {
                            _diagnostics?.Write("result_discarded", new LocalizationTrackingTrace
                            {
                                reason = "duplicate_or_invalid_capture_timestamp",
                                captureRevision = frameTrackingRevision,
                                currentRevision = _trackingMonitor.Revision
                            });
                            ReportProgress(progress, GetProgressStage(_confirmationGate.StableCount),
                                requirement: PointCloudConfirmationRequirement.CameraUnavailable);
                            continue;
                        }
                        attemptId = ++_diagnosticAttemptId;
                        rejectedForLowDetail = !HasEnoughFrameDetail(
                            operationId,
                            cameraData,
                            frameLease,
                            out frameEvidence);
                        RecordFrame(attemptId, cameraData, frameLease, platformResult.Status.TrackingQuality,
                            frameEvidence, platformStarted, platformReturned);
                        if (!rejectedForLowDetail)
                        {
                            cameraPose = new Pose(
                                cameraData.CameraPositionOnCapture,
                                cameraData.CameraRotationOnCapture);
                            if (!IsFinitePose(cameraPose))
                            {
                                throw new InvalidOperationException(
                                    "Immersal camera pose contains invalid values.");
                            }

                            double localizationStarted = Time.realtimeSinceStartupAsDouble;
                            long sinkRevisionBefore = _poseSink.Revision;
                            localizationResult = await LocalizeForOperationAsync(
                                cameraData,
                                cancellationToken);
                            diagnosticResult = RecordSdkResult(attemptId, cameraData, localizationResult,
                                sinkRevisionBefore, Time.realtimeSinceStartupAsDouble - localizationStarted);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!ReferenceEquals(scanGate, _confirmationGate) || !_trackingMonitor.CanAccept(frameTrackingRevision))
                            {
                                rejectedForTracking = true;
                                _diagnostics?.Write("result_discarded", new LocalizationTrackingTrace
                                {
                                    attemptId = attemptId, reason = "tracking_changed_during_localization",
                                    captureRevision = frameTrackingRevision, currentRevision = _trackingMonitor.Revision
                                });
                            }
                            else if (localizationResult == null)
                            {
                                throw new InvalidOperationException(
                                    "Immersal localization returned no result.");
                            }

                            localizationResults = new LocalizationResults
                            {
                                Results = new[] { localizationResult }
                            };

                            if (!rejectedForTracking && localizationResult.Success)
                            {
                                if (localizationResult.MapId != _runtimeMap.mapId)
                                {
                                    throw new InvalidOperationException(
                                        $"Immersal returned unexpected map ID " +
                                        $"{localizationResult.MapId}; expected " +
                                        $"{_runtimeMap.mapId}.");
                                }

                                if (!MapManager.TryGetMapEntry(
                                        localizationResult.MapId,
                                        out MapEntry entry))
                                {
                                    throw new InvalidOperationException(
                                        "Immersal localization result has no registered map.");
                                }

                                await _sdk.SceneUpdater.UpdateScene(
                                    entry,
                                    cameraData,
                                    localizationResult);
                                if (!ReferenceEquals(scanGate, _confirmationGate) || !_trackingMonitor.CanAccept(frameTrackingRevision))
                                {
                                    rejectedForTracking = true;
                                    _diagnostics?.Write("result_discarded", new LocalizationTrackingTrace
                                    {
                                        attemptId = attemptId, reason = "tracking_changed_during_conversion",
                                        captureRevision = frameTrackingRevision, currentRevision = _trackingMonitor.Revision
                                    });
                                }
                                else if (_poseSink.Revision <= sinkRevisionBefore || !_poseSink.TryGetPose(out _))
                                    throw new InvalidOperationException("Immersal scene conversion did not publish this frame's pose.");
                                if (diagnosticResult != null)
                                {
                                    diagnosticResult.sinkRevisionAfter = _poseSink.Revision;
                                    diagnosticResult.convertedPoseAvailable = _poseSink.TryGetPose(out Pose converted);
                                    diagnosticResult.convertedPose = diagnosticResult.convertedPoseAvailable
                                        ? ImmersalLocalizationDiagnostics.Pose(converted) : null;
                                    _diagnostics?.Write("pose_conversion", diagnosticResult);
                                }
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                        }
                    }
                    else
                    {
                        _diagnostics?.Idle(platformResult.Status.TrackingQuality);
                    }
                }
                finally
                {
                    frameLease?.Dispose();
                }

                if (rejectedForTracking || rejectedForLowDetail)
                {
                    ReportProgress(
                        progress,
                        rejectedForTracking ? PointCloudLocalizationStage.TrackingLost : PointCloudLocalizationStage.NeedMoreVisualDetail,
                        requirement: rejectedForTracking ? PointCloudConfirmationRequirement.TrackingRecovery :
                            PointCloudConfirmationRequirement.InsufficientVisualDetail);
                    await WaitForNextFrameAsync(cancellationToken);
                    continue;
                }

                _sdk.TrackingAnalyzer.Analyze(
                    platformResult.Status,
                    localizationResults);

                // Let TrackingAnalyzer process this result once.
                await WaitForNextFrameAsync(cancellationToken);

                if (!ReferenceEquals(scanGate, _confirmationGate) || !_trackingMonitor.CanAccept(frameTrackingRevision))
                {
                    _diagnostics?.Write("result_discarded", new LocalizationTrackingTrace
                    {
                        attemptId = attemptId, reason = "tracking_changed_before_confirmation",
                        captureRevision = frameTrackingRevision, currentRevision = _trackingMonitor.Revision
                    });
                    ReportProgress(progress, PointCloudLocalizationStage.TrackingLost,
                        requirement: PointCloudConfirmationRequirement.TrackingRecovery);
                    continue;
                }

                if (localizationResult?.Success == true)
                {
                    LocalizationGateResult gateResult = RegisterCurrentResult(
                        operationId,
                        localizationResult,
                        cameraPose,
                        frameEvidence,
                        attemptId);
                    ReportProgress(
                        progress,
                        GetProgressStage(gateResult.StableCount),
                        gateResult.IsConfirmed,
                        gateResult.HasCurrentPose ? (PointCloudConfirmationRequirement?)null :
                            PointCloudConfirmationRequirement.StrongerMatch);
                    // Progress callbacks may synchronously cancel or reset the
                    // tracking origin. Revalidate before publishing their pose.
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(scanGate, _confirmationGate) ||
                        !_trackingMonitor.CanAccept(frameTrackingRevision))
                        return null;
                    if (onProvisional != null)
                        await onProvisional(gateResult, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(scanGate, _confirmationGate) ||
                        !_trackingMonitor.CanAccept(frameTrackingRevision))
                        return null;
                    if (gateResult.IsConfirmed)
                        return gateResult.ConfirmedPose;
                }
                else if (localizationResult != null)
                {
                    LocalizationGateResult gateResult =
                        _confirmationGate.Register(
                            cameraPose,
                            frameEvidence.Signature,
                            hasMapPose: false,
                            Pose.identity,
                            qualityScore: 0f,
                            isStrongMatch: false,
                            hasContextView: IsContextView(cameraPose),
                            attemptId: attemptId,
                            observedAtSeconds: Time.realtimeSinceStartupAsDouble);
                    _diagnostics?.Gate(attemptId, gateResult, sdkSuccess: false);
                    _diagnostics?.Expiry(_confirmationGate.LastExpiryTrace);
                    ReportProgress(
                        progress,
                        GetProgressStage(gateResult.StableCount),
                        requirement: PointCloudConfirmationRequirement.NoMapMatch);
                    if (onProvisional != null)
                        await onProvisional(gateResult, cancellationToken);
                    if (!_hasLoggedLocalizationFailure)
                    {
                        _hasLoggedLocalizationFailure = true;
                        Log(operationId, "No map match yet; continuing localization scan.");
                    }
                }
                else
                {
                    ReportProgress(progress, GetProgressStage(_confirmationGate.StableCount),
                        requirement: PointCloudConfirmationRequirement.CameraUnavailable);
                }
            }
        }

        private bool HasEnoughFrameDetail(
            int operationId,
            ICameraData cameraData,
            IImageData frameLease,
            out FrameEvidence evidence)
        {
            if (frameLease == null ||
                !FrameEvidenceAnalyzer.TryAnalyze(
                    frameLease.UnmanagedDataPointer,
                    cameraData.Width,
                    cameraData.Height,
                    cameraData.Channels,
                    out evidence,
                    minimumContrast: _policy.MinimumFrameContrast,
                    minimumEdgeRatio: _policy.MinimumFrameEdgeRatio,
                    minimumCornerCount: _policy.MinimumFrameCornerCount,
                    minimumDetailedRegions: _policy.MinimumFrameDetailedRegions))
            {
                throw new InvalidOperationException(
                    "Immersal camera frame has invalid dimensions or pixel data.");
            }

            if (evidence.HasEnoughDetail)
            {
                if (!_hasLoggedDetailedFrame)
                {
                    _hasLoggedDetailedFrame = true;
                    Log(
                        operationId,
                        $"First detail-qualified frame: contrast=" +
                        $"{evidence.Contrast:0.0}, edgeRatio=" +
                        $"{evidence.EdgeRatio:0.000}, corners=" +
                        $"{evidence.CornerCount}, detailedRegions=" +
                        $"{evidence.DetailedRegionCount}/{evidence.RegionCount}.");
                }

                return true;
            }

            _lowDetailFrameCount++;
            if (_lowDetailFrameCount == 1 || _lowDetailFrameCount % 10 == 0)
            {
                Log(
                    operationId,
                    $"Ignoring low-detail camera frame #{_lowDetailFrameCount}: " +
                    $"contrast={evidence.Contrast:0.0}, edgeRatio=" +
                    $"{evidence.EdgeRatio:0.000}, corners=" +
                    $"{evidence.CornerCount}, detailedRegions=" +
                    $"{evidence.DetailedRegionCount}/{evidence.RegionCount}.");
            }

            return false;
        }

        private async Task<ILocalizationResult> LocalizeForOperationAsync(
            ICameraData cameraData,
            CancellationToken cancellationToken)
        {
            Task<ILocalizationResult> localizeTask =
                _localizationMethod.Localize(cameraData, cancellationToken);
            if (localizeTask == null)
            {
                throw new InvalidOperationException(
                    "Immersal localization method returned a null task.");
            }

            await AwaitWorkWithOwnedPlatformCancellationAsync(localizeTask, cancellationToken);
            return await localizeTask;
        }

        private async Task<IPlatformUpdateResult> UpdatePlatformForOperationAsync(
            CancellationToken cancellationToken)
        {
            Task<IPlatformUpdateResult> updateTask = _platform.UpdatePlatform();
            if (!_ownsPlatformLifecycle || !cancellationToken.CanBeCanceled)
                return await updateTask;

            TaskCompletionSource<bool> cancellation =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                await Task.WhenAny(updateTask, cancellation.Task);
                if (updateTask.IsCompleted)
                    return await updateTask;

                await StopAbandonedOwnedPlatformUpdateAsync(updateTask);
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private async Task StopAbandonedOwnedPlatformUpdateAsync(
            Task<IPlatformUpdateResult> updateTask)
        {
            List<Exception> failures = new List<Exception>();
            IPlatformUpdateResult abandonedResult = null;

            await StopOwnedPlatformAsync(failures);

            try
            {
                abandonedResult = await updateTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the owned adapter cancels its startup work.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                abandonedResult?.CameraData?.CheckReferences();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    $"{LogPrefix} Owned platform cancellation cleanup failed: " +
                    $"{new AggregateException(failures)}",
                    this);
            }
        }

        private async Task StopAbandonedOwnedTaskAsync(Task ownedTask)
        {
            List<Exception> failures = new List<Exception>();
            await StopOwnedPlatformAsync(failures);

            try
            {
                await ownedTask;
            }
            catch (OperationCanceledException)
            {
                // The method observed the same operation cancellation.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count > 0)
            {
                Debug.LogError(
                    $"{LogPrefix} Owned platform cancellation during " +
                    $"SDK work failed: {new AggregateException(failures)}",
                    this);
            }
        }

        private async Task StopOwnedPlatformAsync(
            ICollection<Exception> failures)
        {
            try
            {
                await _platform.StopAndCleanUp();
                _platformConfigured = false;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void ValidatePlatformResult(
            IPlatformUpdateResult platformResult)
        {
            if (platformResult == null)
            {
                throw new InvalidOperationException(
                    "Immersal platform update returned no result.");
            }

            if (platformResult.Status == null)
            {
                throw new InvalidOperationException(
                    "Immersal platform update returned no tracking status.");
            }

            if (platformResult.Success && platformResult.CameraData == null)
            {
                throw new InvalidOperationException(
                    "Immersal platform update succeeded without camera data.");
            }
        }

        private LocalizationGateResult RegisterCurrentResult(
            int operationId,
            ILocalizationResult localizationResult,
            Pose cameraPose,
            FrameEvidence frameEvidence,
            long attemptId)
        {
            int quality = _sdk.TrackingStatus?.TrackingQuality ?? -1;
            LocalizeInfo info = localizationResult.LocalizeInfo;
            _successfulSampleCount++;
            if (_successfulSampleCount == 1)
            {
                Log(
                    operationId,
                    $"First sample quality={quality}, confidence={info.confidence}, " +
                    $"rmse={info.rmse:0.###}.");
            }

            bool hasMapPose = false;
            Pose sample = Pose.identity;
            if (IsReliableLocalization(info) &&
                MapManager.TryGetMapEntry(_runtimeMap.mapId, out MapEntry entry) &&
                ReferenceEquals(entry.SceneParent, _poseSink) &&
                _poseSink.TryGetPose(out sample))
            {
                if (!IsFinitePose(sample))
                {
                    throw new InvalidOperationException(
                        "Immersal map pose contains invalid values.");
                }

                hasMapPose = true;
            }
            else if (!IsReliableLocalization(info))
            {
                if (!_hasLoggedWeakMatch)
                {
                    _hasLoggedWeakMatch = true;
                    Log(
                        operationId,
                        $"Ignoring weak map match: confidence={info.confidence} " +
                        $"(minimum {_policy.MinimumLocalizationConfidence}), rmse=" +
                        $"{info.rmse:0.###} (maximum {_policy.MaximumLocalizationRmse:0.###}).");
                }
            }

            float qualityScore = hasMapPose
                ? info.confidence - (float)info.rmse
                : 0f;
            bool isStrongMatch = hasMapPose && IsStrongLocalization(info);
            LocalizationGateResult gateResult = _confirmationGate.Register(
                cameraPose,
                frameEvidence.Signature,
                hasMapPose,
                sample,
                qualityScore,
                isStrongMatch,
                IsContextView(cameraPose),
                attemptId,
                Time.realtimeSinceStartupAsDouble);
            if (gateResult.Trace != null)
                gateResult.Trace.sourcePoseRevision = _poseSink?.Revision ?? 0;
            _diagnostics?.Gate(attemptId, gateResult, sdkSuccess: true);
            _diagnostics?.Expiry(_confirmationGate.LastExpiryTrace);
            Log(
                operationId,
                $"Gate observation={(gateResult.IsNewObservation ? "new" : "same")}, " +
                $"stable={gateResult.StableCount}/{_confirmationGate.RequiredSamples}, " +
                $"window={gateResult.ObservationCount}, " +
                $"poses={gateResult.PoseCount}, outliers={gateResult.OutlierCount}, " +
                $"viewDelta=({gateResult.NearestPositionDelta:0.###}m," +
                $"{gateResult.NearestRotationDelta:0.###}deg," +
                $"imageBits={gateResult.NearestSignatureDistance}), " +
                $"evidence=(strong={gateResult.HasStrongMatch}," +
                $"context={gateResult.HasContextView}," +
                $"span={gateResult.StablePositionSpan:0.###}m/" +
                $"{gateResult.StableRotationSpan:0.###}deg," +
                $"diverse={gateResult.HasViewDiversity}), " +
                $"accepted={hasMapPose}, quality={quality}, " +
                $"confidence={info.confidence}, " +
                $"rmse={info.rmse:0.###}.");
            return gateResult;
        }

        private bool IsReliableLocalization(LocalizeInfo info)
        {
            return ImmersalPosePolicy.IsReliable(_policy, info.confidence, info.rmse);
        }

        private bool IsStrongLocalization(LocalizeInfo info)
        {
            return ImmersalPosePolicy.IsStrong(_policy, info.confidence, info.rmse);
        }

        private bool IsContextView(Pose cameraPose)
        {
            return ImmersalPosePolicy.IsContext(_policy, cameraPose);
        }

        internal static PointCloudLocalizationStage GetProgressStage(
            int stableCount)
        {
            // Missing quality/diversity is unfinished confirmation, not lost
            // image detail. Keep the HUD in confirmation while votes remain.
            // Counts and the remaining requirement are reported separately.
            return stableCount > 0
                ? PointCloudLocalizationStage.Confirming
                : PointCloudLocalizationStage.Searching;
        }

        private static bool IsFinitePose(Pose pose)
        {
            return IsFinite(pose.position.x) &&
                   IsFinite(pose.position.y) &&
                   IsFinite(pose.position.z) &&
                   IsFinite(pose.rotation.x) &&
                   IsFinite(pose.rotation.y) &&
                   IsFinite(pose.rotation.z) &&
                   IsFinite(pose.rotation.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ReportProgress(
            IProgress<PointCloudLocalizationProgress> progress,
            PointCloudLocalizationStage stage,
            bool isConfirmed = false,
            PointCloudConfirmationRequirement? requirement = null)
        {
            if (progress == null)
                return;

            try
            {
                if (_maintenance?.Reacquiring == true &&
                    (stage == PointCloudLocalizationStage.Searching ||
                     stage == PointCloudLocalizationStage.Confirming ||
                     stage == PointCloudLocalizationStage.NeedMoreVisualDetail))
                    stage = PointCloudLocalizationStage.Reacquiring;
                int required = _confirmationGate?.RequiredSamples ?? _policy.RequiredStableSamples;
                progress.Report(new PointCloudLocalizationProgress(
                    stage,
                    _confirmationGate?.TotalDistinctObservationCount ?? 0,
                    _confirmationGate?.StableCount ?? 0,
                    required,
                    _lastVerifiedAtSeconds,
                    requirement ?? (stage == PointCloudLocalizationStage.TrackingLost ? PointCloudConfirmationRequirement.TrackingRecovery :
                    isConfirmed ? PointCloudConfirmationRequirement.None :
                        _confirmationGate?.PendingRequirement ?? PointCloudConfirmationRequirement.CollectingSamples),
                    isConfirmed));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{LogPrefix} Progress callback failed and was ignored.\n" +
                    $"{exception}",
                    this);
            }
        }

        private Task WaitForNextFrameAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            StartCoroutine(CompleteOnNextFrame(completion));
            return AwaitNextFrameAsync(completion, cancellationToken);
        }

        private static async Task AwaitNextFrameAsync(
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
            {
                await completion.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static IEnumerator CompleteOnNextFrame(
            TaskCompletionSource<bool> completion)
        {
            yield return null;
            completion.TrySetResult(true);
        }

        private async Task<Exception> CleanupAsync(int operationId)
        {
            List<Exception> failures = new List<Exception>();
            CaptureCleanupFailure(() => _sdk?.OnReset?.RemoveListener(OnTrackingSpaceReset), failures);
            CaptureCleanupFailure(() => _session?.OnReset?.RemoveListener(OnTrackingSpaceReset), failures);
            CaptureCleanupFailure(() => _trackingMonitor?.Dispose(), failures);
            _trackingMonitor = null;

            if (_session != null)
            {
                await CaptureCleanupFailureAsync(
                    () => _session.StopSession(),
                    failures);
            }

            // Owned camera input is the most time-sensitive resource. Stop it
            // before unloading maps or tearing down the localization method.
            if (_platformConfigured && _platform != null)
            {
                await CaptureCleanupFailureAsync(
                    () => _platform.StopAndCleanUp(),
                    failures);
            }

            if (_methodConfigured &&
                _localizationMethod != null &&
                _runtimeMap != null)
            {
                await CaptureCleanupFailureAsync(
                    async () =>
                    {
                        DefaultLocalizationMethodConfiguration configuration =
                            new DefaultLocalizationMethodConfiguration
                            {
                                MapsToRemove = new[] { _runtimeMap }
                            };
                        // False means the final map was removed successfully.
                        await _localizationMethod.Configure(configuration);
                    },
                    failures);
            }

            if (_localizationMethod != null)
            {
                await CaptureCleanupFailureAsync(
                    () => _localizationMethod.StopAndCleanUp(),
                    failures);
            }

            if (_localizer != null)
            {
                await CaptureCleanupFailureAsync(
                    () => _localizer.StopAndCleanUp(),
                    failures);
            }

            if (_runtimeMap != null)
            {
                CaptureCleanupFailure(
                    () =>
                    {
                        int mapId = _runtimeMap.mapId;
                        if (MapManager.TryGetMapEntry(mapId, out _))
                        {
                            MapManager.RemoveMap(
                                mapId,
                                removeFromLocalizer: false,
                                destroyObjects: false);
                        }
                    },
                    failures);

                CaptureCleanupFailure(
                    () =>
                    {
                        if (_runtimeMap.IsConfigured)
                            _runtimeMap.Uninitialize();
                    },
                    failures);
            }

            CaptureCleanupFailure(DestroyRuntimeObjects, failures);

            if (_sdk?.TrackingAnalyzer != null)
            {
                CaptureCleanupFailure(
                    () => _sdk.TrackingAnalyzer.Reset(),
                    failures);
            }

            _platformConfigured = false;
            _methodConfigured = false;
            _platform = null;
            _localizationMethod = null;
            _localizer = null;
            _session = null;
            _sdk = null;
            _maintenance = null;
            _operationProgress = null;
            _operationCancellationToken = default;
            _lastTrackingStage = null;
            Log(operationId, "Session, map, localizer and platform resources released.");

            return failures.Count == 0
                ? null
                : new AggregateException(failures);
        }

        private static async Task CaptureCleanupFailureAsync(
            Func<Task> action,
            ICollection<Exception> failures)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private static void CaptureCleanupFailure(
            Action action,
            ICollection<Exception> failures)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        private XRMap CreateRuntimeMap()
        {
            GameObject spaceObject = new GameObject("Runtime Point Cloud Space");
            spaceObject.transform.SetParent(transform, false);
            _poseSink = new RawPoseSink(spaceObject.transform, OnTrackingSpaceReset);

            GameObject mapObject = new GameObject("Runtime Point Cloud Map");
            mapObject.transform.SetParent(spaceObject.transform, false);
            _runtimeMap = mapObject.AddComponent<XRMap>();
            return _runtimeMap;
        }

        private void DestroyRuntimeObjects()
        {
            GameObject root = _poseSink != null
                ? _poseSink.GetTransform().gameObject
                : _runtimeMap != null
                    ? _runtimeMap.gameObject
                    : null;
            _runtimeMap = null;
            _poseSink = null;
            if (root == null)
                return;

            root.SetActive(false);
            if (Application.isPlaying)
                Destroy(root);
            else
                DestroyImmediate(root);
        }

        private sealed class RawPoseSink : ISceneUpdateable
        {
            private readonly Transform _transform;
            private readonly Action _onReset;
            private bool _hasPose;
            private Pose _pose;
            internal long Revision { get; private set; }

            internal RawPoseSink(Transform transform, Action onReset)
            {
                _transform = transform;
                _onReset = onReset;
            }

            public Task SceneUpdate(SceneUpdateData data)
            {
                if (data == null)
                {
                    throw new InvalidOperationException(
                        "Immersal scene updater returned no pose data.");
                }
                if (data.Ignore || !data.Pose.ValidTRS())
                {
                    throw new InvalidOperationException(
                        "Immersal scene updater returned an invalid map pose.");
                }

                _pose = new Pose(data.Pose.GetPosition(), data.Pose.rotation);
                _hasPose = true;
                Revision++;
                _transform.SetPositionAndRotation(_pose.position, _pose.rotation);
                return Task.CompletedTask;
            }

            public Transform GetTransform()
            {
                return _transform;
            }

            public Task ResetScene()
            {
                ClearPose();
                _onReset?.Invoke();
                return Task.CompletedTask;
            }

            internal void ClearPose()
            {
                _hasPose = false;
                Revision++;
                _pose = Pose.identity;
                _transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            internal bool TryGetPose(out Pose pose)
            {
                pose = _pose;
                return _hasPose;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            _applicationPaused = paused;
            _trackingMonitor?.SetPaused(paused);
        }

        private void OnTrackingSpaceReset()
        {
            _trackingMonitor?.Invalidate("sdk_or_map_space_reset");
        }

        private void ResetTrackingEvidence(string reason)
        {
            int previousStableCount = _confirmationGate?.StableCount ?? 0;
            _captureFreshness.Reset();
            _maintenance?.Invalidate(Time.realtimeSinceStartupAsDouble);
            _poseSink?.ClearPose();
            if (!_operationCancellationToken.IsCancellationRequested)
                ReportProgress(_operationProgress, PointCloudLocalizationStage.TrackingLost,
                    requirement: PointCloudConfirmationRequirement.TrackingRecovery);
            _diagnostics?.Write("tracking_evidence_reset", new LocalizationTrackingTrace
            {
                reason = reason, currentRevision = _trackingMonitor?.Revision ?? 0
            });
            Log(_operationId, $"Tracking evidence cleared: {reason}; stable={previousStableCount}->0.");
        }

        private void Update()
        {
            if (!_isMaintaining || _trackingMonitor == null || _operationCancellationToken.IsCancellationRequested) return;
            // This also runs while native localization is pending. Gate identity
            // changes discard that work's result after its image lease is drained.
            RefreshMaintenanceTracking();
        }

        private void RefreshMaintenanceTracking()
        {
            int revision = _maintenance.Revision;
            _maintenance.RefreshTracking(Time.realtimeSinceStartupAsDouble, _trackingMonitor.IsTracking);
            if (_maintenance.Revision != revision)
                ReportTrackingState(PointCloudLocalizationStage.TrackingLost);
            ReportTrackingState(_maintenance.Stage);
        }

        private void ReportTrackingState(PointCloudLocalizationStage stage)
        {
            if (_operationCancellationToken.IsCancellationRequested) return;
            if (_lastTrackingStage == stage && _lastReportedVerification == _lastVerifiedAtSeconds) return;
            _lastTrackingStage = stage;
            _lastReportedVerification = _lastVerifiedAtSeconds;
            ReportProgress(_operationProgress, stage, stage == PointCloudLocalizationStage.Tracking);
        }

        internal static int ParseMapId(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return FallbackRuntimeMapId;

            int digitCount = 0;
            while (digitCount < mapName.Length &&
                   char.IsDigit(mapName[digitCount]))
            {
                digitCount++;
            }

            if (digitCount > 0 &&
                int.TryParse(mapName.Substring(0, digitCount), out int parsed) &&
                parsed > 0)
            {
                return parsed;
            }

            return FallbackRuntimeMapId;
        }

        private void Log(int operationId, string message)
        {
            Debug.Log($"{LogPrefix} [op:{operationId}] {message}", this);
        }

        private static string FormatPose(Pose pose)
        {
            Vector3 euler = pose.rotation.eulerAngles;
            return $"pos=({pose.position.x:0.###},{pose.position.y:0.###}," +
                   $"{pose.position.z:0.###}) rot=({euler.x:0.###}," +
                   $"{euler.y:0.###},{euler.z:0.###})";
        }
    }
}
