using System;
using System.Collections.Generic;
using System.Linq;
using Immersal;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Bujiaban.PointCloud.Immersal.Rokid.Editor
{
    [InitializeOnLoad]
    internal static class RokidImmersalProjectIssueProviderRegistration
    {
        static RokidImmersalProjectIssueProviderRegistration()
        {
            ImmersalProjectValidation.RegisterIssueProvider(new RokidImmersalProjectIssueProvider());
        }
    }

    internal sealed class RokidImmersalProjectIssueProvider : IImmersalProjectIssueProvider
    {
        private static readonly ImmersalProjectIssue[] AndroidIssues =
        {
            new ImmersalProjectIssue
            {
                Message = () => "Graphics API must be set to OpenGLES3 for Immersal Rokid.",
                Check = HasRequiredGraphicsApi,
                Fix = ConfigureGraphicsApi,
                Error = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "IL2CPP must be enabled for Android.",
                Check = () => PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) ==
                              ScriptingImplementation.IL2CPP,
                Fix = () => PlayerSettings.SetScriptingBackend(
                    NamedBuildTarget.Android,
                    ScriptingImplementation.IL2CPP),
                Error = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "Allow 'unsafe' code must be enabled for Immersal Core.",
                Check = () => PlayerSettings.allowUnsafeCode,
                Fix = () => PlayerSettings.allowUnsafeCode = true,
                Error = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "Minimum Android API Level must be 28 or higher for this Rokid integration; also check current SDK/device requirements.",
                Check = () => PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel28,
                Fix = () => PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28,
                Error = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "ARM64 Target Architecture must be enabled.",
                Check = () => (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0,
                Fix = () => PlayerSettings.Android.targetArchitectures |= AndroidArchitecture.ARM64,
                Error = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "OpenXR Loader must be enabled for Android.",
                Check = IsOpenXrLoaderEnabled,
                Fix = OpenXrProjectSettings,
                Error = true,
                RequiresManualFix = true
            },
            new ImmersalProjectIssue
            {
                Message = () => "Rokid OpenXR Support must be enabled in Android OpenXR features.",
                Check = IsRokidOpenXrEnabled,
                Fix = EnableRokidOpenXr,
                Error = true
            }
        };

        public string Name => "Bujiaban Point Cloud Immersal Rokid";
        public bool Enabled { get; set; } = true;

        // The default Immersal Android checks require ARCore. Rokid uses OpenXR,
        // so the relevant Android checks are provided above instead.
        public bool DisableDefaultIssues =>
            ImmersalProjectValidation.ActiveBuildTarget == BuildTarget.Android;

        public IEnumerable<ImmersalProjectIssue> Issues =>
            ImmersalProjectValidation.ActiveBuildTarget == BuildTarget.Android
                ? AndroidIssues
                : Array.Empty<ImmersalProjectIssue>();

        private static bool HasRequiredGraphicsApi()
        {
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            return apis.Length == 1 && apis[0] == GraphicsDeviceType.OpenGLES3;
        }

        private static void ConfigureGraphicsApi()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3 });
        }

        private static bool IsOpenXrLoaderEnabled()
        {
            XRGeneralSettings generalSettings =
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings managerSettings = generalSettings?.AssignedSettings;
            return managerSettings != null && managerSettings.activeLoaders.Any(loader =>
                loader != null && loader.GetType().Name == "OpenXRLoader");
        }

        private static bool IsRokidOpenXrEnabled()
        {
            OpenXRFeature feature = FindRokidFeature();
            return feature != null && feature.enabled;
        }

        private static OpenXRFeature FindRokidFeature()
        {
            OpenXRSettings settings =
                OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            return settings?.GetFeatures<OpenXRFeature>().FirstOrDefault(feature =>
                feature != null && feature.GetType().Name == "RokidFeature");
        }

        private static void EnableRokidOpenXr()
        {
            OpenXRFeature feature = FindRokidFeature();
            if (feature == null)
            {
                OpenXrProjectSettings();
                return;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
        }

        private static void OpenXrProjectSettings()
        {
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management");
        }
    }
}
