using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a singleton provider.
/// </summary>
public readonly record struct ProviderModel
{
    /// <summary>
    /// The fully qualified name of the provider type.
    /// </summary>
    public required string FullyQualifiedName { get; init; }

    /// <summary>
    /// The short name of the provider type (without namespace).
    /// </summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// The namespace of the provider type.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Whether the provider has a method with signature "Task InitializeAsync()".
    /// </summary>
    public required bool HasInitializeAsyncMethod { get; init; }

    /// <summary>
    /// Whether the provider implements IDisposable.
    /// </summary>
    public required bool IsDisposable { get; init; }

    /// <summary>
    /// Dependencies of this provider (types it consumes via [SingletonDIConsume]).
    /// </summary>
    public required ImmutableArray<string> Dependencies { get; init; }
}
