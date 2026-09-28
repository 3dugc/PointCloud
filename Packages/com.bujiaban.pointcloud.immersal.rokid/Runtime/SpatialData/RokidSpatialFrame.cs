using System;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Rokid
{
    internal enum RokidSpatialFrameFormat
    {
        Bgra32 = 1,
        Yuv = 2
    }

    internal readonly struct RokidSpatialFrame
    {
        public RokidSpatialFrame(
            byte[] bytes,
            int width,
            int height,
            long timestamp,
            RokidSpatialFrameFormat format,
            Vector4 intrinsics,
            double[] distortion,
            Pose cameraPose,
            int trackingQuality)
        {
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            Width = width;
            Height = height;
            Timestamp = timestamp;
            Format = format;
            Intrinsics = intrinsics;
            Distortion = distortion ?? Array.Empty<double>();
            CameraPose = cameraPose;
            TrackingQuality = trackingQuality;
        }

        public byte[] Bytes { get; }
        public int Width { get; }
        public int Height { get; }
        public long Timestamp { get; }
        public RokidSpatialFrameFormat Format { get; }
        public Vector4 Intrinsics { get; }
        public double[] Distortion { get; }
        public Pose CameraPose { get; }
        public int TrackingQuality { get; }
    }
}
