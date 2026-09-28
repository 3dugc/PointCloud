#if UNITY_EDITOR
using System;
using System.Text;
using Bujiaban.PointCloud.Immersal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    public static class SimulationChecks
    {
        public const string ScenePath = "Assets/PointCloudSimulation/PointCloudSimulation.unity";

        [MenuItem("Tools/Point Cloud/打开独立模拟场景")]
        public static void OpenScene()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Point Cloud/运行模拟自动检查")]
        public static void RunFromMenu()
        {
            Debug.Log(RunAll(LoadProfile("Rokid"), LoadProfile("iOS")));
        }

        public static ImmersalLocalizationProfile LoadProfile(string platform)
        {
            // Local device profile is the actual production asset; the other platform
            // is an explicit snapshot, never silently substituted with default values.
            var local = AssetDatabase.LoadAssetAtPath<ImmersalLocalizationProfile>(
                $"Assets/Platform/PointCloud/{platform}LocalizationProfile.asset");
            if (local != null) return local;
            var minimal = AssetDatabase.LoadAssetAtPath<ImmersalLocalizationProfile>(
                $"Assets/Minimal/Profiles/{platform}LocalizationProfile.asset");
            return minimal != null ? minimal : AssetDatabase.LoadAssetAtPath<ImmersalLocalizationProfile>(
                $"Assets/PointCloudSimulation/Profiles/{platform}LocalizationProfile.asset");
        }

        public static string RunAll(ImmersalLocalizationProfile rokid, ImmersalLocalizationProfile ios)
        {
            var report = new StringBuilder("PointCloud Simulation Checks\n");
            foreach (var profile in new[] { rokid, ios })
            {
                Check(profile != null, "缺少平台参数资产");
                foreach (Scenario scenario in Enum.GetValues(typeof(Scenario)))
                {
                    var model = new SimulationModel(profile) { Scenario = scenario };
                    for (int i = 0; i < 800; i++) model.Step(.1);
                    string prefix = profile.name + " / " + SimulationModel.Labels[(int)scenario];
                    switch (scenario)
                    {
                        case Scenario.NoMatch:
                        case Scenario.BlankImage:
                        case Scenario.RepeatedView:
                        case Scenario.FloorRotation:
                            Check(!model.HasPose, prefix + " 不应确认");
                            break;
                        case Scenario.LowScore:
                            Check(model.HasPose == (model.Policy.MinimumLocalizationConfidence <= 25 &&
                                model.Policy.MinimumStrongLocalizationConfidence <= 25), prefix);
                            break;
                        case Scenario.SmallJitter:
                            Check(model.HasPose && model.Ignored > 0 && model.Corrections == 0, prefix);
                            break;
                        case Scenario.SmallCorrection:
                            Check(model.Corrections == 1 && Mathf.Abs(model.Pose.position.x - .08f) < .001f, prefix);
                            break;
                        case Scenario.LargeCorrection:
                            Check(model.Rejections > 0 && model.Reacquisitions > 0 && model.Corrections == 0 &&
                                Mathf.Abs(model.Pose.position.x - 1) < .001f, prefix + " 应先拒绝，再经首次门禁重新确认");
                            break;
                        default:
                            Check(model.HasPose, prefix);
                            break;
                    }
                    report.AppendLine((scenario == Scenario.StableWrongResult ? "LIMITATION reproduced: " : "PASS: ") + prefix);
                }
                var moving = new SimulationModel(profile) { Scenario = Scenario.MovementGap };
                for (int i = 0; i < 50; i++) moving.Step(.1);
                Check(!moving.HasPose && moving.Gate.StableCount >= 2, "转头移动期间应保留短暂失配前的结果");
                for (int i = 0; i < 100; i++) moving.Step(.1);
                Check(moving.HasPose, "移动恢复匹配后应能继续完成确认");
                CheckSharedMaintenance(profile, report);
            }
            report.AppendLine(CorrectionExperimentChecks.RunAll(rokid, ios));
            report.AppendLine("这些检查不执行 Immersal 原生识别，不证明真实房间识别正确或 SDK 资源释放。");
            return report.ToString();
        }

        private static void CheckSharedMaintenance(ImmersalLocalizationProfile profile, StringBuilder report)
        {
            var recovery = new SimulationModel(profile);
            AdvanceUntil(recovery, model => model.HasPose, 20, "恢复测试前应先完成放置");
            Check(recovery.Gate.UsesTemporalObservations && recovery.Stage == PointCloudLocalizationStage.Tracking,
                "首次确认后应由共享状态切换维护门禁");
            recovery.SetTracking(false);
            int updates = recovery.PoseUpdates;
            double lastVerified = recovery.LastVerifiedAtSeconds;
            Advance(recovery, 10);
            Check(recovery.Gate.StableCount == 0 && recovery.PoseUpdates == updates &&
                recovery.Stage == PointCloudLocalizationStage.TrackingLost, "丢失跟踪后必须清空、暂停");
            recovery.SetTracking(true);
            recovery.Scenario = Scenario.RepeatedView;
            Advance(recovery, 15);
            Check(recovery.Reacquiring && !recovery.Gate.UsesTemporalObservations &&
                recovery.PoseUpdates == updates && recovery.LastVerifiedAtSeconds == lastVerified,
                "跟踪恢复后静止重复输入不能绕过首次确认");
            report.AppendLine("PASS: " + profile.name + " / shared-state: stationary reacquisition blocked");

            recovery.Scenario = Scenario.AlternatingRecovery;
            AdvanceUntil(recovery, model => !model.Reacquiring, 15,
                "相差 6 厘米的恢复候选应能通过首次门禁，不能卡在旧维护门禁");
            Check(recovery.Reacquisitions == 1 && recovery.PoseUpdates > updates &&
                recovery.Stage == PointCloudLocalizationStage.Tracking && recovery.Gate.UsesTemporalObservations,
                "恢复后应重新确认并回到维护状态");
            report.AppendLine("PASS: " + profile.name + " / shared-state: 6cm recovery");

            recovery.Scenario = Scenario.NoMatch;
            updates = recovery.PoseUpdates;
            lastVerified = recovery.LastVerifiedAtSeconds;
            Advance(recovery, recovery.Policy.MaintenanceObservationAgeSeconds + .2);
            Check(recovery.Reacquiring && recovery.Stage == PointCloudLocalizationStage.Reacquiring &&
                !recovery.Gate.UsesTemporalObservations && recovery.PoseUpdates == updates &&
                recovery.LastVerifiedAtSeconds == lastVerified,
                "长期无合格新结果必须失效，不能继续标记为已验证跟踪");
            report.AppendLine("PASS: " + profile.name + " / shared-state: verification expiry");
            recovery.Stop();
            updates = recovery.PoseUpdates;
            recovery.Step(100);
            Check(recovery.PoseUpdates == updates, "停止后不得回调");

            var smoothing = new SimulationModel(profile) { Scenario = Scenario.SmallCorrection };
            AdvanceUntil(smoothing, model => model.Smoothing && !model.FinalizingCorrection, 30,
                "8 厘米偏移应在最终确认前开始预矫正");
            smoothing.Step(.1);
            Pose partialPose = smoothing.Pose;
            updates = smoothing.PoseUpdates;
            Check(partialPose.position.x > 0 && partialPose.position.x < .08f, "应先发布部分预矫正结果");
            smoothing.ResetTrackingOrigin();
            smoothing.Scenario = Scenario.RepeatedView;
            Advance(smoothing, smoothing.Policy.CorrectionSmoothingSeconds * 2);
            Check(smoothing.Reacquiring && !smoothing.Smoothing && smoothing.Corrections == 0 &&
                smoothing.PoseUpdates == updates && smoothing.Pose.Equals(Pose.identity),
                "预矫正期间原点重置必须回到最后确认位置，重新确认前不得继续移动");
            smoothing.Stop();
            report.AppendLine("PASS: " + profile.name + " / shared-state: origin reset interrupts smoothing");
        }

        private static void Advance(SimulationModel model, double seconds)
        {
            for (int i = 0; i < Math.Ceiling(seconds * 10); i++) model.Step(.1);
        }

        private static void AdvanceUntil(SimulationModel model, Func<SimulationModel, bool> condition,
            double seconds, string message)
        {
            double deadline = model.Clock + seconds;
            while (!condition(model) && model.Clock < deadline) model.Step(.1);
            Check(condition(model), message);
        }

        private static void Check(bool valid, string message)
        {
            if (!valid) throw new InvalidOperationException("Simulation check failed: " + message);
        }
    }
}
#endif
