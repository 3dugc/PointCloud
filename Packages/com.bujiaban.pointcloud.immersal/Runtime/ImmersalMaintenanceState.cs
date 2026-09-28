using System;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal
{
    // Production and simulation share gate transitions, freshness, correction
    // escalation and smoothing. Drivers supply time, tracking and SDK results.
    internal sealed class ImmersalMaintenanceState
    {
        private readonly ImmersalLocalizationProfile.Settings _policy;
        private readonly bool _captureDiagnostics;
        private bool _tracking;
        private Pose _largeCandidate, _verifiedPose, _smoothFrom, _smoothTo;
        private int _largeCount;
        private double _smoothStarted, _smoothDuration;
        private bool _smoothFinalizesCorrection;
        private bool _hasProvisionalPose;

        internal ImmersalMaintenanceState(ImmersalLocalizationProfile.Settings policy, bool captureDiagnostics)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _captureDiagnostics = captureDiagnostics;
            Gate = ImmersalPosePolicy.CreateInitialConfirmationGate(policy, captureDiagnostics);
        }

        internal LocalizationConfirmationGate Gate { get; private set; }
        internal Pose Pose { get; private set; } = UnityEngine.Pose.identity;
        internal bool HasPose { get; private set; }
        internal bool Reacquiring { get; private set; }
        internal bool IsSmoothing { get; private set; }
        internal bool SmoothingFinalizesCorrection => IsSmoothing && _smoothFinalizesCorrection;
        internal int Revision { get; private set; }
        internal double LastVerifiedAtSeconds { get; private set; } = -1d;
        internal double NextAttemptAtSeconds { get; private set; }
        internal PointCloudLocalizationStage Stage { get; private set; } = PointCloudLocalizationStage.Searching;
        internal double AttemptIntervalSeconds => HasPose && !Reacquiring
            ? _policy.MaintenanceAttemptIntervalSeconds : _policy.MinimumAttemptIntervalSeconds;

        internal void BeginAttempt(double now) => NextAttemptAtSeconds = now + AttemptIntervalSeconds;

        internal void Invalidate(double now)
        {
            Revision++;
            Reacquiring = HasPose;
            if (_hasProvisionalPose && !_smoothFinalizesCorrection)
                Pose = _verifiedPose;
            IsSmoothing = false;
            _hasProvisionalPose = false;
            _largeCount = 0;
            Gate = ImmersalPosePolicy.CreateInitialConfirmationGate(_policy, _captureDiagnostics);
            NextAttemptAtSeconds = now;
            Stage = HasPose ? PointCloudLocalizationStage.TrackingLost : PointCloudLocalizationStage.Searching;
        }

        internal void RefreshTracking(double now, bool tracking)
        {
            if ((_tracking && !tracking) || (HasPose && !Reacquiring &&
                now - LastVerifiedAtSeconds >= _policy.MaintenanceObservationAgeSeconds))
                Invalidate(now);
            _tracking = tracking;
            if (Reacquiring)
                Stage = tracking ? PointCloudLocalizationStage.Reacquiring : PointCloudLocalizationStage.TrackingLost;
        }

        // Called only for a candidate confirmed by the current Gate.
        internal string Accept(Pose candidate, double now)
        {
            if (!HasPose || Reacquiring)
            {
                string decision = HasPose ? "reacquired" : "initial";
                Pose = candidate;
                _verifiedPose = candidate;
                _hasProvisionalPose = false;
                HasPose = true;
                Reacquiring = false;
                Verify(now);
                BeginMaintenance(now);
                return decision;
            }

            string correction = ImmersalPosePolicy.CorrectionDecision(_policy, _verifiedPose, candidate);
            if (correction == "rejected_large_correction")
            {
                bool consistent = _largeCount > 0 &&
                    Vector3.Distance(_largeCandidate.position, candidate.position) <= _policy.MaintenanceMaxPositionDrift &&
                    Quaternion.Angle(_largeCandidate.rotation, candidate.rotation) <= _policy.MaintenanceMaxRotationDriftDegrees;
                _largeCount = consistent ? _largeCount + 1 : 1;
                _largeCandidate = candidate;
                if (_largeCount >= 2)
                {
                    // Separate confirmed windows may request fresh acquisition;
                    // they never authorize a large jump by themselves.
                    Invalidate(now);
                    return "reacquire_requested";
                }
                BeginMaintenance(now);
                return correction;
            }

            Verify(now);
            if (correction == "inside_deadband")
            {
                BeginMaintenance(now);
                return correction;
            }
            if (Vector3.Distance(Pose.position, candidate.position) < .00001f &&
                Quaternion.Angle(Pose.rotation, candidate.rotation) < .001f)
            {
                Pose = candidate;
                _verifiedPose = candidate;
                _hasProvisionalPose = false;
                IsSmoothing = false;
                BeginMaintenance(now);
                return "applied_after_preview";
            }
            _smoothFrom = Pose;
            _smoothTo = candidate;
            _smoothStarted = now;
            _smoothDuration = _policy.CorrectionSmoothingSeconds;
            _smoothFinalizesCorrection = true;
            _hasProvisionalPose = false;
            IsSmoothing = true;
            return "smoothing";
        }

        // A maintenance result may start bounded visual convergence before the
        // complete temporal gate is satisfied. The last fully verified pose is
        // kept separately: tracking loss, disagreement or a large candidate can
        // roll the preview back without treating it as a confirmed correction.
        internal string Preview(Pose candidate, int stableCount, int requiredSamples, double now)
        {
            if (!HasPose || Reacquiring || stableCount <= 0 || requiredSamples <= 1)
                return RollbackPreview(now) ? "preview_rollback" : "preview_ignored";

            string decision = ImmersalPosePolicy.CorrectionDecision(_policy, _verifiedPose, candidate);
            if (decision != "applied")
                return RollbackPreview(now) ? "preview_rollback" : "preview_ignored";

            float evidenceFraction = Mathf.Clamp01((float)stableCount / requiredSamples);
            Pose target = ImmersalPosePolicy.Interpolate(_verifiedPose, candidate, evidenceFraction);

            Vector3 offset = target.position - _verifiedPose.position;
            float positionLimit = _policy.MaximumProvisionalPositionStep * stableCount;
            if (offset.magnitude > positionLimit)
                target.position = _verifiedPose.position + offset.normalized * positionLimit;

            float angle = Quaternion.Angle(_verifiedPose.rotation, target.rotation);
            float rotationLimit = _policy.MaximumProvisionalRotationStepDegrees * stableCount;
            if (angle > rotationLimit)
                target.rotation = Quaternion.Slerp(_verifiedPose.rotation, target.rotation,
                    rotationLimit / angle);

            if (Vector3.Distance(Pose.position, target.position) < .00001f &&
                Quaternion.Angle(Pose.rotation, target.rotation) < .001f)
                return "preview_unchanged";

            BeginSmoothing(target, now, _policy.ProvisionalCorrectionSmoothingSeconds,
                finalizesCorrection: false);
            return "previewing";
        }

        internal bool RollbackPreview(double now)
        {
            if (!HasPose || Reacquiring ||
                (Vector3.Distance(Pose.position, _verifiedPose.position) < .00001f &&
                 Quaternion.Angle(Pose.rotation, _verifiedPose.rotation) < .001f))
                return false;
            BeginSmoothing(_verifiedPose, now, _policy.ProvisionalCorrectionSmoothingSeconds,
                finalizesCorrection: false);
            return true;
        }

        internal bool AdvanceSmoothing(double now)
        {
            if (!IsSmoothing) return false;
            float fraction = (float)((now - _smoothStarted) / _smoothDuration);
            Pose = ImmersalPosePolicy.Interpolate(_smoothFrom, _smoothTo, fraction);
            if (fraction >= 1f)
            {
                IsSmoothing = false;
                if (_smoothFinalizesCorrection)
                {
                    _verifiedPose = _smoothTo;
                    _hasProvisionalPose = false;
                    BeginMaintenance(now);
                }
                else if (Vector3.Distance(Pose.position, _verifiedPose.position) < .00001f &&
                         Quaternion.Angle(Pose.rotation, _verifiedPose.rotation) < .001f)
                    _hasProvisionalPose = false;
            }
            return true;
        }

        private void BeginSmoothing(Pose target, double now, double duration,
            bool finalizesCorrection)
        {
            _smoothFrom = Pose;
            _smoothTo = target;
            _smoothStarted = now;
            _smoothDuration = duration;
            _smoothFinalizesCorrection = finalizesCorrection;
            if (!finalizesCorrection) _hasProvisionalPose = true;
            IsSmoothing = true;
        }

        private void Verify(double now)
        {
            _largeCount = 0;
            LastVerifiedAtSeconds = now;
            Stage = PointCloudLocalizationStage.Tracking;
        }

        private void BeginMaintenance(double now)
        {
            Gate = ImmersalPosePolicy.CreateMaintenanceConfirmationGate(_policy, _captureDiagnostics);
            NextAttemptAtSeconds = now + _policy.MaintenanceAttemptIntervalSeconds;
        }
    }
}
