using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Bujiaban.PointCloud
{
    /// <summary>
    /// Extension point implemented by map-format packages. Business code must
    /// call <see cref="PointCloudLocalizer"/>, not a backend directly.
    /// </summary>
    public abstract class PointCloudBackend : MonoBehaviour
    {
        internal bool Supports(string mapType)
        {
            return SupportsMapType(mapType);
        }

        internal Task<Pose> ExecuteAsync(
            string mapType,
            Stream mapZip,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress)
        {
            Task<Pose> operation = LocalizeCoreAsync(
                mapType,
                mapZip,
                cancellationToken,
                progress);
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Backend '{GetType().FullName}' returned a null task.");
            }

            return operation;
        }

        internal Task ExecuteTrackingAsync(
            string mapType,
            Stream mapZip,
            Action<Pose> onPose,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress)
        {
            Task operation = TrackCoreAsync(
                mapType,
                mapZip,
                onPose,
                cancellationToken,
                progress);
            if (operation == null)
            {
                throw new InvalidOperationException(
                    $"Backend '{GetType().FullName}' returned a null tracking task.");
            }

            return operation;
        }

        protected abstract bool SupportsMapType(string mapType);

        protected abstract Task<Pose> LocalizeCoreAsync(
            string mapType,
            Stream mapZip,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress);

        /// <summary>
        /// Runs until cancelled and reports the initial confirmed pose followed
        /// by any accepted maintenance corrections.
        /// </summary>
        protected abstract Task TrackCoreAsync(
            string mapType,
            Stream mapZip,
            Action<Pose> onPose,
            CancellationToken cancellationToken,
            IProgress<PointCloudLocalizationProgress> progress);
    }
}
