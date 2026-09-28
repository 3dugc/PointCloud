using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalPublicApiContractTests
    {
        [Test]
        public void RuntimeAssemblyExposesOnlyBackendAndProfile()
        {
            Type[] exportedTypes = typeof(ImmersalPointCloudBackend).Assembly
                .GetExportedTypes()
                .ToArray();

            Assert.That(
                exportedTypes,
                Is.EquivalentTo(new[] { typeof(ImmersalPointCloudBackend), typeof(ImmersalLocalizationProfile) }));
        }

        [TestCase(typeof(ImmersalPointCloudBackend))]
        [TestCase(typeof(ImmersalLocalizationProfile))]
        public void BackendAndProfileDeclareNoPublicBusinessMethods(Type type)
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly;

            Assert.That(
                type.GetMethods(flags),
                Is.Empty);
            Assert.That(
                type.GetFields(flags),
                Is.Empty);
            Assert.That(
                type.GetProperties(flags),
                Is.Empty);
            Assert.That(
                type.GetEvents(flags),
                Is.Empty);
        }

        [TestCase("123-map", 123)]
        [TestCase("123", 123)]
        [TestCase("map", 1)]
        [TestCase("0-map", 1)]
        [TestCase(null, 1)]
        public void MapIdComesFromLeadingFilenameNumber(
            string mapName,
            int expectedMapId)
        {
            Assert.That(
                ImmersalPointCloudBackend.ParseMapId(mapName),
                Is.EqualTo(expectedMapId));
        }
    }
}
