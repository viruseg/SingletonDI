using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

internal class ProviderRegistration
{
    internal ProviderRegistration(
        Type serviceType,
        Type implementationType,
        Type[] dependencyTypes,
        Func<object> factory,
        Func<object, Task>? initializeAsync,
        Action<object>? dispose,
        Func<object, Task>? disposeAsync)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        InitializeAsync = initializeAsync;
        Dispose = dispose;
        DisposeAsync = disposeAsync;

        if (dependencyTypes is null)
        {
            throw new ArgumentNullException(nameof(dependencyTypes));
        }

        foreach (var dependencyType in dependencyTypes)
        {
            if (dependencyType is null)
            {
                throw new ArgumentException("Dependency types cannot contain null values.", nameof(dependencyTypes));
            }
        }

        // Cloned so the generated array literal cannot be aliased and mutated after registration,
        // and exposed as a read-only view so nothing can mutate it through this instance either.
        DependencyTypes = Array.AsReadOnly((Type[])dependencyTypes.Clone());
    }

    internal Type ServiceType { get; }

    internal Type ImplementationType { get; }

    internal IReadOnlyList<Type> DependencyTypes { get; }

    internal Func<object> Factory { get; }

    internal Func<object, Task>? InitializeAsync { get; }

    internal Action<object>? Dispose { get; }

    internal Func<object, Task>? DisposeAsync { get; }
}

internal sealed class ProviderRegistration<TService, TImplementation> : ProviderRegistration
    where TImplementation : TService
{
    internal ProviderRegistration(
        Func<TImplementation> factory,
        Type[] dependencyTypes,
        Func<TImplementation, Task>? initializeAsync,
        Action<TImplementation>? dispose,
        Func<TImplementation, Task>? disposeAsync)
        : base(
            typeof(TService),
            typeof(TImplementation),
            dependencyTypes,
            AdaptFactory(factory),
            AdaptInitializeAsync(initializeAsync),
            AdaptDispose(dispose),
            AdaptDisposeAsync(disposeAsync))
    {
    }

    private static Func<object> AdaptFactory(Func<TImplementation> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return () => factory()!;
    }

    private static Func<object, Task>? AdaptInitializeAsync(Func<TImplementation, Task>? initializeAsync)
    {
        return initializeAsync is null
            ? null
            : instance => initializeAsync((TImplementation)instance);
    }

    private static Action<object>? AdaptDispose(Action<TImplementation>? dispose)
    {
        return dispose is null
            ? null
            : instance => dispose((TImplementation)instance);
    }

    private static Func<object, Task>? AdaptDisposeAsync(Func<TImplementation, Task>? disposeAsync)
    {
        return disposeAsync is null
            ? null
            : instance => disposeAsync((TImplementation)instance);
    }
}
