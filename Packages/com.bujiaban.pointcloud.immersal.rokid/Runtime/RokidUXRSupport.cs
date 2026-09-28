using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Immersal;
using Immersal.XR;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace Bujiaban.PointCloud.Immersal.Rokid
{
    [MovedFrom(
        true,
        sourceNamespace: "Bujiaban.PointCloud.Immersal.Rokid",
        sourceAssembly: "Bujiaban.PointCloud.Immersal.Rokid",
        sourceClassName: "RokidImmersalPlatformSupport")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RokidSpatialFrameSource))]
    [AddComponentMenu("Bujiaban/Point Cloud/Immersal/Rokid UXR Support")]
    public sealed class RokidUXRSupport : MonoBehaviour, IPlatformSupport
    {
        private const uint ImageOrientation = 0;

        [SerializeField, FormerlySerializedAs("_cameraDataFormat")]
        private CameraDataFormat m_CameraDataFormat = CameraDataFormat.SingleChannel;

        [SerializeField, Tooltip("Log metadata for each consumed localization frame, not every preview frame. No pixels are logged.")]
        private bool m_LogFrameDiagnostics = true;

        [SerializeField, FormerlySerializedAs("_frameSource")]
        private RokidSpatialFrameSource m_FrameSource;

        private IPlatformConfiguration m_Configuration;
        private bool m_ConfigDone;
        private bool m_HasLoggedFirstUpdate;
        private string m_LastFailureReason;
        private readonly SemaphoreSlim m_LifecycleGate = new SemaphoreSlim(1, 1);
        private int m_LifecycleRevision;
        private CancellationTokenSource m_LifecycleCancellation;
        private Task<IPlatformUpdateResult> m_CurrentCameraDataTask =
            Task.FromResult<IPlatformUpdateResult>(FailedUpdate(0));

        public Task<IPlatformConfigureResult> ConfigurePlatform()
        {
            return ConfigurePlatform(new PlatformConfiguration
            {
                CameraDataFormat = m_CameraDataFormat
            });
        }

        public Task<IPlatformConfigureResult> ConfigurePlatform(
            IPlatformConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            return ConfigureWithConfiguration(configuration);
        }

        private async Task<IPlatformConfigureResult> ConfigureWithConfiguration(
            IPlatformConfiguration configuration)
        {
            // A newer configure request owns the platform. Cancel the previous
            // one before waiting so it can release the lifecycle gate.
            int lifecycleRevision = BeginLifecycleRequest();
            await m_LifecycleGate.WaitAsync();

            try
            {
                if (lifecycleRevision != Volatile.Read(ref m_LifecycleRevision))
                {
                    throw new OperationCanceledException(
                        "Rokid UXR configuration was replaced by a newer lifecycle request.");
                }

                await StopCamera();
                ResetLifecycleState(m_LifecycleCancellation);

                CancellationTokenSource lifecycleCancellation =
                    new CancellationTokenSource();
                m_LifecycleCancellation = lifecycleCancellation;
                m_Configuration = new PlatformConfiguration
                {
                    CameraDataFormat = configuration.CameraDataFormat
                };

                Log(
                    $"Configure requested: cameraDataFormat=" +
                    $"{m_Configuration.CameraDataFormat}, orientation={ImageOrientation}.");
                ResolveFrameSource();
                m_HasLoggedFirstUpdate = false;
                m_LastFailureReason = null;
                m_CurrentCameraDataTask = Task.FromResult<IPlatformUpdateResult>(
                    FailedUpdate(0));

                try
                {
                    await ConfigureCamera(lifecycleCancellation.Token);
                    lifecycleCancellation.Token.ThrowIfCancellationRequested();
                    m_ConfigDone = true;
                    Log("Configure completed; camera preview and intrinsics are ready.");

                    return new SimplePlatformConfigureResult { Success = true };
                }
                catch
                {
                    try
                    {
                        await StopCamera();
                    }
                    finally
                    {
                        ResetLifecycleState(lifecycleCancellation);
                    }

                    throw;
                }
            }
            finally
            {
                m_LifecycleGate.Release();
            }
        }

        public Task<IPlatformUpdateResult> UpdatePlatform()
        {
            return UpdateWithConfiguration(m_Configuration);
        }

        public Task<IPlatformUpdateResult> UpdatePlatform(
            IPlatformConfiguration oneShotConfiguration)
        {
            return UpdateWithConfiguration(oneShotConfiguration ?? m_Configuration);
        }

        private Task<IPlatformUpdateResult> UpdateWithConfiguration(
            IPlatformConfiguration configuration)
        {
            if (!m_ConfigDone)
            {
                throw new ComponentTaskCriticalException(
                    "Trying to update Rokid UXR support before configuration.");
            }

            if (configuration == null)
            {
                throw new ComponentTaskCriticalException(
                    "Rokid UXR support has no platform configuration.");
            }

            m_CurrentCameraDataTask = Task.FromResult<IPlatformUpdateResult>(
                GetCameraData(configuration));
            return m_CurrentCameraDataTask;
        }

        public async Task StopAndCleanUp()
        {
            BeginLifecycleRequest();
            await m_LifecycleGate.WaitAsync();
            CancellationTokenSource lifecycleCancellation =
                m_LifecycleCancellation;
            try
            {
                if (m_CurrentCameraDataTask != null)
                {
                    try
                    {
                        await m_CurrentCameraDataTask;
                    }
                    catch (OperationCanceledException)
                        when (lifecycleCancellation?.IsCancellationRequested == true)
                    {
                    }
                }
            }
            finally
            {
                try
                {
                    try
                    {
                        await StopCamera();
                    }
                    finally
                    {
                        ResetLifecycleState(lifecycleCancellation);
                        Log("Cleanup completed.");
                    }
                }
                finally
                {
                    m_LifecycleGate.Release();
                }
            }
        }

        private SimplePlatformUpdateResult GetCameraData(
            IPlatformConfiguration configuration)
        {
            if (!m_FrameSource.TryTakeLatestFrame(out RokidSpatialFrame frame))
                return FailedUpdate("spatial frame unavailable", m_FrameSource.TrackingQuality);

            CameraData cameraData = CreateCameraData(frame, configuration.CameraDataFormat);
            if (cameraData == null)
            {
                return FailedUpdate(
                    $"cannot convert {frame.Format} to {configuration.CameraDataFormat}",
                    frame.TrackingQuality);
            }

            if (!m_HasLoggedFirstUpdate)
            {
                m_HasLoggedFirstUpdate = true;
                Log(
                    $"First CameraData: {frame.Width}x{frame.Height}, " +
                    $"channels={cameraData.Channels}, bytes={cameraData.GetBytes().Length}, " +
                    $"timestamp={frame.Timestamp}, quality={frame.TrackingQuality}.");
            }

            m_LastFailureReason = null;

            if (m_LogFrameDiagnostics)
            {
                Log($"Frame cameraDataId={RuntimeHelpers.GetHashCode(cameraData)}, " +
                    $"sourceTimestamp={frame.Timestamp}, timestampDomain=rokid_native_camera, " +
                    $"historicalPose=true, width={frame.Width}, height={frame.Height}, " +
                    $"trackingQuality={frame.TrackingQuality}.");
            }

            return new SimplePlatformUpdateResult
            {
                Success = true,
                Status = new SimplePlatformStatus
                {
                    TrackingQuality = frame.TrackingQuality
                },
                CameraData = cameraData
            };
        }

        internal static CameraData CreateCameraData(
            RokidSpatialFrame frame,
            CameraDataFormat format)
        {
            byte[] imageBytes = ConvertImage(frame, format, out int channels);
            if (imageBytes == null)
                return null;

            return new CameraData(new SimpleImageData(imageBytes))
            {
                Width = frame.Width,
                Height = frame.Height,
                Channels = channels,
                Format = format,
                Intrinsics = frame.Intrinsics,
                CameraPositionOnCapture = frame.CameraPose.position,
                CameraRotationOnCapture = frame.CameraPose.rotation,
                Distortion = frame.Distortion,
                ScreenOrientation = Quaternion.identity,
                ImageOrientation = ImageOrientation
            };
        }

        internal static byte[] ConvertImage(
            RokidSpatialFrame frame,
            CameraDataFormat format,
            out int channels)
        {
            channels = format == CameraDataFormat.SingleChannel ? 1 : 3;
            int pixelCount = frame.Width * frame.Height;
            if (frame.Bytes == null || pixelCount <= 0)
                return null;

            if (format == CameraDataFormat.SingleChannel)
            {
                if (frame.Bytes.Length < pixelCount)
                    return null;

                byte[] grayscale = new byte[pixelCount];
                Buffer.BlockCopy(frame.Bytes, 0, grayscale, 0, pixelCount);
                return grayscale;
            }

            switch (frame.Format)
            {
                case RokidSpatialFrameFormat.Bgra32:
                    return ConvertBgraToRgb(frame.Bytes, pixelCount);
                case RokidSpatialFrameFormat.Yuv:
                    return ConvertNv21ToRgb(
                        frame.Bytes,
                        frame.Width,
                        frame.Height);
                default:
                    return null;
            }
        }

        private static byte[] ConvertBgraToRgb(byte[] bytes, int pixelCount)
        {
            int bgraLength = pixelCount * 4;
            if (bytes.Length < bgraLength)
                return null;

            byte[] rgb = new byte[pixelCount * 3];
            for (int source = 0, target = 0;
                source < bgraLength;
                source += 4, target += 3)
            {
                rgb[target] = bytes[source + 2];
                rgb[target + 1] = bytes[source + 1];
                rgb[target + 2] = bytes[source];
            }

            return rgb;
        }

        private static byte[] ConvertNv21ToRgb(
            byte[] bytes,
            int width,
            int height)
        {
            if ((width & 1) != 0 || (height & 1) != 0)
                return null;

            int pixelCount = width * height;
            int nv21Length = pixelCount + pixelCount / 2;
            if (bytes.Length < nv21Length)
                return null;

            byte[] rgb = new byte[pixelCount * 3];
            for (int y = 0; y < height; y++)
            {
                int yRow = y * width;
                int uvRow = pixelCount + (y >> 1) * width;
                for (int x = 0; x < width; x++)
                {
                    int luminance = bytes[yRow + x] - 16;
                    int chroma = uvRow + (x & ~1);
                    int v = bytes[chroma] - 128;
                    int u = bytes[chroma + 1] - 128;
                    int target = (yRow + x) * 3;

                    if (luminance < 0)
                        luminance = 0;

                    rgb[target] = ClampToByte(
                        (298 * luminance + 409 * v + 128) >> 8);
                    rgb[target + 1] = ClampToByte(
                        (298 * luminance - 100 * u - 208 * v + 128) >> 8);
                    rgb[target + 2] = ClampToByte(
                        (298 * luminance + 516 * u + 128) >> 8);
                }
            }

            return rgb;
        }

        private static byte ClampToByte(int value)
        {
            if (value < byte.MinValue)
                return byte.MinValue;
            if (value > byte.MaxValue)
                return byte.MaxValue;
            return (byte)value;
        }

        private static SimplePlatformUpdateResult FailedUpdate(int quality)
        {
            return new SimplePlatformUpdateResult
            {
                Success = false,
                Status = new SimplePlatformStatus { TrackingQuality = quality },
                CameraData = null
            };
        }

        private SimplePlatformUpdateResult FailedUpdate(string reason, int quality)
        {
            if (m_LastFailureReason != reason)
            {
                m_LastFailureReason = reason;
                LogWarning($"Update skipped: {reason}, trackingQuality={quality}.");
            }

            return FailedUpdate(quality);
        }

        private void ResolveFrameSource()
        {
            m_FrameSource ??= GetComponent<RokidSpatialFrameSource>();
            if (m_FrameSource == null)
                m_FrameSource = gameObject.AddComponent<RokidSpatialFrameSource>();
        }

        private Task ConfigureCamera(CancellationToken cancellationToken)
        {
            return m_FrameSource.StartAsync(cancellationToken);
        }

        private Task StopCamera()
        {
            return m_FrameSource == null
                ? Task.CompletedTask
                : m_FrameSource.StopAsync();
        }

        private void CancelActiveLifecycle()
        {
            try
            {
                m_LifecycleCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private int BeginLifecycleRequest()
        {
            int revision = Interlocked.Increment(ref m_LifecycleRevision);
            CancelActiveLifecycle();
            return revision;
        }

        private void ResetLifecycleState(
            CancellationTokenSource lifecycleCancellation)
        {
            m_ConfigDone = false;
            m_CurrentCameraDataTask = Task.FromResult<IPlatformUpdateResult>(
                FailedUpdate(0));
            if (ReferenceEquals(
                    m_LifecycleCancellation,
                    lifecycleCancellation))
            {
                m_LifecycleCancellation = null;
            }
            lifecycleCancellation?.Dispose();
            m_HasLoggedFirstUpdate = false;
            m_LastFailureReason = null;
        }

        private void OnDisable()
        {
            StopForUnityLifecycle();
        }

        private void OnDestroy()
        {
            StopForUnityLifecycle();
        }

        private void StopForUnityLifecycle()
        {
            CancellationTokenSource lifecycleCancellation =
                m_LifecycleCancellation;
            try
            {
                BeginLifecycleRequest();

                // Rokid native preview cleanup is synchronous inside StopAsync.
                // Finish it before scene unload releases this component.
                StopCamera().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[RokidPointCloudAdapter] Unity lifecycle cleanup " +
                    $"failed.\n{exception}",
                    this);
            }
            finally
            {
                ResetLifecycleState(lifecycleCancellation);
            }
        }

        private void Log(string message)
        {
            Debug.Log($"[RokidPointCloudAdapter] {message}", this);
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[RokidPointCloudAdapter] {message}", this);
        }

    }
}
