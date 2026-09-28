using System.Linq;
using Bujiaban.PointCloud.Immersal;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation.Tests
{
    public sealed class RuntimePrefabImportTests
    {
        [Test]
        public void iOSRuntimePrefabImportsWithItsProfileAndNoToken()
        {
            const string directory = "Assets/Platform/PointCloud/";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                directory + "Immersal Runtime (iOS).prefab");
            Assert.That(prefab, Is.Not.Null, "The standalone runtime prefab must import.");

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),
                    Is.Zero, $"Missing script on runtime prefab object: {child.name}");
            }

            ImmersalLocalizationProfile profile =
                AssetDatabase.LoadAssetAtPath<ImmersalLocalizationProfile>(
                    directory + "iOSLocalizationProfile.asset");
            Assert.That(profile, Is.Not.Null, "The platform profile must import.");
            var backend = prefab.GetComponent<ImmersalPointCloudBackend>();
            Assert.That(backend, Is.Not.Null);
            using (var serializedBackend = new SerializedObject(backend))
            {
                SerializedProperty assignedProfile = serializedBackend.FindProperty("_localizationProfile");
                Assert.That(assignedProfile, Is.Not.Null);
                Assert.That(assignedProfile.objectReferenceValue, Is.SameAs(profile),
                    "The prefab must retain its platform profile reference after extraction and SDK upgrades.");
            }

            Component[] sdkComponents = prefab.GetComponents<Component>()
                .Where(component => component != null &&
                    component.GetType().FullName == "Immersal.ImmersalSDK")
                .ToArray();
            Assert.That(sdkComponents, Has.Length.EqualTo(1));
            using (var serializedSdk = new SerializedObject(sdkComponents[0]))
            {
                SerializedProperty token = serializedSdk.FindProperty("developerToken");
                Assert.That(token, Is.Not.Null);
                Assert.That(token.stringValue, Is.Null.Or.Empty,
                    "The example must not contain a real developer token.");
            }
        }
    }
}
