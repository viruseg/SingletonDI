using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model combining providers and consumers with topological order.
/// </summary>
public readonly record struct CombinedModel
{
    /// <summary>
    /// All singleton providers in the assembly.
    /// </summary>
    public required ImmutableArray<ProviderModel> Providers { get; init; }

    /// <summary>
    /// All consumers of singleton providers in the assembly.
    /// </summary>
    public required ImmutableArray<ConsumerModel> Consumers { get; init; }

    /// <summary>
    /// Topological order of provider FQNs for initialization.
    /// </summary>
    public required ImmutableArray<string> TopologicalOrder { get; init; }

    /// <summary>
    /// Whether there are any circular dependencies (invalid state).
    /// </summary>
    public bool HasCircularDependency => TopologicalOrder.IsEmpty && !Providers.IsEmpty;
}
