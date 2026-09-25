using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;
using Xunit;

namespace SingletonDI.Tests;

public sealed class TopologicalSorterTests
{
    [Fact]
    public void TryFindCycle_LargeChainWithCyclicTail_ReturnsDeterministicIdentities()
    {
        const int chainLength = 1000;
        var providers = new List<ProviderModel>(chainLength + 3);
        providers.Add(CreateProvider(0));
        for (var index = 1; index < chainLength; index++)
        {
            providers.Add(CreateProvider(index, index - 1));
        }

        providers.Add(CreateProvider(1000, 999, 1002));
        providers.Add(CreateProvider(1001, 1000));
        providers.Add(CreateProvider(1002, 1001));
        var map = ServiceTypeResolver.BuildServiceTypeMap(providers).IdentityMap;

        var cycle = TopologicalSorter.TryFindCycle(
            providers.AsEnumerable().Reverse().ToImmutableArray(),
            map);

        Assert.Equal(
            new[]
            {
                CreateIdentity(1000),
                CreateIdentity(1001),
                CreateIdentity(1002),
                CreateIdentity(1000)
            },
            cycle.ToArray());
    }

    [Fact]
    public void TryFindCycle_AcyclicChain_ReturnsEmpty()
    {
        const int chainLength = 1000;
        var providers = new List<ProviderModel>(chainLength);
        providers.Add(CreateProvider(0));
        for (var index = 1; index < chainLength; index++)
        {
            providers.Add(CreateProvider(index, index - 1));
        }

        var map = ServiceTypeResolver.BuildServiceTypeMap(providers).IdentityMap;

        var cycle = TopologicalSorter.TryFindCycle(providers.ToImmutableArray(), map);

        Assert.Empty(cycle);
    }

    private static ProviderModel CreateProvider(int index, params int[] dependencies)
    {
        var dependencyIdentities = dependencies
            .Select(CreateIdentity)
            .ToImmutableArray();
        return new ProviderModel(
            fullyQualifiedName: GetProviderName(index),
            shortName: $"Provider{index}",
            @namespace: "App",
            assemblyIdentity: "Test",
            hasInitializeAsyncMethod: false,
            isDisposable: false,
            isAsyncDisposable: false,
            dependencies: ImmutableArray<string>.Empty,
            serviceTypeFullyQualifiedName: null,
            serviceTypeShortName: null,
            serviceTypeNamespace: null,
            propertyName: null,
            location: Location.None,
            propertyNameLocation: null,
            dependencyIdentities: dependencyIdentities);
    }

    private static ServiceTypeIdentity CreateIdentity(int index)
    {
        return new ServiceTypeIdentity(GetProviderName(index), "Test");
    }

    private static string GetProviderName(int index)
    {
        return $"global::App.Provider{index:D4}";
    }
}
