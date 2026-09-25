using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Models;

internal readonly record struct ReferencedCompositionSnapshot(
    ImmutableArray<ProviderModel> Providers,
    ImmutableArray<ImmutableArray<ServiceTypeIdentity>> ConsumerDependencySets,
    ImmutableArray<ProviderAssemblyModel> ProviderAssemblies,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public static ReferencedCompositionSnapshot Empty { get; } = new(
        ImmutableArray<ProviderModel>.Empty,
        ImmutableArray<ImmutableArray<ServiceTypeIdentity>>.Empty,
        ImmutableArray<ProviderAssemblyModel>.Empty,
        ImmutableArray<Diagnostic>.Empty);
}
