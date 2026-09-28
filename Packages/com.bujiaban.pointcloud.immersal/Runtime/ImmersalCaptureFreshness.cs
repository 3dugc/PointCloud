using System;

namespace Bujiaban.PointCloud.Immersal
{
    // Prevent a repeated AR Foundation CPU image from becoming a new maintenance vote.
    internal sealed class ImmersalCaptureFreshness
    {
        private double _lastTimestamp = double.NegativeInfinity;

        internal bool TryAccept(double timestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp) ||
                timestamp < 0d || timestamp <= _lastTimestamp)
                return false;
            _lastTimestamp = timestamp;
            return true;
        }

        internal void Reset() => _lastTimestamp = double.NegativeInfinity;
    }
}
