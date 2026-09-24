using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a consumer of singleton services.
/// </summary>
public readonly record struct ConsumerModel
(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    bool IsPartial,
    ImmutableArray<ServiceReferenceModel> Dependencies)
{
    /// <summary>
    /// Gets the fully qualified name of the consumer type.
    /// </summary>
    public string FullyQualifiedName { get; } = FullyQualifiedName;

    /// <summary>
    /// Gets the short name of the consumer type.
    /// </summary>
    public string ShortName { get; } = ShortName;

    /// <summary>
    /// Gets the namespace of the consumer type.
    /// </summary>
    public string Namespace { get; } = Namespace;

    /// <summary>
    /// Gets whether the consumer is declared as partial.
    /// </summary>
    public bool IsPartial { get; } = IsPartial;

    /// <summary>
    /// Gets the typed service references consumed by the consumer.
    /// </summary>
    public ImmutableArray<ServiceReferenceModel> Dependencies { get; } = Dependencies;
}
