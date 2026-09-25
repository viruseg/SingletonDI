using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Helpers;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Identifies a CLR type by its fully qualified metadata name, containing assembly identity, and recursive constructed-type identity.
/// </summary>
public readonly struct ServiceTypeIdentity : IEquatable<ServiceTypeIdentity>
{
    private readonly string? canonicalIdentity;
    private readonly string? typeArgumentDisplay;

    /// <summary>
    /// Initializes a non-constructed or legacy service type identity.
    /// </summary>
    /// <param name="fullyQualifiedName">The fully qualified metadata name of the type.</param>
    /// <param name="assemblyIdentity">The identity of the assembly containing the type.</param>
    public ServiceTypeIdentity(string fullyQualifiedName, string assemblyIdentity)
        : this(fullyQualifiedName, assemblyIdentity, null, null)
    {
    }

    internal ServiceTypeIdentity(
        string fullyQualifiedName,
        string assemblyIdentity,
        string? canonicalIdentity,
        string? typeArgumentDisplay)
    {
        FullyQualifiedName = fullyQualifiedName;
        AssemblyIdentity = assemblyIdentity;
        this.canonicalIdentity = canonicalIdentity;
        this.typeArgumentDisplay = typeArgumentDisplay;
    }

    /// <summary>
    /// Gets the fully qualified metadata name of the type.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>
    /// Gets the identity of the assembly containing the type.
    /// </summary>
    public string AssemblyIdentity { get; }

    internal string CanonicalIdentity =>
        canonicalIdentity ?? CreateDefaultCanonicalIdentity(FullyQualifiedName, AssemblyIdentity);

    /// <summary>
    /// Returns the assembly-qualified diagnostic representation of the type.
    /// </summary>
    public override string ToString()
    {
        return $"{FullyQualifiedName}, {AssemblyIdentity}";
    }

    internal string ToDiagnosticString()
    {
        var typeArgumentSuffix = string.IsNullOrEmpty(typeArgumentDisplay)
            ? string.Empty
            : $" [{typeArgumentDisplay}]";
        return $"{FullyQualifiedName}, {AssemblyIdentity}{typeArgumentSuffix}";
    }

    internal string ToGlobalTypeName()
    {
        return FullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? FullyQualifiedName
            : $"global::{FullyQualifiedName}";
    }

    /// <summary>
    /// Determines whether two service identities represent the same CLR type.
    /// </summary>
    /// <param name="other">The identity to compare.</param>
    /// <returns><see langword="true"/> when the identities are equal.</returns>
    public bool Equals(ServiceTypeIdentity other)
    {
        return string.Equals(CanonicalIdentity, other.CanonicalIdentity, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ServiceTypeIdentity other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(CanonicalIdentity);
    }

    /// <summary>
    /// Determines whether two service identities represent the same CLR type.
    /// </summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns><see langword="true"/> when the identities are equal.</returns>
    public static bool operator ==(ServiceTypeIdentity left, ServiceTypeIdentity right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two service identities represent different CLR types.
    /// </summary>
    /// <param name="left">The first identity.</param>
    /// <param name="right">The second identity.</param>
    /// <returns><see langword="true"/> when the identities are different.</returns>
    public static bool operator !=(ServiceTypeIdentity left, ServiceTypeIdentity right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Deconstructs the identity into its display name and assembly identity.
    /// </summary>
    /// <param name="fullyQualifiedName">The fully qualified metadata name.</param>
    /// <param name="assemblyIdentity">The containing assembly identity.</param>
    public void Deconstruct(out string fullyQualifiedName, out string assemblyIdentity)
    {
        fullyQualifiedName = FullyQualifiedName;
        assemblyIdentity = AssemblyIdentity;
    }

    internal static ServiceTypeIdentity FromSymbol(ITypeSymbol type)
    {
        var fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
        var assemblyIdentity = type.ContainingAssembly?.Identity.ToString() ?? string.Empty;
        return new ServiceTypeIdentity(
            fullyQualifiedName,
            assemblyIdentity,
            CreateSymbolCanonicalIdentity(type),
            CreateTypeArgumentDisplay(type));
    }

    private static string CreateSymbolCanonicalIdentity(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return $"array:{arrayType.Rank}:{CreateSymbolCanonicalIdentity(arrayType.ElementType)}";
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return $"pointer:{CreateSymbolCanonicalIdentity(pointerType.PointedAtType)}";
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return CreateDefaultCanonicalIdentity(
                type.ToDisplayString(SymbolDisplayFormats.CodeGeneration),
                type.ContainingAssembly?.Identity.ToString() ?? string.Empty);
        }

        var hasConstructedContext = namedType.TypeArguments.Length > 0 ||
                                    (namedType.ContainingType is { } genericContainingType &&
                                     (genericContainingType.IsGenericType ||
                                      genericContainingType.TypeArguments.Length > 0));
        if (!hasConstructedContext)
        {
            return CreateDefaultCanonicalIdentity(
                namedType.ToDisplayString(SymbolDisplayFormats.CodeGeneration),
                namedType.ContainingAssembly?.Identity.ToString() ?? string.Empty);
        }

        var typeArguments = string.Join(
            ",",
            namedType.TypeArguments.Select(CreateSymbolCanonicalIdentity));
        var containingType = namedType.ContainingType is null
            ? string.Empty
            : CreateSymbolCanonicalIdentity(namedType.ContainingType);
        return $"constructed:{namedType.ContainingAssembly?.Identity.ToString() ?? string.Empty}\u001f" +
               $"{namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormats.CodeGeneration)}\u001e" +
               $"{containingType}\u001e{typeArguments}";
    }

    private static string? CreateTypeArgumentDisplay(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.TypeArguments.Length == 0)
        {
            return namedType.ContainingType is { IsGenericType: true } containingType
                ? FormatTypeArgument(containingType)
                : null;
        }

        return string.Join(", ", namedType.TypeArguments.Select(FormatTypeArgument));
    }

    private static string FormatTypeArgument(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
        var assemblyIdentity = type.ContainingAssembly?.Identity.ToString() ?? string.Empty;
        if (type is not INamedTypeSymbol namedType || namedType.TypeArguments.Length == 0)
        {
            return $"{display}, {assemblyIdentity}";
        }

        var nestedArguments = string.Join(", ", namedType.TypeArguments.Select(FormatTypeArgument));
        return $"{display}, {assemblyIdentity} [{nestedArguments}]";
    }

    private static string CreateDefaultCanonicalIdentity(
        string? fullyQualifiedName,
        string? assemblyIdentity)
    {
        return $"{assemblyIdentity ?? string.Empty}\u001f{fullyQualifiedName ?? string.Empty}";
    }
}
