using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Performs topological sorting using Kahn's algorithm and detects provider cycles.
/// </summary>
internal static class TopologicalSorter
{
    public static ImmutableArray<ServiceTypeIdentity> TryFindCycle(
        ImmutableArray<ProviderModel> providers,
        IReadOnlyDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeToProvider)
    {
        if (serviceTypeToProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceTypeToProvider));
        }

        if (providers.IsDefault || providers.IsEmpty)
        {
            return ImmutableArray<ServiceTypeIdentity>.Empty;
        }

        var graph = BuildIdentityGraph(
            providers,
            dependency => serviceTypeToProvider.TryGetValue(dependency, out var provider)
                ? provider
                : null);
        var inDegree = new Dictionary<string, int>(graph.InDegree, StringComparer.Ordinal);
        var queue = new Queue<string>(graph.Providers
            .Where(provider => inDegree[GetNodeKey(provider)] == 0)
            .Select(GetNodeKey));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
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
            .ToImmutableArray();
        if (remaining.IsEmpty)
        {
            return ImmutableArray<ServiceTypeIdentity>.Empty;
        }

        return ToProviderIdentities(FindCycleDeterministically(graph, remaining), graph);
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
        var providerIdentityByNodeKey = new Dictionary<string, ServiceTypeIdentity>(StringComparer.Ordinal);
        foreach (var provider in providerList)
        {
            var identity = provider.TypeIdentity;
            var nodeKey = GetNodeKey(identity);
            nodeKeyByIdentity[identity] = nodeKey;
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
            providerIdentityByNodeKey,
            adjacency,
            inDegree);
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

    private static ImmutableArray<string> FindCycleDeterministically(
        GraphData graph,
        IEnumerable<string> startNodes)
    {
        var states = new Dictionary<string, CycleVisitState>(StringComparer.Ordinal);
        var path = new List<string>();
        var pathIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var startNode in startNodes)
        {
            if (states.ContainsKey(startNode))
            {
                continue;
            }

            if (TryFindCycleDFS(
                    graph.Adjacency,
                    startNode,
                    states,
                    path,
                    pathIndices,
                    out var cycle))
            {
                return cycle;
            }
        }

        return ImmutableArray<string>.Empty;
    }

    private static bool TryFindCycleDFS(
        Dictionary<string, List<string>> adjacency,
        string node,
        Dictionary<string, CycleVisitState> states,
        List<string> path,
        Dictionary<string, int> pathIndices,
        out ImmutableArray<string> cycle)
    {
        states[node] = CycleVisitState.Visiting;
        pathIndices[node] = path.Count;
        path.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (states.TryGetValue(neighbor, out var state))
                {
                    if (state == CycleVisitState.Visited)
                    {
                        continue;
                    }

                    var cycleStart = pathIndices[neighbor];
                    cycle = path
                        .Skip(cycleStart)
                        .Append(neighbor)
                        .ToImmutableArray();
                    return true;
                }

                if (TryFindCycleDFS(
                        adjacency,
                        neighbor,
                        states,
                        path,
                        pathIndices,
                        out cycle))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        pathIndices.Remove(node);
        states[node] = CycleVisitState.Visited;
        cycle = ImmutableArray<string>.Empty;
        return false;
    }

    private enum CycleVisitState : byte
    {
        Visiting,
        Visited
    }

    private readonly record struct GraphData(
        List<ProviderModel> Providers,
        Dictionary<string, ServiceTypeIdentity> ProviderIdentityByNodeKey,
        Dictionary<string, List<string>> Adjacency,
        Dictionary<string, int> InDegree);
}
