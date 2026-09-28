using Immersal;
using Immersal.XR;
using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Rokid.Tests
{
    public sealed class RokidSpatialFrameSourceTests
    {
        [Test]
        public void TimestampGateAcceptsEachCapturedFrameOnce()
        {
            RokidSpatialFrameSource.FrameTimestampGate gate = default;

            Assert.That(gate.TryAccept(100), Is.True);
            Assert.That(gate.TryAccept(100), Is.False);
            Assert.That(gate.TryAccept(99), Is.False);
            Assert.That(gate.TryAccept(101), Is.True);

            gate.Reset();
            Assert.That(gate.TryAccept(101), Is.True);
        }

        [Test]
        public void CalculateHeightOffsetReturnsTrackingMinusNativeHeight()
        {
            Assert.That(
                RokidSpatialFrameSource.CalculateHeightOffset(1.65f, 0.15f),
                Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void ApplyHeightOffsetOnlyChangesVerticalPosition()
        {
            Pose source = new Pose(
                new Vector3(1f, 2f, 3f),
                Quaternion.Euler(10f, 20f, 30f));

            Pose result = RokidSpatialFrameSource.ApplyHeightOffset(source, 0.5f);

            Assert.That(result.position, Is.EqualTo(new Vector3(1f, 2.5f, 3f)));
            Assert.That(result.rotation, Is.EqualTo(source.rotation));
        }

        [Test]
        public void OriginTransformConvertsTrackingLocalPoseToUnityWorld()
        {
            GameObject originObject = new GameObject("OriginTransformTest");
            try
            {
                originObject.transform.SetPositionAndRotation(
                    new Vector3(10f, 2f, -4f),
                    Quaternion.Euler(0f, 90f, 0f));
                originObject.transform.localScale = new Vector3(2f, 2f, 2f);
                Pose localPose = new Pose(
                    new Vector3(1f, 1.5f, 3f),
                    Quaternion.Euler(5f, 20f, 0f));

                Pose result = RokidSpatialFrameSource.TransformPoseToWorld(
                    localPose,
                    originObject.transform);

                Assert.That(
                    Vector3.Distance(
                        result.position,
                        originObject.transform.TransformPoint(localPose.position)),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Quaternion.Angle(
                        result.rotation,
                        originObject.transform.rotation * localPose.rotation),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(originObject);
            }
        }

        [Test]
        public void PoseValidationRejectsMissingHistoricalPoseSentinel()
        {
            Assert.That(
                RokidSpatialFrameSource.IsUsablePose(Pose.identity),
                Is.False);
        }

        [Test]
        public void PoseValidationRejectsInvalidNativeValues()
        {
            Assert.That(
                RokidSpatialFrameSource.IsUsablePose(
                    new Pose(Vector3.zero, new Quaternion(0f, 0f, 0f, 0f))),
                Is.False);
            Assert.That(
                RokidSpatialFrameSource.IsUsablePose(
                    new Pose(
                        new Vector3(float.NaN, 0f, 0f),
                        Quaternion.identity)),
                Is.False);
            Assert.That(
                RokidSpatialFrameSource.IsUsablePose(
                    new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.identity)),
                Is.True);
        }

        [Test]
        public void CopyDistortionCoefficientsPreservesZeroPositions()
        {
            double[] result = RokidSpatialFrameSource.CopyDistortionCoefficients(
                new[] { 0f, 0.1f, 0f, -0.2f });

            Assert.That(result, Has.Length.EqualTo(4));
            Assert.That(result[0], Is.EqualTo(0d));
            Assert.That(result[1], Is.EqualTo(0.1d).Within(0.000001d));
            Assert.That(result[2], Is.EqualTo(0d));
            Assert.That(result[3], Is.EqualTo(-0.2d).Within(0.000001d));
        }

        [Test]
        public void CopyDistortionCoefficientsRejectsInvalidInput()
        {
            Assert.That(
                RokidSpatialFrameSource.CopyDistortionCoefficients(
                    new[] { 0f, float.NaN, 0.1f }),
                Is.Empty);
            Assert.That(
                RokidSpatialFrameSource.CopyDistortionCoefficients(
                    new[] { 0f, 0f, 0f }),
                Is.Empty);
        }

        [Test]
        public void IntrinsicsValidationRequiresTwoPositiveFiniteFocalAxes()
        {
            int[] dimensions = { 640, 480 };
            float[] principal = { 320f, 240f };

            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    new[] { 500f, 501f },
                    principal,
                    dimensions),
                Is.True);
            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    new[] { 0f, 501f },
                    principal,
                    dimensions),
                Is.False);
            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    new[] { 500f, float.NaN },
                    principal,
                    dimensions),
                Is.False);
            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    new[] { -500f, 501f },
                    principal,
                    dimensions),
                Is.False);
        }

        [Test]
        public void IntrinsicsValidationAllowsZeroPrincipalButRequiresDimensions()
        {
            float[] focal = { 500f, 501f };

            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    focal,
                    new[] { 0f, 0f },
                    new[] { 640, 480 }),
                Is.True);
            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    focal,
                    new[] { float.PositiveInfinity, 240f },
                    new[] { 640, 480 }),
                Is.False);
            Assert.That(
                RokidSpatialFrameSource.IsValidIntrinsics(
                    focal,
                    new[] { 320f, 240f },
                    new[] { 0, 480 }),
                Is.False);
        }

        [TestCase(CameraDataFormat.SingleChannel)]
        [TestCase(CameraDataFormat.RGB)]
        public void CameraDataHasAUnitScreenOrientationForImmersalPoseConversion(
            CameraDataFormat format)
        {
            Pose capturedPose = new Pose(
                new Vector3(1f, 1.6f, -2f),
                Quaternion.Euler(10f, 30f, 5f));
            var frame = new RokidSpatialFrame(
                new byte[] { 81, 81, 81, 81, 240, 90 },
                width: 2,
                height: 2,
                timestamp: 123,
                RokidSpatialFrameFormat.Yuv,
                new Vector4(500f, 501f, 1f, 1f),
                new double[0],
                capturedPose,
                trackingQuality: 1);

            CameraData data = RokidUXRSupport.CreateCameraData(frame, format);
            try
            {
                Assert.That(data, Is.Not.Null);
                Assert.That(data.ImageOrientation, Is.Zero);
                Assert.That(Quaternion.Dot(data.ScreenOrientation, data.ScreenOrientation),
                    Is.EqualTo(1f).Within(0.00001f),
                    "Immersal 2.4 consumes ScreenOrientation in pose conversion; a default zero quaternion is invalid.");
                Assert.That(data.ScreenOrientation, Is.EqualTo(Quaternion.identity));
                Assert.That(data.CameraPositionOnCapture, Is.EqualTo(capturedPose.position));
                Assert.That(data.CameraRotationOnCapture, Is.EqualTo(capturedPose.rotation));
            }
            finally
            {
                data?.CheckReferences();
            }
        }

        [Test]
        public void YuvLuminanceCanBeAdaptedToSingleChannel()
        {
            RokidSpatialFrame frame = Frame(
                new byte[] { 10, 20, 30, 40, 50, 60 },
                RokidSpatialFrameFormat.Yuv,
                width: 2,
                height: 2);

            byte[] result = RokidUXRSupport.ConvertImage(
                frame,
                CameraDataFormat.SingleChannel,
                out int channels);

            Assert.That(channels, Is.EqualTo(1));
            Assert.That(result, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
        }

        [Test]
        public void BgraCanBeAdaptedToRgb()
        {
            RokidSpatialFrame frame = Frame(
                new byte[] { 1, 2, 3, 255, 10, 20, 30, 255 },
                RokidSpatialFrameFormat.Bgra32,
                width: 2,
                height: 1);

            byte[] result = RokidUXRSupport.ConvertImage(
                frame,
                CameraDataFormat.RGB,
                out int channels);

            Assert.That(channels, Is.EqualTo(3));
            Assert.That(result, Is.EqualTo(new byte[] { 3, 2, 1, 30, 20, 10 }));
        }

        [Test]
        public void Nv21CanBeAdaptedToRgb()
        {
            RokidSpatialFrame frame = Frame(
                new byte[] { 81, 81, 81, 81, 240, 90 },
                RokidSpatialFrameFormat.Yuv,
                width: 2,
                height: 2);

            byte[] result = RokidUXRSupport.ConvertImage(
                frame,
                CameraDataFormat.RGB,
                out int channels);

            Assert.That(channels, Is.EqualTo(3));
            Assert.That(
                result,
                Is.EqualTo(new byte[]
                {
                    255, 0, 0,
                    255, 0, 0,
                    255, 0, 0,
                    255, 0, 0
                }));
        }

        private static RokidSpatialFrame Frame(
            byte[] bytes,
            RokidSpatialFrameFormat format,
            int width,
            int height)
        {
            return new RokidSpatialFrame(
                bytes,
                width,
                height,
                timestamp: 1,
                format,
                Vector4.one,
                new double[0],
                Pose.identity,
                trackingQuality: 1);
        }
    }
}
