using System;
using System.Reflection;
using System.Threading.Tasks;
using global::Immersal;
using global::Immersal.XR;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    /// <summary>
    /// Pins the SDK-to-backend coordinate contract during SDK upgrades. The real
    /// SceneUpdater writes to the production RawPoseSink; only the native solver
    /// result is supplied by the test. These tests do not establish device accuracy.
    /// </summary>
    public sealed class ImmersalSceneUpdaterCompatibilityTests
    {
        private GameObject _host;
        private SceneUpdater _updater;
        private ISceneUpdateable _sink;
        private MapEntry _entry;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("SDK scene conversion contract");
            _updater = _host.AddComponent<SceneUpdater>();
            var mapSpace = new GameObject("Production raw pose sink");
            mapSpace.transform.SetParent(_host.transform, false);
            Type sinkType = typeof(ImmersalPointCloudBackend).GetNestedType(
                "RawPoseSink", BindingFlags.NonPublic);
            Assert.That(sinkType, Is.Not.Null);
            _sink = (ISceneUpdateable)Activator.CreateInstance(sinkType,
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { mapSpace.transform, (Action)(() => { }) }, null);
            _entry = new MapEntry
            {
                SceneParent = _sink,
                Relation = new MapToSpaceRelation
                {
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);
        }

        // Fixed geometric examples, rather than recomputing the SDK matrix formula:
        // the camera observes map point (2, 1, 0) at tracker position (10, 4, 3).
        [TestCase(0f, 12f, 3f)]
        [TestCase(90f, 11f, 6f)]
        [TestCase(-90f, 9f, 2f)]
        [TestCase(180f, 8f, 5f)]
        public async Task ScreenOrientationProducesTheExpectedWorldMapOrigin(
            float screenDegrees, float expectedX, float expectedY)
        {
            CameraData camera = CreateCameraData(
                Quaternion.Euler(0, 0, screenDegrees),
                new Pose(new Vector3(10, 4, 3), Quaternion.identity));
            using IImageData lease = camera.GetImageData();

            await _updater.UpdateScene(_entry, camera,
                new FixedLocalizationResult(new Vector3(2, 1, 0), Quaternion.identity));

            AssertWorldPose(new Vector3(expectedX, expectedY, 3),
                Quaternion.Euler(0, 0, screenDegrees));
        }

        [Test]
        public async Task SolverAndScreenRotationsPreserveTheirNonCommutingOrder()
        {
            CameraData camera = CreateCameraData(Quaternion.Euler(0, 0, 90),
                new Pose(new Vector3(4, 2, 1), Quaternion.identity));
            using IImageData lease = camera.GetImageData();

            await _updater.UpdateScene(_entry, camera,
                new FixedLocalizationResult(Vector3.zero, Quaternion.Euler(0, 90, 0)));

            Transform mapOrigin = _sink.GetTransform();
            AssertVector(mapOrigin.position, new Vector3(4, 2, 1), "world position");
            // A 90-degree solver yaw followed by portrait screen correction maps
            // the map's forward axis to tracker up and its up axis to tracker left.
            AssertVector(mapOrigin.forward, Vector3.up, "map forward axis");
            AssertVector(mapOrigin.up, Vector3.left, "map up axis");
        }

        [Test]
        public async Task MapRelationProducesWorldPoseWithoutMovingTheHostOrigin()
        {
            // A non-default parent exposes accidental local-space pose writes.
            _host.transform.SetPositionAndRotation(new Vector3(100, 20, -30),
                Quaternion.Euler(0, 37, 0));
            _entry.Relation = new MapToSpaceRelation
            {
                Position = new Vector3(5, 0, 0),
                Rotation = Quaternion.Euler(0, 0, 90),
                Scale = new Vector3(2, 3, 1)
            };
            CameraData camera = CreateCameraData(Quaternion.Euler(0, 0, 90),
                new Pose(new Vector3(10, 4, 3), Quaternion.Euler(0, 0, 90)));
            using IImageData lease = camera.GetImageData();

            await _updater.UpdateScene(_entry, camera,
                new FixedLocalizationResult(new Vector3(2, 1, 0), Quaternion.identity));

            // The map-space observation is (2, -4, 0), with no remaining rotation.
            // Under the captured camera's 90-degree rotation its origin is (6, 2, 3).
            AssertWorldPose(new Vector3(6, 2, 3), Quaternion.Euler(0, 0, 90));
            AssertVector(_host.transform.position, new Vector3(100, 20, -30), "host origin");
            Assert.That(Quaternion.Angle(_host.transform.rotation, Quaternion.Euler(0, 37, 0)),
                Is.LessThan(.01f), "Localization must not rotate the host origin.");
        }

        private static CameraData CreateCameraData(Quaternion screenOrientation, Pose capturePose)
        {
            return new CameraData(new SimpleImageData(new byte[4]))
            {
                Width = 2,
                Height = 2,
                Channels = 1,
                Format = CameraDataFormat.SingleChannel,
                Intrinsics = new Vector4(1, 1, 1, 1),
                CameraPositionOnCapture = capturePose.position,
                CameraRotationOnCapture = capturePose.rotation,
                ScreenOrientation = screenOrientation,
                Distortion = Array.Empty<double>()
            };
        }

        private void AssertWorldPose(Vector3 position, Quaternion rotation)
        {
            Transform mapOrigin = _sink.GetTransform();
            AssertVector(mapOrigin.position, position, "world map origin");
            Assert.That(Quaternion.Angle(mapOrigin.rotation, rotation), Is.LessThan(.01f),
                "The production sink must retain the SDK's world rotation.");
        }

        private static void AssertVector(Vector3 actual, Vector3 expected, string context)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f), context);
        }

        private sealed class FixedLocalizationResult : ILocalizationResult
        {
            internal FixedLocalizationResult(Vector3 position, Quaternion rotation)
            {
                LocalizeInfo = new LocalizeInfo
                {
                    mapId = 1, confidence = 100, rmse = .5d,
                    position = position, rotation = rotation
                };
            }

            public bool Success => true;
            public int MapId => 1;
            public LocalizeInfo LocalizeInfo { get; }
        }
    }
}
