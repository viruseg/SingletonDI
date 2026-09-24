using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

internal static class ReferencedConsumerCollector
{
    private const string ConsumeAttributeName = "SingletonDI.Attributes.SingletonDIConsumeAttribute";

    internal static ImmutableArray<ImmutableArray<string>> CollectReferencedConsumerDependencies(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        return CollectReferencedConsumerDependencyIdentities(compilation, cancellationToken)
            .Select(dependencies => dependencies
                .Select(dependency => dependency.FullyQualifiedName)
                .ToImmutableArray())
            .ToImmutableArray();
    }

    internal static ImmutableArray<ImmutableArray<ServiceTypeIdentity>>
        CollectReferencedConsumerDependencyIdentities(
            Compilation compilation,
            CancellationToken cancellationToken)
    {
        var dependencies = new List<ImmutableArray<ServiceTypeIdentity>>();
        foreach (var assembly in ProviderSymbolCollector.GetReferencedAssemblies(
                     compilation,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in ProviderSymbolCollector.EnumerateTypes(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var attribute in type.GetAttributes())
                {
                    if (!ProviderSymbolCollector.IsAttribute(attribute, ConsumeAttributeName))
                    {
                        continue;
                    }

                    var keys = ReadDependencyIdentities(attribute)
                        .Distinct()
                        .OrderBy(dependency => dependency.FullyQualifiedName, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.AssemblyIdentity, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.CanonicalIdentity, StringComparer.Ordinal)
                        .ToImmutableArray();
                    dependencies.Add(keys);
                }
            }
        }

        return dependencies
            .Where(keys => !keys.IsEmpty)
            .GroupBy(
                keys => string.Join(
                    "\u001f",
                    keys.Select(key => $"{key.FullyQualifiedName}\u001e{key.AssemblyIdentity}")),
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(
                keys => string.Join(
                    "\u001f",
                    keys.Select(key => $"{key.FullyQualifiedName}\u001e{key.AssemblyIdentity}")),
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static IEnumerable<ServiceTypeIdentity> ReadDependencyIdentities(AttributeData attribute)
    {
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var value in argument.Values)
                {
                    if (value is { Kind: TypedConstantKind.Type, Value: ITypeSymbol type })
                    {
                        yield return CreateIdentity(type);
                    }
                }
            }
            else if (argument is { Kind: TypedConstantKind.Type, Value: ITypeSymbol singleType })
            {
                yield return CreateIdentity(singleType);
            }
        }
    }

    private static ServiceTypeIdentity CreateIdentity(ITypeSymbol type)
    {
        return ServiceTypeIdentity.FromSymbol(type);
    }
}
