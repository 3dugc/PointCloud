using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalLocalizationProfileAssetTests
    {
        [Test]
        public void InspectorFieldsSerializeAndSnapshotsRemainIndependent()
        {
            var profile = ScriptableObject.CreateInstance<ImmersalLocalizationProfile>();
            var restored = ScriptableObject.CreateInstance<ImmersalLocalizationProfile>();
            try
            {
                var active = profile.CreateSnapshot();
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_settings.MinimumLocalizationConfidence").intValue = 15;
                serialized.FindProperty("_settings.MinimumStrongLocalizationConfidence").intValue = 25;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var next = profile.CreateSnapshot();
                Assert.That(active.MinimumLocalizationConfidence, Is.EqualTo(50));
                Assert.That(next.MinimumLocalizationConfidence, Is.EqualTo(15));
                Assert.That(next.MinimumStrongLocalizationConfidence, Is.EqualTo(25));

                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(profile), restored);
                Assert.That(restored.CreateSnapshot().MinimumLocalizationConfidence, Is.EqualTo(15));
                Assert.That(restored.CreateSnapshot().MinimumStrongLocalizationConfidence, Is.EqualTo(25));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(restored);
            }
        }

    }
}
