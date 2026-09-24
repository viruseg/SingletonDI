using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a singleton provider and its service-key dependencies.
/// </summary>
public readonly record struct ProviderModel(
    string fullyQualifiedName,
    string shortName,
    string @namespace,
    string assemblyIdentity,
    bool hasInitializeAsyncMethod,
    bool isDisposable,
    bool isAsyncDisposable,
    ImmutableArray<string> dependencies,
    string? serviceTypeFullyQualifiedName,
    string? serviceTypeShortName,
    string? serviceTypeNamespace,
    string? propertyName,
    Location location,
    Location? propertyNameLocation,
    ImmutableArray<ServiceTypeIdentity> dependencyIdentities = default,
    ServiceTypeIdentity? serviceTypeIdentity = null)
{
    /// <summary>
    /// Gets the fully qualified name of the provider type.
    /// </summary>
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    /// <summary>
    /// Gets the short name of the provider type.
    /// </summary>
    public string ShortName { get; } = shortName;

    /// <summary>
    /// Gets the namespace of the provider type.
    /// </summary>
    public string Namespace { get; } = @namespace;

    /// <summary>
    /// Gets the identity of the assembly containing the provider type.
    /// </summary>
    public string AssemblyIdentity { get; } = assemblyIdentity;

    /// <summary>
    /// Gets the canonical identity of the provider type.
    /// </summary>
    public ServiceTypeIdentity TypeIdentity { get; } =
        new(fullyQualifiedName, assemblyIdentity);

    /// <summary>
    /// Gets whether the provider has an asynchronous initialization method.
    /// </summary>
    public bool HasInitializeAsyncMethod { get; } = hasInitializeAsyncMethod;

    /// <summary>
    /// Gets whether the provider implements <see cref="IDisposable"/>.
    /// </summary>
    public bool IsDisposable { get; } = isDisposable;

    /// <summary>
    /// Gets whether the provider implements <see cref="IAsyncDisposable"/>.
    /// </summary>
    public bool IsAsyncDisposable { get; } = isAsyncDisposable;

    /// <summary>
    /// Gets the service keys consumed by the provider through <c>SingletonDIConsume</c>.
    /// </summary>
    public ImmutableArray<string> Dependencies { get; } = dependencies;

    /// <summary>
    /// Gets the assembly-qualified identities of services consumed by the provider.
    /// </summary>
    public ImmutableArray<ServiceTypeIdentity> DependencyIdentities { get; } =
        dependencyIdentities.IsDefault
            ? dependencies.IsDefault
                ? ImmutableArray<ServiceTypeIdentity>.Empty
                : dependencies
                    .Select(dependency => new ServiceTypeIdentity(dependency, assemblyIdentity))
                    .ToImmutableArray()
            : dependencyIdentities;

    /// <summary>
    /// Gets the fully qualified name of the exposed service contract, if any.
    /// </summary>
    public string? ServiceTypeFullyQualifiedName { get; } = serviceTypeFullyQualifiedName;

    /// <summary>
    /// Gets the short name of the exposed service contract, if any.
    /// </summary>
    public string? ServiceTypeShortName { get; } = serviceTypeShortName;

    /// <summary>
    /// Gets the namespace of the exposed service contract, if any.
    /// </summary>
    public string? ServiceTypeNamespace { get; } = serviceTypeNamespace;

    /// <summary>
    /// Gets the assembly-qualified identity of the exposed service contract, if any.
    /// </summary>
    public ServiceTypeIdentity? ServiceTypeIdentity { get; } =
        serviceTypeIdentity ??
        (string.IsNullOrEmpty(serviceTypeFullyQualifiedName)
            ? null
            : new ServiceTypeIdentity(serviceTypeFullyQualifiedName!, assemblyIdentity));

    /// <summary>
    /// Gets the custom property name of the provider, if one was specified.
    /// </summary>
    public string? PropertyName { get; } = propertyName;

    /// <summary>
    /// Gets the source location of the provider declaration, or <see cref="Location.None"/> for metadata providers.
    /// </summary>
    public Location Location { get; } = location;

    /// <summary>
    /// Gets the source location of the custom property name argument, if available.
    /// </summary>
    public Location? PropertyNameLocation { get; } = propertyNameLocation;
}
