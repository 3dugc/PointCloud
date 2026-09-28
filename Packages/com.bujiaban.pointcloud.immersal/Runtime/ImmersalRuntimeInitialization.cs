using System;
using System.Threading.Tasks;

namespace Bujiaban.PointCloud.Immersal
{
    // The backend owns SDK initialization. Every caller awaits the same in-flight
    // task; a failed attempt may be tried again only by a new localization request.
    internal sealed class ImmersalRuntimeInitialization
    {
        private Task _initialization;

        internal Task EnsureReadyAsync(Func<Task> initialize, Func<bool> isReady)
        {
            if (initialize == null) throw new ArgumentNullException(nameof(initialize));
            if (isReady == null) throw new ArgumentNullException(nameof(isReady));
            if (_initialization != null && !_initialization.IsCompleted)
                return _initialization;
            if (isReady())
                return Task.CompletedTask;

            _initialization = InitializeAsync(initialize, isReady);
            return _initialization;
        }

        private static async Task InitializeAsync(Func<Task> initialize, Func<bool> isReady)
        {
            Task task = initialize();
            if (task == null)
                throw new InvalidOperationException("Immersal initialization returned no task.");
            await task;
            if (!isReady())
                throw new InvalidOperationException("Immersal SDK initialization did not complete successfully.");
        }
    }
}
