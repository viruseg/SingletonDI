using System.Collections.Generic;
using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

internal readonly record struct ConsumerPropertyNameConflict(
    string PropertyName,
    ServiceReferenceModel FirstReference,
    ServiceReferenceModel SecondReference);

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

    public static ImmutableDictionary<ServiceTypeIdentity, string> ResolvePropertyNamesByIdentity(
        IReadOnlyCollection<ProviderModel> providers)
    {
        if (providers.Count == 0)
        {
            return ImmutableDictionary<ServiceTypeIdentity, string>.Empty;
        }

        var result = ImmutableDictionary.CreateBuilder<ServiceTypeIdentity, string>();
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var shortNameGroups = new HashSet<string>(
            providers
                .Where(provider => string.IsNullOrEmpty(provider.PropertyName))
                .GroupBy(provider => provider.ShortName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            var baseName = !string.IsNullOrEmpty(provider.PropertyName)
                ? provider.PropertyName!
                : shortNameGroups.Contains(provider.ShortName)
                    ? $"{GetNamespacePrefix(provider.Namespace)}_{provider.ShortName}Instance"
                    : $"{provider.ShortName}Instance";
            var propertyName = GetUniquePropertyName(baseName, usedPropertyNames);
            result[provider.TypeIdentity] = propertyName;
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Resolves property names using only the service references of one consumer.
    /// </summary>
    /// <param name="dependencies">The service references consumed by the consumer.</param>
    /// <returns>Property names keyed by service fully qualified name.</returns>
    public static ImmutableDictionary<string, string> ResolveConsumerPropertyNames(
        IEnumerable<ServiceReferenceModel> dependencies)
    {
        return ResolveConsumerPropertyNames(
            dependencies,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string?>.Empty,
            out _);
    }

    public static ImmutableDictionary<string, string> ResolveConsumerPropertyNames(
        IEnumerable<ServiceReferenceModel> dependencies,
        IReadOnlyDictionary<string, string> providerPropertyNames,
        IReadOnlyDictionary<string, string?> customPropertyNames,
        out ImmutableArray<ConsumerPropertyNameConflict> conflicts)
    {
        var references = dependencies.ToList();
        if (references.Count == 0)
        {
            conflicts = ImmutableArray<ConsumerPropertyNameConflict>.Empty;
            return ImmutableDictionary<string, string>.Empty;
        }

        var localPropertyNames = ResolveLocalConsumerPropertyNames(references);
        var preferredPropertyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var propertyName = localPropertyNames[reference.FullyQualifiedName];
            if (!reference.IsContract &&
                providerPropertyNames.TryGetValue(reference.FullyQualifiedName, out var providerPropertyName))
            {
                propertyName = providerPropertyName;
                if (customPropertyNames.TryGetValue(reference.FullyQualifiedName, out var customName) &&
                    !string.IsNullOrEmpty(customName))
                {
                    propertyName = customName!;
                }
            }

            preferredPropertyNames[reference.FullyQualifiedName] = propertyName;
        }

        var result = ImmutableDictionary.CreateBuilder<string, string>();
        var propertyOwners = new Dictionary<string, ServiceReferenceModel>(StringComparer.Ordinal);
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var conflictBuilder = ImmutableArray.CreateBuilder<ConsumerPropertyNameConflict>();

        foreach (var reference in references)
        {
            var propertyName = preferredPropertyNames[reference.FullyQualifiedName];
            if (usedPropertyNames.Add(propertyName))
            {
                propertyOwners[propertyName] = reference;
                result[reference.FullyQualifiedName] = propertyName;
                continue;
            }

            conflictBuilder.Add(new ConsumerPropertyNameConflict(
                propertyName,
                propertyOwners[propertyName],
                reference));
            var fallbackName = GetUniqueFallbackPropertyName(reference, usedPropertyNames);
            result[reference.FullyQualifiedName] = fallbackName;
            propertyOwners[fallbackName] = reference;
        }

        conflicts = conflictBuilder.ToImmutable();
        return result.ToImmutable();
    }

    public static ImmutableDictionary<ServiceTypeIdentity, string> ResolveConsumerPropertyNamesByIdentity(
        IEnumerable<ServiceReferenceModel> dependencies)
    {
        return ResolveConsumerPropertyNamesByIdentity(
            dependencies,
            ImmutableDictionary<ServiceTypeIdentity, string>.Empty,
            ImmutableDictionary<ServiceTypeIdentity, string?>.Empty,
            out _);
    }

    public static ImmutableDictionary<ServiceTypeIdentity, string> ResolveConsumerPropertyNamesByIdentity(
        IEnumerable<ServiceReferenceModel> dependencies,
        IReadOnlyDictionary<ServiceTypeIdentity, string> providerPropertyNames,
        IReadOnlyDictionary<ServiceTypeIdentity, string?> customPropertyNames,
        out ImmutableArray<ConsumerPropertyNameConflict> conflicts)
    {
        var references = dependencies.ToList();
        if (references.Count == 0)
        {
            conflicts = ImmutableArray<ConsumerPropertyNameConflict>.Empty;
            return ImmutableDictionary<ServiceTypeIdentity, string>.Empty;
        }

        var localPropertyNames = ResolveLocalConsumerPropertyNamesByIdentity(references);
        var preferredPropertyNames = new Dictionary<ServiceTypeIdentity, string>();
        foreach (var reference in references)
        {
            var identity = GetReferenceIdentity(reference);
            var propertyName = localPropertyNames[identity];
            if (!reference.IsContract &&
                providerPropertyNames.TryGetValue(identity, out var providerPropertyName))
            {
                propertyName = providerPropertyName;
                if (customPropertyNames.TryGetValue(identity, out var customName) &&
                    !string.IsNullOrEmpty(customName))
                {
                    propertyName = customName!;
                }
            }

            preferredPropertyNames[identity] = propertyName;
        }

        var result = ImmutableDictionary.CreateBuilder<ServiceTypeIdentity, string>();
        var propertyOwners = new Dictionary<string, ServiceReferenceModel>(StringComparer.Ordinal);
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var conflictBuilder = ImmutableArray.CreateBuilder<ConsumerPropertyNameConflict>();

        foreach (var reference in references)
        {
            var identity = GetReferenceIdentity(reference);
            var propertyName = preferredPropertyNames[identity];
            if (usedPropertyNames.Add(propertyName))
            {
                propertyOwners[propertyName] = reference;
                result[identity] = propertyName;
                continue;
            }

            conflictBuilder.Add(new ConsumerPropertyNameConflict(
                propertyName,
                propertyOwners[propertyName],
                reference));
            var fallbackName = GetUniqueFallbackPropertyName(reference, usedPropertyNames);
            result[identity] = fallbackName;
            propertyOwners[fallbackName] = reference;
        }

        conflicts = conflictBuilder.ToImmutable();
        return result.ToImmutable();
    }

    private static ImmutableDictionary<ServiceTypeIdentity, string> ResolveLocalConsumerPropertyNamesByIdentity(
        IReadOnlyCollection<ServiceReferenceModel> references)
    {
        var shortNameConflicts = new HashSet<string>(
            references
                .Where(reference => reference.IsContract || string.IsNullOrEmpty(reference.PropertyName))
                .GroupBy(reference => reference.ShortName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);
        var result = ImmutableDictionary.CreateBuilder<ServiceTypeIdentity, string>();

        foreach (var reference in references)
        {
            var propertyName = !reference.IsContract && !string.IsNullOrEmpty(reference.PropertyName)
                ? reference.PropertyName!
                : shortNameConflicts.Contains(reference.ShortName)
                    ? $"{GetNamespacePrefix(reference.Namespace)}_{reference.ShortName}Instance"
                    : $"{reference.ShortName}Instance";
            result[GetReferenceIdentity(reference)] = propertyName;
        }

        return result.ToImmutable();
    }

    private static ServiceTypeIdentity GetReferenceIdentity(ServiceReferenceModel reference)
    {
        return reference.Identity is { } identity &&
               !string.IsNullOrEmpty(identity.FullyQualifiedName)
            ? identity
            : new ServiceTypeIdentity(reference.FullyQualifiedName, string.Empty);
    }

    private static ImmutableDictionary<string, string> ResolveLocalConsumerPropertyNames(
        IReadOnlyCollection<ServiceReferenceModel> references)
    {
        var shortNameConflicts = new HashSet<string>(
            references
                .Where(reference => reference.IsContract || string.IsNullOrEmpty(reference.PropertyName))
                .GroupBy(reference => reference.ShortName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);
        var result = ImmutableDictionary.CreateBuilder<string, string>();

        foreach (var reference in references)
        {
            string propertyName;
            if (!reference.IsContract && !string.IsNullOrEmpty(reference.PropertyName))
            {
                propertyName = reference.PropertyName!;
            }
            else if (shortNameConflicts.Contains(reference.ShortName))
            {
                propertyName = $"{GetNamespacePrefix(reference.Namespace)}_{reference.ShortName}Instance";
            }
            else
            {
                propertyName = $"{reference.ShortName}Instance";
            }

            result[reference.FullyQualifiedName] = propertyName;
        }

        return result.ToImmutable();
    }

    private static string GetUniquePropertyName(
        string baseName,
        ISet<string> usedPropertyNames)
    {
        var propertyName = baseName;
        var suffix = 2;
        while (!usedPropertyNames.Add(propertyName))
        {
            propertyName = $"{baseName}_{suffix}";
            suffix++;
        }

        return propertyName;
    }

    private static string GetUniqueFallbackPropertyName(
        ServiceReferenceModel reference,
        ISet<string> usedPropertyNames)
    {
        var namespacePrefix = GetNamespacePrefix(reference.Namespace);
        var baseName = string.IsNullOrEmpty(namespacePrefix)
            ? $"{reference.ShortName}Instance"
            : $"{namespacePrefix}_{reference.ShortName}Instance";
        var propertyName = baseName;
        var suffix = 2;
        while (!usedPropertyNames.Add(propertyName))
        {
            propertyName = $"{baseName}_{suffix}";
            suffix++;
        }

        return propertyName;
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
        var names = new List<string>
        {
            // Short name format (always possible)
            $"{provider.ShortName}Instance"
        };

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
        if (string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>")
        {
            return string.Empty;
        }

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
