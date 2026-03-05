using System.Collections.Generic;
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

        // Step 1: Find ShortName conflicts among providers WITHOUT custom names
        // Only providers without custom names participate in ShortName conflicts
        var shortNameGroups = new HashSet<string>(
            providers
                .Where(p => string.IsNullOrEmpty(p.PropertyName))
                .GroupBy(p => p.ShortName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key));

        // Step 2: Resolve names for all providers
        foreach (var provider in providers)
        {
            string finalName;

            if (!string.IsNullOrEmpty(provider.PropertyName))
            {
                // Provider with custom name uses it directly (no suffix)
                finalName = provider.PropertyName!;
            }
            else if (shortNameGroups.Contains(provider.ShortName))
            {
                // ShortName conflict - use namespace prefix with Instance suffix
                var namespacePrefix = GetNamespacePrefix(provider.Namespace);
                finalName = $"{namespacePrefix}_{provider.ShortName}Instance";
            }
            else
            {
                // No conflict - use ShortName with Instance suffix
                finalName = $"{provider.ShortName}Instance";
            }

            result[provider.FullyQualifiedName] = finalName;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Resolves auto-generated property names for all providers, ignoring custom names.
    /// This is used for DM0003 validation to detect conflicts between custom names
    /// and auto-generated names.
    /// </summary>
    public static ImmutableDictionary<string, string> ResolveGeneratedPropertyNames(
        List<ProviderModel> providers)
    {
        if (providers.Count == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var result = ImmutableDictionary.CreateBuilder<string, string>();

        // Find ShortName conflicts among ALL providers (ignoring custom names)
        var shortNameGroups = new HashSet<string>(
            providers
                .GroupBy(p => p.ShortName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key));

        // Resolve names for all providers as if they had no custom names
        foreach (var provider in providers)
        {
            string generatedName;

            if (shortNameGroups.Contains(provider.ShortName))
            {
                // ShortName conflict - use namespace prefix with Instance suffix
                var namespacePrefix = GetNamespacePrefix(provider.Namespace);
                generatedName = $"{namespacePrefix}_{provider.ShortName}Instance";
            }
            else
            {
                // No conflict - use ShortName with Instance suffix
                generatedName = $"{provider.ShortName}Instance";
            }

            result[provider.FullyQualifiedName] = generatedName;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Gets ALL possible generated property names for a provider.
    /// This includes both short name format and full name format (with namespace prefix).
    /// Used for DM0003 validation to catch conflicts with any potential generated name.
    /// </summary>
    public static IReadOnlyList<string> GetAllPossibleGeneratedNames(ProviderModel provider)
    {
        var names = new List<string>();

        // Short name format (always possible)
        names.Add($"{provider.ShortName}Instance");

        // Full name format (with namespace prefix)
        var namespacePrefix = GetNamespacePrefix(provider.Namespace);
        if (!string.IsNullOrEmpty(namespacePrefix))
        {
            names.Add($"{namespacePrefix}_{provider.ShortName}Instance");
        }

        return names;
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
