using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model combining providers and consumers with topological order.
/// Provider dependencies are service keys, while consumer dependencies are typed service references.
/// </summary>
public readonly record struct CombinedModel
(
    ImmutableArray<ProviderModel> Providers,
    ImmutableArray<ConsumerModel> Consumers,
    ImmutableArray<string> TopologicalOrder)
{
    /// <summary>
    /// All singleton providers in the assembly.
    /// </summary>
    public ImmutableArray<ProviderModel> Providers { get; } = Providers;

    /// <summary>
    /// All consumers of singleton providers in the assembly.
    /// </summary>
    public ImmutableArray<ConsumerModel> Consumers { get; } = Consumers;

    /// <summary>
    /// Topological order of provider FQNs for initialization.
    /// </summary>
    public ImmutableArray<string> TopologicalOrder { get; } = TopologicalOrder;

    /// <summary>
    /// Whether there are any circular dependencies (invalid state).
    /// </summary>
    public bool HasCircularDependency => TopologicalOrder.IsEmpty && !Providers.IsEmpty;
}