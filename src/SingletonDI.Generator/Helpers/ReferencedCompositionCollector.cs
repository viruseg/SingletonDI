using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

internal sealed class ReferencedCompositionCollector : IReferencedCompositionCollector
{
    public static ReferencedCompositionCollector Instance { get; } = new();

    public ReferencedCompositionSnapshot Collect(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var referencedTypes = ProviderSymbolCollector.GetReferencedTypes(
            compilation,
            cancellationToken);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var providers = ProviderSymbolCollector.BuildReferencedProviders(
            compilation,
            referencedTypes,
            cancellationToken,
            diagnostics.Add,
            out var providerAssemblies);
        var consumerDependencies =
            ReferencedConsumerCollector.CollectReferencedConsumerDependencyIdentities(
                referencedTypes,
                cancellationToken);
        return new ReferencedCompositionSnapshot(
            providers,
            consumerDependencies,
            providerAssemblies,
            diagnostics.ToImmutable());
    }

    public static IncrementalValueProvider<ReferencedCompositionSnapshot> CreateProvider(
        IncrementalValueProvider<Compilation> compilationProvider,
        IReferencedCompositionCollector collector)
    {
        return compilationProvider
            .WithComparer(ReferenceOnlyCompilationComparer.Instance)
            .Select((compilation, cancellationToken) =>
                collector.Collect(compilation, cancellationToken));
    }
}
