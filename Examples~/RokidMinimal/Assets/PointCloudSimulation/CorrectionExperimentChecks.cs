#if UNITY_EDITOR
using System;
using System.Text;
using Bujiaban.PointCloud.Immersal;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    // The scene button and NUnit run the same bounded, deterministic experiments.
    // Expected world errors come from injected physical drift, not the gate's decision.
    internal static class CorrectionExperimentChecks
    {
        internal static string RunAll(ImmersalLocalizationProfile rokid, ImmersalLocalizationProfile ios)
        {
            var report = new StringBuilder("Quantitative correction experiments\n");
            foreach (var profile in new[] { rokid, ios })
            {
                Check(profile != null, "缺少量化实验的平台参数");
                CheckRepeatedCorrections(profile, report);
                CheckGradualDrift(profile, DriftScenario.Continuous, report);
                CheckGradualDrift(profile, DriftScenario.Noisy, report);
                CheckOutlier(profile, false, report);
                CheckOutlier(profile, true, report);
                CheckLossAndStop(profile, report);
            }
            report.AppendLine("结果只验证已知真值下的共享维护逻辑；合成输入不执行图像识别。持续一致错配仍可能通过门禁。");
            return report.ToString();
        }

        private static void CheckRepeatedCorrections(ImmersalLocalizationProfile profile, StringBuilder report)
        {
            var experiment = new CorrectionExperiment(DriftScenario.Manual);
            var model = Start(profile, experiment);
            for (int injection = 0; injection < 2; injection++)
            {
                int corrections = model.Corrections;
                experiment.InjectDrift(8, 3);
                Check(Mathf.Abs(experiment.PositionErrorCm - 8) < .05f &&
                    Mathf.Abs(experiment.RotationErrorDegrees - 3) < .03f,
                    "注入后必须先出现已知的 8cm / 3° 物理误差");
                if (injection == 0)
                    Check(Mathf.Abs(experiment.UncorrectedPositionErrorCm - 8) < .05f &&
                        Mathf.Abs(experiment.UncorrectedRotationErrorDegrees - 3) < .03f,
                        "首次注入时开关对照必须受到同样的漂移");
                Check(experiment.RecoverySeconds < 0, "注入瞬间不能已恢复");
                bool partialCorrection = false;
                bool movedBeforeConfirmation = false;
                int confirmations = model.Confirmations;
                Until(model, () =>
                {
                    partialCorrection |= model.Smoothing && experiment.PositionErrorCm > .1f &&
                        experiment.PositionErrorCm < 7.9f;
                    movedBeforeConfirmation |= model.Confirmations == confirmations &&
                        experiment.PositionErrorCm < 7.9f;
                    return experiment.RecoverySeconds >= 0;
                }, 45, "阶跃漂移没有完成恢复");
                Check(partialCorrection && movedBeforeConfirmation && model.Corrections == corrections + 1,
                    "每次独立注入必须先预矫正，再经过最终确认完成修正");
                Check(experiment.PositionErrorCm <= 1 && experiment.RotationErrorDegrees <= .5f &&
                    experiment.RecoverySeconds > 2,
                    "恢复应从本次注入开始计时，且满足物理误差和持续稳定条件");
                Check(experiment.UncorrectedPositionErrorCm >= 7.9f &&
                    experiment.UncorrectedRotationErrorDegrees >= 2.9f,
                    "关闭维护的对照组不应自动消除漂移");
                Append(report, profile, "step " + (injection + 1), experiment);
            }
            model.Stop();
        }

        private static void CheckGradualDrift(ImmersalLocalizationProfile profile, DriftScenario scenario,
            StringBuilder report)
        {
            var experiment = new CorrectionExperiment(scenario);
            var model = Start(profile, experiment);
            Check(experiment.RecoverySeconds < 0, "尚未经历扰动时不能记为零秒恢复");
            Advance(model, 95);
            Check(model.Corrections >= 2, "连续漂移必须产生多次独立维护修正");
            Check(experiment.PositionErrorCm < experiment.UncorrectedPositionErrorCm * .5f &&
                experiment.RotationErrorDegrees < experiment.UncorrectedRotationErrorDegrees * .5f,
                "连续漂移或噪声下的最终位置和角度误差必须优于关闭维护");
            Check(experiment.PositionRmsCm < experiment.UncorrectedPositionRmsCm,
                "整段位置 RMS 必须优于关闭维护，不能只挑最终一帧");
            Check(experiment.RecoverySeconds > 30 && experiment.PositionErrorCm <= 1 &&
                experiment.RotationErrorDegrees <= .5f,
                "持续漂移结束并稳定后才允许统计恢复");
            Append(report, profile, scenario.ToString(), experiment);
            model.Stop();
        }

        private static void CheckOutlier(ImmersalLocalizationProfile profile, bool persistent,
            StringBuilder report)
        {
            var experiment = new CorrectionExperiment(persistent
                ? DriftScenario.PersistentWrong : DriftScenario.BriefOutlier);
            var model = Start(profile, experiment);
            float maximumError = 0;
            bool strictReacquisition = false;
            int wrongObservations = 0;
            for (int i = 0; i < 1600; i++)
            {
                int attempts = model.Attempts;
                model.Step(.05);
                maximumError = Mathf.Max(maximumError, experiment.PositionErrorCm);
                strictReacquisition |= model.Reacquiring && !model.Gate.UsesTemporalObservations;
                if (model.Attempts != attempts && experiment.CandidateWorldPose.position.magnitude > .9f)
                    wrongObservations++;
            }
            if (persistent)
            {
                // With unchanged device timing, verification may expire before
                // a second large-correction window. Either route must reacquire
                // through the initial gate, and neither proves physical truth.
                Check(model.Rejections > 0 && strictReacquisition && model.Reacquisitions > 0 &&
                    experiment.PositionErrorCm > 90 && experiment.UncorrectedPositionErrorCm < .1f,
                    "应明确复现一致错误观测通过重新确认后，将原本正确的物体移错");
                Check(experiment.RecoverySeconds < 0, "错误的位置不能统计为物理恢复成功");
                report.Append("LIMITATION reproduced: ");
            }
            else
                Check(wrongObservations >= 1 && wrongObservations < model.Policy.MaintenanceRequiredStableSamples &&
                    maximumError < .1f && model.Corrections == 0 && model.Reacquisitions == 0,
                    "短暂窗口应产生不足确认次数的 1m 错配，且不得造成错误大跳或重新放置");
            Append(report, profile, persistent ? "persistent wrong match" : "brief outlier", experiment);
            model.Stop();
        }

        private static void CheckLossAndStop(ImmersalLocalizationProfile profile, StringBuilder report)
        {
            var experiment = new CorrectionExperiment(DriftScenario.Manual);
            var model = Start(profile, experiment);
            experiment.InjectDrift(8, 3);
            model.SetTracking(false);
            int updates = model.PoseUpdates;
            Advance(model, 35);
            Check(model.Stage == PointCloudLocalizationStage.TrackingLost && model.PoseUpdates == updates &&
                experiment.RecoverySeconds < 0 && experiment.PositionErrorCm > 7.9f,
                "跟踪失效不能继续发布修正或统计恢复成功");
            model.Stop();
            int samples = experiment.Samples.Count;
            double clock = model.Clock;
            double recovery = experiment.RecoverySeconds;
            Advance(model, 10);
            Check(experiment.Samples.Count == samples && model.Clock == clock &&
                model.PoseUpdates == updates && experiment.RecoverySeconds == recovery,
                "停止后不能继续推进曲线、时钟、姿态或恢复统计");
            report.AppendLine("PASS: " + profile.name + " / loss and stop freeze success accounting");
        }

        private static SimulationModel Start(ImmersalLocalizationProfile profile, CorrectionExperiment experiment)
        {
            var model = new SimulationModel(profile, experiment);
            Until(model, () => experiment.HasBaseline, 25, "初次确认未建立物理对照组");
            Check(model.HasPose && experiment.PositionErrorCm < .01f &&
                experiment.UncorrectedPositionErrorCm < .01f, "初次放置应落在独立的物理真值");
            return model;
        }

        private static void Until(SimulationModel model, Func<bool> condition, double seconds, string failure)
        {
            int steps = (int)Math.Ceiling(seconds / .05);
            for (int i = 0; i < steps && !condition(); i++) model.Step(.05);
            Check(condition(), failure);
        }

        private static void Advance(SimulationModel model, double seconds)
        {
            for (int i = 0; i < (int)Math.Ceiling(seconds / .05); i++) model.Step(.05);
        }

        private static void Append(StringBuilder report, ImmersalLocalizationProfile profile, string scenario,
            CorrectionExperiment experiment) => report.AppendLine(
            $"{profile.name} / {scenario}: error {experiment.PositionErrorCm:0.000}cm / " +
            $"{experiment.RotationErrorDegrees:0.000}°, off {experiment.UncorrectedPositionErrorCm:0.000}cm / " +
            $"{experiment.UncorrectedRotationErrorDegrees:0.000}°, RMS {experiment.PositionRmsCm:0.000}cm / " +
            $"off {experiment.UncorrectedPositionRmsCm:0.000}cm, recovery {experiment.RecoverySeconds:0.00}s");

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Correction experiment failed: " + message);
        }
    }
}
#endif
