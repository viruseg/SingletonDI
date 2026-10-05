using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a singleton provider and its service-key dependencies.
/// </summary>
/// <param name="fullyQualifiedName">The provider type name including its namespace, as emitted into generated code.</param>
/// <param name="shortName">The provider type name without its namespace or type arguments.</param>
/// <param name="namespace">The namespace declaring the provider, empty for the global namespace.</param>
/// <param name="assemblyIdentity">The identity of the assembly declaring the provider, used to tell same-named types apart.</param>
/// <param name="hasInitializeAsyncMethod">Whether a usable initializer was found and is called during initialization.</param>
/// <param name="isDisposable">Whether the provider implements <see cref="System.IDisposable"/>.</param>
/// <param name="isAsyncDisposable">Whether the provider implements <see cref="System.IAsyncDisposable"/>, which is preferred.</param>
/// <param name="dependencies">The fully qualified service keys the provider requires, in dependency order.</param>
/// <param name="serviceTypeFullyQualifiedName">The service contract the provider is registered under, or <see langword="null"/> when it is registered under its own type.</param>
/// <param name="serviceTypeShortName">The short name of the service contract, or <see langword="null"/>.</param>
/// <param name="serviceTypeNamespace">The namespace of the service contract, or <see langword="null"/>.</param>
/// <param name="propertyName">The property name the consumer exposes, or <see langword="null"/> to derive it.</param>
/// <param name="location">Where the provider name is declared, used to anchor diagnostics.</param>
/// <param name="propertyNameLocation">Where the property name argument was written, or <see langword="null"/>.</param>
/// <param name="dependencyIdentities">
/// The resolved identity of each entry in <paramref name="dependencies"/>. Default when the
/// provider was rejected, which is how a caller tells a rejected provider from a valid one.
/// </param>
/// <param name="serviceTypeIdentity">
/// The resolved identity of <paramref name="serviceTypeFullyQualifiedName"/>, or <see langword="null"/>
/// when the provider declares no service contract.
/// </param>
/// <param name="dependencyLocations">
/// Where each entry of <paramref name="dependencyIdentities"/> is named in the provider's consume
/// attribute, keyed by canonical identity. A dependency declared through a base type or without a
/// resolvable location has no entry.
/// </param>
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
    ServiceTypeIdentity? serviceTypeIdentity = null,
    ImmutableDictionary<string, Location>? dependencyLocations = null)
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
    /// Gets the source location of the provider name, or <see cref="Location.None"/> for metadata providers.
    /// </summary>
    public Location Location { get; } = location;

    /// <summary>
    /// Gets the source location of the custom property name argument, if available.
    /// </summary>
    public Location? PropertyNameLocation { get; } = propertyNameLocation;

    /// <summary>
    /// Gets where each dependency of <see cref="DependencyIdentities"/> is named, keyed by canonical
    /// identity, so that a diagnostic about a missing provider underlines the argument that asks for it.
    /// </summary>
    public ImmutableDictionary<string, Location> DependencyLocations { get; } =
        dependencyLocations ?? ImmutableDictionary<string, Location>.Empty;

    /// <summary>
    /// Gets the location naming the given dependency, or <see cref="Location.None"/> when the
    /// dependency has no entry.
    /// </summary>
    /// <param name="identity">The identity of the dependency to locate.</param>
    public Location GetDependencyLocation(ServiceTypeIdentity identity) =>
        DependencyLocations.TryGetValue(identity.CanonicalIdentity, out var location)
            ? location
            : Location.None;
}
