using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a singleton provider.
/// </summary>
public readonly record struct ProviderModel(string fullyQualifiedName,
                                            string shortName,
                                            string @namespace,
                                            bool hasInitializeAsyncMethod,
                                            bool isDisposable,
                                            ImmutableArray<string> dependencies,
                                            string? propertyName,
                                            Location location,
                                            Location? propertyNameLocation)
{
    /// <summary>
    /// The fully qualified name of the provider type.
    /// </summary>
    public string FullyQualifiedName { get; } = fullyQualifiedName;

    /// <summary>
    /// The short name of the provider type (without namespace).
    /// </summary>
    public string ShortName { get; } = shortName;

    /// <summary>
    /// The namespace of the provider type.
    /// </summary>
    public string Namespace { get; } = @namespace;

    /// <summary>
    /// Whether the provider has a method with signature "Task InitializeAsync()".
    /// </summary>
    public bool HasInitializeAsyncMethod { get; } = hasInitializeAsyncMethod;

    /// <summary>
    /// Whether the provider implements IDisposable.
    /// </summary>
    public bool IsDisposable { get; } = isDisposable;

    /// <summary>
    /// Dependencies of this provider (types it consumes via [SingletonDIConsume]).
    /// </summary>
    public ImmutableArray<string> Dependencies { get; } = dependencies;

    /// <summary>
    /// Custom property name for the singleton instance, if specified in the attribute.
    /// If null, the default name "{TypeName}Instance" will be used.
    /// </summary>
    public string? PropertyName { get; } = propertyName;

    /// <summary>
    /// The location of the provider type declaration in the source code.
    /// Used for reporting diagnostics with precise location information.
    /// </summary>
    public Location Location { get; } = location;

    /// <summary>
    /// The location of the PropertyName argument in the [SingletonDIProvide] attribute.
    /// Used for precise diagnostic highlighting of the property name string literal.
    /// Null if no custom property name was specified.
    /// </summary>
    public Location? PropertyNameLocation { get; } = propertyNameLocation;
}
