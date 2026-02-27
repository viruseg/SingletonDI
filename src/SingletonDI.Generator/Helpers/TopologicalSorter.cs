using System.Collections.Immutable;
using System.Diagnostics;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Performs topological sorting using Kahn's algorithm (BFS).
/// Detects circular dependencies between singleton providers.
/// </summary>
internal static class TopologicalSorter
{
    /// <summary>
    /// Result of topological sorting.
    /// </summary>
    public readonly struct SortResult
    {
        public required ImmutableArray<string> SortedOrder { get; init; }
        public required ImmutableArray<string> Cycle { get; init; }
        public bool HasCycle => !Cycle.IsEmpty;
    }

    /// <summary>
    /// Performs topological sort on providers based on their dependencies.
    /// Only providers that are also consumers are included in the graph.
    /// </summary>
    /// <param name="providers">All providers to sort.</param>
    /// <returns>Sort result with ordered FQNs or detected cycle.</returns>
    public static SortResult Sort(ImmutableArray<ProviderModel> providers)
    {
        if (providers.IsEmpty)
        {
            return new SortResult
            {
                SortedOrder = ImmutableArray<string>.Empty,
                Cycle = ImmutableArray<string>.Empty
            };
        }

        // Convert to list
        var validProviders = providers.ToList();

        if (validProviders.Count == 0)
        {
            return new SortResult
            {
                SortedOrder = ImmutableArray<string>.Empty,
                Cycle = ImmutableArray<string>.Empty
            };
        }

        // Build dependency graph: only include providers that have dependencies
        var providerDict = new Dictionary<string, ProviderModel>();
        foreach (var p in validProviders)
        {
            providerDict[p.FullyQualifiedName] = p;
        }

        // Build adjacency list and in-degree count
        // Edge from A to B means A depends on B (B must be initialized before A)
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        foreach (var provider in validProviders)
        {
            inDegree[provider.FullyQualifiedName] = 0;
            adjacency[provider.FullyQualifiedName] = new List<string>();
        }

        // Build edges: for each provider that depends on other providers
        foreach (var provider in validProviders)
        {
            if (provider.Dependencies.IsEmpty)
                continue;

            foreach (var dep in provider.Dependencies)
            {
                if (!providerDict.ContainsKey(dep))
                    continue; // Dependency not in current providers

                // Edge: dep -> provider (dep must come before provider)
                adjacency[dep].Add(provider.FullyQualifiedName);
                inDegree[provider.FullyQualifiedName]++;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>();

        // Start with nodes that have no incoming edges
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
            {
                queue.Enqueue(kvp.Key);
            }
        }

        var result = ImmutableArray.CreateBuilder<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        // Check for cycle
        var remainingWithDegree = inDegree.Where(kvp => kvp.Value > 0).Select(kvp => kvp.Key).ToList();

        if (remainingWithDegree.Count > 0)
        {
            // Cycle detected - find the cycle path
            var cycle = FindCycle(adjacency, remainingWithDegree[0]);

            return new SortResult
            {
                SortedOrder = ImmutableArray<string>.Empty,
                Cycle = cycle
            };
        }

        return new SortResult
        {
            SortedOrder = result.ToImmutable(),
            Cycle = ImmutableArray<string>.Empty
        };
    }

    /// <summary>
    /// Finds a cycle path starting from the given node using DFS.
    /// </summary>
    private static ImmutableArray<string> FindCycle(Dictionary<string, List<string>> adjacency, string startNode)
    {
        var visited = new HashSet<string>();
        var path = new List<string>();

        if (TryFindCycleDFS(adjacency, startNode, visited, path, startNode))
        {
            return ImmutableArray.CreateRange(path);
        }

        return ImmutableArray<string>.Empty;
    }

    private static bool TryFindCycleDFS(
        Dictionary<string, List<string>> adjacency,
        string node,
        HashSet<string> visited,
        List<string> path,
        string startNode)
    {
        if (path.Contains(node))
        {
            // Found cycle - trim path to start from the cycle start
            var cycleStartIndex = path.IndexOf(node);
            var cycle = path.Skip(cycleStartIndex).ToList();
            cycle.Add(node); // Close the cycle
            path.Clear();
            path.AddRange(cycle);
            return true;
        }

        if (visited.Contains(node))
            return false;

        visited.Add(node);
        path.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (TryFindCycleDFS(adjacency, neighbor, visited, path, startNode))
                    return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }
}
