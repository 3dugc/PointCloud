using System;

namespace Bujiaban.PointCloud
{
    /// <summary>
    /// Reports every non-cancellation failure produced by a localization run.
    /// </summary>
    public sealed class PointCloudLocalizationException : Exception
    {
        internal PointCloudLocalizationException()
            : base("Point-cloud localization failed.")
        {
        }
    }
}
