using System;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal
{
    /// <summary>
    /// Inspector-editable Immersal confirmation settings. Assign one asset to
    /// each platform's backend; business code still uses PointCloudLocalizer.
    /// Changes affect the next localization operation, never an active one.
    /// </summary>
    [CreateAssetMenu(fileName = "ImmersalLocalizationProfile",
        menuName = "Bujiaban/Point Cloud/Immersal Localization Profile")]
    public sealed class ImmersalLocalizationProfile : ScriptableObject
    {
        [SerializeField, InspectorName("门禁参数")]
        private Settings _settings = new Settings();

        internal Settings CreateSnapshot()
        {
            if (_settings == null)
                throw new InvalidOperationException("Immersal localization profile has no settings.");
            return _settings.CopyValidated();
        }

        private void OnValidate()
        {
            try { CreateSnapshot(); }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[ImmersalLocalizationProfile] '{name}': {exception.Message}", this);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[ImmersalLocalizationProfile] '{name}': {exception.Message}", this);
            }
        }

        // All fields are values. A validated memberwise copy is an independent
        // operation snapshot, not a reference to the editable asset's settings.
        [Serializable]
        internal sealed class Settings
        {
            [Header("画面细节（定位前检查，不判断房间或地面）")]
            [Range(.001f, 127.5f), InspectorName("最低画面对比度")]
            public double MinimumFrameContrast = FrameEvidenceAnalyzer.DefaultMinimumContrast;
            [Range(.0001f, 1f), InspectorName("最低边缘占比")]
            public double MinimumFrameEdgeRatio = FrameEvidenceAnalyzer.DefaultMinimumEdgeRatio;
            [Min(1), InspectorName("至少需要几个画面角点")]
            public int MinimumFrameCornerCount = FrameEvidenceAnalyzer.DefaultMinimumCornerCount;
            [Range(1, FrameEvidenceAnalyzer.RegionCount), InspectorName("至少几个区域具有细节")]
            [Tooltip("画面分为 16 个区域；一个区域至少包含 3 个角点才算具有细节。")]
            public int MinimumFrameDetailedRegions = FrameEvidenceAnalyzer.DefaultMinimumDetailedRegions;

            [Header("匹配质量（仅适用于 Immersal，不代表正确率）")]
            [Min(1), InspectorName("最低匹配分数")]
            public int MinimumLocalizationConfidence = 50;
            [Min(0.001f), InspectorName("最大匹配误差 RMSE")]
            public double MaximumLocalizationRmse = 1.2d;
            [Min(1), InspectorName("强匹配最低分数")]
            public int MinimumStrongLocalizationConfidence = 80;
            [Min(0.001f), InspectorName("强匹配最大误差 RMSE")]
            public double MaximumStrongLocalizationRmse = 1d;
            [Min(1), InspectorName("至少需要几次强匹配")]
            public int MinimumStrongMatches = 3;

            [Header("位置确认")]
            [Range(2, LocalizationConfirmationGate.MaximumSupportedObservations), InspectorName("稳定确认次数")]
            [Tooltip("需要不同观察支持同一个地图位置，不是图像特征点数量。")]
            public int RequiredStableSamples = 5;
            [Min(0.001f), InspectorName("最大位置偏差（米）")]
            [Tooltip("在每对采样相机位置比较对齐偏差，不用远处地图原点的移动距离判断。")]
            public float MaxPositionDrift = .05f;
            [Range(.001f, 180f), InspectorName("最大旋转偏差（度）")]
            public float MaxRotationDriftDegrees = 2f;
            [Min(0.001f), InspectorName("至少确认多久（秒）")]
            public double MinimumConfirmationSeconds = 4d;

            [Header("首次定位 / 跟踪恢复的视角要求（不用于普通维护）")]
            [Min(0.001f), InspectorName("新观察的位置变化（米）")]
            public float MinimumViewPositionDelta = .05f;
            [Range(.001f, 180f), InspectorName("新观察的转角变化（度）")]
            [Tooltip("位置或转角变化满足一项就单独保留；最终一起投票的观察仍须画面变化达标。")]
            public float MinimumViewRotationDelta = 5f;
            [Range(1, 64), InspectorName("新观察的画面变化（位）")]
            public int MinimumFrameSignatureDistance = 12;
            [Min(0.001f), InspectorName("确认所需位置跨度（米）")]
            public float MinimumConfirmationPositionSpan = .15f;
            [Range(.001f, 180f), InspectorName("确认所需转角跨度（度）")]
            [Tooltip("首次定位和跟踪恢复需要转角跨度；另外需要位置跨度，或足够的上下文观察。普通维护不要求转动。转角包含横滚，并不代表一定抬头。")]
            public float MinimumConfirmationRotationSpan = 20f;
            [Range(-1f, 1f), InspectorName("上下文视角朝下分量上限")]
            [Tooltip("0 是水平，1 是朝下。只是朝向检查，不代表识别到了非地面特征。")]
            public float MaximumDownwardDotForContextView = .35f;
            [Min(1), InspectorName("上下文路线至少需要几次匹配")]
            public int MinimumContextMatches = 2;

            [Header("采样与保留（不是定位总超时）")]
            [Range(2, LocalizationConfirmationGate.MaximumSupportedObservations), InspectorName("最多保留几个观察")]
            public int MaximumObservationWindow = 9;
            [Min(0.001f), InspectorName("样本最长保留（秒）")]
            public double MaximumObservationAgeSeconds = 10d;
            [Min(0.001f), InspectorName("多久没有合格结果就清空（秒）")]
            public double MaximumEvidenceGapSeconds = 3d;
            [Min(0.001f), InspectorName("两次尝试的最短间隔（秒）")]
            public double MinimumAttemptIntervalSeconds = .75d;

            [Header("持续矫正")]
            [Min(0.001f), InspectorName("维护定位间隔（秒）")]
            public double MaintenanceAttemptIntervalSeconds = 1d;
            [Range(2, LocalizationConfirmationGate.MaximumSupportedObservations), InspectorName("维护确认次数")]
            [Tooltip("使用间隔采集的新画面确认同一位置，不要求换视角；同一张相机帧不能重复计数。")]
            public int MaintenanceRequiredStableSamples = 3;
            [Range(2, LocalizationConfirmationGate.MaximumSupportedObservations), InspectorName("维护观察窗口")]
            public int MaintenanceMaximumObservationWindow = 5;
            [Min(0.001f), InspectorName("维护候选最大位置偏差（米）")]
            [Tooltip("与首次确认一样，比较采样相机位置附近的对齐偏差。")]
            public float MaintenanceMaxPositionDrift = .03f;
            [Range(.001f, 180f), InspectorName("维护候选最大旋转偏差（度）")]
            public float MaintenanceMaxRotationDriftDegrees = 1f;
            [Min(0.001f), InspectorName("维护至少确认多久（秒）")]
            public double MaintenanceMinimumConfirmationSeconds = 1.5d;
            [Min(0.001f), InspectorName("维护样本最长保留（秒）")]
            public double MaintenanceObservationAgeSeconds = 15d;
            [Min(0.001f), InspectorName("维护证据清空间隔（秒）")]
            public double MaintenanceEvidenceGapSeconds = 5d;
            [Min(0.001f), InspectorName("最大自动位置修正（米）")]
            public float MaximumAutomaticPositionCorrection = .2f;
            [Range(.001f, 180f), InspectorName("最大自动旋转修正（度）")]
            public float MaximumAutomaticRotationCorrection = 5f;
            [Min(0f), InspectorName("位置修正忽略区间（米）")]
            public float PositionCorrectionDeadband = .005f;
            [Range(0f, 180f), InspectorName("旋转修正忽略区间（度）")]
            public float RotationCorrectionDeadband = .15f;
            [Min(0.001f), InspectorName("每个预确认结果最多移动（米）")]
            [Tooltip("普通维护尚未最终确认时，每增加一个一致结果允许的累计位置预矫正上限。大偏差和重新定位不预矫正。")]
            public float MaximumProvisionalPositionStep = .03f;
            [Range(.001f, 180f), InspectorName("每个预确认结果最多旋转（度）")]
            public float MaximumProvisionalRotationStepDegrees = 1f;
            [Min(0.001f), InspectorName("预矫正平滑时间（秒）")]
            public float ProvisionalCorrectionSmoothingSeconds = .25f;
            [Min(0.001f), InspectorName("平滑修正时间（秒）")]
            public float CorrectionSmoothingSeconds = 1f;

            internal Settings CopyValidated()
            {
                var copy = (Settings)MemberwiseClone();
                copy.Validate();
                return copy;
            }

            internal void Validate()
            {
                Require(RequiredStableSamples >= 2 &&
                    RequiredStableSamples <= LocalizationConfirmationGate.MaximumSupportedObservations,
                    nameof(RequiredStableSamples), $"稳定确认次数必须在 2 到 {LocalizationConfirmationGate.MaximumSupportedObservations} 之间。");
                Require(MaximumObservationWindow >= RequiredStableSamples &&
                    MaximumObservationWindow <= LocalizationConfirmationGate.MaximumSupportedObservations,
                    nameof(MaximumObservationWindow), $"观察窗口不能小于稳定确认次数，且最多为 {LocalizationConfirmationGate.MaximumSupportedObservations}。");
                Positive(MinimumFrameContrast, nameof(MinimumFrameContrast));
                Require(MinimumFrameContrast <= 127.5d, nameof(MinimumFrameContrast),
                    "灰度画面对比度上限为 127.5。");
                Positive(MinimumFrameEdgeRatio, nameof(MinimumFrameEdgeRatio));
                Require(MinimumFrameEdgeRatio <= 1d, nameof(MinimumFrameEdgeRatio),
                    "边缘占比必须在大于 0 到 1 之间。");
                Require(MinimumFrameCornerCount >= 1, nameof(MinimumFrameCornerCount),
                    "画面角点数量必须大于 0。");
                Require(MinimumFrameDetailedRegions >= 1 &&
                    MinimumFrameDetailedRegions <= FrameEvidenceAnalyzer.RegionCount,
                    nameof(MinimumFrameDetailedRegions), $"细节区域数量必须在 1 到 {FrameEvidenceAnalyzer.RegionCount} 之间。");
                Require(MinimumLocalizationConfidence >= 1,
                    nameof(MinimumLocalizationConfidence), "最低匹配分数必须大于 0。");
                Require(MinimumStrongLocalizationConfidence >= MinimumLocalizationConfidence,
                    nameof(MinimumStrongLocalizationConfidence), "强匹配分数不能低于普通匹配分数。");
                Positive(MaximumLocalizationRmse, nameof(MaximumLocalizationRmse));
                Positive(MaximumStrongLocalizationRmse, nameof(MaximumStrongLocalizationRmse));
                Require(MaximumStrongLocalizationRmse <= MaximumLocalizationRmse,
                    nameof(MaximumStrongLocalizationRmse), "强匹配 RMSE 上限不能大于普通匹配上限。");
                Require(MinimumStrongMatches >= 1 && MinimumStrongMatches <= RequiredStableSamples,
                    nameof(MinimumStrongMatches), "强匹配次数必须在 1 到稳定确认次数之间。");
                Require(MinimumContextMatches >= 1 && MinimumContextMatches <= RequiredStableSamples,
                    nameof(MinimumContextMatches), "上下文匹配次数必须在 1 到稳定确认次数之间。");
                Require(MinimumFrameSignatureDistance >= 1 && MinimumFrameSignatureDistance <= 64,
                    nameof(MinimumFrameSignatureDistance), "画面变化阈值必须在 1 到 64 之间。");
                Positive(MinimumViewPositionDelta, nameof(MinimumViewPositionDelta));
                Positive(MaxPositionDrift, nameof(MaxPositionDrift));
                Positive(MinimumConfirmationPositionSpan, nameof(MinimumConfirmationPositionSpan));
                Angle(MinimumViewRotationDelta, nameof(MinimumViewRotationDelta));
                Angle(MaxRotationDriftDegrees, nameof(MaxRotationDriftDegrees));
                Angle(MinimumConfirmationRotationSpan, nameof(MinimumConfirmationRotationSpan));
                Require(!float.IsNaN(MaximumDownwardDotForContextView) &&
                    MaximumDownwardDotForContextView >= -1f && MaximumDownwardDotForContextView <= 1f,
                    nameof(MaximumDownwardDotForContextView), "朝下分量必须在 -1 到 1 之间。");
                Positive(MinimumConfirmationSeconds, nameof(MinimumConfirmationSeconds));
                Positive(MaximumObservationAgeSeconds, nameof(MaximumObservationAgeSeconds));
                Positive(MaximumEvidenceGapSeconds, nameof(MaximumEvidenceGapSeconds));
                Positive(MinimumAttemptIntervalSeconds, nameof(MinimumAttemptIntervalSeconds));
                Require(MaximumObservationAgeSeconds > MinimumConfirmationSeconds &&
                    MaximumObservationAgeSeconds > (RequiredStableSamples - 1) * MinimumAttemptIntervalSeconds,
                    nameof(MaximumObservationAgeSeconds), "样本保留时间必须长于最短确认时间和收集所需样本的最短时间。");
                Require(MaximumEvidenceGapSeconds > MinimumAttemptIntervalSeconds,
                    nameof(MaximumEvidenceGapSeconds), "无合格结果的清空时间必须长于尝试间隔。");
                Require(MaintenanceRequiredStableSamples >= 2 &&
                    MaintenanceRequiredStableSamples <= LocalizationConfirmationGate.MaximumSupportedObservations,
                    nameof(MaintenanceRequiredStableSamples), "维护确认次数超出支持范围。");
                Require(MaintenanceMaximumObservationWindow >= MaintenanceRequiredStableSamples &&
                    MaintenanceMaximumObservationWindow <= LocalizationConfirmationGate.MaximumSupportedObservations,
                    nameof(MaintenanceMaximumObservationWindow), "维护观察窗口不能小于确认次数。");
                Positive(MaintenanceAttemptIntervalSeconds, nameof(MaintenanceAttemptIntervalSeconds));
                Positive(MaintenanceMaxPositionDrift, nameof(MaintenanceMaxPositionDrift));
                Angle(MaintenanceMaxRotationDriftDegrees, nameof(MaintenanceMaxRotationDriftDegrees));
                Positive(MaintenanceMinimumConfirmationSeconds, nameof(MaintenanceMinimumConfirmationSeconds));
                Positive(MaintenanceObservationAgeSeconds, nameof(MaintenanceObservationAgeSeconds));
                Positive(MaintenanceEvidenceGapSeconds, nameof(MaintenanceEvidenceGapSeconds));
                Require(MaintenanceObservationAgeSeconds > MaintenanceMinimumConfirmationSeconds &&
                    MaintenanceObservationAgeSeconds >
                    (MaintenanceRequiredStableSamples - 1) * MaintenanceAttemptIntervalSeconds,
                    nameof(MaintenanceObservationAgeSeconds), "维护样本保留时间不足以完成确认。");
                Require(MaintenanceEvidenceGapSeconds > MaintenanceAttemptIntervalSeconds,
                    nameof(MaintenanceEvidenceGapSeconds), "维护证据清空时间必须长于定位间隔。");
                Positive(MaximumAutomaticPositionCorrection, nameof(MaximumAutomaticPositionCorrection));
                Angle(MaximumAutomaticRotationCorrection, nameof(MaximumAutomaticRotationCorrection));
                NonNegative(PositionCorrectionDeadband, nameof(PositionCorrectionDeadband));
                NonNegative(RotationCorrectionDeadband, nameof(RotationCorrectionDeadband));
                Require(RotationCorrectionDeadband <= 180f,
                    nameof(RotationCorrectionDeadband), "旋转忽略区间不能大于 180 度。");
                Require(PositionCorrectionDeadband < MaximumAutomaticPositionCorrection,
                    nameof(PositionCorrectionDeadband), "位置忽略区间必须小于最大自动修正。");
                Require(RotationCorrectionDeadband < MaximumAutomaticRotationCorrection,
                    nameof(RotationCorrectionDeadband), "旋转忽略区间必须小于最大自动修正。");
                Positive(MaximumProvisionalPositionStep, nameof(MaximumProvisionalPositionStep));
                Require(MaximumProvisionalPositionStep <= MaximumAutomaticPositionCorrection,
                    nameof(MaximumProvisionalPositionStep), "单次预矫正位置上限不能大于最大自动修正。");
                Angle(MaximumProvisionalRotationStepDegrees, nameof(MaximumProvisionalRotationStepDegrees));
                Require(MaximumProvisionalRotationStepDegrees <= MaximumAutomaticRotationCorrection,
                    nameof(MaximumProvisionalRotationStepDegrees), "单次预矫正角度上限不能大于最大自动修正。");
                Positive(ProvisionalCorrectionSmoothingSeconds, nameof(ProvisionalCorrectionSmoothingSeconds));
                Positive(CorrectionSmoothingSeconds, nameof(CorrectionSmoothingSeconds));
            }

            private static void Positive(double value, string field)
            {
                Require(!double.IsNaN(value) && !double.IsInfinity(value) && value > 0d,
                    field, "必须是大于 0 的有限数值。");
            }

            private static void Angle(float value, string field)
            {
                Positive(value, field);
                Require(value <= 180f, field, "角度不能大于 180 度。");
            }

            private static void NonNegative(double value, string field)
            {
                Require(!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d,
                    field, "必须是大于或等于 0 的有限数值。");
            }

            private static void Require(bool valid, string field, string message)
            {
                if (!valid) throw new ArgumentException(message, field);
            }
        }
    }
}
