namespace Bujiaban.PointCloud
{
    /// <summary>
    /// Vendor-neutral stages reported while one localization call is running.
    /// </summary>
    public enum PointCloudLocalizationStage
    {
        Searching = 0,
        Confirming = 1,
        NeedMoreVisualDetail = 2,
        Tracking = 3,
        TrackingLost = 4,
        Reacquiring = 5
    }

    /// <summary>The remaining confirmation requirement, independent of sample count.</summary>
    public enum PointCloudConfirmationRequirement
    {
        None = 0,
        CollectingSamples = 1,
        StrongerMatch = 2,
        DifferentView = 3,
        ObservationDuration = 4,
        CurrentMatch = 5,
        EvidenceExpired = 6,
        TrackingRecovery = 7,
        NoMapMatch = 8,
        InsufficientVisualDetail = 9,
        PoseDisagreement = 10,
        CameraUnavailable = 11
    }

    /// <summary>
    /// Operation-scoped guidance and tracking state. Searching, Confirming and
    /// NeedMoreVisualDetail are diagnostic hints; Tracking confirms a verified
    /// pose, while TrackingLost and Reacquiring mark the previous pose unverified.
    /// </summary>
    public readonly struct PointCloudLocalizationProgress
    {
        public PointCloudLocalizationProgress(
            PointCloudLocalizationStage stage,
            int attemptCount,
            int stableSampleCount,
            int requiredStableSampleCount,
            double lastVerifiedAtSeconds = -1d,
            PointCloudConfirmationRequirement confirmationRequirement = PointCloudConfirmationRequirement.None,
            bool isConfirmed = false)
        {
            Stage = stage;
            AttemptCount = attemptCount < 0 ? 0 : attemptCount;
            StableSampleCount = stableSampleCount < 0 ? 0 : stableSampleCount;
            RequiredStableSampleCount =
                requiredStableSampleCount < 0 ? 0 : requiredStableSampleCount;
            LastVerifiedAtSeconds = lastVerifiedAtSeconds >= 0d &&
                !double.IsInfinity(lastVerifiedAtSeconds) ? lastVerifiedAtSeconds : -1d;
            ConfirmationRequirement = confirmationRequirement;
            IsConfirmed = isConfirmed;
        }

        public PointCloudLocalizationStage Stage { get; }
        public int AttemptCount { get; }
        /// <summary>Actual retained observations; reaching or exceeding the target does not confirm a pose.</summary>
        public int StableSampleCount { get; }
        public int RequiredStableSampleCount { get; }
        public PointCloudConfirmationRequirement ConfirmationRequirement { get; }
        /// <summary>
        /// Evidence was confirmed when this report was emitted. It does not mean a pose
        /// has been applied: cancellation or tracking reset can still invalidate it.
        /// Tracking reports may describe a verified pose while a new gate has zero votes.
        /// </summary>
        public bool IsConfirmed { get; }
        /// <summary>
        /// Last accepted map verification in Unity realtime seconds, or -1 if
        /// never verified. A recorded time does not imply the pose is still valid.
        /// </summary>
        public double LastVerifiedAtSeconds { get; }
    }
}
