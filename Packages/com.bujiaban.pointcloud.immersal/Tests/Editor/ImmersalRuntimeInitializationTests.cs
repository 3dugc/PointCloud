using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalRuntimeInitializationTests
    {
        [Test]
        public async Task ConcurrentRequestsAwaitOneInitializationAndReuseReadyRuntime()
        {
            var runtime = new ImmersalRuntimeInitialization();
            var pending = new TaskCompletionSource<bool>();
            int starts = 0;
            bool ready = false;
            async Task Initialize()
            {
                starts++;
                await pending.Task;
                ready = true;
            }

            Task first = runtime.EnsureReadyAsync(Initialize, () => ready);
            Task second = runtime.EnsureReadyAsync(Initialize, () => ready);
            Assert.That(second, Is.SameAs(first));
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(first.IsCompleted, Is.False);
            pending.SetResult(true);
            await Task.WhenAll(first, second);
            await runtime.EnsureReadyAsync(Initialize, () => ready);
            Assert.That(starts, Is.EqualTo(1));
        }

        [Test]
        public async Task CompletedButNotReadyInitializationFailsWithoutAutomaticRetry()
        {
            var runtime = new ImmersalRuntimeInitialization();
            int starts = 0;
            bool ready = false;
            Task Initialize() { starts++; return Task.CompletedTask; }

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.EnsureReadyAsync(Initialize, () => ready));
            Assert.That(starts, Is.EqualTo(1));
            // An explicit new request can initialize again after a previous failure.
            await runtime.EnsureReadyAsync(() =>
            {
                starts++;
                ready = true;
                return Task.CompletedTask;
            }, () => ready);
            Assert.That(starts, Is.EqualTo(2));
        }

        [Test]
        public async Task AlreadyReadyRuntimeDoesNotConfigureTheCameraAgain()
        {
            var runtime = new ImmersalRuntimeInitialization();
            await runtime.EnsureReadyAsync(
                () => throw new InvalidOperationException("Must not initialize twice"),
                () => true);
        }
    }
}
