#if UNITY_EDITOR
using System;
using Bujiaban.PointCloud.Immersal;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    internal enum Scenario
    {
        Normal, NoMatch, LowScore, BlankImage, RepeatedView, FloorRotation,
        StableWrongResult, SmallJitter, SmallCorrection, LargeCorrection, MovementGap,
        AlternatingRecovery
    }

    // A synthetic source AFTER SDK pose conversion, not an Immersal recognition emulator.
    // Production owns maintenance decisions; this model supplies synthetic input and a clock.
    internal sealed class SimulationModel
    {
        internal static readonly string[] Labels =
        {
            "正常观察", "始终匹配失败", "匹配分数较低", "空白画面",
            "重复同一视角", "只低头转动", "稳定的错误结果（能力边界）",
            "放置后微小抖动", "放置后偏移 8 厘米", "放置后突跳 1 米", "转头移动中短暂匹配失败",
            "恢复候选相差 6 厘米"
        };

        internal readonly ImmersalLocalizationProfile.Settings Policy;
        internal readonly CorrectionExperiment Experiment;
        private readonly ImmersalMaintenanceState _state;
        internal LocalizationConfirmationGate Gate => _state.Gate;
        internal Scenario Scenario;
        internal Pose Pose => _state.Pose;
        internal Pose Candidate { get; private set; } = Pose.identity;
        internal Pose Camera { get; private set; } = Pose.identity;
        internal bool HasPose => _state.HasPose;
        internal bool Reacquiring => _state.Reacquiring;
        internal bool Tracking { get; private set; } = true;
        internal bool Running { get; private set; } = true;
        internal bool Smoothing => _state.IsSmoothing;
        internal bool FinalizingCorrection => _state.SmoothingFinalizesCorrection;
        internal double LastVerifiedAtSeconds => _state.LastVerifiedAtSeconds;
        internal double NextAttemptAtSeconds => _state.NextAttemptAtSeconds;
        internal PointCloudLocalizationStage Stage => _state.Stage;
        internal int Attempts { get; private set; }
        internal int Confirmations { get; private set; }
        internal int Corrections { get; private set; }
        internal int Rejections { get; private set; }
        internal int Reacquisitions { get; private set; }
        internal int ReacquisitionRequests { get; private set; }
        internal int Ignored { get; private set; }
        internal int PoseUpdates { get; private set; }
        internal double Clock { get; private set; }
        internal string Status { get; private set; } = "等待模拟观察";
        internal event Action<Pose> OnPose;
        internal event Action<string> OnEvent;
        private int _view;
        private bool _hasPendingConfirmation;
        private Pose _pendingConfirmedPose;

        internal SimulationModel(ImmersalLocalizationProfile profile, CorrectionExperiment experiment = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            Policy = profile.CreateSnapshot();
            Experiment = experiment;
            _state = new ImmersalMaintenanceState(Policy, true);
            _state.RefreshTracking(Clock, Tracking);
        }

        internal void Step(double delta)
        {
            if (!Running) return;
            if (delta < 0 || double.IsNaN(delta) || double.IsInfinity(delta))
                throw new ArgumentOutOfRangeException(nameof(delta));
            Clock += delta;
            Experiment?.Advance(Clock);
            try
            {
                var previousStage = Stage;
                _state.RefreshTracking(Clock, Tracking);
                if (Stage != previousStage)
                    SetStatus(Reacquiring ? "维护证据失效：重新执行首次确认" : "跟踪状态已更新");
                if (!Tracking) return;
                bool finalizingCorrection = _state.SmoothingFinalizesCorrection;
                if (_state.AdvanceSmoothing(Clock))
                {
                    Publish(Pose);
                    if (!Smoothing)
                    {
                        if (finalizingCorrection)
                        {
                            Corrections++;
                            SetStatus("applied：最终确认完成");
                        }
                        else if (_hasPendingConfirmation)
                        {
                            Pose confirmedPose = _pendingConfirmedPose;
                            _hasPendingConfirmation = false;
                            CompleteConfirmation(confirmedPose);
                        }
                        else SetStatus("预矫正完成，继续收集确认结果");
                    }
                    return;
                }
                Gate.AdvanceTime(Clock);
                if (Clock < NextAttemptAtSeconds) return;
                _state.BeginAttempt(Clock);
                Observe();
            }
            finally { Experiment?.Record(Clock, HasPose, Pose, Stage); }
        }

        private void Observe()
        {
            Attempts++;
            int view = Experiment == null ? _view++ % 8 : 0;
            ulong signature;
            if (Experiment != null)
            {
                Camera = Experiment.CameraPose(Clock);
                signature = Experiment.FrameSignature(Clock);
            }
            else
            {
                // Legacy scenarios explicitly exercise stationary maintenance.
                bool repeated = Scenario == Scenario.RepeatedView || (HasPose && !Reacquiring);
                bool floor = Scenario == Scenario.FloorRotation;
                Camera = new Pose(new Vector3(Scenario == Scenario.MovementGap ? view * .3f : 0, 1.5f, -2),
                    Quaternion.Euler(floor ? 85 : 0, repeated ? 0 : view * 8, 0));
                signature = 255UL << ((repeated ? 0 : view) * 8);
            }
            if (Experiment == null && Scenario == Scenario.BlankImage)
            {
                var pixels = new byte[96 * 72];
                FrameEvidenceAnalyzer.TryAnalyze(pixels, 96, 72, 1, out var evidence,
                    Policy.MinimumFrameContrast, Policy.MinimumFrameEdgeRatio,
                    Policy.MinimumFrameCornerCount, Policy.MinimumFrameDetailedRegions);
                if (!evidence.HasEnoughDetail)
                {
                    SetStatus("画面细节不足：未进入匹配确认");
                    return;
                }
            }

            int confidence = Experiment == null && Scenario == Scenario.LowScore ? 25 : 100;
            double rmse = .5;
            bool accepted = ImmersalPosePolicy.IsReliable(Policy, confidence, rmse);
            if (Experiment != null) Candidate = Experiment.Observe(Clock);
            else
            {
                accepted &= Scenario != Scenario.NoMatch &&
                    !(Scenario == Scenario.MovementGap && !HasPose && Clock >= 1.5 && Clock < 6);
                float offset = 0;
                if (Scenario == Scenario.StableWrongResult) offset = 1;
                if (HasPose && Scenario == Scenario.SmallJitter) offset = .002f;
                if (HasPose && Scenario == Scenario.SmallCorrection) offset = .08f;
                if (HasPose && Scenario == Scenario.LargeCorrection) offset = 1;
                if (HasPose && Scenario == Scenario.AlternatingRecovery) offset = view % 2 == 0 ? 0 : .06f;
                Candidate = new Pose(new Vector3(offset, 0, 0), Quaternion.identity);
            }
            var result = Gate.Register(Camera, signature, accepted, Candidate,
                accepted ? confidence - (float)rmse : 0,
                accepted && ImmersalPosePolicy.IsStrong(Policy, confidence, rmse),
                ImmersalPosePolicy.IsContext(Policy, Camera), Attempts, Clock);
            SetStatus(Describe(result.Trace?.blockReason));
            if (HasPose && !Reacquiring)
            {
                bool hasPreviewPose = result.HasStablePose || result.HasCurrentPose;
                Pose previewPose = result.HasStablePose ? result.StablePose : result.CurrentPose;
                int previewCount = result.HasStablePose ? result.StableCount : 1;
                string preview = hasPreviewPose
                    ? _state.Preview(previewPose, previewCount,
                        Gate.RequiredSamples, Clock)
                    : _state.RollbackPreview(Clock) ? "preview_rollback" : "preview_ignored";
                if (preview == "previewing")
                {
                    if (result.IsConfirmed)
                    {
                        _hasPendingConfirmation = true;
                        _pendingConfirmedPose = result.ConfirmedPose;
                        SetStatus("最终结果已到，先完成本次连续预矫正");
                    }
                    else SetStatus($"预矫正：当前候选支持 {previewCount}/{Gate.RequiredSamples}");
                    return;
                }
                if (preview == "preview_rollback")
                    SetStatus("候选不再一致：回到最后确认位置");
            }

            if (!result.IsConfirmed) return;
            CompleteConfirmation(result.ConfirmedPose);
        }

        private void CompleteConfirmation(Pose confirmedPose)
        {
            Confirmations++;
            Candidate = confirmedPose;
            string decision = _state.Accept(Candidate, Clock);
            switch (decision)
            {
                case "initial":
                    Publish(Pose);
                    SetStatus("首次确认完成，进入低频维护");
                    break;
                case "reacquired":
                    Reacquisitions++;
                    Publish(Pose);
                    SetStatus("reacquired：重新确认完成");
                    break;
                case "inside_deadband":
                    Ignored++;
                    SetStatus("偏移很小，保持位置");
                    break;
                case "rejected_large_correction":
                    Rejections++;
                    SetStatus("偏移过大，拒绝直接修正");
                    break;
                case "reacquire_requested":
                    ReacquisitionRequests++;
                    SetStatus("多轮大偏差一致：重新执行首次确认");
                    break;
                case "smoothing":
                    SetStatus("连续结果通过，正在缓慢修正");
                    break;
                case "applied_after_preview":
                    Corrections++;
                    SetStatus("applied：预矫正已到达最终确认位置");
                    break;
            }
        }

        internal void SetTracking(bool tracking)
        {
            if (!Running || Tracking == tracking) return;
            Tracking = tracking;
            _hasPendingConfirmation = false;
            _state.RefreshTracking(Clock, tracking);
            Experiment?.Record(Clock, HasPose, Pose, Stage);
            _view = 0;
            SetStatus(tracking ? "跟踪恢复：重新执行首次确认" : "跟踪丢失：已清空确认结果");
        }

        internal void ResetTrackingOrigin()
        {
            if (!Running) return;
            _hasPendingConfirmation = false;
            _state.Invalidate(Clock);
            Experiment?.Record(Clock, HasPose, Pose, Stage);
            _view = 0;
            SetStatus("跟踪原点变化：重新执行首次确认");
        }

        internal void Stop()
        {
            Running = false;
            _hasPendingConfirmation = false;
            _state.Invalidate(Clock);
            Experiment?.Stop();
            SetStatus("模拟任务已停止");
        }

        private void Publish(Pose pose)
        {
            PoseUpdates++;
            OnPose?.Invoke(pose);
        }

        private void SetStatus(string status)
        {
            Status = status;
            OnEvent?.Invoke($"t={Clock:0.0}s | {status} | stable={Gate.StableCount}/{Gate.RequiredSamples}");
        }

        private static string Describe(string reason)
        {
            switch (reason)
            {
                case "current_result_not_in_stable_group": return "本次结果未支持同一个位置";
                case "insufficient_stable_samples": return "继续观察，稳定结果还不够";
                case "insufficient_strong_matches": return "高质量匹配还不够";
                case "insufficient_view_diversity": return "视角变化或观察范围还不够";
                case "insufficient_confirmation_duration": return "继续确认，观察时间还不够";
                default: return "观察中";
            }
        }
    }
}
#endif
