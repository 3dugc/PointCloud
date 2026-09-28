using System;
using System.Reflection;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalLocalizationProfileTests
    {
        [Test]
        public void DefaultsPreserveTheExistingStrictPolicy()
        {
            var settings = new ImmersalLocalizationProfile.Settings().CopyValidated();
            Assert.That(settings.MinimumFrameContrast, Is.EqualTo(10d));
            Assert.That(settings.MinimumFrameEdgeRatio, Is.EqualTo(.018d));
            Assert.That(settings.MinimumFrameCornerCount, Is.EqualTo(18));
            Assert.That(settings.MinimumFrameDetailedRegions, Is.EqualTo(3));
            Assert.That(settings.RequiredStableSamples, Is.EqualTo(5));
            Assert.That(settings.MaximumObservationWindow, Is.EqualTo(9));
            Assert.That(settings.MinimumLocalizationConfidence, Is.EqualTo(50));
            Assert.That(settings.MaximumLocalizationRmse, Is.EqualTo(1.2d));
            Assert.That(settings.MinimumStrongLocalizationConfidence, Is.EqualTo(80));
            Assert.That(settings.MaximumStrongLocalizationRmse, Is.EqualTo(1d));
            Assert.That(settings.MinimumStrongMatches, Is.EqualTo(3));
            Assert.That(settings.MinimumContextMatches, Is.EqualTo(2));
            Assert.That(settings.MinimumViewPositionDelta, Is.EqualTo(.05f));
            Assert.That(settings.MinimumViewRotationDelta, Is.EqualTo(5f));
            Assert.That(settings.MinimumFrameSignatureDistance, Is.EqualTo(12));
            Assert.That(settings.MaxPositionDrift, Is.EqualTo(.05f));
            Assert.That(settings.MaxRotationDriftDegrees, Is.EqualTo(2f));
            Assert.That(settings.MinimumConfirmationPositionSpan, Is.EqualTo(.15f));
            Assert.That(settings.MinimumConfirmationRotationSpan, Is.EqualTo(20f));
            Assert.That(settings.MaximumDownwardDotForContextView, Is.EqualTo(.35f));
            Assert.That(settings.MinimumConfirmationSeconds, Is.EqualTo(4d));
            Assert.That(settings.MaximumObservationAgeSeconds, Is.EqualTo(10d));
            Assert.That(settings.MaximumEvidenceGapSeconds, Is.EqualTo(3d));
            Assert.That(settings.MinimumAttemptIntervalSeconds, Is.EqualTo(.75d));
            Assert.That(settings.MaintenanceAttemptIntervalSeconds, Is.EqualTo(1d));
            Assert.That(settings.MaintenanceRequiredStableSamples, Is.EqualTo(3));
            Assert.That(settings.MaintenanceMaximumObservationWindow, Is.EqualTo(5));
            Assert.That(settings.MaintenanceMaxPositionDrift, Is.EqualTo(.03f));
            Assert.That(settings.MaintenanceMaxRotationDriftDegrees, Is.EqualTo(1f));
            Assert.That(settings.MaximumAutomaticPositionCorrection, Is.EqualTo(.2f));
            Assert.That(settings.MaximumAutomaticRotationCorrection, Is.EqualTo(5f));
            Assert.That(settings.MaximumProvisionalPositionStep, Is.EqualTo(.03f));
            Assert.That(settings.MaximumProvisionalRotationStepDegrees, Is.EqualTo(1f));
            Assert.That(settings.ProvisionalCorrectionSmoothingSeconds, Is.EqualTo(.25f));
            Assert.That(settings.CorrectionSmoothingSeconds, Is.EqualTo(1f));
        }

        [Test]
        public void EditingTheSourceDoesNotChangeAnActiveSnapshotOrAnotherProfile()
        {
            var source = new ImmersalLocalizationProfile.Settings();
            var active = source.CopyValidated();
            var otherPlatform = new ImmersalLocalizationProfile.Settings();
            // Example values for isolation testing, not a calibrated Rokid preset.
            source.MinimumLocalizationConfidence = 15;
            source.MinimumStrongLocalizationConfidence = 25;
            source.RequiredStableSamples = 7;
            source.MinimumFrameContrast = 12d;
            var next = source.CopyValidated();

            Assert.That(active.MinimumLocalizationConfidence, Is.EqualTo(50));
            Assert.That(active.MinimumStrongLocalizationConfidence, Is.EqualTo(80));
            Assert.That(active.RequiredStableSamples, Is.EqualTo(5));
            Assert.That(active.MinimumFrameContrast, Is.EqualTo(10d));
            Assert.That(otherPlatform.MinimumFrameContrast, Is.EqualTo(10d));
            Assert.That(next.MinimumFrameContrast, Is.EqualTo(12d));
            Assert.That(otherPlatform.MinimumLocalizationConfidence, Is.EqualTo(50));
            Assert.That(next.MinimumLocalizationConfidence, Is.EqualTo(15));
            Assert.That(next.MinimumStrongLocalizationConfidence, Is.EqualTo(25));
            Assert.That(next.RequiredStableSamples, Is.EqualTo(7));
            Assert.That(next, Is.Not.SameAs(source));
            Assert.That(active, Is.Not.SameAs(next));
        }

        [Test]
        public void SnapshotContainsOnlyValueFields()
        {
            foreach (FieldInfo field in typeof(ImmersalLocalizationProfile.Settings).GetFields())
                Assert.That(field.FieldType.IsValueType, Is.True, field.Name);
        }

        [TestCase("RequiredStableSamples", 1d)]
        [TestCase("RequiredStableSamples", 13d)]
        [TestCase("RequiredStableSamples", 65d)]
        [TestCase("MaximumObservationWindow", 4d)]
        [TestCase("MaximumObservationWindow", 13d)]
        [TestCase("MaximumObservationWindow", 31d)]
        [TestCase("MaximumObservationWindow", 32d)]
        [TestCase("MaximumObservationWindow", 65d)]
        [TestCase("MinimumFrameContrast", 0d)]
        [TestCase("MinimumFrameContrast", 128d)]
        [TestCase("MinimumFrameContrast", double.NaN)]
        [TestCase("MinimumFrameEdgeRatio", 0d)]
        [TestCase("MinimumFrameEdgeRatio", 1.01d)]
        [TestCase("MinimumFrameEdgeRatio", double.PositiveInfinity)]
        [TestCase("MinimumFrameCornerCount", 0d)]
        [TestCase("MinimumFrameDetailedRegions", 0d)]
        [TestCase("MinimumFrameDetailedRegions", 17d)]
        [TestCase("MinimumLocalizationConfidence", 0d)]
        [TestCase("MinimumLocalizationConfidence", 81d)]
        [TestCase("MinimumStrongLocalizationConfidence", 49d)]
        [TestCase("MaximumLocalizationRmse", 0d)]
        [TestCase("MaximumLocalizationRmse", double.NaN)]
        [TestCase("MaximumLocalizationRmse", double.PositiveInfinity)]
        [TestCase("MaximumStrongLocalizationRmse", 1.21d)]
        [TestCase("MaximumStrongLocalizationRmse", -1d)]
        [TestCase("MaximumStrongLocalizationRmse", double.NaN)]
        [TestCase("MinimumStrongMatches", 0d)]
        [TestCase("MinimumStrongMatches", 6d)]
        [TestCase("MinimumContextMatches", 0d)]
        [TestCase("MinimumContextMatches", 6d)]
        [TestCase("MinimumFrameSignatureDistance", 0d)]
        [TestCase("MinimumFrameSignatureDistance", 65d)]
        [TestCase("MinimumViewPositionDelta", 0d)]
        [TestCase("MinimumViewPositionDelta", double.PositiveInfinity)]
        [TestCase("MinimumViewRotationDelta", 0d)]
        [TestCase("MinimumViewRotationDelta", 181d)]
        [TestCase("MaxPositionDrift", 0d)]
        [TestCase("MaxPositionDrift", double.NaN)]
        [TestCase("MaxRotationDriftDegrees", 181d)]
        [TestCase("MinimumConfirmationPositionSpan", 0d)]
        [TestCase("MinimumConfirmationRotationSpan", 0d)]
        [TestCase("MinimumConfirmationRotationSpan", double.NaN)]
        [TestCase("MaximumDownwardDotForContextView", -1.1d)]
        [TestCase("MaximumDownwardDotForContextView", 1.1d)]
        [TestCase("MaximumDownwardDotForContextView", double.NaN)]
        [TestCase("MinimumConfirmationSeconds", 0d)]
        [TestCase("MinimumConfirmationSeconds", 10d)]
        [TestCase("MaximumObservationAgeSeconds", 4d)]
        [TestCase("MaximumObservationAgeSeconds", double.PositiveInfinity)]
        [TestCase("MaximumEvidenceGapSeconds", .75d)]
        [TestCase("MaximumEvidenceGapSeconds", double.NaN)]
        [TestCase("MinimumAttemptIntervalSeconds", 0d)]
        [TestCase("MinimumAttemptIntervalSeconds", 3d)]
        public void InvalidSettingsFailInsteadOfBeingSilentlyClamped(string fieldName, double value)
        {
            var settings = new ImmersalLocalizationProfile.Settings();
            FieldInfo field = typeof(ImmersalLocalizationProfile.Settings).GetField(fieldName);
            Assert.That(field, Is.Not.Null);
            field.SetValue(settings, Convert.ChangeType(value, field.FieldType));
            Assert.Throws<ArgumentException>(() => settings.CopyValidated());
        }

        [Test]
        public void MaximumSupportedWindowRemainsValid()
        {
            var settings = new ImmersalLocalizationProfile.Settings
            {
                RequiredStableSamples = 12,
                MaximumObservationWindow = 12
            };
            Assert.DoesNotThrow(() => settings.CopyValidated());
        }

        [Test]
        public void SampleLifetimeMustAllowAllRequiredAttempts()
        {
            var settings = new ImmersalLocalizationProfile.Settings
            {
                RequiredStableSamples = 7,
                MinimumAttemptIntervalSeconds = 2d,
                MaximumObservationAgeSeconds = 12d
            };
            Assert.Throws<ArgumentException>(() => settings.CopyValidated());
            settings.MaximumObservationAgeSeconds = 13d;
            Assert.DoesNotThrow(() => settings.CopyValidated());
        }

        [Test]
        public void ConfidenceIsNotLimitedToAPercentage()
        {
            var settings = new ImmersalLocalizationProfile.Settings
            {
                MinimumLocalizationConfidence = 150,
                MinimumStrongLocalizationConfidence = 200
            };
            Assert.DoesNotThrow(() => settings.CopyValidated());
        }
    }
}
