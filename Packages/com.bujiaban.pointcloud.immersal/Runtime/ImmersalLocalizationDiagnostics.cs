using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Immersal;
using Immersal.XR;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal
{
    internal sealed class ImmersalLocalizationDiagnostics : IDisposable
    {
        internal const string Revision = "confirmation-guidance-2026-09-09.4";
        private readonly PointCloudDiagnosticJournal _journal;
        private readonly string _runId;
        private double _lastIdleAt;
        private int _idleCount;

        internal ImmersalLocalizationDiagnostics(string mapName, byte[] mapBytes, int operationId,
            object policy)
        {
            _runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
            string digest;
            using (var sha = SHA256.Create())
                digest = BitConverter.ToString(sha.ComputeHash(mapBytes)).Replace("-", "").ToLowerInvariant();
            string header = "{\"revision\":" + PointCloudDiagnosticJournal.Quote(Revision) +
                ",\"operationId\":" + operationId +
                ",\"appVersion\":" + PointCloudDiagnosticJournal.Quote(Application.version) +
                ",\"appIdentifier\":" + PointCloudDiagnosticJournal.Quote(Application.identifier) +
                ",\"buildGuid\":" + PointCloudDiagnosticJournal.Quote(Application.buildGUID) +
                ",\"unityVersion\":" + PointCloudDiagnosticJournal.Quote(Application.unityVersion) +
                ",\"platform\":" + PointCloudDiagnosticJournal.Quote(Application.platform.ToString()) +
                ",\"sdkVersion\":" + PointCloudDiagnosticJournal.Quote(ImmersalSDK.sdkVersion) +
                ",\"backendModule\":" + PointCloudDiagnosticJournal.Quote(ModuleId()) +
                ",\"mapType\":\"immersal\",\"mapName\":" + PointCloudDiagnosticJournal.Quote(mapName) +
                ",\"mapSha256\":" + PointCloudDiagnosticJournal.Quote(digest) +
                ",\"mapBytes\":" + mapBytes.Length + ",\"policy\":" + JsonUtility.ToJson(policy) + "}";
            _journal = new PointCloudDiagnosticJournal(
                Path.Combine(Application.persistentDataPath, "PointCloudDiagnostics"), _runId, header,
                warning => Debug.LogWarning("[PointCloudDiagnostics] " + warning));
            Application.logMessageReceivedThreaded += OnPlatformLog;
            Debug.Log($"[PointCloudDiagnostics] revision={Revision}, run={_runId}, file={_journal.CurrentPath}");
        }

        private static string ModuleId()
        {
            try { return typeof(ImmersalPointCloudBackend).Module.ModuleVersionId.ToString(); }
            catch { return "unavailable"; }
        }

        internal static string Setting(object component, string fieldName)
        {
            try
            {
                return component.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(component)?.ToString() ?? "unavailable";
            }
            catch { return "unavailable"; }
        }

        internal static string CaptureTimestamp(IImageData image, out string status)
        {
            status = image is ARFImageData ? "ARFImageData.Image.timestamp_seconds" : "not_exposed_by_image_data";
            return image is ARFImageData arfImage ? Number(arfImage.Image.timestamp) : null;
        }

        // Independent Rokid support stays independent: correlate its diagnostic line with
        // the same CameraData object, without adding a platform-to-backend API dependency.
        private void OnPlatformLog(string message, string stack, LogType type)
        {
            if (message == null) return;
            if (message.StartsWith("[RokidPointCloudAdapter]", StringComparison.Ordinal) ||
                message.StartsWith("[RokidSpatialData]", StringComparison.Ordinal))
                _journal.Write("platform_log", "{\"message\":" + PointCloudDiagnosticJournal.Quote(message) + "}");
        }

        internal void Write(string eventName, object data)
        {
            try { _journal.Write(eventName, JsonUtility.ToJson(data)); }
            catch (Exception exception) { Error("diagnostic_serialization", exception); }
        }

        internal void Error(string phase, Exception exception)
        {
            _journal.Write("error", "{\"phase\":" + PointCloudDiagnosticJournal.Quote(phase) +
                ",\"exception\":" + PointCloudDiagnosticJournal.Quote(exception.ToString()) + "}");
        }

        internal void Idle(int trackingQuality)
        {
            _idleCount++;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastIdleAt < 1d) return;
            Write("platform_unavailable", new IdleRecord { polls = _idleCount, trackingQuality = trackingQuality });
            _lastIdleAt = now;
            _idleCount = 0;
        }

        internal void Gate(long attemptId, LocalizationGateResult result, bool sdkSuccess)
        {
            Write("gate", result.Trace);
            var decision = new DecisionRecord
            {
                attemptId = attemptId, sdkSuccess = sdkSuccess,
                currentPoseAccepted = result.Trace?.inputHasMapPose ?? false,
                gateConfirmed = result.IsConfirmed,
                gateAllowsPoseReturn = sdkSuccess && result.IsConfirmed,
                confirmationPath = result.Trace?.confirmationPath,
                stableObservationIds = result.Trace?.stableObservationIds,
                medoidObservationId = result.Trace?.medoidObservationId ?? 0,
                pose = result.IsConfirmed ? LocalizationDiagnosticFormat.Pose(result.ConfirmedPose) : null
            };
            Write("decision", decision);
            Debug.Log($"[PointCloudDiagnostics] run={_runId}, attempt={attemptId}, " +
                $"sdkSuccess={sdkSuccess}, currentPoseAccepted={decision.currentPoseAccepted}, " +
                $"stable={result.StableCount}, gateConfirmed={result.IsConfirmed}, " +
                $"gateAllowsPoseReturn={decision.gateAllowsPoseReturn}, path={decision.confirmationPath}, " +
                $"strong={result.Trace?.strongMatchCount}, context={result.Trace?.contextMatchCount}, " +
                $"duration={Number(result.Trace?.durationSeconds ?? 0)}, block={result.Trace?.blockReason}, " +
                $"pairConflicts=(view={result.Trace?.currentViewConflicts},image={result.Trace?.currentImageConflicts}," +
                $"position={result.Trace?.currentPositionConflicts},rotation={result.Trace?.currentRotationConflicts}), " +
                $"pairDelta=(local={result.Trace?.currentMaxLocalPositionDelta:0.###}m," +
                $"origin={result.Trace?.currentMaxOriginPositionDelta:0.###}m,rotation={result.Trace?.currentMaxRotationDelta:0.###}deg).");
        }

        internal void Expiry(LocalizationGateTrace trace)
        {
            if (trace?.removed == null || trace.removed.Length == 0) return;
            Write("gate_expiry", trace);
            Debug.Log($"[PointCloudDiagnostics] run={_runId}, evidence expired: " +
                $"removed={trace.removed.Length}, stable={trace.stableObservationIds?.Length ?? 0}, " +
                $"reason={trace.removed[0].removalReason}.");
        }

        internal static int CameraId(ICameraData camera) => RuntimeHelpers.GetHashCode(camera);
        internal static string Pose(Pose pose) => LocalizationDiagnosticFormat.Pose(pose);
        internal static string Number(double value) => LocalizationDiagnosticFormat.Number(value);

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= OnPlatformLog;
            _journal.Dispose();
        }

        [Serializable] private sealed class IdleRecord { public int polls, trackingQuality; }
        [Serializable] private sealed class DecisionRecord
        {
            public long attemptId, medoidObservationId;
            public bool sdkSuccess, currentPoseAccepted, gateConfirmed, gateAllowsPoseReturn;
            public long[] stableObservationIds;
            public string confirmationPath, pose;
        }
    }

    [Serializable]
    internal sealed class LocalizationAttemptTrace
    {
        public long attemptId;
        public int unityFrame, cameraDataId, width, height, channels, trackingQuality, expectedMapId;
        public double platformStartedAtSeconds, platformReturnedAtSeconds, inspectedAtSeconds;
        public string sourceTimestampStatus, sourceCaptureTimestamp, imageOrientation, intrinsics, cameraPose, frameSignature;
        public string skipReason;
        public bool enoughDetail;
        public double contrast, edgeRatio;
        public int corners, detailedRegions;
    }

    [Serializable]
    internal sealed class LocalizationResultTrace
    {
        public long attemptId, sinkRevisionBefore, sinkRevisionAfter;
        public int cameraDataId, expectedMapId, returnedMapId, confidence;
        public bool sdkSuccess, rawInfoAvailable, reliable, strong, convertedPoseAvailable;
        public string rmse, rawPose, convertedPose, qualityReason;
        public double localizationSeconds;
    }

    [Serializable]
    internal sealed class LocalizationLifecycleTrace
    {
        public string phase, method, platform, sceneUpdater, pose, solverType, filterRadius;
        public int mapId;
        public bool cancelled, failed, willReturnPose;
    }

    [Serializable]
    internal sealed class LocalizationTrackingTrace
    {
        public long attemptId;
        public string reason;
        public int captureRevision, currentRevision;
    }

    [Serializable]
    internal sealed class MaintenanceDecisionTrace
    {
        public string action, currentPose, candidatePose;
        public float positionDelta, rotationDelta;
    }

    [Serializable]
    internal sealed class LocalizationPolicyTrace
    {
        public string profileName, profileSource;
        public int samples, window, confidence, strongConfidence, signatureDistance;
        public double rmse, strongRmse;
        public float viewPositionDelta, viewRotationDelta, positionDrift, rotationDrift;
        public float confirmationPositionSpan, confirmationRotationSpan, contextDownwardDot;
        public int strongMatches, contextMatches;
        public double confirmationSeconds, observationAgeSeconds, evidenceGapSeconds, attemptIntervalSeconds;
        public double minimumFrameContrast, minimumFrameEdgeRatio;
        public int minimumFrameCornerCount, minimumFrameDetailedRegions;
        public int maintenanceSamples, maintenanceWindow;
        public double maintenanceAttemptIntervalSeconds, maintenanceConfirmationSeconds;
        public double maintenanceObservationAgeSeconds, maintenanceEvidenceGapSeconds;
        public float maintenancePositionDrift, maintenanceRotationDrift;
        public float maximumAutomaticPositionCorrection, maximumAutomaticRotationCorrection;
        public float positionCorrectionDeadband, rotationCorrectionDeadband;
        public float maximumProvisionalPositionStep, maximumProvisionalRotationStepDegrees;
        public float provisionalCorrectionSmoothingSeconds, correctionSmoothingSeconds;
    }
}
