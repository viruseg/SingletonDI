using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Models;
using SingletonDI.Generator.Validators;

namespace SingletonDI.Generator.Helpers;

internal static class ProviderSymbolCollector
{
    private const string ProvideAttributeName = "SingletonDI.Attributes.SingletonDIProvideAttribute";
    private const string ProviderModuleMarkerName =
        "SingletonDI.Attributes.SingletonDIProviderModuleAttribute";

    internal static ImmutableArray<ProviderModel> CollectReferencedProviders(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        return CollectReferencedProviders(
            compilation,
            cancellationToken,
            static _ => { },
            out _);
    }

    internal static ImmutableArray<ProviderModel> CollectReferencedProviders(
        Compilation compilation,
        CancellationToken cancellationToken,
        Action<Diagnostic> reportDiagnostic,
        out ImmutableArray<ProviderAssemblyModel> providerAssemblies)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (reportDiagnostic is null)
        {
            throw new ArgumentNullException(nameof(reportDiagnostic));
        }

        var referencedAssemblies = ProviderSymbolCollector
            .GetReferencedAssemblies(compilation, cancellationToken)
            .ToList();
        var candidates = new List<ReferencedProviderCandidate>();
        var visitedCandidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in referencedAssemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAccessiblePublic(type) || !HasAttribute(type, ProvideAttributeName))
                {
                    continue;
                }

                var fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var assemblyIdentity = type.ContainingAssembly.Identity.ToString();
                var candidateKey = CreateProviderKey(assemblyIdentity, fullyQualifiedName);
                if (visitedCandidates.Add(candidateKey))
                {
                    candidates.Add(new ReferencedProviderCandidate(
                        type,
                        assemblyIdentity,
                        fullyQualifiedName));
                }
            }
        }

        var knownProviderNames = candidates
            .Select(candidate => candidate.FullyQualifiedName)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var providers = new List<ProviderModel>();
        var providersByAssembly = new Dictionary<string, List<ProviderModel>>(StringComparer.Ordinal);
        var candidateAssemblies = candidates
            .GroupBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var candidate in candidates
                     .OrderBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.FullyQualifiedName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = ProviderValidator.Validate(
                candidate.Type,
                compilation,
                Location.None,
                knownProviderNames,
                reportDiagnostic);
            if (model is null)
            {
                continue;
            }

            providers.Add(model.Value);
            if (!providersByAssembly.TryGetValue(candidate.AssemblyIdentity, out var assemblyProviders))
            {
                assemblyProviders = [];
                providersByAssembly.Add(candidate.AssemblyIdentity, assemblyProviders);
            }

            assemblyProviders.Add(model.Value);
        }

        var assemblyModels = new List<ProviderAssemblyModel>();
        foreach (var assembly in referencedAssemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assemblyIdentity = assembly.Identity.ToString();
            if (!candidateAssemblies.ContainsKey(assemblyIdentity))
            {
                continue;
            }

            ProviderModel? bootstrapProvider = providersByAssembly.TryGetValue(
                assemblyIdentity,
                out var assemblyProviders)
                ? assemblyProviders
                    .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
                    .First()
                : null;
            var hasModuleMarker = assembly.GetAttributes().Any(attribute =>
                IsAttribute(attribute, ProviderModuleMarkerName));
            assemblyModels.Add(new ProviderAssemblyModel(
                assemblyIdentity,
                bootstrapProvider?.FullyQualifiedName ?? string.Empty,
                hasModuleMarker,
                bootstrapProvider?.TypeIdentity));
        }

        providerAssemblies = assemblyModels
            .OrderBy(model => model.AssemblyIdentity, StringComparer.Ordinal)
            .ToImmutableArray();
        return providers
            .OrderBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ThenBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static IEnumerable<IAssemblySymbol> GetReferencedAssemblies(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var currentAssemblyIdentity = compilation.Assembly.Identity.ToString();
        var pending = new Queue<IAssemblySymbol>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Enqueue(IAssemblySymbol? assembly)
        {
            if (assembly is null)
            {
                return;
            }

            var identity = assembly.Identity.ToString();
            if (string.Equals(identity, currentAssemblyIdentity, StringComparison.Ordinal) ||
                !visited.Add(identity))
            {
                return;
            }

            pending.Enqueue(assembly);
        }

        foreach (var reference in compilation.References
                     .OrderBy(reference => reference.Display, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                Enqueue(assembly);
            }
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assembly = pending.Dequeue();
            yield return assembly;

            foreach (var module in assembly.Modules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var referencedAssembly in module.ReferencedAssemblySymbols)
                {
                    Enqueue(referencedAssembly);
                }
            }
        }
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol symbol)
    {
        if (symbol is INamespaceSymbol namespaceSymbol)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                if (member is INamespaceOrTypeSymbol namespaceOrType)
                {
                    foreach (var type in EnumerateTypes(namespaceOrType))
                    {
                        yield return type;
                    }
                }
            }

            yield break;
        }

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            yield break;
        }

        yield return typeSymbol;
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            foreach (var nestedTypeSymbol in EnumerateTypes(nestedType))
            {
                yield return nestedTypeSymbol;
            }
        }
    }

    internal static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute => IsAttribute(attribute, metadataName));
    }

    internal static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
        {
            return false;
        }

        var displayName = attributeClass.ToDisplayString();
        return string.Equals(displayName, metadataName, StringComparison.Ordinal) ||
               string.Equals(displayName, "global::" + metadataName, StringComparison.Ordinal);
    }

    private static bool IsAccessiblePublic(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static string CreateProviderKey(string assemblyIdentity, string fullyQualifiedName)
    {
        return assemblyIdentity + "\u001f" + fullyQualifiedName;
    }

    private sealed record ReferencedProviderCandidate(
        INamedTypeSymbol Type,
        string AssemblyIdentity,
        string FullyQualifiedName);
}
