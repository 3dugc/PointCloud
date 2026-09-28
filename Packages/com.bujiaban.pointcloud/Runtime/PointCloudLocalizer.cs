using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Bujiaban.PointCloud
{
    /// <summary>
    /// Vendor-neutral entry point for one-shot or continuous point-cloud localization.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Bujiaban/Point Cloud/Localizer")]
    public sealed class PointCloudLocalizer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Exactly one backend must support each configured map type.")]
        private PointCloudBackend[] _backends = Array.Empty<PointCloudBackend>();

        private readonly object _operationLock = new object();
        private CancellationTokenSource _activeCancellation;
        private Task _activeOperation = Task.CompletedTask;
        private int _activeOperationId;

        /// <summary>
        /// Localizes one downloaded map archive and returns the map origin pose
        /// in the current Unity world. The caller retains ownership of
        /// <paramref name="mapZip"/> and must keep it open until the task ends.
        /// A new call cancels and fully waits for the previous call first.
        /// </summary>
        public Task<Pose> LocalizeAsync(
            string mapType,
            Stream mapZip,
            CancellationToken cancellationToken = default,
            IProgress<PointCloudLocalizationProgress> progress = null)
        {
            return RunAsync(mapType, mapZip, cancellationToken, progress,
                (backend, token, report, _) =>
                    backend.ExecuteAsync(mapType, mapZip, token, report));
        }

        /// <summary>
        /// Localizes one downloaded map archive, reports the initial confirmed
        /// pose, then keeps reporting accepted maintenance corrections until
        /// cancelled. The caller must keep <paramref name="mapZip"/> open until
        /// the returned task ends. A new request replaces the previous request.
        /// </summary>
        public Task TrackAsync(
            string mapType,
            Stream mapZip,
            Action<Pose> onPose,
            CancellationToken cancellationToken = default,
            IProgress<PointCloudLocalizationProgress> progress = null)
        {
            if (onPose == null)
                return Task.FromException(ToLocalizationException(mapType,
                    new ArgumentNullException(nameof(onPose))));

            return RunAsync(mapType, mapZip, cancellationToken, progress,
                async (backend, token, report, isCurrent) =>
                {
                    await backend.ExecuteTrackingAsync(mapType, mapZip, pose =>
                    {
                        if (!isCurrent()) return;
                        try { onPose(pose); }
                        catch (Exception exception)
                        {
                            Debug.LogError(
                                $"[PointCloudLocalizer] Pose callback failed and was ignored.\n{exception}",
                                this);
                        }
                    }, token, report);
                    return true;
                });
        }

        private Task<T> RunAsync<T>(
            string mapType,
            Stream mapZip,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress,
            Func<PointCloudBackend, CancellationToken,
                IProgress<PointCloudLocalizationProgress>, Func<bool>, Task<T>> execute)
        {
            PointCloudBackend backend;
            try
            {
                if (string.IsNullOrWhiteSpace(mapType))
                    throw new ArgumentException("Map type is required.", nameof(mapType));
                if (mapZip == null)
                    throw new ArgumentNullException(nameof(mapZip));
                if (!mapZip.CanRead)
                    throw new ArgumentException("Map ZIP stream must be readable.", nameof(mapZip));
                backend = ResolveBackend(mapType);
            }
            catch (Exception exception)
            {
                return Task.FromException<T>(ToLocalizationException(mapType, exception));
            }

            CancellationTokenSource operationCancellation;
            CancellationTokenSource previousCancellation;
            Task previousOperation;
            int operationId;
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_operationLock)
            {
                previousOperation = _activeOperation;
                previousCancellation = _activeCancellation;
                operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                operationId = ++_activeOperationId;
                _activeCancellation = operationCancellation;
                _activeOperation = completion.Task;
            }

            // Cancel invokes third-party callbacks synchronously, including ones
            // which start another request. Keep it outside the state lock.
            TryCancel(previousCancellation);
            _ = ExecuteAsync();
            return completion.Task;

            async Task ExecuteAsync()
            {
                CancellationToken token = operationCancellation.Token;
                bool IsCurrent() => !token.IsCancellationRequested &&
                    operationId == Volatile.Read(ref _activeOperationId) &&
                    !completion.Task.IsCompleted;
                try
                {
                    await ObservePreviousOperationAsync(previousOperation);
                    token.ThrowIfCancellationRequested();
                    if (mapZip.CanSeek) mapZip.Position = 0;
                    IProgress<PointCloudLocalizationProgress> report = progress == null
                        ? null
                        : new SynchronousProgress(value =>
                        {
                            if (IsCurrent()) progress.Report(value);
                        });
                    T result = await execute(backend, token, report, IsCurrent);
                    token.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
                catch (Exception) when (token.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(ToLocalizationException(mapType, exception));
                }
                finally
                {
                    lock (_operationLock)
                    {
                        if (operationId == _activeOperationId &&
                            ReferenceEquals(_activeCancellation, operationCancellation))
                            _activeCancellation = null;
                    }
                    operationCancellation.Dispose();
                }
            }
        }

        // Do not add a Progress<T> queue here: validate the operation at the
        // moment the backend reports, before forwarding to the caller's sink.
        private sealed class SynchronousProgress : IProgress<PointCloudLocalizationProgress>
        {
            private readonly Action<PointCloudLocalizationProgress> _report;

            internal SynchronousProgress(Action<PointCloudLocalizationProgress> report)
            {
                _report = report;
            }

            public void Report(PointCloudLocalizationProgress value) => _report(value);
        }

        private PointCloudBackend ResolveBackend(string mapType)
        {
            PointCloudBackend match = null;
            int matchCount = 0;

            if (_backends != null)
            {
                foreach (PointCloudBackend backend in _backends)
                {
                    if (backend == null ||
                        !backend.isActiveAndEnabled ||
                        !backend.Supports(mapType))
                    {
                        continue;
                    }

                    match = backend;
                    matchCount++;
                }
            }

            if (matchCount == 0)
            {
                throw new NotSupportedException(
                    $"No point-cloud backend supports map type '{mapType}'.");
            }

            if (matchCount > 1)
            {
                throw new InvalidOperationException(
                    $"More than one point-cloud backend supports map type '{mapType}'.");
            }

            return match;
        }

        private static async Task ObservePreviousOperationAsync(Task operation)
        {
            if (operation == null)
                return;

            try
            {
                await operation;
            }
            catch
            {
                // The previous caller receives its own result. This await only
                // enforces complete cleanup before the replacement can start.
            }
        }

        private PointCloudLocalizationException ToLocalizationException(
            string mapType,
            Exception exception)
        {
            Debug.LogError(
                $"[PointCloudLocalizer] Localization failed for map type " +
                $"'{mapType ?? "null"}'.\n{exception}",
                this);
            return new PointCloudLocalizationException();
        }

        private void OnDisable()
        {
            CancelActiveOperation();
        }

        private void OnDestroy()
        {
            CancelActiveOperation();
        }

        private void CancelActiveOperation()
        {
            CancellationTokenSource cancellation;
            lock (_operationLock)
                cancellation = _activeCancellation;

            TryCancel(cancellation);
        }

        private void TryCancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
                return;

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion may dispose the previous source immediately
                // after we release the state lock. It no longer needs canceling.
            }
            catch (Exception exception)
            {
                // Backend token callbacks are third-party extension code.
                // Their failures must not strand the replacement operation.
                Debug.LogError(
                    $"[PointCloudLocalizer] Cancellation callback failed.\n" +
                    $"{exception}",
                    this);
            }
        }
    }
}
