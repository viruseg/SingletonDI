using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a consumer of singleton providers.
/// </summary>
public readonly record struct ConsumerModel
{
    /// <summary>
    /// The fully qualified name of the consumer type.
    /// </summary>
    public required string FullyQualifiedName { get; init; }

    /// <summary>
    /// The short name of the consumer type (without namespace).
    /// </summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// The namespace of the consumer type.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Whether the consumer is declared as partial.
    /// </summary>
    public required bool IsPartial { get; init; }

    /// <summary>
    /// The fully qualified names of the provider types this consumer depends on.
    /// </summary>
    public required ImmutableArray<string> Dependencies { get; init; }
}
