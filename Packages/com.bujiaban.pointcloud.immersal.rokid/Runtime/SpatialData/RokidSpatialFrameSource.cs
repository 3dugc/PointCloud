using System;
using System.Threading;
using System.Threading.Tasks;
using Rokid.UXR.Native;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace Bujiaban.PointCloud.Immersal.Rokid
{
    [DisallowMultipleComponent]
    internal sealed class RokidSpatialFrameSource : MonoBehaviour
    {
        private const RokidSpatialFrameFormat PreviewFormat =
            RokidSpatialFrameFormat.Yuv;
        private const bool UseHistoricalCameraPose = true;
        private const bool RecenterOnStart = false;
        private const bool CalibrateNativePoseHeight = true;
        private const int MaxStartAttempts = 10;
        private const int MillisecondsBetweenAttempts = 100;
        private const float MinimumTrackingCameraHeight = 0.25f;

        private XROrigin _xrOrigin;

        private readonly object _frameLock = new object();
        private byte[] _latestBytes;
        private int _latestWidth;
        private int _latestHeight;
        private long _latestTimestamp;
        private FrameTimestampGate _timestampGate;
        private Vector4 _intrinsics;
        private double[] _distortion = Array.Empty<double>();
        private bool _running;
        private bool _previewStarted;
        private bool _hasRecentered;
        private bool _hasLoggedFirstFrame;
        private bool _hasLoggedWaitingForFrame;
        private bool _hasLoggedPoseUnavailable;
        private float _nativeToTrackingHeightOffset;
        private bool _hasNativeHeightCalibration;

        internal int TrackingQuality => ReadTrackingQuality();

        internal async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_running)
                return;

            // Finish a previously incomplete native cleanup before starting a
            // fresh raw-preview session.
            if (_previewStarted)
                await StopAsync();

            if (Application.platform != RuntimePlatform.Android)
            {
                throw new PlatformNotSupportedException(
                    $"Rokid spatial frames require an Android player. Current platform: {Application.platform}.");
            }

            _xrOrigin ??= FindAnyObjectByType<XROrigin>();
            if (_xrOrigin == null || _xrOrigin.Origin == null)
            {
                throw new InvalidOperationException(
                    "Rokid spatial frames require an XR Origin.");
            }

            ResetHeightCalibration();
            _timestampGate.Reset();
            Log(
                $"Starting preview format={PreviewFormat}, " +
                $"historicalPose={UseHistoricalCameraPose}, " +
                $"heightCalibration={CalibrateNativePoseHeight}, " +
                $"recenter={RecenterOnStart}.");
            cancellationToken.ThrowIfCancellationRequested();
            bool nativeCallbackConfigured = false;
            try
            {
                // IsPreviewing is a readiness/status signal in Rokid UXR, not
                // an ownership signal. The raw callback does not produce frames
                // until StartCameraPreview is called, even when OpenXR image
                // tracking has already made IsPreviewing return true.
                NativeInterface.NativeAPI.SetCameraPreviewDataType(
                    (int)PreviewFormat);
                nativeCallbackConfigured = true;
                RegisterCameraListener();
                NativeInterface.NativeAPI.StartCameraPreview();
                _previewStarted = true;
                Log("Started the Rokid raw camera preview for point-cloud localization.");
            }
            catch (Exception acquisitionFailure)
            {
                UnregisterCameraListener();
                Exception cleanupFailure = nativeCallbackConfigured
                    ? StopAndClearPreview()
                    : null;
                _previewStarted = cleanupFailure != null;
                if (cleanupFailure != null)
                {
                    throw new AggregateException(
                        "Rokid raw preview start and cleanup both failed.",
                        acquisitionFailure,
                        cleanupFailure);
                }

                throw;
            }

            if (RecenterOnStart && !_hasRecentered)
            {
                NativeInterface.NativeAPI.Recenter();
                _hasRecentered = true;
            }

            try
            {
                for (int attempt = 0; attempt < MaxStartAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryReadIntrinsics(out _intrinsics))
                    {
                        TryReadDistortion(out _distortion);
                        _running = true;
                        Log(
                            $"Ready after attempt={attempt + 1}, " +
                            $"intrinsics=({_intrinsics.x:0.###},{_intrinsics.y:0.###}," +
                            $"{_intrinsics.z:0.###},{_intrinsics.w:0.###}), " +
                            $"distortionCoefficients={_distortion.Length}.");
                        return;
                    }

                    await Task.Delay(
                        MillisecondsBetweenAttempts,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Start canceled.");
                await StopAsync();
                throw;
            }
            catch (Exception exception)
            {
                LogError($"Start failed: {exception.Message}");
                await StopAsync();
                throw;
            }

            LogError(
                $"Could not read camera intrinsics after " +
                $"{MaxStartAttempts} attempts.");
            await StopAsync();
            throw new InvalidOperationException("Could not read Rokid camera intrinsics.");
        }

        internal Task StopAsync()
        {
            bool wasStarted = _running || _previewStarted;
            Exception cleanupFailure = null;
            UnregisterCameraListener();

            try
            {
                if (_previewStarted)
                {
                    cleanupFailure = StopAndClearPreview();
                    if (cleanupFailure != null)
                        throw cleanupFailure;
                }
            }
            finally
            {
                lock (_frameLock)
                {
                    _latestBytes = null;
                    _latestWidth = 0;
                    _latestHeight = 0;
                    _latestTimestamp = 0;
                    _hasLoggedFirstFrame = false;
                }

                _timestampGate.Reset();

                _running = false;
                // Retain the flag when native state is uncertain so the public
                // support lifecycle or the next start can retry cleanup.
                _previewStarted = cleanupFailure != null;
                _hasLoggedWaitingForFrame = false;
                _hasLoggedPoseUnavailable = false;
                ResetHeightCalibration();
                if (wasStarted)
                {
                    if (cleanupFailure == null)
                        Log("Raw preview stopped and cached frame cleared.");
                    else
                        LogWarning("Raw preview cleanup failed and will be retried.");
                }
            }

            return Task.CompletedTask;
        }

        private static Exception StopAndClearPreview()
        {
            Exception failure = null;
            try
            {
                NativeInterface.NativeAPI.StopCameraPreview();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                NativeInterface.NativeAPI.ClearCameraDataUpdate();
            }
            catch (Exception exception)
            {
                failure = failure == null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            return failure;
        }

        internal bool TryTakeLatestFrame(out RokidSpatialFrame frame)
        {
            frame = default;
            if (!_running)
                return false;

            byte[] bytes;
            int width;
            int height;
            long timestamp;
            lock (_frameLock)
            {
                if (_latestBytes == null)
                {
                    if (!_hasLoggedWaitingForFrame)
                    {
                        _hasLoggedWaitingForFrame = true;
                        LogWarning("Preview is running; waiting for the first camera frame.");
                    }
                    return false;
                }

                if (!_timestampGate.TryAccept(_latestTimestamp))
                    return false;

                bytes = _latestBytes;
                width = _latestWidth;
                height = _latestHeight;
                timestamp = _latestTimestamp;
            }

            _hasLoggedWaitingForFrame = false;

            if (!TryGetCameraPose(timestamp, out Pose pose))
            {
                if (!_hasLoggedPoseUnavailable)
                {
                    _hasLoggedPoseUnavailable = true;
                    LogWarning($"Camera pose is unavailable for timestamp={timestamp}.");
                }
                return false;
            }

            frame = new RokidSpatialFrame(
                bytes,
                width,
                height,
                timestamp,
                PreviewFormat,
                _intrinsics,
                _distortion,
                pose,
                TrackingQuality);
            return true;
        }

        internal struct FrameTimestampGate
        {
            private bool _hasTimestamp;
            private long _lastTimestamp;

            public bool TryAccept(long timestamp)
            {
                if (_hasTimestamp && timestamp <= _lastTimestamp)
                    return false;

                _hasTimestamp = true;
                _lastTimestamp = timestamp;
                return true;
            }

            public void Reset()
            {
                _hasTimestamp = false;
                _lastTimestamp = 0;
            }
        }

        private void OnCameraDataUpdate(
            int width,
            int height,
            byte[] imageBytes,
            long timestamp)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return;

            lock (_frameLock)
            {
                _latestWidth = width;
                _latestHeight = height;
                _latestTimestamp = timestamp;
                _latestBytes = (byte[])imageBytes.Clone();
            }

            if (!_hasLoggedFirstFrame)
            {
                _hasLoggedFirstFrame = true;
                Log(
                    $"First frame: {width}x{height}, bytes={imageBytes.Length}, " +
                    $"timestamp={timestamp}.");
            }
        }

        private bool TryGetCameraPose(long timestamp, out Pose pose)
        {
            pose = default;
            if (!UseHistoricalCameraPose ||
                NativeInterface.NativeAPI.GetHeadTrackingStatus() !=
                HeadTrackingStatus.Tracking)
            {
                return false;
            }

            pose = NativeInterface.NativeAPI.GetHistoryCameraPhysicsPose(timestamp);
            if (!IsUsablePose(pose) || !ApplyHeightCalibration(ref pose))
                return false;

            pose = TransformPoseToWorld(pose, _xrOrigin.Origin.transform);
            return IsUsablePose(pose);
        }

        internal static bool IsUsablePose(Pose pose)
        {
            bool finitePosition =
                IsFinite(pose.position.x) &&
                IsFinite(pose.position.y) &&
                IsFinite(pose.position.z);
            bool finiteRotation =
                IsFinite(pose.rotation.x) &&
                IsFinite(pose.rotation.y) &&
                IsFinite(pose.rotation.z) &&
                IsFinite(pose.rotation.w);
            float rotationMagnitude =
                pose.rotation.x * pose.rotation.x +
                pose.rotation.y * pose.rotation.y +
                pose.rotation.z * pose.rotation.z +
                pose.rotation.w * pose.rotation.w;
            return finitePosition &&
                   finiteRotation &&
                   rotationMagnitude > 0.0001f &&
                   pose != Pose.identity;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private bool ApplyHeightCalibration(ref Pose pose)
        {
            if (!_hasNativeHeightCalibration && !TryCalibrateNativePoseHeight())
                return true;

            pose = ApplyHeightOffset(pose, _nativeToTrackingHeightOffset);
            return true;
        }

        private bool TryCalibrateNativePoseHeight()
        {
            if (NativeInterface.NativeAPI.GetHeadTrackingStatus() !=
                HeadTrackingStatus.Tracking)
            {
                return false;
            }

            _xrOrigin ??= FindAnyObjectByType<XROrigin>();
            if (_xrOrigin == null)
                return false;

            Pose nativePose = NativeInterface.NativeAPI.GetCameraPhysicsPose(out _);
            if (!IsUsablePose(nativePose))
                return false;

            float trackingHeight = _xrOrigin.CameraInOriginSpaceHeight;
            if (Mathf.Abs(trackingHeight) < MinimumTrackingCameraHeight)
                return false;

            _nativeToTrackingHeightOffset = CalculateHeightOffset(
                trackingHeight,
                nativePose.position.y);
            _hasNativeHeightCalibration = true;

            Log(
                $"Height calibrated: tracking={trackingHeight:0.###}, " +
                $"native={nativePose.position.y:0.###}, " +
                $"offset={_nativeToTrackingHeightOffset:0.###}.");

            return true;
        }

        private static int ReadTrackingQuality()
        {
            return NativeInterface.NativeAPI.GetHeadTrackingStatus() ==
                HeadTrackingStatus.Tracking ? 1 : 0;
        }

        private static bool TryReadIntrinsics(out Vector4 intrinsics)
        {
            intrinsics = Vector4.zero;
            float[] focal = new float[2];
            float[] principal = new float[2];
            int[] dimensions = new int[2];

            NativeInterface.NativeAPI.GetFocalLength(focal);
            NativeInterface.NativeAPI.GetPrincipalPoint(principal);
            NativeInterface.NativeAPI.GetImageDimensions(dimensions);

            if (dimensions[0] <= 0 || dimensions[1] <= 0)
            {
                dimensions[0] = NativeInterface.NativeAPI.GetPreviewWidth();
                dimensions[1] = NativeInterface.NativeAPI.GetPreviewHeight();
            }

            if (!IsValidIntrinsics(focal, principal, dimensions))
                return false;

            intrinsics = new Vector4(
                focal[0],
                focal[1],
                principal[0],
                principal[1]);
            return true;
        }

        internal static bool IsValidIntrinsics(
            float[] focal,
            float[] principal,
            int[] dimensions)
        {
            return focal?.Length >= 2 &&
                   principal?.Length >= 2 &&
                   dimensions?.Length >= 2 &&
                   IsFinite(focal[0]) &&
                   IsFinite(focal[1]) &&
                   focal[0] > 0f &&
                   focal[1] > 0f &&
                   IsFinite(principal[0]) &&
                   IsFinite(principal[1]) &&
                   dimensions[0] > 0 &&
                   dimensions[1] > 0;
        }

        private static bool TryReadDistortion(out double[] distortion)
        {
            float[] raw = new float[8];
            NativeInterface.NativeAPI.GetDistortion(raw);
            distortion = CopyDistortionCoefficients(raw);
            return distortion.Length > 0;
        }

        internal static double[] CopyDistortionCoefficients(float[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<double>();

            bool hasCoefficient = false;
            double[] result = new double[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                float value = source[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return Array.Empty<double>();

                result[i] = value;
                hasCoefficient |= !Mathf.Approximately(value, 0f);
            }

            return hasCoefficient ? result : Array.Empty<double>();
        }

        private void RegisterCameraListener()
        {
            NativeInterface.NativeAPI.OnCameraDataUpdate -= OnCameraDataUpdate;
            NativeInterface.NativeAPI.OnCameraDataUpdate += OnCameraDataUpdate;
        }

        private void UnregisterCameraListener()
        {
            NativeInterface.NativeAPI.OnCameraDataUpdate -= OnCameraDataUpdate;
        }

        private void ResetHeightCalibration()
        {
            _nativeToTrackingHeightOffset = 0f;
            _hasNativeHeightCalibration = false;
        }

        internal static float CalculateHeightOffset(
            float trackingHeight,
            float nativeHeight)
        {
            return trackingHeight - nativeHeight;
        }

        internal static Pose ApplyHeightOffset(Pose pose, float heightOffset)
        {
            pose.position.y += heightOffset;
            return pose;
        }

        internal static Pose TransformPoseToWorld(
            Pose localPose,
            Transform origin)
        {
            if (origin == null)
                throw new ArgumentNullException(nameof(origin));

            return new Pose(
                origin.TransformPoint(localPose.position),
                origin.rotation * localPose.rotation);
        }

        private void Log(string message)
        {
            Debug.Log($"[RokidSpatialData] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[RokidSpatialData] {message}", this);
        }

        private void LogError(string message)
        {
            Debug.LogError($"[RokidSpatialData] {message}", this);
        }
    }
}
