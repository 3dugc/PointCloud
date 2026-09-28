using System;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal
{
    // Shared by the real SDK backend and the editor simulator; no device calls here.
    internal static class ImmersalPosePolicy
    {
        internal static LocalizationConfirmationGate CreateInitialConfirmationGate(
            ImmersalLocalizationProfile.Settings policy, bool captureDiagnostics)
        {
            return new LocalizationConfirmationGate(
                policy.RequiredStableSamples,
                policy.MaximumObservationWindow,
                policy.MinimumViewPositionDelta,
                policy.MinimumViewRotationDelta,
                policy.MinimumFrameSignatureDistance,
                policy.MaxPositionDrift,
                policy.MaxRotationDriftDegrees,
                policy.MinimumConfirmationPositionSpan,
                policy.MinimumConfirmationRotationSpan,
                captureDiagnostics: captureDiagnostics,
                minimumStrongMatches: policy.MinimumStrongMatches,
                minimumContextMatches: policy.MinimumContextMatches,
                minimumConfirmationSeconds: policy.MinimumConfirmationSeconds,
                maximumObservationAgeSeconds: policy.MaximumObservationAgeSeconds,
                maximumEvidenceGapSeconds: policy.MaximumEvidenceGapSeconds);
        }

        internal static LocalizationConfirmationGate CreateMaintenanceConfirmationGate(
            ImmersalLocalizationProfile.Settings policy, bool captureDiagnostics)
        {
            int required = policy.MaintenanceRequiredStableSamples;
            return new LocalizationConfirmationGate(
                required,
                policy.MaintenanceMaximumObservationWindow,
                0f, 0f, 0,
                policy.MaintenanceMaxPositionDrift,
                policy.MaintenanceMaxRotationDriftDegrees,
                0f, 0f,
                captureDiagnostics: captureDiagnostics,
                minimumStrongMatches: Math.Min(policy.MinimumStrongMatches, required),
                minimumContextMatches: Math.Min(policy.MinimumContextMatches, required),
                minimumConfirmationSeconds: policy.MaintenanceMinimumConfirmationSeconds,
                maximumObservationAgeSeconds: policy.MaintenanceObservationAgeSeconds,
                maximumEvidenceGapSeconds: policy.MaintenanceEvidenceGapSeconds,
                minimumIndependentObservationSeconds: policy.MaintenanceAttemptIntervalSeconds * .5d);
        }

        internal static bool IsReliable(ImmersalLocalizationProfile.Settings policy, int confidence, double rmse)
        {
            return confidence >= policy.MinimumLocalizationConfidence &&
                !double.IsNaN(rmse) && !double.IsInfinity(rmse) &&
                rmse >= 0d && rmse <= policy.MaximumLocalizationRmse;
        }

        internal static bool IsStrong(ImmersalLocalizationProfile.Settings policy, int confidence, double rmse)
        {
            return confidence >= policy.MinimumStrongLocalizationConfidence &&
                rmse >= 0d && rmse <= policy.MaximumStrongLocalizationRmse;
        }

        internal static bool IsContext(ImmersalLocalizationProfile.Settings policy, Pose camera)
        {
            return Vector3.Dot(camera.rotation * Vector3.forward, Vector3.down) <
                policy.MaximumDownwardDotForContextView;
        }

        internal static string CorrectionDecision(ImmersalLocalizationProfile.Settings policy, Pose current, Pose candidate)
        {
            float position = Vector3.Distance(current.position, candidate.position);
            float rotation = Quaternion.Angle(current.rotation, candidate.rotation);
            if (position <= policy.PositionCorrectionDeadband && rotation <= policy.RotationCorrectionDeadband)
                return "inside_deadband";
            if (position > policy.MaximumAutomaticPositionCorrection || rotation > policy.MaximumAutomaticRotationCorrection)
                return "rejected_large_correction";
            return "applied";
        }

        internal static Pose Interpolate(Pose from, Pose to, float fraction)
        {
            float t = Mathf.Clamp01(fraction);
            return new Pose(Vector3.Lerp(from.position, to.position, t),
                Quaternion.Slerp(from.rotation, to.rotation, t));
        }
    }
}
