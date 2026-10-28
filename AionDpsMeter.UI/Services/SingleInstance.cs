using System.Runtime.InteropServices;

namespace AionDpsMeter.UI.Services
{
    /// <summary>
    /// Keeps the meter to one running copy per Windows session: two copies would capture the same packets
    /// and fight over the same history database. A second launch tells the running copy to show itself,
    /// then quits. The updater waits for the old process to exit before starting the new one, so updates
    /// aren't affected.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        // "Local\" = this Windows session, so another user on the same PC can still run their own.
        private const string MutexName = @"Local\AionDpsMeter.SingleInstance";
        private const string ShowEventName = @"Local\AionDpsMeter.ShowExisting";
        private const int ASFW_ANY = -1;

        private readonly Mutex mutex;
        private readonly EventWaitHandle showEvent;
        private readonly bool owned;
        private RegisteredWaitHandle? registration;

        private SingleInstance()
        {
            mutex = new Mutex(false, MutexName);
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous copy crashed without letting go; it's ours now.
                owned = true;
            }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }

        /// <summary>
        /// Returns the guard for the first copy, or null after asking the running copy to show itself
        /// (the caller should then exit).
        /// </summary>
        public static SingleInstance? TryAcquire()
        {
            var instance = new SingleInstance();
            if (instance.owned) return instance;

            // Let the running copy take the foreground; Windows only allows that for the active process.
            AllowSetForegroundWindow(ASFW_ANY);
            instance.showEvent.Set();
            instance.Dispose();
            return null;
        }

        /// <summary>Calls <paramref name="onShowRequested"/> (on a thread-pool thread) whenever another copy is launched.</summary>
        public void ListenForShowRequests(Action onShowRequested)
        {
            registration = ThreadPool.RegisterWaitForSingleObject(
                showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public void Dispose()
        {
            registration?.Unregister(null);
            if (owned)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
            mutex.Dispose();
            showEvent.Dispose();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
    }
}
