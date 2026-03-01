using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a consumer of singleton providers.
/// </summary>
public readonly record struct ConsumerModel
(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    bool IsPartial,
    ImmutableArray<string> Dependencies)
{
    /// <summary>
    /// The fully qualified name of the consumer type.
    /// </summary>
    public string FullyQualifiedName { get; } = FullyQualifiedName;

    /// <summary>
    /// The short name of the consumer type (without namespace).
    /// </summary>
    public string ShortName { get; } = ShortName;

    /// <summary>
    /// The namespace of the consumer type.
    /// </summary>
    public string Namespace { get; } = Namespace;

    /// <summary>
    /// Whether the consumer is declared as partial.
    /// </summary>
    public bool IsPartial { get; } = IsPartial;

    /// <summary>
    /// The fully qualified names of the provider types this consumer depends on.
    /// </summary>
    public ImmutableArray<string> Dependencies { get; } = Dependencies;
}
