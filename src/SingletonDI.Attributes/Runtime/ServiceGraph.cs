using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

internal sealed class ServiceGraph
{
    private readonly IReadOnlyList<IReadOnlyList<ProviderRegistration>> _levels;
    private readonly Dictionary<Type, object> _instances = new();
    private readonly object _sync = new();
    private Task? _disposeTask;
    private bool _disposed;

    internal ServiceGraph(
        IReadOnlyList<ProviderRegistration> registrations,
        IReadOnlyDictionary<Type, ProviderRegistration> registrationsByKey)
    {
        if (registrations is null)
        {
            throw new ArgumentNullException(nameof(registrations));
        }

        if (registrationsByKey is null)
        {
            throw new ArgumentNullException(nameof(registrationsByKey));
        }

        _levels = BuildLevels(registrations, registrationsByKey);
    }

    internal async Task InitializeAsync()
    {
        try
        {
            CreateInstances();

            foreach (var level in _levels)
            {
                var initializers = new List<Task>();
                foreach (var registration in level)
                {
                    if (registration.InitializeAsync is null)
                    {
                        continue;
                    }

                    try
                    {
                        var instance = GetInstance(registration);
                        var initialization = registration.InitializeAsync(instance)
                            ?? throw new InvalidOperationException(
                                $"Initializer for '{GetTypeName(registration.ImplementationType)}' returned null.");
                        initializers.Add(initialization);
                    }
                    catch (Exception exception)
                    {
                        initializers.Add(Task.FromException(exception));
                    }
                }

                if (initializers.Count > 0)
                {
                    await Task.WhenAll(initializers).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    internal object GetInstance(ProviderRegistration registration)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new InvalidOperationException("The singleton service graph has been disposed.");
            }

            if (!TryGetInstance(registration.ImplementationType, out var instance))
            {
                throw new InvalidOperationException(
                    $"Provider '{GetTypeName(registration.ImplementationType)}' has not been created.");
            }

            return instance;
        }
    }

    internal ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_sync)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = true;
            disposeTask = DisposeCoreAsync();
            _disposeTask = disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();

        try
        {
            for (var levelIndex = _levels.Count - 1; levelIndex >= 0; levelIndex--)
            {
                var disposals = new List<Task>();
                foreach (var registration in _levels[levelIndex])
                {
                    if (!TryGetInstance(registration.ImplementationType, out var instance))
                    {
                        continue;
                    }

                    if (registration.DisposeAsync is not null)
                    {
                        try
                        {
                            var disposal = registration.DisposeAsync(instance)
                                ?? throw new InvalidOperationException(
                                    $"Asynchronous disposer for '{GetTypeName(registration.ImplementationType)}' returned null.");
                            disposals.Add(disposal);
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }
                    else if (registration.Dispose is not null)
                    {
                        try
                        {
                            disposals.Add(Task.Run(() => registration.Dispose(instance)));
                        }
                        catch (Exception exception)
                        {
                            failures.Add(exception);
                        }
                    }
                }

                if (disposals.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(disposals).ConfigureAwait(false);
                    }
                    catch
                    {
                        // WhenAll surfaces a single fault and drops the rest, so a second failing
                        // disposer in the same level was never observed at all. Read the faults back
                        // off the tasks: which providers failed to release their resources is the
                        // information needed when diagnosing a bad shutdown.
                        foreach (var disposal in disposals)
                        {
                            if (disposal.IsFaulted && disposal.Exception is { } fault)
                            {
                                failures.AddRange(fault.InnerExceptions);
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _instances.Clear();
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        else if (failures.Count > 1)
        {
            throw new AggregateException("One or more providers failed to dispose.", failures);
        }
    }

    private void CreateInstances()
    {
        foreach (var level in _levels)
        {
            foreach (var registration in level)
            {
                if (IsCreated(registration.ImplementationType))
                {
                    continue;
                }

                var instance = registration.Factory()
                    ?? throw new InvalidOperationException(
                        $"Factory for '{GetTypeName(registration.ImplementationType)}' returned null.");
                AddInstance(registration.ImplementationType, instance);
            }
        }
    }

    private bool IsCreated(Type implementationType)
    {
        lock (_sync)
        {
            return _instances.ContainsKey(implementationType);
        }
    }

    private void AddInstance(Type implementationType, object instance)
    {
        lock (_sync)
        {
            _instances[implementationType] = instance;
        }
    }

    private bool TryGetInstance(Type implementationType, out object instance)
    {
        lock (_sync)
        {
            return _instances.TryGetValue(implementationType, out instance!);
        }
    }

    private static IReadOnlyList<IReadOnlyList<ProviderRegistration>> BuildLevels(
        IReadOnlyList<ProviderRegistration> registrations,
        IReadOnlyDictionary<Type, ProviderRegistration> registrationsByKey)
    {
        var uniqueRegistrations = new List<ProviderRegistration>();
        var registrationsByImplementation = new Dictionary<Type, ProviderRegistration>();

        foreach (var registration in registrations)
        {
            if (registrationsByImplementation.ContainsKey(registration.ImplementationType))
            {
                throw new InvalidOperationException(
                    $"Multiple providers use implementation type '{GetTypeName(registration.ImplementationType)}'.");
            }

            registrationsByImplementation.Add(registration.ImplementationType, registration);
            uniqueRegistrations.Add(registration);
        }

        var dependentsByImplementation = new Dictionary<Type, List<ProviderRegistration>>();
        var dependencyCounts = new Dictionary<Type, int>();

        foreach (var registration in uniqueRegistrations)
        {
            var dependencies = new List<ProviderRegistration>();
            var uniqueDependencies = new HashSet<Type>();

            foreach (var dependencyType in registration.DependencyTypes)
            {
                if (!registrationsByKey.TryGetValue(dependencyType, out var dependency))
                {
                    throw new InvalidOperationException(
                        $"Missing dependency '{GetTypeName(dependencyType)}' for provider " +
                        $"'{GetTypeName(registration.ImplementationType)}'.");
                }

                if (!uniqueDependencies.Add(dependency.ImplementationType))
                {
                    continue;
                }

                dependencies.Add(dependency);
                if (!dependentsByImplementation.TryGetValue(dependency.ImplementationType, out var dependents))
                {
                    dependents = new List<ProviderRegistration>();
                    dependentsByImplementation.Add(dependency.ImplementationType, dependents);
                }

                dependents.Add(registration);
            }

            dependencyCounts.Add(registration.ImplementationType, dependencies.Count);
        }

        var currentLevel = new List<ProviderRegistration>();
        foreach (var registration in uniqueRegistrations)
        {
            if (dependencyCounts[registration.ImplementationType] == 0)
            {
                currentLevel.Add(registration);
            }
        }

        var levels = new List<IReadOnlyList<ProviderRegistration>>();
        var processed = 0;

        while (currentLevel.Count > 0)
        {
            levels.Add(currentLevel);
            processed += currentLevel.Count;
            var nextLevel = new List<ProviderRegistration>();

            foreach (var registration in currentLevel)
            {
                if (!dependentsByImplementation.TryGetValue(registration.ImplementationType, out var dependents))
                {
                    continue;
                }

                foreach (var dependent in dependents)
                {
                    var dependencyCount = dependencyCounts[dependent.ImplementationType];
                    dependencyCounts[dependent.ImplementationType] = dependencyCount - 1;
                    if (dependencyCount == 1)
                    {
                        nextLevel.Add(dependent);
                    }
                }
            }

            currentLevel = nextLevel;
        }

        if (processed != uniqueRegistrations.Count)
        {
            var cyclicTypes = new List<string>();
            foreach (var registration in uniqueRegistrations)
            {
                if (dependencyCounts[registration.ImplementationType] > 0)
                {
                    cyclicTypes.Add(GetTypeName(registration.ImplementationType));
                }
            }

            throw new InvalidOperationException(
                $"Dependency cycle detected in singleton service graph: {string.Join(", ", cyclicTypes)}.");
        }

        return levels;
    }

    private static string GetTypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}
