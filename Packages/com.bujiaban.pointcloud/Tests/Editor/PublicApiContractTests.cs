using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bujiaban.PointCloud.Tests
{
    public sealed class PublicApiContractTests
    {
        [Test]
        public void RuntimeAssemblyExposesOnlyFacadeProgressExceptionAndBackendSpi()
        {
            Type[] exportedTypes = typeof(PointCloudLocalizer).Assembly
                .GetExportedTypes()
                .ToArray();

            Assert.That(
                exportedTypes,
                Is.EquivalentTo(new[]
                {
                    typeof(PointCloudLocalizer),
                    typeof(PointCloudLocalizationStage),
                    typeof(PointCloudConfirmationRequirement),
                    typeof(PointCloudLocalizationProgress),
                    typeof(PointCloudLocalizationException),
                    typeof(PointCloudBackend)
                }));
        }

        [Test]
        public void ProgressStageValuesRemainBackwardCompatible()
        {
            Assert.That((int)PointCloudLocalizationStage.Searching, Is.Zero);
            Assert.That((int)PointCloudLocalizationStage.Confirming, Is.EqualTo(1));
            Assert.That(
                (int)PointCloudLocalizationStage.NeedMoreVisualDetail,
                Is.EqualTo(2));
            Assert.That((int)PointCloudLocalizationStage.Tracking, Is.EqualTo(3));
            Assert.That((int)PointCloudLocalizationStage.TrackingLost, Is.EqualTo(4));
            Assert.That((int)PointCloudLocalizationStage.Reacquiring, Is.EqualTo(5));
        }

        [Test]
        public void TrackingProgressRetainsVerificationTimeAfterPoseBecomesUnverified()
        {
            var initial = new PointCloudLocalizationProgress(PointCloudLocalizationStage.Searching, 0, 0, 5);
            var tracked = new PointCloudLocalizationProgress(PointCloudLocalizationStage.Tracking, 5, 5, 5, 12.5);
            var lost = new PointCloudLocalizationProgress(PointCloudLocalizationStage.TrackingLost, 6, 0, 5,
                tracked.LastVerifiedAtSeconds);
            Assert.That(initial.LastVerifiedAtSeconds, Is.EqualTo(-1));
            Assert.That(lost.LastVerifiedAtSeconds, Is.EqualTo(12.5));
            Assert.That(lost.Stage, Is.EqualTo(PointCloudLocalizationStage.TrackingLost));
        }

        [Test]
        public void FacadeExposesOnlyOneShotAndContinuousBusinessMethods()
        {
            MethodInfo[] methods = typeof(PointCloudLocalizer).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .ToArray();

            Assert.That(methods.Select(method => method.Name),
                Is.EquivalentTo(new[] { "LocalizeAsync", "TrackAsync" }));
            MethodInfo localize = methods.Single(method => method.Name == "LocalizeAsync");
            Assert.That(localize.ReturnType, Is.EqualTo(typeof(Task<Pose>)));
            ParameterInfo[] parameters = localize.GetParameters();
            Assert.That(
                parameters
                    .Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[]
                {
                    typeof(string),
                    typeof(Stream),
                    typeof(CancellationToken),
                    typeof(IProgress<PointCloudLocalizationProgress>)
                }));
            Assert.That(parameters[2].IsOptional, Is.True);
            Assert.That(parameters[2].HasDefaultValue, Is.True);
            Assert.That(parameters[2].DefaultValue, Is.Null);
            Assert.That(parameters[3].IsOptional, Is.True);
            Assert.That(parameters[3].HasDefaultValue, Is.True);
            Assert.That(parameters[3].DefaultValue, Is.Null);
            MethodInfo track = methods.Single(method => method.Name == "TrackAsync");
            Assert.That(track.ReturnType, Is.EqualTo(typeof(Task)));
            Assert.That(track.GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[]
                {
                    typeof(string), typeof(Stream), typeof(Action<Pose>),
                    typeof(CancellationToken), typeof(IProgress<PointCloudLocalizationProgress>)
                }));
            Assert.That(
                typeof(PointCloudLocalizer).GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
                Is.Empty);
            Assert.That(
                typeof(PointCloudLocalizer).GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
                Is.Empty);
            Assert.That(
                typeof(PointCloudLocalizer).GetEvents(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.DeclaredOnly),
                Is.Empty);
        }

        [Test]
        public void BackendSpiDeclaresNoPublicCallableSurface()
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly;

            Assert.That(typeof(PointCloudBackend).GetMethods(flags), Is.Empty);
            Assert.That(typeof(PointCloudBackend).GetFields(flags), Is.Empty);
            Assert.That(typeof(PointCloudBackend).GetProperties(flags), Is.Empty);
            Assert.That(typeof(PointCloudBackend).GetEvents(flags), Is.Empty);
        }

        [Test]
        public void BackendSpiHasOnlyTheThreeRequiredProtectedHooks()
        {
            MethodInfo[] hooks = typeof(PointCloudBackend).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(method => method.IsFamily && method.IsAbstract)
                .OrderBy(method => method.Name)
                .ToArray();

            Assert.That(hooks, Has.Length.EqualTo(3));
            Assert.That(hooks[0].Name, Is.EqualTo("LocalizeCoreAsync"));
            Assert.That(hooks[0].ReturnType, Is.EqualTo(typeof(Task<Pose>)));
            Assert.That(
                hooks[0].GetParameters()
                    .Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[]
                {
                    typeof(string),
                    typeof(Stream),
                    typeof(CancellationToken),
                    typeof(IProgress<PointCloudLocalizationProgress>)
                }));
            Assert.That(hooks[1].Name, Is.EqualTo("SupportsMapType"));
            Assert.That(hooks[1].ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(
                hooks[1].GetParameters()
                    .Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(string) }));
            Assert.That(hooks[2].Name, Is.EqualTo("TrackCoreAsync"));
            Assert.That(hooks[2].ReturnType, Is.EqualTo(typeof(Task)));
        }

        [Test]
        public void FailureTypeCanOnlyBeCaughtNotConstructedByBusinessCode()
        {
            Assert.That(
                typeof(PointCloudLocalizationException).GetConstructors(),
                Is.Empty);
        }

        [Test]
        public void UnknownMapTypeIsReportedAsLocalizationFailure()
        {
            GameObject gameObject = new GameObject("PointCloudApiTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                using MemoryStream zip = new MemoryStream(new byte[] { 1 });

                LogAssert.Expect(
                    LogType.Error,
                    new Regex("Localization failed for map type 'unknown'"));
                Assert.ThrowsAsync<PointCloudLocalizationException>(async () =>
                    await localizer.LocalizeAsync("unknown", zip));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InvalidInputIsReportedAsLocalizationFailure(bool tracking)
        {
            GameObject gameObject = new GameObject("PointCloudInputTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                Task Start(string mapType, Stream stream) => tracking
                    ? localizer.TrackAsync(mapType, stream, _ => { })
                    : localizer.LocalizeAsync(mapType, stream);

                LogAssert.Expect(
                    LogType.Error,
                    new Regex("Localization failed for map type 'null'"));
                Assert.ThrowsAsync<PointCloudLocalizationException>(async () =>
                    await Start(null, Stream.Null));
                LogAssert.Expect(
                    LogType.Error,
                    new Regex("Localization failed for map type 'immersal'"));
                Assert.ThrowsAsync<PointCloudLocalizationException>(async () =>
                    await Start("immersal", null));
                using MemoryStream closedStream = new MemoryStream();
                closedStream.Dispose();
                LogAssert.Expect(LogType.Error, new Regex("Localization failed for map type 'immersal'"));
                Assert.ThrowsAsync<PointCloudLocalizationException>(async () =>
                    await Start("immersal", closedStream));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public async Task ValidBackendReturnsPoseWithoutClosingCallerStream()
        {
            GameObject gameObject = new GameObject("PointCloudRouteTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                ImmediateBackend backend =
                    gameObject.AddComponent<ImmediateBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream zip = new MemoryStream(new byte[] { 1 });

                Pose pose = await localizer.LocalizeAsync("test", zip);

                Assert.That(pose.position, Is.EqualTo(Vector3.one));
                Assert.That(zip.CanRead, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public async Task ContinuousTrackingReportsInitialPoseAndCancelsWithoutClosingStream()
        {
            GameObject gameObject = new GameObject("PointCloudTrackingApiTest");
            try
            {
                PointCloudLocalizer localizer = gameObject.AddComponent<PointCloudLocalizer>();
                ImmediateBackend backend = gameObject.AddComponent<ImmediateBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream zip = new MemoryStream(new byte[] { 1 });
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                var reported = new TaskCompletionSource<Pose>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                Task tracking = localizer.TrackAsync(
                    "test", zip, pose => reported.TrySetResult(pose), cancellation.Token);
                Pose pose = await reported.Task;
                cancellation.Cancel();

                Assert.That(pose.position, Is.EqualTo(Vector3.one));
                await ExpectCancellationAsync(tracking);
                Assert.That(zip.CanRead, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public async Task FacadePassesOperationProgressToBackend()
        {
            GameObject gameObject = new GameObject("PointCloudProgressTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                ImmediateBackend backend =
                    gameObject.AddComponent<ImmediateBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream zip = new MemoryStream(new byte[] { 1 });
                RecordingProgress progress = new RecordingProgress();

                await localizer.LocalizeAsync(
                    "test",
                    zip,
                    CancellationToken.None,
                    progress);

                Assert.That(progress.ReportCount, Is.EqualTo(1));
                Assert.That(
                    progress.Last.Stage,
                    Is.EqualTo(PointCloudLocalizationStage.Searching));
                Assert.That(progress.Last.AttemptCount, Is.EqualTo(2));
                Assert.That(progress.Last.StableSampleCount, Is.EqualTo(1));
                Assert.That(
                    progress.Last.RequiredStableSampleCount,
                    Is.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public async Task SecondCallCancelsCleansAndThenReplacesFirstCall()
        {
            GameObject gameObject = new GameObject("PointCloudReplaceTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                ReplacingBackend backend =
                    gameObject.AddComponent<ReplacingBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream firstZip = new MemoryStream(new byte[] { 1 });
                using MemoryStream secondZip = new MemoryStream(new byte[] { 2 });

                Task<Pose> first = localizer.LocalizeAsync("test", firstZip);
                await backend.FirstStarted.Task;
                Task<Pose> second = localizer.LocalizeAsync("test", secondZip);

                try
                {
                    await first;
                    Assert.Fail("The replaced operation should be canceled.");
                }
                catch (OperationCanceledException)
                {
                }
                Pose result = await second;

                Assert.That(backend.FirstCleaned, Is.True);
                Assert.That(backend.SecondStartedAfterFirstCleanup, Is.True);
                Assert.That(result.position, Is.EqualTo(Vector3.forward));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public async Task ThrowingCancellationCallbackCannotStrandReplacement()
        {
            GameObject gameObject = new GameObject("PointCloudCancelCallbackTest");
            try
            {
                PointCloudLocalizer localizer =
                    gameObject.AddComponent<PointCloudLocalizer>();
                ThrowingCancellationBackend backend =
                    gameObject.AddComponent<ThrowingCancellationBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream firstZip = new MemoryStream(new byte[] { 1 });
                using MemoryStream secondZip = new MemoryStream(new byte[] { 2 });

                Task<Pose> first = localizer.LocalizeAsync("test", firstZip);
                await backend.FirstStarted.Task;
                LogAssert.Expect(
                    LogType.Error,
                    new Regex("Cancellation callback failed"));

                Task<Pose> second = localizer.LocalizeAsync("test", secondZip);

                try
                {
                    await first;
                    Assert.Fail("The replaced operation should be canceled.");
                }
                catch (OperationCanceledException)
                {
                }
                Pose result = await second;
                Assert.That(result.position, Is.EqualTo(Vector3.up));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacementWaitsForCleanupAndRejectsLateNotifications(bool firstTracks)
        {
            GameObject host = new GameObject("PointCloudScopedCallbacksTest");
            try
            {
                PointCloudLocalizer localizer = host.AddComponent<PointCloudLocalizer>();
                ControlledBackend backend = host.AddComponent<ControlledBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream firstZip = new MemoryStream(new byte[] { 1 });
                using MemoryStream secondZip = new MemoryStream(new byte[] { 2 });
                RecordingProgress firstProgress = new RecordingProgress();
                RecordingProgress secondProgress = new RecordingProgress();
                int firstPoses = 0;
                int secondPoses = 0;
                Task first = firstTracks
                    ? localizer.TrackAsync("test", firstZip, _ => firstPoses++, progress: firstProgress)
                    : localizer.LocalizeAsync("test", firstZip, progress: firstProgress);
                ControlledRequest previous = await backend.Started(0);
                previous.Report();
                previous.OnPose?.Invoke(Pose.identity);
                Assert.That(firstProgress.ReportCount, Is.EqualTo(1), "Forward progress synchronously.");

                Task second = firstTracks
                    ? localizer.LocalizeAsync("test", secondZip, progress: secondProgress)
                    : localizer.TrackAsync("test", secondZip, _ => secondPoses++, progress: secondProgress);
                Assert.That(previous.Token.IsCancellationRequested, Is.True);
                Assert.That(backend.CallCount, Is.EqualTo(1), "Wait for native cleanup before starting replacement.");
                Assert.That(second.IsCompleted, Is.False);
                previous.Report();
                previous.OnPose?.Invoke(Pose.identity);
                Assert.That(firstProgress.ReportCount, Is.EqualTo(1));
                Assert.That(firstPoses, Is.EqualTo(firstTracks ? 1 : 0));

                previous.Completion.TrySetResult(Pose.identity);
                await ExpectCancellationAsync(first);
                ControlledRequest current = await backend.Started(1);
                Assert.That(current.Stream, Is.SameAs(secondZip));
                current.Report();
                current.OnPose?.Invoke(Pose.identity);
                current.Completion.TrySetResult(Pose.identity);
                await second;

                previous.Report();
                previous.OnPose?.Invoke(Pose.identity);
                current.Report();
                current.OnPose?.Invoke(Pose.identity);
                Assert.That(firstProgress.ReportCount, Is.EqualTo(1));
                Assert.That(secondProgress.ReportCount, Is.EqualTo(1), "Completed requests cannot publish progress.");
                Assert.That(firstPoses, Is.EqualTo(firstTracks ? 1 : 0));
                Assert.That(secondPoses, Is.EqualTo(firstTracks ? 0 : 1), "Completed tracking cannot publish poses.");
                Assert.That(firstZip.CanRead && secondZip.CanRead, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public async Task ProgressCallbackCanSynchronouslyReplaceItsRequest()
        {
            GameObject host = new GameObject("PointCloudProgressReentryTest");
            try
            {
                PointCloudLocalizer localizer = host.AddComponent<PointCloudLocalizer>();
                ControlledBackend backend = host.AddComponent<ControlledBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream firstZip = new MemoryStream(new byte[] { 1 });
                using MemoryStream secondZip = new MemoryStream(new byte[] { 2 });
                Task<Pose> second = null;
                var progress = new CallbackProgress(_ => second = localizer.LocalizeAsync("test", secondZip));
                Task<Pose> first = localizer.LocalizeAsync("test", firstZip, progress: progress);
                ControlledRequest previous = await backend.Started(0);

                previous.Report();

                Assert.That(second, Is.Not.Null, "The facade must not enqueue an extra Progress<T> callback.");
                Assert.That(previous.Token.IsCancellationRequested, Is.True);
                Assert.That(backend.CallCount, Is.EqualTo(1));
                previous.Completion.TrySetResult(Pose.identity);
                await ExpectCancellationAsync(first);
                ControlledRequest current = await backend.Started(1);
                current.Completion.TrySetResult(new Pose(Vector3.up, Quaternion.identity));
                Assert.That((await second).position, Is.EqualTo(Vector3.up));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public async Task CancellationReentryCanSupersedeAReplacementWaitingForCleanup()
        {
            GameObject host = new GameObject("PointCloudCancellationReentryTest");
            try
            {
                PointCloudLocalizer localizer = host.AddComponent<PointCloudLocalizer>();
                ControlledBackend backend = host.AddComponent<ControlledBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream firstZip = new MemoryStream(new byte[] { 1 });
                using MemoryStream secondZip = new MemoryStream(new byte[] { 2 });
                using MemoryStream thirdZip = new MemoryStream(new byte[] { 3 });
                Task<Pose> first = localizer.LocalizeAsync("test", firstZip);
                ControlledRequest previous = await backend.Started(0);
                Task<Pose> third = null;
                using CancellationTokenRegistration registration = previous.Token.Register(
                    () => third = localizer.LocalizeAsync("test", thirdZip));

                Task<Pose> second = localizer.LocalizeAsync("test", secondZip);

                Assert.That(third, Is.Not.Null);
                Assert.That(backend.CallCount, Is.EqualTo(1));
                previous.Completion.TrySetResult(Pose.identity);
                await ExpectCancellationAsync(first);
                await ExpectCancellationAsync(second);
                ControlledRequest current = await backend.Started(1);
                Assert.That(current.Stream, Is.SameAs(thirdZip), "The canceled middle request must never enter the backend.");
                current.Completion.TrySetResult(new Pose(Vector3.forward, Quaternion.identity));
                Assert.That((await third).position, Is.EqualTo(Vector3.forward));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public async Task ThrowingPoseListenerDoesNotEndTrackingAndCannotRunAfterCompletion()
        {
            GameObject host = new GameObject("PointCloudPoseCallbackIsolationTest");
            try
            {
                PointCloudLocalizer localizer = host.AddComponent<PointCloudLocalizer>();
                ControlledBackend backend = host.AddComponent<ControlledBackend>();
                AssignBackends(localizer, backend);
                using MemoryStream zip = new MemoryStream(new byte[] { 1 });
                int calls = 0;
                Task tracking = localizer.TrackAsync("test", zip, _ =>
                {
                    calls++;
                    throw new InvalidOperationException("Synthetic pose listener failure.");
                });
                ControlledRequest request = await backend.Started(0);
                LogAssert.Expect(LogType.Error, new Regex("Pose callback failed and was ignored"));

                Assert.DoesNotThrow(() => request.OnPose(Pose.identity));
                Assert.That(tracking.IsCompleted, Is.False);
                request.Completion.TrySetResult(Pose.identity);
                await tracking;
                request.OnPose(Pose.identity);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        private static async Task ExpectCancellationAsync(Task operation)
        {
            // NUnit CatchAsync blocks the Unity thread while continuations need
            // that same thread. Await cleanup before asserting cancellation.
            try
            {
                await operation;
                Assert.Fail("The replaced operation should be canceled.");
            }
            catch (OperationCanceledException) { }
        }

        private static void AssignBackends(
            PointCloudLocalizer localizer,
            params PointCloudBackend[] backends)
        {
            FieldInfo field = typeof(PointCloudLocalizer).GetField(
                "_backends",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(localizer, backends);
        }

        private sealed class ImmediateBackend : PointCloudBackend
        {
            protected override bool SupportsMapType(string mapType)
            {
                return mapType == "test";
            }

            protected override Task<Pose> LocalizeCoreAsync(
                string mapType,
                Stream mapZip,
                CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                progress?.Report(
                    new PointCloudLocalizationProgress(
                        PointCloudLocalizationStage.Searching,
                        attemptCount: 2,
                        stableSampleCount: 1,
                        requiredStableSampleCount: 5));
                return Task.FromResult(
                    new Pose(Vector3.one, Quaternion.identity));
            }

            protected override async Task TrackCoreAsync(string mapType, Stream mapZip,
                Action<Pose> onPose, CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                onPose(await LocalizeCoreAsync(mapType, mapZip, cancellationToken, progress));
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        private sealed class ReplacingBackend : PointCloudBackend
        {
            private int _runCount;

            internal TaskCompletionSource<bool> FirstStarted { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            internal bool FirstCleaned { get; private set; }
            internal bool SecondStartedAfterFirstCleanup { get; private set; }

            protected override bool SupportsMapType(string mapType)
            {
                return mapType == "test";
            }

            protected override async Task<Pose> LocalizeCoreAsync(
                string mapType,
                Stream mapZip,
                CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                int run = ++_runCount;
                if (run == 1)
                {
                    FirstStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    finally
                    {
                        FirstCleaned = true;
                    }
                }

                SecondStartedAfterFirstCleanup = FirstCleaned;
                return new Pose(Vector3.forward, Quaternion.identity);
            }

            protected override async Task TrackCoreAsync(string mapType, Stream mapZip,
                Action<Pose> onPose, CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                onPose(await LocalizeCoreAsync(mapType, mapZip, cancellationToken, progress));
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        private sealed class ThrowingCancellationBackend : PointCloudBackend
        {
            private int _runCount;

            internal TaskCompletionSource<bool> FirstStarted { get; } =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            protected override bool SupportsMapType(string mapType)
            {
                return mapType == "test";
            }

            protected override async Task<Pose> LocalizeCoreAsync(
                string mapType,
                Stream mapZip,
                CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                if (++_runCount == 1)
                {
                    TaskCompletionSource<bool> canceled =
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(
                               () =>
                               {
                                   canceled.TrySetCanceled();
                                   throw new InvalidOperationException(
                                       "Synthetic cancellation callback failure.");
                               }))
                    {
                        FirstStarted.TrySetResult(true);
                        await canceled.Task;
                    }
                }

                return new Pose(Vector3.up, Quaternion.identity);
            }

            protected override async Task TrackCoreAsync(string mapType, Stream mapZip,
                Action<Pose> onPose, CancellationToken cancellationToken,
                IProgress<PointCloudLocalizationProgress> progress)
            {
                onPose(await LocalizeCoreAsync(mapType, mapZip, cancellationToken, progress));
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        private sealed class ControlledRequest
        {
            internal readonly TaskCompletionSource<Pose> Completion = new TaskCompletionSource<Pose>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            internal Stream Stream;
            internal CancellationToken Token;
            internal Action<Pose> OnPose;
            internal IProgress<PointCloudLocalizationProgress> Progress;

            internal void Report() => Progress?.Report(new PointCloudLocalizationProgress(
                PointCloudLocalizationStage.Tracking, 5, 5, 5, 12d));
        }

        private sealed class ControlledBackend : PointCloudBackend
        {
            private readonly List<ControlledRequest> _requests = new List<ControlledRequest>();
            private readonly TaskCompletionSource<ControlledRequest>[] _started =
            {
                new TaskCompletionSource<ControlledRequest>(TaskCreationOptions.RunContinuationsAsynchronously),
                new TaskCompletionSource<ControlledRequest>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            internal int CallCount => _requests.Count;
            internal async Task<ControlledRequest> Started(int index)
            {
                Task<ControlledRequest> pending = _started[index].Task;
                Assert.That(await Task.WhenAny(pending, Task.Delay(2000)), Is.SameAs(pending),
                    "The expected backend request did not start after cleanup.");
                return await pending;
            }
            protected override bool SupportsMapType(string mapType) => mapType == "test";

            protected override Task<Pose> LocalizeCoreAsync(string mapType, Stream mapZip,
                CancellationToken cancellationToken, IProgress<PointCloudLocalizationProgress> progress) =>
                StartRequest(mapZip, cancellationToken, progress, null);

            protected override Task TrackCoreAsync(string mapType, Stream mapZip, Action<Pose> onPose,
                CancellationToken cancellationToken, IProgress<PointCloudLocalizationProgress> progress) =>
                StartRequest(mapZip, cancellationToken, progress, onPose);

            private Task<Pose> StartRequest(Stream stream, CancellationToken token,
                IProgress<PointCloudLocalizationProgress> progress, Action<Pose> onPose)
            {
                var request = new ControlledRequest
                {
                    Stream = stream, Token = token, Progress = progress, OnPose = onPose
                };
                _requests.Add(request);
                _started[_requests.Count - 1].TrySetResult(request);
                return request.Completion.Task;
            }

            private void OnDestroy()
            {
                foreach (ControlledRequest request in _requests)
                    request.Completion.TrySetCanceled();
            }
        }

        private sealed class CallbackProgress : IProgress<PointCloudLocalizationProgress>
        {
            private readonly Action<PointCloudLocalizationProgress> _callback;
            internal CallbackProgress(Action<PointCloudLocalizationProgress> callback) => _callback = callback;
            public void Report(PointCloudLocalizationProgress value) => _callback(value);
        }

        private sealed class RecordingProgress :
            IProgress<PointCloudLocalizationProgress>
        {
            internal int ReportCount { get; private set; }
            internal PointCloudLocalizationProgress Last { get; private set; }

            public void Report(PointCloudLocalizationProgress value)
            {
                ReportCount++;
                Last = value;
            }
        }
    }
}
