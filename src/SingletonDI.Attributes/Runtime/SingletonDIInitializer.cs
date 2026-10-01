using System;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

/// <summary>
/// Coordinates process-wide provider initialization and disposal in the <c>SingletonDI.Generated</c> namespace.
/// </summary>
public static class SingletonDIInitializer
{
    private static readonly ServiceRegistry Registry = new();
    private static readonly object Sync = new();
    private static IShutdownSignalSource _signalSource = PlatformShutdownSignalSource.Instance;
    private static ShutdownManager? _shutdownManager;
    private static long _lifecycleVersion;

    /// <summary>
    /// Initializes all registered providers asynchronously.
    /// </summary>
    /// <param name="registerShutdownHandlers">
    /// Whether process-exit, console-cancel, and supported POSIX signal handlers should be registered.
    /// When enabled, handled signals cancel default termination, wait for one bounded disposal operation,
    /// and terminate with exit code 130 for SIGINT, 143 for SIGTERM, or 131 for SIGQUIT. A second handled
    /// signal terminates immediately using the first signal's exit code. Subscribing the handlers can fail
    /// on a host that does not allow it, which does not fail initialization; the container stays usable and
    /// shutdown then depends on an explicit <see cref="DisposeAsync"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops a request that is still waiting. A token that is already cancelled is rejected before
    /// any provider is created. The token bounds the caller's wait, not the work a provider does: a
    /// provider initializer takes no token, so cancelling cannot interrupt one that has already
    /// started.
    /// </param>
    /// <returns>
    /// A task that completes after every provider instance has been created and every
    /// <c>InitializeAsync</c> method returning <see cref="Task"/> or <see cref="ValueTask"/> has completed.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The container has already been initialized, an initialization is already in progress, or an
    /// earlier initialization failed and cannot be retried. A singleton exists for the lifetime of
    /// the application, so the first call is the only one. The rejection is thrown by the call
    /// itself rather than delivered through the returned task. The method also throws when it is
    /// called reentrantly from a provider factory, initializer, or disposer.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before the request was accepted.
    /// </exception>
    public static Task InitializeAsync(
        bool registerShutdownHandlers = true,
        CancellationToken cancellationToken = default)
    {
        long lifecycleVersion;

        lock (Sync)
        {
            lifecycleVersion = _lifecycleVersion;
        }

        // Deliberately outside the lock. This call creates every provider instance and runs the
        // synchronous prefix of every initializer, so holding a process-wide lock across it would
        // block every other lifecycle call in the process for the duration, stall the main thread
        // inside AppDomain.ProcessExit, and deadlock against a factory that starts a thread which
        // does not flow the execution context. The registry serializes the start on its own state,
        // and rejects every repeat at the call site.
        var initializationTask = Registry.InitializeAsync(cancellationToken);

        if (!registerShutdownHandlers)
        {
            return initializationTask;
        }

        lock (Sync)
        {
            if (lifecycleVersion == _lifecycleVersion)
            {
                RegisterShutdownHandlers();
            }
        }

        if (initializationTask.Status == TaskStatus.RanToCompletion)
        {
            return initializationTask;
        }

        return RegisterShutdownHandlersAfterInitializationAsync(initializationTask, lifecycleVersion);
    }

    /// <summary>
    /// Disposes all initialized providers asynchronously.
    /// </summary>
    /// <param name="cancellationToken">
    /// Bounds how long the caller waits for an initialization that is still in progress, so a
    /// provider initializer that never completes does not leave the caller awaiting forever. The
    /// token bounds the wait, not the work: a provider disposer takes no token, so cancelling cannot
    /// interrupt one that has already started. A disposal that is cancelled this way has not run, and
    /// the container stays initialized.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes when each provider has been disposed.
    /// Asynchronous disposal is preferred when both disposal interfaces are implemented.
    /// Repeated and concurrent calls are idempotent. Await this task when cleanup must complete
    /// before process exit; process-exit handling alone is best-effort.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The method is called reentrantly from a provider factory, initializer, or disposer.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled while waiting for an initialization that
    /// had not completed.
    /// </exception>
    public static ValueTask DisposeAsync(CancellationToken cancellationToken = default)
    {
        long lifecycleVersion;

        lock (Sync)
        {
            lifecycleVersion = ++_lifecycleVersion;
        }

        // Outside the lock for the same reason as InitializeAsync: this invokes provider disposers.
        // The registry serializes the disposal on its own state and hands every concurrent caller
        // the same task, so a repeated or parallel disposal still runs exactly once.
        try
        {
            return new ValueTask(CompleteDisposalAsync(
                Registry.DisposeAsync(cancellationToken).AsTask(),
                lifecycleVersion));
        }
        catch (Exception exception)
        {
            // The registry rejects reentrant lifecycle calls synchronously. Fault the task rather
            // than leaving a caller that already took it waiting on a task nothing completes.
            return new ValueTask(Task.FromException(exception));
        }
    }

    internal static void RegisterProvider<TService, TImplementation>(
        Func<TImplementation> factory,
        Type[] dependencyTypes,
        Func<TImplementation, Task>? initializeAsync,
        Action<TImplementation>? dispose,
        Func<TImplementation, Task>? disposeAsync)
        where TImplementation : TService
    {
        Registry.RegisterProvider<TService, TImplementation>(
            factory,
            dependencyTypes,
            initializeAsync,
            dispose,
            disposeAsync);
    }

    internal static void RollbackRegistrations()
    {
        Registry.RollbackRegistrations();
    }

    internal static T Resolve<T>()
    {
        return Registry.Resolve<T>();
    }

    internal static void ResetForTesting()
    {
        lock (Sync)
        {
            _shutdownManager?.Dispose();
            _shutdownManager = null;
        }

        SingletonDIStartupData.ResetForTesting();
        Registry.ResetForTesting();
    }

    private static async Task RegisterShutdownHandlersAfterInitializationAsync(
        Task initializationTask,
        long lifecycleVersion)
    {
        try
        {
            await initializationTask.ConfigureAwait(false);
        }
        catch
        {
            lock (Sync)
            {
                if (lifecycleVersion == _lifecycleVersion)
                {
                    _shutdownManager?.Dispose();
                    _shutdownManager = null;
                }
            }

            throw;
        }

        lock (Sync)
        {
            if (lifecycleVersion == _lifecycleVersion && Registry.IsInitialized)
            {
                RegisterShutdownHandlers();
            }
        }
    }

    private static async Task CompleteDisposalAsync(
        Task registryDisposalTask,
        long lifecycleVersion)
    {
        Exception? exception = null;
        try
        {
            await registryDisposalTask.ConfigureAwait(false);
        }
        catch (Exception disposalException)
        {
            exception = disposalException;
        }

        lock (Sync)
        {
            if (lifecycleVersion == _lifecycleVersion)
            {
                _shutdownManager?.Dispose();
                _shutdownManager = null;
            }
        }

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static void RegisterShutdownHandlers()
    {
        // The graph is already built when the handlers are subscribed, so a subscription failure
        // cannot be reported as a failed initialization without leaving a working container behind
        // a thrown call. Drop the manager so a later request subscribes again, and leave shutdown
        // to the explicit disposal path.
        try
        {
            _shutdownManager ??= new ShutdownManager(
                static () => DisposeAsync(),
                Environment.Exit,
                _signalSource);
            _shutdownManager.Register();
        }
        catch
        {
            _shutdownManager?.Dispose();
            _shutdownManager = null;
        }
    }
}
