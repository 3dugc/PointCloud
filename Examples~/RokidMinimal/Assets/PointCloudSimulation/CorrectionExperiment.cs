#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    internal enum DriftScenario { Manual, Continuous, Noisy, BriefOutlier, PersistentWrong }

    internal readonly struct CorrectionSample
    {
        internal CorrectionSample(double time, float correctedCm, float uncorrectedCm,
            float correctedDegrees, float uncorrectedDegrees)
        {
            Time = time;
            CorrectedCm = correctedCm;
            UncorrectedCm = uncorrectedCm;
            CorrectedDegrees = correctedDegrees;
            UncorrectedDegrees = uncorrectedDegrees;
        }

        internal double Time { get; }
        internal float CorrectedCm { get; }
        internal float UncorrectedCm { get; }
        internal float CorrectedDegrees { get; }
        internal float UncorrectedDegrees { get; }
    }

    // The physical fixture and measurement source are independent of the
    // localizer's output. Only synthetic observations enter the production gate;
    // ground truth is used here to measure the resulting physical placement.
    internal sealed class CorrectionExperiment
    {
        private const int MaximumSamples = 1200;
        private const double SampleIntervalSeconds = .1d;
        private const double AutomaticDriftSeconds = 30d;
        private readonly List<CorrectionSample> _samples = new List<CorrectionSample>(MaximumSamples);
        private Pose _baselineTrackingPose, _lastTrackingPose;
        private double _clock, _baselineAt, _automaticSecondsApplied, _nextSampleAt;
        private double _disturbanceStartedAt = -1d, _disturbanceEndsAt = double.PositiveInfinity;
        private double _withinToleranceSince = -1d;
        private double _correctedSquares, _uncorrectedSquares;
        private bool _stopped;

        internal CorrectionExperiment(DriftScenario scenario) => Scenario = scenario;

        internal DriftScenario Scenario { get; }
        internal Pose GroundTruthPose => Pose.identity;
        internal Pose TrackingFromWorld { get; private set; } = Pose.identity;
        internal Pose CorrectedWorldPose { get; private set; } = Pose.identity;
        internal Pose UncorrectedWorldPose { get; private set; } = Pose.identity;
        internal Pose CandidateWorldPose { get; private set; } = Pose.identity;
        internal bool HasBaseline { get; private set; }
        internal float PositionErrorCm { get; private set; } = float.NaN;
        internal float UncorrectedPositionErrorCm { get; private set; } = float.NaN;
        internal float RotationErrorDegrees { get; private set; } = float.NaN;
        internal float UncorrectedRotationErrorDegrees { get; private set; } = float.NaN;
        internal double RecoverySeconds { get; private set; } = -1d;
        internal IReadOnlyList<CorrectionSample> Samples => _samples;
        internal float PositionRmsCm => _samples.Count == 0 ? float.NaN :
            (float)Math.Sqrt(Math.Max(0d, _correctedSquares) / _samples.Count);
        internal float UncorrectedPositionRmsCm => _samples.Count == 0 ? float.NaN :
            (float)Math.Sqrt(Math.Max(0d, _uncorrectedSquares) / _samples.Count);

        internal void InjectDrift(float centimeters, float yawDegrees)
        {
            if (!HasBaseline || _stopped)
                throw new InvalidOperationException("Drift injection requires an active initial placement.");
            if (float.IsNaN(centimeters) || float.IsInfinity(centimeters) ||
                float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees))
                throw new ArgumentOutOfRangeException(nameof(centimeters), "Drift must be finite.");
            AddDrift(centimeters, yawDegrees);
            _disturbanceStartedAt = _clock;
            _disturbanceEndsAt = Math.Max(_clock, TimedDisturbanceEnd());
            ClearRecovery(clearCompleted: true);
            UpdateWorldPoses();
        }

        internal void Advance(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _clock)
                throw new ArgumentOutOfRangeException(nameof(now));
            _clock = now;
            if (_stopped || !HasBaseline ||
                (Scenario != DriftScenario.Continuous && Scenario != DriftScenario.Noisy)) return;
            double elapsed = Math.Min(AutomaticDriftSeconds, now - _baselineAt);
            double delta = elapsed - _automaticSecondsApplied;
            AddDrift((float)(delta * .2d), (float)(delta * .05d));
            _automaticSecondsApplied = elapsed;
        }

        internal Pose Observe(double now)
        {
            Pose measurement = GroundTruthPose;
            if (HasBaseline)
            {
                double elapsed = now - _baselineAt;
                if (Scenario == DriftScenario.PersistentWrong ||
                    (Scenario == DriftScenario.BriefOutlier && elapsed >= 1d && elapsed < 2.5d))
                    measurement.position += Vector3.right;
                if (Scenario == DriftScenario.Noisy)
                {
                    // Noise belongs to source time, not how often a localizer
                    // samples or whether its previous output triggered recovery.
                    uint noiseState = 0xC0FFEEu ^ unchecked((uint)Math.Floor(elapsed * 1000d) * 0x9E3779B9u);
                    measurement.position += new Vector3(Noise(ref noiseState) * .0015f,
                        Noise(ref noiseState) * .0015f, Noise(ref noiseState) * .0015f);
                    measurement.rotation *= Quaternion.Euler(0, Noise(ref noiseState) * .1f, 0);
                }
            }
            CandidateWorldPose = measurement;
            return ToTracking(measurement);
        }

        internal void Record(double now, bool hasPose, Pose trackingPose, PointCloudLocalizationStage stage)
        {
            if (_stopped) return;
            if (!hasPose)
            {
                ClearRecovery();
                return;
            }
            if (!HasBaseline)
            {
                HasBaseline = true;
                _baselineTrackingPose = trackingPose;
                _baselineAt = now;
                if (Scenario != DriftScenario.Manual)
                {
                    _disturbanceStartedAt = Scenario == DriftScenario.BriefOutlier ? now + 1d : now;
                    _disturbanceEndsAt = TimedDisturbanceEnd();
                }
            }
            _lastTrackingPose = trackingPose;
            UpdateWorldPoses();

            // A quiet but stale placement is not recovery. Continuous drift must
            // finish before timing the two-second period of verified stability.
            bool recovered = _disturbanceStartedAt >= 0d && now >= _disturbanceEndsAt &&
                stage == PointCloudLocalizationStage.Tracking && PositionErrorCm <= 1f &&
                RotationErrorDegrees <= .5f;
            if (!recovered) ClearRecovery();
            else
            {
                if (_withinToleranceSince < 0d) _withinToleranceSince = now;
                if (RecoverySeconds < 0d && now - _withinToleranceSince >= 2d)
                    RecoverySeconds = now - _disturbanceStartedAt;
            }

            // Store measured states, not interpolated samples fabricated during
            // long waits. At most 1200 actual samples (no more than 10 Hz)
            // bound memory. Variable steps may span more than two minutes.
            if (now + 1e-8d < _nextSampleAt) return;
            _nextSampleAt = now + SampleIntervalSeconds;
            if (_samples.Count == MaximumSamples)
            {
                CorrectionSample oldest = _samples[0];
                _correctedSquares -= (double)oldest.CorrectedCm * oldest.CorrectedCm;
                _uncorrectedSquares -= (double)oldest.UncorrectedCm * oldest.UncorrectedCm;
                _samples.RemoveAt(0);
            }
            _samples.Add(new CorrectionSample(now, PositionErrorCm, UncorrectedPositionErrorCm,
                RotationErrorDegrees, UncorrectedRotationErrorDegrees));
            _correctedSquares += (double)PositionErrorCm * PositionErrorCm;
            _uncorrectedSquares += (double)UncorrectedPositionErrorCm * UncorrectedPositionErrorCm;
        }

        internal void Stop()
        {
            _stopped = true;
            ClearRecovery();
        }

        private void AddDrift(float centimeters, float yawDegrees)
        {
            TrackingFromWorld = new Pose(TrackingFromWorld.position + Vector3.right * (centimeters * .01f),
                Quaternion.Euler(0, yawDegrees, 0) * TrackingFromWorld.rotation);
        }

        private void UpdateWorldPoses()
        {
            CorrectedWorldPose = ToWorld(_lastTrackingPose);
            UncorrectedWorldPose = ToWorld(_baselineTrackingPose);
            PositionErrorCm = Vector3.Distance(CorrectedWorldPose.position, GroundTruthPose.position) * 100f;
            UncorrectedPositionErrorCm = Vector3.Distance(UncorrectedWorldPose.position, GroundTruthPose.position) * 100f;
            RotationErrorDegrees = Quaternion.Angle(CorrectedWorldPose.rotation, GroundTruthPose.rotation);
            UncorrectedRotationErrorDegrees = Quaternion.Angle(UncorrectedWorldPose.rotation, GroundTruthPose.rotation);
        }

        private void ClearRecovery(bool clearCompleted = false)
        {
            _withinToleranceSince = -1d;
            // The first completed recovery is a historical result for this
            // disturbance. Loss/stop only interrupt an unfinished interval.
            if (clearCompleted) RecoverySeconds = -1d;
        }

        private double TimedDisturbanceEnd()
        {
            switch (Scenario)
            {
                case DriftScenario.Continuous:
                case DriftScenario.Noisy: return _baselineAt + AutomaticDriftSeconds;
                case DriftScenario.BriefOutlier: return _baselineAt + 2.5d;
                case DriftScenario.PersistentWrong: return double.PositiveInfinity;
                default: return _clock;
            }
        }

        private static float Noise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 8388607.5f - 1f;
        }

        // The physical observer keeps scanning on the same schedule, even when
        // a localizer is waiting, smoothing, lost or reacquiring.
        internal Pose CameraPose(double now) => ToTracking(new Pose(new Vector3(0, 1.5f, -2),
            Quaternion.Euler(0, ViewAt(now) * 8, 0)));
        internal ulong FrameSignature(double now) => 255UL << (ViewAt(now) * 8);
        private static int ViewAt(double now) => (int)(Math.Floor(now) % 8d);

        internal Pose ToTracking(Pose worldPose) => Compose(TrackingFromWorld, worldPose);
        internal Pose ToWorld(Pose trackingPose) => Compose(Inverse(TrackingFromWorld), trackingPose);

        internal static Pose Compose(Pose parent, Pose child) =>
            new Pose(parent.position + parent.rotation * child.position, parent.rotation * child.rotation);

        internal static Pose Inverse(Pose pose)
        {
            Quaternion inverse = Quaternion.Inverse(pose.rotation);
            return new Pose(inverse * -pose.position, inverse);
        }
    }
}
#endif
