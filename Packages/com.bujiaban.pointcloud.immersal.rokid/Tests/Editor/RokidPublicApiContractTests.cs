using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Rokid.Tests
{
    public sealed class RokidPublicApiContractTests
    {
        [Test]
        public void RuntimeAssemblyExposesOnlyUnityAdapter()
        {
            Type[] exportedTypes = typeof(RokidUXRSupport).Assembly
                .GetExportedTypes()
                .ToArray();

            Assert.That(
                exportedTypes,
                Is.EqualTo(new[] { typeof(RokidUXRSupport) }));
        }

        [Test]
        public void AdapterPublishesTheOfficialPlatformSupportMethods()
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly;

            MethodInfo[] methods = typeof(RokidUXRSupport)
                .GetMethods(flags)
                .OrderBy(method => method.Name)
                .ThenBy(method => method.GetParameters().Length)
                .ToArray();

            Assert.That(methods, Has.Length.EqualTo(5));
            Assert.That(
                methods.Select(method => method.Name),
                Is.EqualTo(new[]
                {
                    "ConfigurePlatform",
                    "ConfigurePlatform",
                    "StopAndCleanUp",
                    "UpdatePlatform",
                    "UpdatePlatform"
                }));
            Assert.That(
                methods.Count(method => method.GetParameters().Length == 0),
                Is.EqualTo(3));
            Assert.That(
                methods.Count(method => method.GetParameters().Length == 1),
                Is.EqualTo(2));
            Assert.That(
                typeof(RokidUXRSupport).GetFields(flags),
                Is.Empty);
            Assert.That(
                typeof(RokidUXRSupport).GetProperties(flags),
                Is.Empty);
            Assert.That(
                typeof(RokidUXRSupport).GetEvents(flags),
                Is.Empty);
        }

        [Test]
        public void OnlyPublicSupportOwnsUnityLifecycleCleanup()
        {
            const BindingFlags lifecycleFlags =
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            Assert.That(
                typeof(RokidUXRSupport).GetMethod("OnDisable", lifecycleFlags),
                Is.Not.Null);
            Assert.That(
                typeof(RokidUXRSupport).GetMethod("OnDestroy", lifecycleFlags),
                Is.Not.Null);
            Assert.That(
                typeof(RokidSpatialFrameSource).GetMethod("OnDisable", lifecycleFlags),
                Is.Null);
            Assert.That(
                typeof(RokidSpatialFrameSource).GetMethod("OnDestroy", lifecycleFlags),
                Is.Null);
        }
    }
}
