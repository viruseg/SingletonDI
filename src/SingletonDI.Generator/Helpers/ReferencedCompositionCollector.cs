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

    public static IncrementalValueProvider<ReferencedCompositionSnapshot> CreateProvider(
        IncrementalValueProvider<Compilation> compilationProvider,
        IReferencedCompositionCollector collector,
        Func<Compilation, bool> shouldCollect)
    {
        if (shouldCollect == null)
        {
            throw new ArgumentNullException(nameof(shouldCollect));
        }

        return compilationProvider
            .WithComparer(ReferenceOnlyCompilationComparer.Instance)
            .Select((compilation, cancellationToken) => shouldCollect(compilation)
                ? collector.Collect(compilation, cancellationToken)
                : ReferencedCompositionSnapshot.Empty);
    }

    public static IncrementalValueProvider<ReferencedCompositionSnapshot> CreateProvider<TOptions>(
        IncrementalValueProvider<Compilation> compilationProvider,
        IncrementalValueProvider<TOptions> optionsProvider,
        IReferencedCompositionCollector collector,
        Func<Compilation, TOptions, bool> shouldCollect)
    {
        if (shouldCollect == null)
        {
            throw new ArgumentNullException(nameof(shouldCollect));
        }

        return compilationProvider
            .WithComparer(ReferenceOnlyCompilationComparer.Instance)
            .Combine(optionsProvider)
            .Select((pair, cancellationToken) => shouldCollect(pair.Left, pair.Right)
                ? collector.Collect(pair.Left, cancellationToken)
                : ReferencedCompositionSnapshot.Empty);
    }
}
