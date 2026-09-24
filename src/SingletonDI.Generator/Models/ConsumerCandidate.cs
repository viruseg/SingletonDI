using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Models;

internal readonly struct ConsumerCandidate : IEquatable<ConsumerCandidate>
{
    public ConsumerCandidate(
        ConsumerModel? model,
        ImmutableArray<Diagnostic> diagnostics,
        Location declarationLocation,
        ImmutableDictionary<string, Location> existingMemberLocations,
        string cacheKey)
    {
        Model = model;
        Diagnostics = diagnostics;
        DeclarationLocation = declarationLocation;
        ExistingMemberLocations = existingMemberLocations;
        CacheKey = cacheKey;
    }

    public ConsumerModel? Model { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public Location DeclarationLocation { get; }

    public ImmutableDictionary<string, Location> ExistingMemberLocations { get; }

    public string CacheKey { get; }

    public bool Equals(ConsumerCandidate other)
    {
        return string.Equals(CacheKey, other.CacheKey, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ConsumerCandidate other && Equals(other);
    }

    public override int GetHashCode()
    {
        return CacheKey is null ? 0 : StringComparer.Ordinal.GetHashCode(CacheKey);
    }
}
