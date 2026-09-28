using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Bujiaban.PointCloud
{
    internal readonly struct LocalizationGateResult
    {
        internal LocalizationGateResult(
            bool isNewObservation,
            bool isConfirmed,
            Pose confirmedPose,
            bool hasCurrentPose,
            Pose currentPose,
            bool hasStablePose,
            Pose stablePose,
            int stableCount,
            int observationCount,
            int poseCount,
            int outlierCount,
            float nearestPositionDelta,
            float nearestRotationDelta,
            int nearestSignatureDistance,
            bool hasStrongMatch,
            bool hasViewDiversity,
            bool hasContextView,
            float stablePositionSpan,
            float stableRotationSpan,
            LocalizationGateTrace trace = null)
        {
            IsNewObservation = isNewObservation;
            IsConfirmed = isConfirmed;
            ConfirmedPose = confirmedPose;
            HasCurrentPose = hasCurrentPose;
            CurrentPose = currentPose;
            HasStablePose = hasStablePose;
            StablePose = stablePose;
            StableCount = stableCount;
            ObservationCount = observationCount;
            PoseCount = poseCount;
            OutlierCount = outlierCount;
            NearestPositionDelta = nearestPositionDelta;
            NearestRotationDelta = nearestRotationDelta;
            NearestSignatureDistance = nearestSignatureDistance;
            HasStrongMatch = hasStrongMatch;
            HasViewDiversity = hasViewDiversity;
            HasContextView = hasContextView;
            StablePositionSpan = stablePositionSpan;
            StableRotationSpan = stableRotationSpan;
            Trace = trace;
        }

        internal bool IsNewObservation { get; }
        internal bool IsConfirmed { get; }
        internal Pose ConfirmedPose { get; }
        internal bool HasCurrentPose { get; }
        internal Pose CurrentPose { get; }
        // Best pose in the current stable group, even before every confirmation
        // condition passes. Maintenance may use this only for bounded visual
        // convergence; initial placement and reacquisition still require IsConfirmed.
        internal bool HasStablePose { get; }
        internal Pose StablePose { get; }
        internal int StableCount { get; }
        internal int ObservationCount { get; }
        internal int PoseCount { get; }
        internal int OutlierCount { get; }
        internal float NearestPositionDelta { get; }
        internal float NearestRotationDelta { get; }
        internal int NearestSignatureDistance { get; }
        internal bool HasStrongMatch { get; }
        internal bool HasViewDiversity { get; }
        internal bool HasContextView { get; }
        internal float StablePositionSpan { get; }
        internal float StableRotationSpan { get; }
        internal LocalizationGateTrace Trace { get; }
    }

    /// <summary>
    /// Gives one vote to each genuinely different camera observation and
    /// confirms only when several votes agree on the same map-origin pose.
    /// </summary>
    internal sealed class LocalizationConfirmationGate
    {
        // The exact stable-group search enumerates 2^n subsets. Keep the window
        // bounded to 4096 subsets and share this limit with editable profiles.
        internal const int MaximumSupportedObservations = 12;

        private readonly int _requiredSamples;
        private readonly int _maximumObservations;
        private readonly float _minimumViewPositionDelta;
        private readonly float _minimumViewRotationDelta;
        private readonly int _minimumSignatureDistance;
        private readonly float _maximumPosePositionDrift;
        private readonly float _maximumPoseRotationDrift;
        private readonly float _minimumConfirmationPositionSpan;
        private readonly float _minimumConfirmationRotationSpan;
        private readonly int _minimumStrongMatches;
        private readonly int _minimumContextMatches;
        private readonly double _minimumConfirmationSeconds;
        private readonly double _maximumObservationAgeSeconds;
        private readonly double _maximumEvidenceGapSeconds;
        private readonly bool _captureDiagnostics;
        private readonly double _minimumIndependentObservationSeconds;
        private double _lastAcceptedAtSeconds = double.NegativeInfinity;
        private double _lastObservedAtSeconds = double.NegativeInfinity;
        private readonly List<Observation> _observations =
            new List<Observation>();

        internal LocalizationConfirmationGate(
            int requiredSamples,
            int maximumObservations,
            float minimumViewPositionDelta,
            float minimumViewRotationDelta,
            int minimumSignatureDistance,
            float maximumPosePositionDrift,
            float maximumPoseRotationDrift,
            float minimumConfirmationPositionSpan,
            float minimumConfirmationRotationSpan,
            bool captureDiagnostics = false,
            int minimumStrongMatches = 3,
            int minimumContextMatches = 2,
            double minimumConfirmationSeconds = 4d,
            double maximumObservationAgeSeconds = 10d,
            double maximumEvidenceGapSeconds = 3d,
            double minimumIndependentObservationSeconds = 0d)
        {
            if (requiredSamples < 1 || requiredSamples > MaximumSupportedObservations)
                throw new ArgumentOutOfRangeException(nameof(requiredSamples));
            if (maximumObservations < requiredSamples ||
                maximumObservations > MaximumSupportedObservations)
                throw new ArgumentOutOfRangeException(nameof(maximumObservations));
            if (double.IsNaN(minimumIndependentObservationSeconds) ||
                double.IsInfinity(minimumIndependentObservationSeconds) ||
                minimumIndependentObservationSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(minimumIndependentObservationSeconds));
            _minimumIndependentObservationSeconds = minimumIndependentObservationSeconds;
            _requiredSamples = requiredSamples;
            _maximumObservations = maximumObservations;
            _minimumViewPositionDelta = Mathf.Max(
                0f,
                minimumViewPositionDelta);
            _minimumViewRotationDelta = Mathf.Max(
                0f,
                minimumViewRotationDelta);
            _minimumSignatureDistance = Mathf.Clamp(
                minimumSignatureDistance,
                0,
                64);
            _maximumPosePositionDrift = Mathf.Max(
                0f,
                maximumPosePositionDrift);
            _maximumPoseRotationDrift = Mathf.Max(
                0f,
                maximumPoseRotationDrift);
            _minimumConfirmationPositionSpan = Mathf.Max(
                0f,
                minimumConfirmationPositionSpan);
            _minimumConfirmationRotationSpan = Mathf.Max(
                0f,
                minimumConfirmationRotationSpan);
            _captureDiagnostics = captureDiagnostics;
            _minimumStrongMatches = Math.Max(1, minimumStrongMatches);
            _minimumContextMatches = Math.Max(1, minimumContextMatches);
            _minimumConfirmationSeconds = Math.Max(0d, minimumConfirmationSeconds);
            _maximumObservationAgeSeconds = Math.Max(.01d, maximumObservationAgeSeconds);
            _maximumEvidenceGapSeconds = Math.Max(.01d, maximumEvidenceGapSeconds);
        }

        internal int RequiredSamples => _requiredSamples;
        internal bool UsesTemporalObservations => _minimumIndependentObservationSeconds > 0d;
        internal int StableCount { get; private set; }
        internal int ObservationCount => _observations.Count;
        internal int TotalDistinctObservationCount { get; private set; }
        internal LocalizationGateTrace LastExpiryTrace { get; private set; }
        internal PointCloudConfirmationRequirement PendingRequirement { get; private set; } =
            PointCloudConfirmationRequirement.CollectingSamples;

        // Also called while frames are unavailable/low-detail, so the HUD cannot
        // retain old votes indefinitely. This is evidence expiry, not a timeout
        // on the localization operation: scanning remains cancellable and unbounded.
        internal bool AdvanceTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < _lastObservedAtSeconds)
                throw new ArgumentOutOfRangeException(nameof(now), "Use a finite monotonic observation clock.");
            _lastObservedAtSeconds = now;
            LastExpiryTrace = null;
            bool gapExpired = now - _lastAcceptedAtSeconds >= _maximumEvidenceGapSeconds;
            var removed = _captureDiagnostics ? new List<LocalizationObservationTrace>() : null;
            bool changed = false;
            for (int i = _observations.Count - 1; i >= 0; i--)
            {
                Observation observation = _observations[i];
                double sourceTime = observation.HasMapPose ? observation.PoseAtSeconds : observation.CreatedAtSeconds;
                bool tooOld = now - sourceTime >= _maximumObservationAgeSeconds;
                // Before any accepted pose exists, keep the bounded recent window
                // for observation diagnostics; there is no candidate to expire.
                bool clearForGap = gapExpired && !double.IsNegativeInfinity(_lastAcceptedAtSeconds);
                if (!tooOld && !clearForGap) continue;
                if (removed != null)
                {
                    var trace = Snapshot(observation, now);
                    trace.removalReason = clearForGap ? "evidence_gap_expired" : "observation_age_expired";
                    removed.Add(trace);
                }
                _observations.RemoveAt(i);
                changed = true;
            }
            if (gapExpired) _lastAcceptedAtSeconds = double.NegativeInfinity;
            if (!changed) return false;
            List<int> group = FindBestStableGroup(out _);
            StableCount = group.Count;
            PendingRequirement = PointCloudConfirmationRequirement.EvidenceExpired;
            if (_captureDiagnostics)
            {
                LastExpiryTrace = new LocalizationGateTrace
                {
                    retained = Snapshots(now), removed = removed.ToArray(),
                    stableObservationIds = StableIds(group),
                    confirmationPath = "blocked", blockReason = "evidence_expired",
                    poseAction = "expired_before_observation"
                };
            }
            return true;
        }

        internal LocalizationGateResult Register(
            Pose cameraPose,
            ulong frameSignature,
            bool hasMapPose,
            Pose mapPose,
            float qualityScore,
            bool isStrongMatch,
            bool hasContextView,
            long attemptId,
            double observedAtSeconds)
        {
            AdvanceTime(observedAtSeconds);
            LocalizationObservationTrace[] expired = LastExpiryTrace?.removed;
            int observationIndex = FindEquivalentObservation(
                cameraPose,
                frameSignature,
                attemptId,
                observedAtSeconds,
                out float nearestPositionDelta,
                out float nearestRotationDelta,
                out int nearestSignatureDistance);
            bool isNewObservation = observationIndex < 0;
            if (isNewObservation)
            {
                Observation observation = new Observation(
                    cameraPose,
                    frameSignature);
                observation.Id = TotalDistinctObservationCount + 1;
                observation.CreatedAttemptId = attemptId;
                observation.CreatedAtSeconds = observedAtSeconds;
                _observations.Add(observation);
                observationIndex = _observations.Count - 1;
                TotalDistinctObservationCount++;
            }

            Observation current = _observations[observationIndex];
            bool contextAdded = !current.HasContextView && hasMapPose && hasContextView;
            bool strongAdded = !current.HasStrongMatch && hasMapPose && isStrongMatch;
            string poseAction = hasMapPose
                ? current.HasMapPose ? "replaced_with_latest_pose" : "stored_first_pose"
                : current.HasMapPose ? "miss_preserves_unexpired_evidence" : "no_valid_pose";
            current.LastAttemptId = attemptId;
            // A miss has no new pose to contradict the old one. It may retain
            // the entire old sample without refreshing its age or making a new
            // vote. A returned reliable pose still replaces the whole sample.
            if (hasMapPose)
            {
                current.HasContextView = hasContextView;
                current.HasStrongMatch = isStrongMatch;
                current.ContextAttemptId = current.HasContextView ? attemptId : 0;
                current.ContextHadMapPose = current.HasContextView;
                current.StrongMatchAttemptId = current.HasStrongMatch ? attemptId : 0;
                if (current.HasContextView)
                {
                    current.ContextCameraPose = cameraPose;
                }
                current.HasMapPose = true;
                _lastAcceptedAtSeconds = observedAtSeconds;
                current.MapPose = mapPose;
                current.QualityScore = qualityScore;
                current.PoseAttemptId = attemptId;
                current.PoseAtSeconds = observedAtSeconds;
                current.PoseCameraPose = cameraPose;
                current.PoseFrameSignature = frameSignature;
            }
            _observations[observationIndex] = current;

            List<LocalizationObservationTrace> removed = TrimObservationWindow(observedAtSeconds);
            if (expired != null) removed.InsertRange(0, expired);

            List<int> stableGroup = FindBestStableGroup(out int poseCount);
            // Select among fully qualified groups before ranking their size or
            // distance. A larger weak group may share the current observation
            // with a smaller group that has enough strong, diverse, fresh votes.
            // When no group qualifies, retain the largest one for diagnostics.
            if (hasMapPose)
            {
                List<int> currentGroup = FindBestStableGroup(out _, current.Id,
                    requireConfirmation: true);
                if (currentGroup.Count >= _requiredSamples)
                    stableGroup = currentGroup;
            }
            StableCount = stableGroup.Count;
            bool hasConfirmationEvidence = HasConfirmationEvidence(stableGroup, out ConfirmationEvidence evidence);
            bool currentSupportsGroup = hasMapPose && stableGroup.Exists(i => _observations[i].Id == current.Id);
            bool confirmed = hasConfirmationEvidence && currentSupportsGroup;
            PendingRequirement = confirmed ? PointCloudConfirmationRequirement.None :
                !currentSupportsGroup ? hasMapPose
                    ? GetCurrentDisagreementRequirement(current, stableGroup)
                    : PointCloudConfirmationRequirement.CurrentMatch :
                StableCount < _requiredSamples ? PointCloudConfirmationRequirement.CollectingSamples :
                !evidence.HasStrongMatch ? PointCloudConfirmationRequirement.StrongerMatch :
                !evidence.HasViewDiversity ? PointCloudConfirmationRequirement.DifferentView :
                PointCloudConfirmationRequirement.ObservationDuration;
            int medoidIndex = -1;
            bool hasStablePose = currentSupportsGroup && stableGroup.Count > 0;
            Pose stablePose = hasStablePose
                ? FindMedoid(stableGroup, out medoidIndex)
                : Pose.identity;
            Pose confirmedPose = confirmed ? stablePose : Pose.identity;
            LocalizationGateTrace trace = null;
            if (_captureDiagnostics)
            {
                trace = new LocalizationGateTrace
                {
                    attemptId = attemptId,
                    observationId = current.Id,
                    newObservation = isNewObservation,
                    inputHasMapPose = hasMapPose,
                    inputStrongMatch = isStrongMatch,
                    inputContext = hasContextView,
                    inputMapPose = hasMapPose ? LocalizationDiagnosticFormat.Pose(mapPose) : null,
                    inputCameraPose = LocalizationDiagnosticFormat.Pose(cameraPose),
                    poseAction = poseAction,
                    contextAdded = contextAdded,
                    strongMatchAdded = strongAdded,
                    retained = Snapshots(observedAtSeconds),
                    removed = removed.ToArray(),
                    stableObservationIds = StableIds(stableGroup),
                    medoidObservationId = medoidIndex < 0 ? 0 : _observations[medoidIndex].Id,
                    confirmed = confirmed,
                    hasStrongMatch = evidence.HasStrongMatch,
                    hasContext = evidence.HasContextView,
                    positionSpan = evidence.PositionSpan,
                    rotationSpan = evidence.RotationSpan,
                    durationSeconds = evidence.DurationSeconds,
                    strongMatchCount = evidence.StrongMatchCount,
                    contextMatchCount = evidence.ContextMatchCount,
                    currentSupportsGroup = currentSupportsGroup,
                    confirmationPath = !confirmed ? "blocked" :
                        UsesTemporalObservations ? "temporal_consistency" :
                        evidence.HasContextView ? "context_and_rotation" : "translation_and_rotation",
                    blockReason = !currentSupportsGroup ? "current_result_not_in_stable_group" :
                        StableCount < _requiredSamples ? "insufficient_stable_samples" :
                        !evidence.HasStrongMatch ? "insufficient_strong_matches" :
                        !evidence.HasViewDiversity ? "insufficient_view_diversity" :
                        evidence.DurationSeconds < _minimumConfirmationSeconds ? "insufficient_confirmation_duration" : null
                };
                if (hasMapPose) RecordCurrentDisagreement(current, stableGroup, trace);
            }
            return new LocalizationGateResult(
                isNewObservation,
                confirmed,
                confirmedPose,
                hasMapPose,
                hasMapPose ? mapPose : Pose.identity,
                hasStablePose,
                stablePose,
                StableCount,
                ObservationCount,
                poseCount,
                poseCount - StableCount,
                nearestPositionDelta,
                nearestRotationDelta,
                nearestSignatureDistance,
                evidence.HasStrongMatch,
                evidence.HasViewDiversity,
                evidence.HasContextView,
                evidence.PositionSpan,
                evidence.RotationSpan,
                trace);
        }

        internal void Reset()
        {
            _observations.Clear();
            StableCount = 0;
            TotalDistinctObservationCount = 0;
            _lastAcceptedAtSeconds = double.NegativeInfinity;
            _lastObservedAtSeconds = double.NegativeInfinity;
            LastExpiryTrace = null;
            PendingRequirement = PointCloudConfirmationRequirement.CollectingSamples;
        }

        private int FindEquivalentObservation(
            Pose cameraPose,
            ulong frameSignature,
            long attemptId,
            double observedAtSeconds,
            out float nearestPositionDelta,
            out float nearestRotationDelta,
            out int nearestSignatureDistance)
        {
            int equivalentIndex = -1;
            float bestSimilarity = float.PositiveInfinity;
            nearestPositionDelta = float.PositiveInfinity;
            nearestRotationDelta = float.PositiveInfinity;
            nearestSignatureDistance = 64;

            for (int i = 0; i < _observations.Count; i++)
            {
                Observation existing = _observations[i];
                float positionDelta = Vector3.Distance(
                    cameraPose.position,
                    existing.CameraPose.position);
                float rotationDelta = Quaternion.Angle(
                    cameraPose.rotation,
                    existing.CameraPose.rotation);
                int signatureDistance = HammingDistance(
                    frameSignature,
                    existing.FrameSignature);
                bool geometricChange =
                    positionDelta >= _minimumViewPositionDelta ||
                    rotationDelta >= _minimumViewRotationDelta;

                if (positionDelta < nearestPositionDelta)
                    nearestPositionDelta = positionDelta;
                if (rotationDelta < nearestRotationDelta)
                    nearestRotationDelta = rotationDelta;
                if (signatureDistance < nearestSignatureDistance)
                    nearestSignatureDistance = signatureDistance;

                // Maintenance counts separated captures, not new viewpoints.
                // Repeated attempt IDs or bursts must not create extra votes.
                // The caller still has to reject repeated physical camera frames.
                if (UsesTemporalObservations)
                {
                    if (attemptId <= existing.LastAttemptId ||
                        observedAtSeconds - existing.CreatedAtSeconds <
                        _minimumIndependentObservationSeconds)
                        equivalentIndex = i;
                    continue;
                }

                // Similar-looking distant views must not overwrite each other.
                // Image diversity is still checked pairwise before confirmation.
                if (geometricChange)
                    continue;

                float similarity = SimilarityScore(
                    positionDelta,
                    rotationDelta,
                    signatureDistance);
                if (similarity < bestSimilarity)
                {
                    bestSimilarity = similarity;
                    equivalentIndex = i;
                }
            }

            return equivalentIndex;
        }

        private float SimilarityScore(
            float positionDelta,
            float rotationDelta,
            int signatureDistance)
        {
            float position = _minimumViewPositionDelta > 0f
                ? positionDelta / _minimumViewPositionDelta
                : positionDelta;
            float rotation = _minimumViewRotationDelta > 0f
                ? rotationDelta / _minimumViewRotationDelta
                : rotationDelta;
            float signature = _minimumSignatureDistance > 0
                ? (float)signatureDistance / _minimumSignatureDistance
                : signatureDistance;
            return Mathf.Min(position, rotation) + signature;
        }

        private List<int> FindBestStableGroup(out int poseCount, long requiredObservationId = 0,
            bool requireConfirmation = false)
        {
            List<int> poseIndices = new List<int>();
            for (int i = 0; i < _observations.Count; i++)
            {
                if (_observations[i].HasMapPose)
                    poseIndices.Add(i);
            }

            poseCount = poseIndices.Count;
            List<int> best = new List<int>();
            float bestDistance = float.PositiveInfinity;
            int subsetCount = 1 << poseIndices.Count;
            for (int mask = 1; mask < subsetCount; mask++)
            {
                int memberCount = CountBits(mask);
                if (memberCount < best.Count || requireConfirmation && memberCount < _requiredSamples)
                    continue;

                List<int> candidate = BuildSubset(poseIndices, mask);
                if (requiredObservationId != 0 &&
                    !candidate.Exists(i => _observations[i].Id == requiredObservationId))
                    continue;
                if (!IsPairwiseStable(candidate))
                    continue;
                if (requireConfirmation && !HasConfirmationEvidence(candidate, out _))
                    continue;

                float distance = TotalPairwiseDistance(candidate);
                if (memberCount > best.Count || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private List<LocalizationObservationTrace> TrimObservationWindow(double now)
        {
            var removed = _captureDiagnostics ? new List<LocalizationObservationTrace>() : null;
            while (_observations.Count > _maximumObservations)
            {
                int removeIndex = _observations.FindIndex(item => !item.HasMapPose);
                if (removeIndex < 0) removeIndex = 0;
                if (_captureDiagnostics)
                {
                    var snapshot = Snapshot(_observations[removeIndex], now);
                    snapshot.removalReason = !_observations[removeIndex].HasMapPose
                        ? "window_removes_unmatched_observation" : "window_removes_oldest_observation";
                    removed.Add(snapshot);
                }
                _observations.RemoveAt(removeIndex);
            }
            return removed;
        }

        private struct ConfirmationEvidence
        {
            internal int StrongMatchCount, ContextMatchCount;
            internal float PositionSpan, RotationSpan;
            internal double DurationSeconds;
            internal bool HasStrongMatch, HasContextView, HasViewDiversity;
        }

        private bool HasConfirmationEvidence(IReadOnlyList<int> indices, out ConfirmationEvidence evidence)
        {
            evidence = default;
            double firstAt = double.PositiveInfinity, lastAt = double.NegativeInfinity;

            for (int i = 0; i < indices.Count; i++)
            {
                Observation first = _observations[indices[i]];
                if (first.HasStrongMatch) evidence.StrongMatchCount++;
                if (first.HasContextView) evidence.ContextMatchCount++;
                firstAt = Math.Min(firstAt, first.PoseAtSeconds);
                lastAt = Math.Max(lastAt, first.PoseAtSeconds);
                for (int j = i + 1; j < indices.Count; j++)
                {
                    Observation second = _observations[indices[j]];
                    evidence.PositionSpan = Mathf.Max(
                        evidence.PositionSpan,
                        Vector3.Distance(
                            first.PoseCameraPose.position,
                            second.PoseCameraPose.position));
                    evidence.RotationSpan = Mathf.Max(
                        evidence.RotationSpan,
                        Quaternion.Angle(
                            first.PoseCameraPose.rotation,
                            second.PoseCameraPose.rotation));
                }
            }
            evidence.DurationSeconds = indices.Count == 0 ? 0 : lastAt - firstAt;
            evidence.HasStrongMatch = evidence.StrongMatchCount >= _minimumStrongMatches;
            evidence.HasContextView = evidence.ContextMatchCount >= _minimumContextMatches;
            evidence.HasViewDiversity = UsesTemporalObservations ||
                evidence.RotationSpan >= _minimumConfirmationRotationSpan &&
                (evidence.PositionSpan >= _minimumConfirmationPositionSpan || evidence.HasContextView);
            return indices.Count >= _requiredSamples && evidence.HasStrongMatch &&
                evidence.HasViewDiversity && evidence.DurationSeconds >= _minimumConfirmationSeconds;
        }

        private bool IsPairwiseStable(IReadOnlyList<int> indices)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    Observation a = _observations[indices[i]], b = _observations[indices[j]];
                    // Replacement must not let two votes collapse onto one view.
                    if (!HaveDistinctCameraGeometry(a, b) || HammingDistance(a.PoseFrameSignature, b.PoseFrameSignature) < _minimumSignatureDistance)
                        return false;
                    if (LocalPositionDrift(a, b) > _maximumPosePositionDrift)
                    {
                        return false;
                    }

                    if (Quaternion.Angle(a.MapPose.rotation, b.MapPose.rotation) >
                        _maximumPoseRotationDrift)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Pose FindMedoid(IReadOnlyList<int> indices, out int selectedIndex)
        {
            int bestIndex = indices[0];
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < indices.Count; i++)
            {
                int candidateIndex = indices[i];
                float distance = 0f;
                for (int j = 0; j < indices.Count; j++)
                {
                    distance += PoseDistance(
                        _observations[candidateIndex],
                        _observations[indices[j]]);
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = candidateIndex;
                }
            }

            selectedIndex = bestIndex;
            return _observations[bestIndex].MapPose;
        }

        private float TotalPairwiseDistance(IReadOnlyList<int> indices)
        {
            float distance = 0f;
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    distance += PoseDistance(
                        _observations[indices[i]],
                        _observations[indices[j]]);
                }
            }

            return distance;
        }

        private float PoseDistance(Observation first, Observation second)
        {
            float position = _maximumPosePositionDrift > 0f
                ? LocalPositionDrift(first, second) /
                  _maximumPosePositionDrift
                : 0f;
            float rotation = _maximumPoseRotationDrift > 0f
                ? Quaternion.Angle(first.MapPose.rotation, second.MapPose.rotation) /
                  _maximumPoseRotationDrift
                : 0f;
            return position + rotation;
        }

        private bool HaveDistinctCameraGeometry(Observation a, Observation b) =>
            Vector3.Distance(a.PoseCameraPose.position, b.PoseCameraPose.position) >= _minimumViewPositionDelta ||
            Quaternion.Angle(a.PoseCameraPose.rotation, b.PoseCameraPose.rotation) >= _minimumViewRotationDelta;

        // MapPose maps map coordinates into tracking coordinates. Compare the
        // SAME tracking point under both inverse transforms, at BOTH observed
        // camera positions. This removes the arbitrary map-origin lever arm,
        // without counting camera travel as error or canceling endpoint errors.
        private static float LocalPositionDrift(Observation a, Observation b)
        {
            Quaternion inverseA = Quaternion.Inverse(a.MapPose.rotation);
            Quaternion inverseB = Quaternion.Inverse(b.MapPose.rotation);
            Vector3 first = a.PoseCameraPose.position;
            Vector3 second = b.PoseCameraPose.position;
            return Mathf.Max(
                Vector3.Distance(inverseA * (first - a.MapPose.position), inverseB * (first - b.MapPose.position)),
                Vector3.Distance(inverseA * (second - a.MapPose.position), inverseB * (second - b.MapPose.position)));
        }

        private void RecordCurrentDisagreement(Observation current, IReadOnlyList<int> group, LocalizationGateTrace trace)
        {
            foreach (int index in group)
            {
                Observation other = _observations[index];
                if (other.Id == current.Id) continue;
                float localPosition = LocalPositionDrift(current, other);
                float rotation = Quaternion.Angle(current.MapPose.rotation, other.MapPose.rotation);
                trace.currentMaxLocalPositionDelta = Mathf.Max(trace.currentMaxLocalPositionDelta, localPosition);
                trace.currentMaxOriginPositionDelta = Mathf.Max(trace.currentMaxOriginPositionDelta,
                    Vector3.Distance(current.MapPose.position, other.MapPose.position));
                trace.currentMaxRotationDelta = Mathf.Max(trace.currentMaxRotationDelta, rotation);
                if (!HaveDistinctCameraGeometry(current, other)) trace.currentViewConflicts++;
                if (HammingDistance(current.PoseFrameSignature, other.PoseFrameSignature) < _minimumSignatureDistance)
                    trace.currentImageConflicts++;
                if (localPosition > _maximumPosePositionDrift) trace.currentPositionConflicts++;
                if (rotation > _maximumPoseRotationDrift) trace.currentRotationConflicts++;
            }
        }

        private PointCloudConfirmationRequirement GetCurrentDisagreementRequirement(
            Observation current, IReadOnlyList<int> group)
        {
            bool distinct = true;
            foreach (int index in group)
            {
                Observation other = _observations[index];
                if (LocalPositionDrift(current, other) > _maximumPosePositionDrift ||
                    Quaternion.Angle(current.MapPose.rotation, other.MapPose.rotation) > _maximumPoseRotationDrift)
                    return PointCloudConfirmationRequirement.PoseDisagreement;
                distinct &= HaveDistinctCameraGeometry(current, other) &&
                    HammingDistance(current.PoseFrameSignature, other.PoseFrameSignature) >= _minimumSignatureDistance;
            }
            return distinct ? PointCloudConfirmationRequirement.CurrentMatch : PointCloudConfirmationRequirement.DifferentView;
        }

        private static List<int> BuildSubset(
            IReadOnlyList<int> indices,
            int mask)
        {
            List<int> subset = new List<int>();
            for (int i = 0; i < indices.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                    subset.Add(indices[i]);
            }

            return subset;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }

        private static int HammingDistance(ulong first, ulong second)
        {
            ulong value = first ^ second;
            int count = 0;
            while (value != 0UL)
            {
                value &= value - 1UL;
                count++;
            }

            return count;
        }

        private struct Observation
        {
            internal Observation(
                Pose cameraPose,
                ulong frameSignature)
            {
                this = default;
                CameraPose = cameraPose;
                FrameSignature = frameSignature;
                HasMapPose = false;
                MapPose = Pose.identity;
                QualityScore = float.NegativeInfinity;
                HasStrongMatch = false;
                HasContextView = false;
            }

            internal Pose CameraPose;
            internal ulong FrameSignature;
            internal bool HasMapPose;
            internal Pose MapPose;
            internal float QualityScore;
            internal bool HasStrongMatch;
            internal bool HasContextView;
            // Pose time/camera/signature are evidence; IDs retain diagnostic provenance.
            internal long Id, CreatedAttemptId, LastAttemptId, PoseAttemptId;
            internal long ContextAttemptId, StrongMatchAttemptId;
            internal double CreatedAtSeconds, PoseAtSeconds;
            internal bool ContextHadMapPose;
            internal Pose PoseCameraPose, ContextCameraPose;
            internal ulong PoseFrameSignature;
        }

        private LocalizationObservationTrace[] Snapshots(double now)
        {
            var result = new LocalizationObservationTrace[_observations.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Snapshot(_observations[i], now);
            return result;
        }

        private long[] StableIds(List<int> group)
        {
            var result = new long[group.Count];
            for (int i = 0; i < result.Length; i++) result[i] = _observations[group[i]].Id;
            return result;
        }

        private static LocalizationObservationTrace Snapshot(Observation value, double now)
        {
            return new LocalizationObservationTrace
            {
                id = value.Id, createdAttemptId = value.CreatedAttemptId,
                lastAttemptId = value.LastAttemptId, poseAttemptId = value.PoseAttemptId,
                contextAttemptId = value.ContextAttemptId, strongMatchAttemptId = value.StrongMatchAttemptId,
                ageSeconds = Math.Max(0d, now - value.CreatedAtSeconds),
                poseAgeSeconds = value.HasMapPose ? Math.Max(0d, now - value.PoseAtSeconds) : -1d,
                hasMapPose = value.HasMapPose, hasContext = value.HasContextView,
                hasStrongMatch = value.HasStrongMatch, contextHadMapPose = value.ContextHadMapPose,
                qualityScore = LocalizationDiagnosticFormat.Number(value.QualityScore),
                cameraPose = LocalizationDiagnosticFormat.Pose(value.CameraPose),
                frameSignature = value.FrameSignature.ToString("X16"),
                mapPose = value.HasMapPose ? LocalizationDiagnosticFormat.Pose(value.MapPose) : null,
                poseCameraPose = value.HasMapPose ? LocalizationDiagnosticFormat.Pose(value.PoseCameraPose) : null,
                poseFrameSignature = value.HasMapPose ? value.PoseFrameSignature.ToString("X16") : null,
                contextCameraPose = value.HasContextView ? LocalizationDiagnosticFormat.Pose(value.ContextCameraPose) : null
            };
        }
    }

    [Serializable]
    internal sealed class LocalizationGateTrace
    {
        public long attemptId, observationId, medoidObservationId, sourcePoseRevision;
        public bool newObservation, inputHasMapPose, inputStrongMatch, inputContext;
        public bool contextAdded, strongMatchAdded, confirmed, hasStrongMatch, hasContext;
        public float positionSpan, rotationSpan;
        public int strongMatchCount, contextMatchCount;
        public double durationSeconds;
        public bool currentSupportsGroup;
        // Current observation versus the selected stable group; categories may overlap.
        public int currentViewConflicts, currentImageConflicts, currentPositionConflicts, currentRotationConflicts;
        public float currentMaxLocalPositionDelta, currentMaxOriginPositionDelta, currentMaxRotationDelta;
        public string inputMapPose, inputCameraPose, poseAction, confirmationPath, blockReason;
        public long[] stableObservationIds;
        public LocalizationObservationTrace[] retained, removed;
    }

    [Serializable]
    internal sealed class LocalizationObservationTrace
    {
        public long id, createdAttemptId, lastAttemptId, poseAttemptId, contextAttemptId, strongMatchAttemptId;
        public double ageSeconds, poseAgeSeconds;
        public bool hasMapPose, hasContext, hasStrongMatch, contextHadMapPose;
        public string qualityScore, cameraPose, frameSignature, mapPose, poseCameraPose, poseFrameSignature;
        public string contextCameraPose, removalReason;
    }

    internal static class LocalizationDiagnosticFormat
    {
        internal static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        internal static string Pose(Pose value) =>
            $"p=({Number(value.position.x)},{Number(value.position.y)},{Number(value.position.z)});" +
            $"q=({Number(value.rotation.x)},{Number(value.rotation.y)},{Number(value.rotation.z)},{Number(value.rotation.w)})";
    }
}
