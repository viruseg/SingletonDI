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
    private static ShutdownManager? _shutdownManager;
    private static Task? _disposeTask;
    private static Task? _queuedInitializationTask;
    private static long _lifecycleVersion;

    /// <summary>
    /// Initializes all registered providers asynchronously.
    /// </summary>
    /// <param name="registerShutdownHandlers">
    /// Whether process-exit, console-cancel, and supported POSIX signal handlers should be registered.
    /// When enabled, handled signals cancel default termination, await one disposal operation, and
    /// terminate with exit code 130 for SIGINT, 143 for SIGTERM, or 131 for SIGQUIT.
    /// </param>
    /// <returns>
    /// A task that completes after every provider instance has been created and every
    /// <c>InitializeAsync</c> method returning <see cref="Task"/> or <see cref="ValueTask"/> has completed.
    /// </returns>
    public static Task InitializeAsync(bool registerShutdownHandlers = true)
    {
        Task initializationTask;
        Task? pendingDisposal;
        TaskCompletionSource<object?>? queuedCompletion = null;
        long lifecycleVersion;

        lock (Sync)
        {
            pendingDisposal = _disposeTask;
            lifecycleVersion = _lifecycleVersion;
            if (pendingDisposal is not null)
            {
                if (_queuedInitializationTask is null)
                {
                    queuedCompletion = new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _queuedInitializationTask = queuedCompletion.Task;
                }

                initializationTask = _queuedInitializationTask!;
            }
            else
            {
                initializationTask = Registry.InitializeAsync();
            }
        }

        if (queuedCompletion is not null)
        {
            _ = CompleteQueuedInitializationAsync(pendingDisposal!, queuedCompletion);
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
    public static ValueTask DisposeAsync()
    {
        TaskCompletionSource<object?> completion;
        Task registryDisposalTask;
        long lifecycleVersion;

        lock (Sync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            lifecycleVersion = ++_lifecycleVersion;
            registryDisposalTask = Registry.DisposeAsync().AsTask();
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
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

    internal static T Resolve<T>()
    {
        return Registry.Resolve<T>();
    }

    private static async Task CompleteQueuedInitializationAsync(
        Task disposalTask,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            await disposalTask.ConfigureAwait(false);
            var initializationTask = Registry.InitializeAsync();
            await initializationTask.ConfigureAwait(false);
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (Sync)
            {
                if (ReferenceEquals(_queuedInitializationTask, completion.Task))
                {
                    _queuedInitializationTask = null;
                }
            }
        }
    }

    private static async Task RegisterShutdownHandlersAfterInitializationAsync(
        Task initializationTask,
        long lifecycleVersion)
    {
        await initializationTask.ConfigureAwait(false);

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
        _shutdownManager ??= new ShutdownManager(static () => DisposeAsync(), Environment.Exit);
        _shutdownManager.Register();
    }
}
