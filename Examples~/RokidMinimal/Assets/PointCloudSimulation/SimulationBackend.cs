#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bujiaban.PointCloud.Immersal;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    // Only exists in Editor. It never registers the real "immersal" map type.
    public sealed class SimulationBackend : PointCloudBackend
    {
        internal ImmersalLocalizationProfile Profile;
        internal Scenario SelectedScenario;
        internal DriftScenario? ExperimentScenario;
        internal bool Paused;
        internal SimulationModel Model { get; private set; }
        internal float Speed = 1;
        internal int Started { get; private set; }
        internal int Released { get; private set; }
        internal int Concurrent { get; private set; }
        internal int MaximumConcurrent { get; private set; }

        protected override bool SupportsMapType(string mapType) => mapType == "simulation";

        protected override Task<Pose> LocalizeCoreAsync(string mapType, Stream mapZip,
            CancellationToken token, IProgress<PointCloudLocalizationProgress> progress) =>
            Task.FromException<Pose>(new NotSupportedException("This scene tests continuous localization."));

        protected override async Task TrackCoreAsync(string mapType, Stream mapZip,
            Action<Pose> onPose, CancellationToken token,
            IProgress<PointCloudLocalizationProgress> progress)
        {
            token.ThrowIfCancellationRequested();
            var experiment = ExperimentScenario.HasValue ? new CorrectionExperiment(ExperimentScenario.Value) : null;
            var model = new SimulationModel(Profile, experiment) { Scenario = SelectedScenario };
            Model = model;
            model.OnPose += onPose;
            Started++;
            Concurrent++;
            MaximumConcurrent = Math.Max(MaximumConcurrent, Concurrent);
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                model.OnPose -= onPose;
                model.Stop();
                Released++;
                Concurrent--;
            }
        }

        private void Update()
        {
            if (!Paused) Model?.Step(Time.unscaledDeltaTime * Speed);
        }
    }
}
#endif
