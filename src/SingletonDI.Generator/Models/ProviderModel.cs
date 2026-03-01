using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a singleton provider.
/// </summary>
public readonly record struct ProviderModel(string fullyQualifiedName,
                                            string shortName,
                                            string @namespace,
                                            bool hasInitializeAsyncMethod,
                                            bool isDisposable,
                                            ImmutableArray<string> dependencies)
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
}
