namespace PoolScoreboard.Controller;

/// <summary>
/// Keeps one Controller running per desktop session. Two copies share <c>%AppData%\PoolScoreboard</c> — the
/// settings and roster files are saved last-writer-wins — and both want 127.0.0.1:51234, where only one can
/// host the overlay. A second launch therefore asks the running instance to come to the front and exits,
/// which is what the operator meant by launching it again.
/// <para>
/// Election is a named mutex: whoever creates the object is the running instance, and the object disappears
/// with its last handle, so a crashed Controller leaves no stale lock behind. Activation is a named auto-reset
/// event that the running instance waits on and the duplicate sets.
/// </para>
/// <para>
/// Names are session-scoped (<c>Local\</c>): another Windows session cannot raise this one's window, and the
/// app is a single-operator console.
/// </para>
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    public const string DefaultMutexName = @"Local\PoolScoreboard.Controller";
    public const string DefaultActivationName = @"Local\PoolScoreboard.Controller.Activate";

    /// <summary>
    /// How often a duplicate looks for the running instance's event. The running instance creates it moments
    /// after the mutex, so this only covers that window, not a long wait.
    /// </summary>
    private const int SignalAttempts = 5;

    private static readonly TimeSpan SignalRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activation, Action activated)
    {
        _mutex = mutex;
        _activation = activation;

        // The callback runs on a thread-pool thread; the caller marshals to the UI itself.
        ActivationWait = ThreadPool.RegisterWaitForSingleObject(
            activation,
            (_, _) => activated(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private RegisteredWaitHandle ActivationWait { get; }

    /// <summary>
    /// Returns the guard when this is the only Controller running, or null after asking the running instance to
    /// come to the front. <paramref name="activated"/> runs on a thread-pool thread.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string mutexName, string activationName, Action activated)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName, out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            mutex.Dispose();
            SignalRunningInstance(activationName);
            return null;
        }

        var activation = new EventWaitHandle(false, EventResetMode.AutoReset, activationName);
        return new SingleInstanceGuard(mutex, activation, activated);
    }

    private static void SignalRunningInstance(string activationName)
    {
        for (var attempt = 0; attempt < SignalAttempts; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(activationName);
                activation.Set();
                return;
            }
            catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
                                                 or UnauthorizedAccessException)
            {
                // The other instance has the mutex but may not have created its event yet.
                Thread.Sleep(SignalRetryDelay);
            }
        }

        // Nothing to raise: the duplicate still exits, and its own launch is already in the log.
    }

    public void Dispose()
    {
        ActivationWait.Unregister(null);
        _activation.Dispose();
        _mutex.Dispose();
    }
}
