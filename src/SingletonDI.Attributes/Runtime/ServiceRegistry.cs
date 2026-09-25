using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

internal sealed class ServiceRegistry
{
    private readonly object _sync = new();
    private readonly AsyncLocal<ProviderCallbackScope?> _initializationContext = new();
    private readonly AsyncLocal<ProviderCallbackScope?> _providerCallbackContext = new();
    private readonly Dictionary<Type, ProviderRegistration> _registrations = new();
    private readonly List<ProviderRegistration> _orderedRegistrations = new();
    private Task? _initializationTask;
    private Task? _disposeTask;
    private Task? _queuedInitializationTask;
    private ServiceGraph? _graph;
    private LifecycleState _state;

    internal bool IsInitialized
    {
        get
        {
            lock (_sync)
            {
                return _state == LifecycleState.Initialized;
            }
        }
    }

    internal void RegisterProvider<TService, TImplementation>(
        Func<TImplementation> factory,
        Type[] dependencyTypes,
        Func<TImplementation, Task>? initializeAsync,
        Action<TImplementation>? dispose,
        Func<TImplementation, Task>? disposeAsync)
        where TImplementation : TService
    {
        var registration = new ProviderRegistration<TService, TImplementation>(
            factory,
            dependencyTypes,
            initializeAsync,
            dispose,
            disposeAsync);
        var serviceType = typeof(TService);
        var implementationType = typeof(TImplementation);

        lock (_sync)
        {
            if (_state is LifecycleState.Initializing or LifecycleState.Initialized or LifecycleState.Disposing)
            {
                throw new InvalidOperationException(
                    "Providers cannot be registered after initialization has started.");
            }

            if (_registrations.TryGetValue(serviceType, out var existingServiceRegistration))
            {
                throw DuplicateKeyException(serviceType, existingServiceRegistration);
            }

            if (_registrations.TryGetValue(implementationType, out var existingImplementationRegistration))
            {
                throw DuplicateKeyException(implementationType, existingImplementationRegistration);
            }

            _registrations.Add(serviceType, registration);
            if (serviceType != implementationType)
            {
                _registrations.Add(implementationType, registration);
            }

            _orderedRegistrations.Add(registration);
        }
    }

    internal Task InitializeAsync()
    {
        Task task;
        TaskCompletionSource<object?>? startCompletion = null;
        Task? queuedDisposalTask = null;
        ServiceGraph? graph = null;

        lock (_sync)
        {
            ThrowIfProviderCallbackIsActive();
            switch (_state)
            {
                case LifecycleState.Initialized:
                    return Task.CompletedTask;
                case LifecycleState.Initializing:
                    return _initializationTask!;
                case LifecycleState.Disposing:
                    if (_queuedInitializationTask is null)
                    {
                        startCompletion = new TaskCompletionSource<object?>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        _queuedInitializationTask = startCompletion.Task;
                        queuedDisposalTask = _disposeTask!;
                    }

                    task = _queuedInitializationTask!;
                    break;
                default:
                    _state = LifecycleState.Initializing;
                    var completion = new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _initializationTask = completion.Task;

                    try
                    {
                        graph = new ServiceGraph(_orderedRegistrations, _registrations);
                        _graph = graph;
                        startCompletion = completion;
                    }
                    catch (Exception exception)
                    {
                        _state = LifecycleState.NotInitialized;
                        _initializationTask = null;
                        completion.TrySetException(exception);
                    }

                    task = completion.Task;
                    break;
            }
        }

        if (graph is not null)
        {
            _ = InitializeCoreAsync(graph, startCompletion!);
        }

        if (queuedDisposalTask is not null)
        {
            _ = CompleteQueuedInitializationAsync(queuedDisposalTask, startCompletion!);
        }

        return task;
    }

    internal T Resolve<T>()
    {
        lock (_sync)
        {
            var initializationScope = _initializationContext.Value;
            var canResolveDuringInitialization = initializationScope?.IsActive == true &&
                                                  ReferenceEquals(initializationScope.Graph, _graph);
            if (_graph is null || (!_state.Equals(LifecycleState.Initialized) && !canResolveDuringInitialization))
            {
                throw new InvalidOperationException(
                    "Singleton container has not been initialized. " +
                    "Call SingletonDIInitializer.InitializeAsync() before accessing singletons.");
            }

            if (!_registrations.TryGetValue(typeof(T), out var registration))
            {
                throw new InvalidOperationException(
                    $"No singleton provider is registered for service key '{GetTypeName(typeof(T))}'.");
            }

            return (T)_graph.GetInstance(registration);
        }
    }

    internal ValueTask DisposeAsync()
    {
        TaskCompletionSource<object?> completion;
        Task? initializationTask;

        lock (_sync)
        {
            ThrowIfProviderCallbackIsActive();
            if (_state == LifecycleState.Disposing)
            {
                return new ValueTask(_disposeTask!);
            }

            if (_state == LifecycleState.NotInitialized)
            {
                return default;
            }

            _state = LifecycleState.Disposing;
            initializationTask = _initializationTask;
            completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(initializationTask, completion);
        return new ValueTask(completion.Task);
    }

    private async Task InitializeCoreAsync(
        ServiceGraph graph,
        TaskCompletionSource<object?> completion)
    {
        var callbackScope = new ProviderCallbackScope(graph);
        _providerCallbackContext.Value = callbackScope;
        _initializationContext.Value = callbackScope;
        try
        {
            await graph.InitializeAsync().ConfigureAwait(false);

            lock (_sync)
            {
                if (_state == LifecycleState.Initializing)
                {
                    _graph = graph;
                    _state = LifecycleState.Initialized;
                }
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _graph = null;
                if (_state == LifecycleState.Initializing)
                {
                    _state = LifecycleState.NotInitialized;
                    _initializationTask = null;
                }
            }

            completion.TrySetException(exception);
        }
        finally
        {
            callbackScope.IsActive = false;
            _providerCallbackContext.Value = null;
            _initializationContext.Value = null;
        }
    }

    private async Task CompleteQueuedInitializationAsync(
        Task disposalTask,
        TaskCompletionSource<object?> completion)
    {
        try
        {
            await disposalTask.ConfigureAwait(false);
            await InitializeAsync().ConfigureAwait(false);
            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DisposeCoreAsync(
        Task? initializationTask,
        TaskCompletionSource<object?> completion)
    {
        var callbackScope = new ProviderCallbackScope();
        _providerCallbackContext.Value = callbackScope;
        try
        {
            if (initializationTask is not null)
            {
                try
                {
                    await initializationTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            ServiceGraph? graph;
            lock (_sync)
            {
                graph = _graph;
                _graph = null;
            }

            if (graph is not null)
            {
                await graph.DisposeAsync().ConfigureAwait(false);
            }

            lock (_sync)
            {
                _state = LifecycleState.NotInitialized;
                _initializationTask = null;
                _disposeTask = null;
                _queuedInitializationTask = null;
            }

            completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _state = LifecycleState.NotInitialized;
                _initializationTask = null;
                _disposeTask = null;
                _queuedInitializationTask = null;
            }

            completion.TrySetException(exception);
        }
        finally
        {
            callbackScope.IsActive = false;
            _providerCallbackContext.Value = null;
        }
    }

    private void ThrowIfProviderCallbackIsActive()
    {
        if (_providerCallbackContext.Value?.IsActive == true)
        {
            throw new InvalidOperationException(
                "Lifecycle operations cannot be called from provider callbacks.");
        }
    }

    private static InvalidOperationException DuplicateKeyException(
        Type key,
        ProviderRegistration existingRegistration)
    {
        return new InvalidOperationException(
            $"A provider for service key '{GetTypeName(key)}' is already registered by " +
            $"'{GetTypeName(existingRegistration.ImplementationType)}'.");
    }

    private static string GetTypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }

    private sealed class ProviderCallbackScope
    {
        internal ProviderCallbackScope(ServiceGraph? graph = null)
        {
            Graph = graph;
        }

        internal ServiceGraph? Graph { get; }

        internal bool IsActive { get; set; } = true;
    }

    private enum LifecycleState
    {
        NotInitialized,
        Initializing,
        Initialized,
        Disposing
    }
}
