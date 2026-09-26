using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Emitters;
using SingletonDI.Generator.Models;
using SingletonDI.Generator.Validators;

namespace SingletonDI.Generator.Helpers;

internal static class ProviderSymbolCollector
{
    private const string ProvideAttributeName = "SingletonDI.Attributes.SingletonDIProvideAttribute";
    internal const string ConsumeAttributeName = "SingletonDI.Attributes.SingletonDIConsumeAttribute";
    private const string ProviderModuleMarkerName =
        "SingletonDI.Attributes.SingletonDIProviderModuleAttribute";
    private const string GeneratedModuleNamespace = "SingletonDI.Generated.";
    private const string AttributesAssemblyName = "SingletonDI.Attributes";
    private const string LegacyGeneratedBootstrapTypeName =
        GeneratedModuleNamespace + "__SingletonDIProviderModule__";

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

        var referencedTypes = GetReferencedTypes(compilation, cancellationToken);
        return BuildReferencedProviders(
            compilation,
            referencedTypes,
            cancellationToken,
            reportDiagnostic,
            out providerAssemblies);
    }

    internal static ImmutableArray<ProviderModel> BuildReferencedProviders(
        Compilation compilation,
        ImmutableArray<ReferencedTypeData> referencedTypes,
        CancellationToken cancellationToken,
        Action<Diagnostic> reportDiagnostic,
        out ImmutableArray<ProviderAssemblyModel> providerAssemblies)
    {
        var candidates = new List<ReferencedProviderCandidate>();
        var visitedCandidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var referencedType in referencedTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = referencedType.Type;
            if (!IsAccessibleFrom(type, compilation.Assembly) || !HasAttribute(type, ProvideAttributeName))
            {
                continue;
            }

            var fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
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

        var providers = new List<ProviderModel>();
        var candidateAssemblies = candidates
            .GroupBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var bootstrapTypes = new Dictionary<string, INamedTypeSymbol?>(StringComparer.Ordinal);
        foreach (var group in referencedTypes
                     .GroupBy(
                         referencedType => referencedType.Assembly.Identity.ToString(),
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bootstrapTypes[group.Key] = FindBootstrapType(group.First().Assembly);
        }
        var referencedAssemblies = referencedTypes
            .GroupBy(referencedType => referencedType.Assembly.Identity.ToString(), StringComparer.Ordinal)
            .Select(group => group.First().Assembly)
            .ToList();

        foreach (var candidate in candidates
                     .OrderBy(candidate => candidate.AssemblyIdentity, StringComparer.Ordinal)
                     .ThenBy(candidate => candidate.FullyQualifiedName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = ProviderValidator.Validate(
                candidate.Type,
                compilation,
                Location.None,
                reportDiagnostic);
            if (model is null)
            {
                continue;
            }

            providers.Add(model.Value);
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

            bootstrapTypes.TryGetValue(assemblyIdentity, out var bootstrapType);
            var bootstrapTypeName = bootstrapType?.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
            var hasModuleMarker = assembly.GetAttributes().Any(attribute =>
                IsAttribute(attribute, ProviderModuleMarkerName));
            var hasBootstrapMethod = bootstrapType is not null &&
                                      bootstrapType.DeclaredAccessibility == Accessibility.Public &&
                                      bootstrapType.IsStatic &&
                                      HasPublicBootstrapMethod(bootstrapType);
            assemblyModels.Add(new ProviderAssemblyModel(
                assemblyIdentity,
                hasModuleMarker,
                bootstrapTypeName is null
                    ? null
                    : new ServiceTypeIdentity(bootstrapTypeName, assemblyIdentity),
                hasBootstrapMethod));
        }

        providerAssemblies = assemblyModels
            .OrderBy(model => model.AssemblyIdentity, StringComparer.Ordinal)
            .ToImmutableArray();
        return providers
            .OrderBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ThenBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static INamedTypeSymbol? FindBootstrapType(IAssemblySymbol assembly)
    {
        var assemblyIdentity = assembly.Identity.ToString();
        var metadataNames = new[]
        {
            GeneratedModuleNamespace + ProviderModuleEmitter.GetProviderModuleTypeName(assemblyIdentity),
            GeneratedModuleNamespace + ProviderModuleEmitter.CompositionRootModuleTypeName,
            LegacyGeneratedBootstrapTypeName,
        };

        foreach (var metadataName in metadataNames)
        {
            var type = assembly.GetTypeByMetadataName(metadataName);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static bool HasPublicBootstrapMethod(INamedTypeSymbol type)
    {
        return type.GetMembers("Bootstrap")
            .OfType<IMethodSymbol>()
            .Any(method =>
                method.IsStatic &&
                method.Arity == 0 &&
                method.DeclaredAccessibility == Accessibility.Public &&
                method.Parameters.Length == 0 &&
                method.ReturnsVoid);
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

    internal static ImmutableArray<ReferencedTypeData> GetReferencedTypes(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var types = new List<ReferencedTypeData>();
        foreach (var assembly in GetCandidateAssemblies(compilation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasAttribute(type, ProvideAttributeName) &&
                    !HasAttribute(type, ConsumeAttributeName))
                {
                    continue;
                }

                types.Add(new ReferencedTypeData(assembly, type));
            }
        }

        return types
            .OrderBy(type => type.Assembly.Identity.ToString(), StringComparer.Ordinal)
            .ThenBy(type => type.Type.ToDisplayString(SymbolDisplayFormats.CodeGeneration), StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static ImmutableArray<IAssemblySymbol> GetCandidateAssemblies(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        return GetReferencedAssemblies(compilation, cancellationToken)
            .Where(ReferencesAttributesAssembly)
            .ToImmutableArray();
    }

    /// <summary>
    /// Reports whether an assembly can carry a SingletonDI attribute. A type can only be
    /// annotated with an attribute it references, so assemblies that never reference the
    /// attribute assembly are skipped before their metadata is enumerated.
    /// </summary>
    private static bool ReferencesAttributesAssembly(IAssemblySymbol assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var referencedAssembly in module.ReferencedAssemblySymbols)
            {
                if (string.Equals(
                        referencedAssembly.Identity.Name,
                        AttributesAssemblyName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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

    /// <summary>
    /// Reports whether a referenced provider is nameable and usable from the compilation doing the
    /// importing.
    /// </summary>
    /// <remarks>
    /// Assembly accessibility counts here, not just <see cref="Accessibility.Public"/>. A provider
    /// that is internal is still importable when its assembly names the importing assembly as a
    /// friend, and filtering it out left a registered provider invisible, so the root reported a
    /// consumer dependency as unmapped.
    /// </remarks>
    private static bool IsAccessibleFrom(INamedTypeSymbol type, IAssemblySymbol importingAssembly)
    {
        var friendAssemblies = GetFriendAssemblies(type.ContainingAssembly);
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility == Accessibility.Public)
            {
                continue;
            }

            if (current.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }

            if (current.ContainingType is not null)
            {
                return false;
            }

            if (!friendAssemblies.Contains(importingAssembly.Identity.Name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the simple assembly names a referenced assembly names as friends through
    /// <c>InternalsVisibleTo</c>.
    /// </summary>
    private static HashSet<string> GetFriendAssemblies(IAssemblySymbol assembly)
    {
        var friends = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() !=
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute" ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string friendName)
            {
                continue;
            }

            // The argument may carry a public key, which the identity name does not.
            var comma = friendName.IndexOf(',');
            friends.Add((comma < 0 ? friendName : friendName.Substring(0, comma)).Trim());
        }

        return friends;
    }

    private static string CreateProviderKey(string assemblyIdentity, string fullyQualifiedName)
    {
        return assemblyIdentity + "\u001f" + fullyQualifiedName;
    }

    internal readonly record struct ReferencedTypeData(
        IAssemblySymbol Assembly,
        INamedTypeSymbol Type);

    private sealed record ReferencedProviderCandidate(
        INamedTypeSymbol Type,
        string AssemblyIdentity,
        string FullyQualifiedName);
}
