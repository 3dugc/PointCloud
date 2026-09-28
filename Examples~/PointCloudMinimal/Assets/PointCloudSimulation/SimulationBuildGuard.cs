#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Bujiaban.PointCloud.Simulation
{
    internal sealed class SimulationBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == SimulationChecks.ScenePath))
                throw new BuildFailedException("PointCloudSimulation is an Editor-only scene. Remove it from the build scene list.");
        }
    }
}
#endif
