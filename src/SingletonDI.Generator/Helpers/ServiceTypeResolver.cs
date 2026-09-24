using System.Collections.Immutable;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

internal readonly record struct ServiceTypeMapResult(
    ImmutableDictionary<string, string> Map,
    ImmutableArray<ServiceTypeConflict> Conflicts)
{
    public ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity> IdentityMap { get; init; } =
        ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity>.Empty;
}

internal readonly record struct ServiceTypeConflict(
    string ServiceTypeFullyQualifiedName,
    ImmutableArray<string> ProviderFullyQualifiedNames)
{
    public ServiceTypeIdentity? ServiceTypeIdentity { get; init; }

    public ImmutableArray<ServiceTypeIdentity> ProviderTypeIdentities { get; init; } =
        ImmutableArray<ServiceTypeIdentity>.Empty;

    public ImmutableArray<ServiceTypeIdentity> ServiceTypeIdentities { get; init; } =
        ImmutableArray<ServiceTypeIdentity>.Empty;
}

/// <summary>
/// Builds a map from concrete and contract service keys to provider identities.
/// </summary>
internal static class ServiceTypeResolver
{
    /// <summary>
    /// Builds concrete and contract service keys for the supplied providers.
    /// </summary>
    /// <param name="providers">Providers to include in the service map.</param>
    /// <returns>The service map and all duplicate-key conflicts.</returns>
    public static ServiceTypeMapResult BuildServiceTypeMap(IReadOnlyCollection<ProviderModel> providers)
    {
        var legacyMappings = new Dictionary<string, List<ProviderIdentity>>(StringComparer.Ordinal);
        var identityMappings = new Dictionary<ServiceTypeIdentity, List<ServiceTypeIdentity>>();

        foreach (var provider in providers
                     .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
                     .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal))
        {
            var providerIdentity = provider.TypeIdentity;
            AddLegacyMapping(legacyMappings, provider.FullyQualifiedName, provider);
            AddIdentityMapping(identityMappings, providerIdentity, providerIdentity);

            var serviceTypeIdentity = provider.ServiceTypeIdentity;
            if (serviceTypeIdentity is not null)
            {
                AddLegacyMapping(
                    legacyMappings,
                    serviceTypeIdentity.Value.FullyQualifiedName,
                    provider);
                AddIdentityMapping(
                    identityMappings,
                    serviceTypeIdentity.Value,
                    providerIdentity);
            }
        }

        var mapBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var mapping in legacyMappings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var providerIdentities = mapping.Value
                .Distinct()
                .ToImmutableArray();
            mapBuilder[mapping.Key] = providerIdentities[0].FullyQualifiedName;
        }

        var identityMapBuilder = ImmutableDictionary.CreateBuilder<ServiceTypeIdentity, ServiceTypeIdentity>();
        var conflictBuilder = ImmutableArray.CreateBuilder<ServiceTypeConflict>();
        foreach (var mapping in identityMappings
                     .OrderBy(pair => pair.Key.FullyQualifiedName, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.AssemblyIdentity, StringComparer.Ordinal))
        {
            var providerIdentities = mapping.Value
                .Distinct()
                .OrderBy(identity => identity.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(identity => identity.AssemblyIdentity, StringComparer.Ordinal)
                .ToImmutableArray();
            identityMapBuilder[mapping.Key] = providerIdentities[0];

            if (providerIdentities.Length > 1)
            {
                conflictBuilder.Add(new ServiceTypeConflict(
                    mapping.Key.FullyQualifiedName,
                    providerIdentities
                        .Select(identity => identity.FullyQualifiedName)
                        .Distinct(StringComparer.Ordinal)
                        .ToImmutableArray())
                {
                    ServiceTypeIdentity = mapping.Key,
                    ProviderTypeIdentities = providerIdentities,
                });
            }
        }

        var ambiguousSourceNames = identityMappings.Keys
            .Concat(identityMappings.Values.SelectMany(identities => identities))
            .GroupBy(identity => identity.FullyQualifiedName, StringComparer.Ordinal)
            .Where(group => group
                .Distinct()
                .Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in ambiguousSourceNames)
        {
            var identities = group
                .Distinct()
                .OrderBy(identity => identity.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(identity => identity.AssemblyIdentity, StringComparer.Ordinal)
                .ThenBy(identity => identity.CanonicalIdentity, StringComparer.Ordinal)
                .ToImmutableArray();
            var providerIdentities = providers
                .SelectMany(provider => provider.ServiceTypeIdentity is { } serviceIdentity
                    ? new[] { provider.TypeIdentity, serviceIdentity }
                    : new[] { provider.TypeIdentity })
                .Where(identity => identities.Contains(identity))
                .Distinct()
                .OrderBy(identity => identity.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(identity => identity.AssemblyIdentity, StringComparer.Ordinal)
                .ToImmutableArray();
            conflictBuilder.Add(new ServiceTypeConflict(
                group.Key,
                identities
                    .Select(identity => identity.FullyQualifiedName)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray())
            {
                ServiceTypeIdentities = identities,
                ProviderTypeIdentities = providerIdentities,
            });
        }

        return new ServiceTypeMapResult(mapBuilder.ToImmutable(), conflictBuilder.ToImmutable())
        {
            IdentityMap = identityMapBuilder.ToImmutable(),
        };
    }

    private static void AddLegacyMapping(
        Dictionary<string, List<ProviderIdentity>> mappings,
        string serviceKey,
        ProviderModel provider)
    {
        if (!mappings.TryGetValue(serviceKey, out var identities))
        {
            identities = [];
            mappings.Add(serviceKey, identities);
        }

        identities.Add(new ProviderIdentity(provider.FullyQualifiedName, provider.AssemblyIdentity));
    }

    private static void AddIdentityMapping(
        Dictionary<ServiceTypeIdentity, List<ServiceTypeIdentity>> mappings,
        ServiceTypeIdentity serviceKey,
        ServiceTypeIdentity providerIdentity)
    {
        if (!mappings.TryGetValue(serviceKey, out var identities))
        {
            identities = [];
            mappings.Add(serviceKey, identities);
        }

        identities.Add(providerIdentity);
    }

    private readonly record struct ProviderIdentity(string FullyQualifiedName, string AssemblyIdentity);
}
