using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Resolves property names for consumers, handling namespace conflicts.
/// If multiple providers have the same short name, uses Namespace_TypeName format.
/// </summary>
internal static class PropertyNameResolver
{
    /// <summary>
    /// Mapping from provider FQN to resolved property name.
    /// </summary>
    public static ImmutableDictionary<string, string> ResolvePropertyNames(
        List<ProviderModel> providers)
    {
        if (providers.Count == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var result = ImmutableDictionary.CreateBuilder<string, string>();

        // First, process providers with custom property names (they don't participate in grouping)
        foreach (var provider in providers.Where(p => !string.IsNullOrEmpty(p.PropertyName)))
        {
            result[provider.FullyQualifiedName] = provider.PropertyName!;
        }

        // Then, group only providers without custom names
        var providersNeedingName = providers.Where(p => string.IsNullOrEmpty(p.PropertyName)).ToList();
        var shortNameGroups = providersNeedingName
            .GroupBy(p => p.ShortName)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var provider in providersNeedingName)
        {
            var shortName = provider.ShortName;
            var group = shortNameGroups[shortName];

            string propertyName;
            if (group.Count == 1)
            {
                // No conflict - use short name
                propertyName = shortName;
            }
            else
            {
                // Conflict - use Namespace_TypeName format
                var namespacePrefix = GetNamespacePrefix(provider.Namespace);
                propertyName = $"{namespacePrefix}_{shortName}";
            }

            result[provider.FullyQualifiedName] = propertyName;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Gets a namespace prefix by replacing dots with underscores and removing
    /// common prefixes like "global::".
    /// </summary>
    private static string GetNamespacePrefix(string namespaceName)
    {
        // Remove "global::" prefix if present
        var cleaned = namespaceName;

        if (cleaned.StartsWith("global::"))
        {
            cleaned = cleaned.Substring("global::".Length);
        }

        // Replace dots with underscores
        return cleaned.Replace('.', '_');
    }

    /// <summary>
    /// Generates property declaration with proper type reference.
    /// </summary>
    public static string GeneratePropertyDeclaration(
        string propertyName,
        string providerFullyQualifiedName,
        bool useNullable = true)
    {
        var nullableMarker = useNullable ? "?" : "";

        // Format the type name
        var typeName = FormatTypeName(providerFullyQualifiedName);

        return $"public {typeName}{nullableMarker} {propertyName} {{ get; }}";
    }

    /// <summary>
    /// Formats a fully qualified type name for use in generated code.
    /// </summary>
    public static string FormatTypeName(string fullyQualifiedName)
    {
        // Remove "global::" prefix if present
        if (fullyQualifiedName.StartsWith("global::"))
        {
            return fullyQualifiedName.Substring("global::".Length);
        }

        return fullyQualifiedName;
    }
}
