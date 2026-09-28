using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Immersal.XR;
using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalPlatformCancellationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public async Task OwnedWorkCancellationStopsPreviewBeforeWaitingForLateWork()
        {
            var gameObject = new GameObject("Owned cancellation test");
            var backend = gameObject.AddComponent<ImmersalPointCloudBackend>();
            var platform = new FakePlatform { PreviewOpen = true };
            var lateWork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task operation = null;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    SetField(backend, "_platform", platform);
                    SetField(backend, "_ownsPlatformLifecycle", true);
                    SetField(backend, "_platformConfigured", true);
                    operation = InvokeTask(backend, "AwaitWorkWithOwnedPlatformCancellationAsync",
                        lateWork.Task, cancellation.Token);
                    cancellation.Cancel();

                    await RequireSignal(platform.Stopped.Task);
                    Assert.That(platform.PreviewOpen, Is.False);
                    Assert.That(platform.StopCalls, Is.EqualTo(1));
                    Assert.That(operation.IsCompleted, Is.False,
                        "A replacement must wait until in-flight SDK work has drained.");

                    lateWork.SetResult(true);
                    await RequireCancellation(operation);
                    Assert.That(GetField<bool>(backend, "_platformConfigured"), Is.False);
                }
                finally
                {
                    cancellation.Cancel();
                    lateWork.TrySetCanceled();
                    await Drain(operation);
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }

        [Test]
        public async Task HostOwnedCancellationDrainsWorkWithoutStoppingItsPreview()
        {
            var gameObject = new GameObject("Host cancellation test");
            var backend = gameObject.AddComponent<ImmersalPointCloudBackend>();
            var platform = new FakePlatform { PreviewOpen = true };
            var lateWork = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task operation = null;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    SetField(backend, "_platform", platform);
                    SetField(backend, "_ownsPlatformLifecycle", false);
                    operation = InvokeTask(backend, "AwaitWorkWithOwnedPlatformCancellationAsync",
                        lateWork.Task, cancellation.Token);
                    cancellation.Cancel();

                    Assert.That(operation.IsCompleted, Is.False);
                    Assert.That(platform.PreviewOpen, Is.True);
                    Assert.That(platform.StopCalls, Is.Zero);

                    lateWork.SetResult(true);
                    await RequireCancellation(operation);
                    Assert.That(platform.PreviewOpen, Is.True);
                    Assert.That(platform.StopCalls, Is.Zero);
                }
                finally
                {
                    cancellation.Cancel();
                    lateWork.TrySetCanceled();
                    await Drain(operation);
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }

        [Test]
        public async Task CancellingSecondOwnedConfigureClosesReopenedPreviewAndDrainsConfiguration()
        {
            var gameObject = new GameObject("Repeated configure cancellation test");
            var backend = gameObject.AddComponent<ImmersalPointCloudBackend>();
            var platform = new FakePlatform();
            var pendingConfigure = new TaskCompletionSource<IPlatformConfigureResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task secondOperation = null;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    SetField(backend, "_platform", platform);
                    SetField(backend, "_ownsPlatformLifecycle", true);
                    await InvokeTask(backend, "ConfigurePlatformAsync", CancellationToken.None);
                    Assert.That(platform.ConfigureCalls, Is.EqualTo(1));
                    Assert.That(platform.PreviewOpen, Is.True);

                    var cleanup = (Task<Exception>)InvokeTask(backend, "CleanupAsync", 1);
                    Assert.That(await cleanup, Is.Null);
                    Assert.That(platform.PreviewOpen, Is.False);
                    Assert.That(platform.StopCalls, Is.EqualTo(1));

                    // The second localization uses the ready SDK and calls
                    // ConfigurePlatformAsync directly to re-open the owned camera.
                    platform.NextConfiguration = pendingConfigure.Task;
                    platform.Stopped = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    SetField(backend, "_platform", platform);
                    secondOperation = InvokeTask(backend, "ConfigurePlatformAsync", cancellation.Token);
                    Assert.That(platform.ConfigureCalls, Is.EqualTo(2));
                    Assert.That(platform.PreviewOpen, Is.True);
                    cancellation.Cancel();

                    await RequireSignal(platform.Stopped.Task);
                    Assert.That(platform.PreviewOpen, Is.False);
                    Assert.That(platform.StopCalls, Is.EqualTo(2));
                    Assert.That(secondOperation.IsCompleted, Is.False,
                        "Stopping preview must not let a new localization overtake unfinished configuration.");

                    pendingConfigure.SetCanceled();
                    await RequireCancellation(secondOperation);
                    Assert.That(GetField<bool>(backend, "_platformConfigured"), Is.False);
                }
                finally
                {
                    cancellation.Cancel();
                    pendingConfigure.TrySetCanceled();
                    await Drain(secondOperation);
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }

        private static Task InvokeTask(ImmersalPointCloudBackend backend, string name, params object[] args)
        {
            MethodInfo method = typeof(ImmersalPointCloudBackend).GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, name);
            return (Task)method.Invoke(backend, args);
        }

        private static void SetField(ImmersalPointCloudBackend backend, string name, object value)
        {
            FieldInfo field = typeof(ImmersalPointCloudBackend).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(backend, value);
        }

        private static T GetField<T>(ImmersalPointCloudBackend backend, string name)
        {
            FieldInfo field = typeof(ImmersalPointCloudBackend).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(backend);
        }

        private static async Task RequireSignal(Task signal)
        {
            Task first = await Task.WhenAny(signal, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.That(first, Is.SameAs(signal), "Cancellation did not stop the owned preview promptly.");
            await signal;
        }

        private static async Task RequireCancellation(Task operation)
        {
            try { await operation; }
            catch (OperationCanceledException) { return; }
            Assert.Fail("Expected the localization work to report cancellation.");
        }

        private static async Task Drain(Task operation)
        {
            if (operation == null) return;
            try { await operation; }
            catch { /* The assertion above owns the operation outcome. */ }
        }

        private sealed class FakePlatform : IPlatformSupport
        {
            internal bool PreviewOpen;
            internal int ConfigureCalls;
            internal int StopCalls;
            internal Task<IPlatformConfigureResult> NextConfiguration =
                Task.FromResult<IPlatformConfigureResult>(new SimplePlatformConfigureResult { Success = true });
            internal TaskCompletionSource<bool> Stopped =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<IPlatformConfigureResult> ConfigurePlatform()
            {
                ConfigureCalls++;
                PreviewOpen = true;
                return NextConfiguration;
            }

            public Task<IPlatformConfigureResult> ConfigurePlatform(IPlatformConfiguration configuration) =>
                ConfigurePlatform();

            public Task StopAndCleanUp()
            {
                StopCalls++;
                PreviewOpen = false;
                Stopped.TrySetResult(true);
                // Deliberately leave the SDK/configure task pending. The test
                // controls its completion to prove that the backend drains it.
                return Task.CompletedTask;
            }

            public Task<IPlatformUpdateResult> UpdatePlatform() =>
                throw new NotSupportedException("This test does not acquire camera frames.");

            public Task<IPlatformUpdateResult> UpdatePlatform(IPlatformConfiguration configuration) =>
                UpdatePlatform();
        }
    }
}
