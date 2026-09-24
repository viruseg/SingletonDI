using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Performs topological sorting using Kahn's algorithm and detects provider cycles.
/// </summary>
internal static class TopologicalSorter
{
    public readonly struct SortResult(ImmutableArray<string> sortedOrder, ImmutableArray<string> cycle)
    {
        public ImmutableArray<string> SortedOrder { get; } = sortedOrder;
        public ImmutableArray<string> Cycle { get; } = cycle;
        public bool HasCycle => !Cycle.IsEmpty;
    }

    public static SortResult Sort(ImmutableArray<ProviderModel> providers)
    {
        if (providers.IsDefault || providers.IsEmpty)
        {
            return new SortResult(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
        }

        return SortGraph(BuildGraph(providers, static dependencyKey => dependencyKey));
    }

    public readonly struct LevelSortResult(List<List<ProviderModel>> levels, ImmutableArray<string> cycle)
    {
        public List<List<ProviderModel>> Levels { get; } = levels;

        public ImmutableArray<string> Cycle { get; } = cycle;

        public ImmutableArray<ServiceTypeIdentity> CycleIdentities { get; init; } =
            ImmutableArray<ServiceTypeIdentity>.Empty;

        public bool HasCycle => !Cycle.IsEmpty;
    }

    public static LevelSortResult SortByLevels(ImmutableArray<ProviderModel> providers)
    {
        if (providers.IsDefault || providers.IsEmpty)
        {
            return new LevelSortResult([], ImmutableArray<string>.Empty);
        }

        return SortByLevelsCore(providers, static dependencyKey => dependencyKey);
    }

    /// <summary>
    /// Sorts providers by dependency levels using concrete and contract service keys.
    /// </summary>
    /// <param name="providers">Providers to sort.</param>
    /// <param name="serviceKeyToProvider">Service-key to provider-FQN mapping.</param>
    /// <returns>Provider levels and any detected cycle.</returns>
    public static LevelSortResult SortByLevels(
        ImmutableArray<ProviderModel> providers,
        ImmutableDictionary<string, string> serviceKeyToProvider)
    {
        if (serviceKeyToProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceKeyToProvider));
        }

        if (providers.IsDefault || providers.IsEmpty)
        {
            return new LevelSortResult([], ImmutableArray<string>.Empty);
        }

        return SortByLevelsCore(
            providers,
            dependencyKey => serviceKeyToProvider.TryGetValue(dependencyKey, out var provider)
                ? provider
                : null);
    }

    /// <summary>
    /// Sorts providers by dependency levels using a read-only service-key map.
    /// </summary>
    /// <param name="providers">Providers to sort.</param>
    /// <param name="serviceKeyToProvider">Service-key to provider-FQN mapping.</param>
    /// <returns>Provider levels and any detected cycle.</returns>
    public static LevelSortResult SortByLevels(
        ImmutableArray<ProviderModel> providers,
        IReadOnlyDictionary<string, string> serviceKeyToProvider)
    {
        if (serviceKeyToProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceKeyToProvider));
        }

        if (providers.IsDefault || providers.IsEmpty)
        {
            return new LevelSortResult([], ImmutableArray<string>.Empty);
        }

        return SortByLevelsCore(
            providers,
            dependencyKey => serviceKeyToProvider.TryGetValue(dependencyKey, out var provider)
                ? provider
                : null);
    }

    public static LevelSortResult SortByLevels(
        ImmutableArray<ProviderModel> providers,
        ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeToProvider)
    {
        if (serviceTypeToProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceTypeToProvider));
        }

        if (providers.IsDefault || providers.IsEmpty)
        {
            return new LevelSortResult([], ImmutableArray<string>.Empty);
        }

        return SortByLevelsIdentityCore(
            providers,
            dependency => serviceTypeToProvider.TryGetValue(dependency, out var provider)
                ? provider
                : null);
    }

    public static LevelSortResult SortByLevels(
        ImmutableArray<ProviderModel> providers,
        IReadOnlyDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeToProvider)
    {
        if (serviceTypeToProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceTypeToProvider));
        }

        if (providers.IsDefault || providers.IsEmpty)
        {
            return new LevelSortResult([], ImmutableArray<string>.Empty);
        }

        return SortByLevelsIdentityCore(
            providers,
            dependency => serviceTypeToProvider.TryGetValue(dependency, out var provider)
                ? provider
                : null);
    }

    private static LevelSortResult SortByLevelsIdentityCore(
        ImmutableArray<ProviderModel> providers,
        Func<ServiceTypeIdentity, ServiceTypeIdentity?> resolveDependency)
    {
        var graph = BuildIdentityGraph(providers, resolveDependency);
        var levels = new List<List<ProviderModel>>();
        var currentInDegree = new Dictionary<string, int>(graph.InDegree, StringComparer.Ordinal);
        var processed = new HashSet<string>(StringComparer.Ordinal);

        while (processed.Count < graph.Providers.Count)
        {
            var currentLevel = graph.Providers
                .Where(provider =>
                {
                    var nodeKey = GetNodeKey(provider);
                    return !processed.Contains(nodeKey) && currentInDegree[nodeKey] == 0;
                })
                .ToList();

            if (currentLevel.Count == 0)
            {
                var remaining = graph.Providers
                    .Where(provider => !processed.Contains(GetNodeKey(provider)))
                    .Select(GetNodeKey)
                    .ToList();
                var cycleKeys = FindCycle(
                    graph.Adjacency,
                    remaining.FirstOrDefault() ?? string.Empty);
                return new LevelSortResult([], ToProviderNames(cycleKeys, graph))
                {
                    CycleIdentities = ToProviderIdentities(cycleKeys, graph),
                };
            }

            levels.Add(currentLevel);
            foreach (var provider in currentLevel)
            {
                var nodeKey = GetNodeKey(provider);
                processed.Add(nodeKey);
                foreach (var dependent in graph.Adjacency[nodeKey])
                {
                    currentInDegree[dependent]--;
                }
            }
        }

        return new LevelSortResult(levels, ImmutableArray<string>.Empty);
    }

    private static LevelSortResult SortByLevelsCore(
        ImmutableArray<ProviderModel> providers,
        Func<string, string?> resolveDependency)
    {
        var graph = BuildGraph(providers, resolveDependency);
        var levels = new List<List<ProviderModel>>();
        var currentInDegree = new Dictionary<string, int>(graph.InDegree, StringComparer.Ordinal);
        var processed = new HashSet<string>(StringComparer.Ordinal);

        while (processed.Count < graph.Providers.Count)
        {
            var currentLevel = graph.Providers
                .Where(provider =>
                {
                    var nodeKey = GetNodeKey(provider);
                    return !processed.Contains(nodeKey) && currentInDegree[nodeKey] == 0;
                })
                .ToList();

            if (currentLevel.Count == 0)
            {
                var remaining = graph.Providers
                    .Where(provider => !processed.Contains(GetNodeKey(provider)))
                    .Select(GetNodeKey)
                    .ToList();
                var cycle = FindCycle(
                    graph.Adjacency,
                    remaining.FirstOrDefault() ?? string.Empty);
                return new LevelSortResult([], ToProviderNames(cycle, graph));
            }

            levels.Add(currentLevel);
            foreach (var provider in currentLevel)
            {
                var nodeKey = GetNodeKey(provider);
                processed.Add(nodeKey);
                foreach (var dependent in graph.Adjacency[nodeKey])
                {
                    currentInDegree[dependent]--;
                }
            }
        }

        return new LevelSortResult(levels, ImmutableArray<string>.Empty);
    }

    private static SortResult SortGraph(GraphData graph)
    {
        var inDegree = new Dictionary<string, int>(graph.InDegree, StringComparer.Ordinal);
        var queue = new Queue<string>(graph.Providers
            .Where(provider => inDegree[GetNodeKey(provider)] == 0)
            .Select(GetNodeKey));
        var result = ImmutableArray.CreateBuilder<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(graph.ProviderNameByNodeKey[current]);
            foreach (var dependent in graph.Adjacency[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        var remaining = graph.Providers
            .Where(provider => inDegree[GetNodeKey(provider)] > 0)
            .Select(GetNodeKey)
            .ToList();

        if (remaining.Count == 0)
        {
            return new SortResult(result.ToImmutable(), ImmutableArray<string>.Empty);
        }

        return new SortResult(
            ImmutableArray<string>.Empty,
            ToProviderNames(FindCycle(graph.Adjacency, remaining[0]), graph));
    }

    private static GraphData BuildGraph(
        ImmutableArray<ProviderModel> providers,
        Func<string, string?> resolveDependency)
    {
        var providerList = providers
            .GroupBy(GetNodeKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();
        var nodeKeyByProviderName = new Dictionary<string, string>(StringComparer.Ordinal);
        var providerNameByNodeKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var providerIdentityByNodeKey = new Dictionary<string, ServiceTypeIdentity>(StringComparer.Ordinal);
        foreach (var provider in providerList)
        {
            var nodeKey = GetNodeKey(provider);
            if (!nodeKeyByProviderName.ContainsKey(provider.FullyQualifiedName))
            {
                nodeKeyByProviderName.Add(provider.FullyQualifiedName, nodeKey);
            }

            providerNameByNodeKey[nodeKey] = provider.FullyQualifiedName;
            providerIdentityByNodeKey[nodeKey] = new ServiceTypeIdentity(
                provider.FullyQualifiedName,
                provider.AssemblyIdentity);
        }

        var adjacency = providerList
            .Select(GetNodeKey)
            .ToDictionary(
                nodeKey => nodeKey,
                _ => new List<string>(),
                StringComparer.Ordinal);
        var inDegree = providerList
            .Select(GetNodeKey)
            .ToDictionary(
                nodeKey => nodeKey,
                _ => 0,
                StringComparer.Ordinal);

        foreach (var provider in providerList)
        {
            if (provider.Dependencies.IsDefault)
            {
                continue;
            }

            var providerNodeKey = GetNodeKey(provider);
            var resolvedDependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependencyKey in provider.Dependencies)
            {
                var targetProviderName = resolveDependency(dependencyKey);
                if (targetProviderName == null ||
                    !nodeKeyByProviderName.TryGetValue(targetProviderName, out var targetNodeKey))
                {
                    continue;
                }

                if (resolvedDependencies.Add(targetNodeKey))
                {
                    adjacency[targetNodeKey].Add(providerNodeKey);
                    inDegree[providerNodeKey]++;
                }
            }
        }

        return new GraphData(
            providerList,
            providerNameByNodeKey,
            providerIdentityByNodeKey,
            adjacency,
            inDegree);
    }

    private static GraphData BuildIdentityGraph(
        ImmutableArray<ProviderModel> providers,
        Func<ServiceTypeIdentity, ServiceTypeIdentity?> resolveDependency)
    {
        var providerList = providers
            .GroupBy(GetNodeKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();
        var nodeKeyByIdentity = new Dictionary<ServiceTypeIdentity, string>();
        var providerNameByNodeKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var providerIdentityByNodeKey = new Dictionary<string, ServiceTypeIdentity>(StringComparer.Ordinal);
        foreach (var provider in providerList)
        {
            var identity = provider.TypeIdentity;
            var nodeKey = GetNodeKey(identity);
            nodeKeyByIdentity[identity] = nodeKey;
            providerNameByNodeKey[nodeKey] = provider.FullyQualifiedName;
            providerIdentityByNodeKey[nodeKey] = identity;
        }

        var adjacency = providerList
            .Select(GetNodeKey)
            .ToDictionary(
                nodeKey => nodeKey,
                _ => new List<string>(),
                StringComparer.Ordinal);
        var inDegree = providerList
            .Select(GetNodeKey)
            .ToDictionary(
                nodeKey => nodeKey,
                _ => 0,
                StringComparer.Ordinal);

        foreach (var provider in providerList)
        {
            var providerNodeKey = GetNodeKey(provider);
            var resolvedDependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in provider.DependencyIdentities)
            {
                var targetIdentity = resolveDependency(dependency);
                if (targetIdentity is null ||
                    !nodeKeyByIdentity.TryGetValue(targetIdentity.Value, out var targetNodeKey))
                {
                    continue;
                }

                if (resolvedDependencies.Add(targetNodeKey))
                {
                    adjacency[targetNodeKey].Add(providerNodeKey);
                    inDegree[providerNodeKey]++;
                }
            }
        }

        return new GraphData(
            providerList,
            providerNameByNodeKey,
            providerIdentityByNodeKey,
            adjacency,
            inDegree);
    }

    private static ImmutableArray<string> ToProviderNames(
        ImmutableArray<string> nodeKeys,
        GraphData graph)
    {
        return nodeKeys
            .Select(nodeKey => graph.ProviderNameByNodeKey.TryGetValue(nodeKey, out var providerName)
                ? providerName
                : nodeKey)
            .ToImmutableArray();
    }

    private static ImmutableArray<ServiceTypeIdentity> ToProviderIdentities(
        ImmutableArray<string> nodeKeys,
        GraphData graph)
    {
        return nodeKeys
            .Select(nodeKey => graph.ProviderIdentityByNodeKey.TryGetValue(nodeKey, out var identity)
                ? identity
                : new ServiceTypeIdentity(nodeKey, string.Empty))
            .ToImmutableArray();
    }

    private static string GetNodeKey(ProviderModel provider)
    {
        return GetNodeKey(new ServiceTypeIdentity(
            provider.FullyQualifiedName,
            provider.AssemblyIdentity));
    }

    private static string GetNodeKey(ServiceTypeIdentity identity)
    {
        return identity.AssemblyIdentity + "\u001f" + identity.FullyQualifiedName;
    }

    private static ImmutableArray<string> FindCycle(
        Dictionary<string, List<string>> adjacency,
        string startNode)
    {
        if (!adjacency.ContainsKey(startNode))
        {
            return ImmutableArray<string>.Empty;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        return TryFindCycleDFS(adjacency, startNode, visited, path)
            ? path.ToImmutableArray()
            : ImmutableArray<string>.Empty;
    }

    private static bool TryFindCycleDFS(
        Dictionary<string, List<string>> adjacency,
        string node,
        HashSet<string> visited,
        List<string> path)
    {
        var pathIndex = path.IndexOf(node);
        if (pathIndex >= 0)
        {
            var cycle = path.Skip(pathIndex).ToList();
            cycle.Add(node);
            path.Clear();
            path.AddRange(cycle);
            return true;
        }

        if (!visited.Add(node))
        {
            return false;
        }

        path.Add(node);
        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (TryFindCycleDFS(adjacency, neighbor, visited, path))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private readonly record struct GraphData(
        List<ProviderModel> Providers,
        Dictionary<string, string> ProviderNameByNodeKey,
        Dictionary<string, ServiceTypeIdentity> ProviderIdentityByNodeKey,
        Dictionary<string, List<string>> Adjacency,
        Dictionary<string, int> InDegree);
}
