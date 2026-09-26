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
    private static Task? _disposeTask;
    private static QueuedInitialization? _queuedInitialization;
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
    /// <returns>
    /// A task that completes after every provider instance has been created and every
    /// <c>InitializeAsync</c> method returning <see cref="Task"/> or <see cref="ValueTask"/> has completed.
    /// A request queued behind an active disposal fails with <see cref="InvalidOperationException"/>
    /// when a later disposal request supersedes it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The method is called reentrantly from a provider factory, initializer, or disposer.
    /// </exception>
    public static Task InitializeAsync(bool registerShutdownHandlers = true)
    {
        Task? pendingDisposal;
        QueuedInitialization? queuedInitialization = null;
        long lifecycleVersion;

        lock (Sync)
        {
            pendingDisposal = _disposeTask;
            lifecycleVersion = _lifecycleVersion;
            if (pendingDisposal is not null)
            {
                queuedInitialization = _queuedInitialization ??= new QueuedInitialization();
            }
        }

        // Deliberately outside the lock. This call creates every provider instance and runs the
        // synchronous prefix of every initializer, so holding a process-wide lock across it would
        // block every other lifecycle call in the process for the duration, stall the main thread
        // inside AppDomain.ProcessExit, and deadlock against a factory that starts a thread which
        // does not flow the execution context. The registry serializes the start on its own state.
        var initializationTask = queuedInitialization is not null
            ? queuedInitialization.Task
            : Registry.InitializeAsync();

        if (queuedInitialization is not null)
        {
            _ = CompleteQueuedInitializationAsync(pendingDisposal!, queuedInitialization);
        }

        if (registerShutdownHandlers && pendingDisposal is null)
        {
            lock (Sync)
            {
                if (lifecycleVersion == _lifecycleVersion)
                {
                    RegisterShutdownHandlers();
                }
            }
        }

        if (!registerShutdownHandlers)
        {
            return initializationTask;
        }

        if (initializationTask.Status == TaskStatus.RanToCompletion)
        {
            lock (Sync)
            {
                if (lifecycleVersion == _lifecycleVersion && Registry.IsInitialized)
                {
                    RegisterShutdownHandlers();
                }
            }

            return Task.CompletedTask;
        }

        return RegisterShutdownHandlersAfterInitializationAsync(initializationTask, lifecycleVersion);
    }

    /// <summary>
    /// Disposes all initialized providers asynchronously.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask"/> that completes when each provider has been disposed.
    /// Asynchronous disposal is preferred when both disposal interfaces are implemented.
    /// Repeated and concurrent calls are idempotent. Await this task when cleanup must complete
    /// before process exit; process-exit handling alone is best-effort.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The method is called reentrantly from a provider factory, initializer, or disposer.
    /// </exception>
    public static ValueTask DisposeAsync()
    {
        TaskCompletionSource<object?> completion;
        long lifecycleVersion;

        lock (Sync)
        {
            if (_queuedInitialization is not null)
            {
                _queuedInitialization.IsInvalidated = true;
                _queuedInitialization = null;
            }

            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            lifecycleVersion = ++_lifecycleVersion;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        // Outside the lock for the same reason as InitializeAsync: this invokes provider disposers.
        // A disposal that starts in between is handled by the registry, which queues an
        // initialization behind it rather than running concurrently.
        Task registryDisposalTask;
        try
        {
            registryDisposalTask = Registry.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            // The registry rejects reentrant lifecycle calls synchronously. Release the state
            // claimed above, and fault the task rather than leaving it pending forever, so a
            // caller that already took it fails fast instead of waiting on a task nothing completes.
            lock (Sync)
            {
                if (ReferenceEquals(_disposeTask, completion.Task))
                {
                    _disposeTask = null;
                }
            }

            completion.TrySetException(exception);
            return new ValueTask(completion.Task);
        }

        _ = CompleteDisposalAsync(registryDisposalTask, completion, lifecycleVersion);
        return new ValueTask(completion.Task);
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
    private static async Task CompleteQueuedInitializationAsync(
        Task disposalTask,
        QueuedInitialization queuedInitialization)
    {
        try
        {
            await disposalTask.ConfigureAwait(false);

            bool invalidated;
            lock (Sync)
            {
                invalidated = queuedInitialization.IsInvalidated;
            }

            if (invalidated)
            {
                throw new InvalidOperationException(
                    "Initialization was superseded by a disposal request.");
            }

            await Registry.InitializeAsync().ConfigureAwait(false);
            queuedInitialization.Completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            queuedInitialization.Completion.TrySetException(exception);
        }
        finally
        {
            lock (Sync)
            {
                if (ReferenceEquals(_queuedInitialization, queuedInitialization))
                {
                    _queuedInitialization = null;
                }
            }
        }
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
        TaskCompletionSource<object?> completion,
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

            if (ReferenceEquals(_disposeTask, completion.Task))
            {
                _disposeTask = null;
            }
        }

        if (exception is not null)
        {
            completion.TrySetException(exception);
        }
        else
        {
            completion.TrySetResult(null);
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

    private sealed class QueuedInitialization
    {
        internal QueuedInitialization()
        {
            Completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal TaskCompletionSource<object?> Completion { get; }

        internal bool IsInvalidated { get; set; }

        internal Task Task => Completion.Task;
    }
}
