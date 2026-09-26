using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Identifies the declaration keyword of a consumer type.
/// </summary>
public enum ConsumerDeclarationKind
{
    /// <summary>
    /// A class declaration.
    /// </summary>
    Class,

    /// <summary>
    /// A struct declaration.
    /// </summary>
    Struct,

    /// <summary>
    /// A record class declaration.
    /// </summary>
    RecordClass,

    /// <summary>
    /// A record struct declaration.
    /// </summary>
    RecordStruct,
}

/// <summary>
/// Describes a containing type that must be reopened for a nested consumer.
/// </summary>
/// <param name="Name">The escaped containing type identifier.</param>
/// <param name="DeclarationKind">The containing type declaration kind.</param>
/// <param name="Arity">The number of containing type parameters.</param>
/// <param name="TypeParameters">The escaped containing type parameter names.</param>
/// <param name="TypeParameterList">The source type parameter list, including variance and attributes.</param>
/// <param name="ConstraintClauses">The source constraint clauses.</param>
/// <param name="IsPartial">Whether the containing declaration is partial.</param>
public readonly record struct ConsumerContainingTypeShape(
    string Name,
    ConsumerDeclarationKind DeclarationKind,
    int Arity,
    ImmutableArray<string> TypeParameters,
    string TypeParameterList,
    string ConstraintClauses,
    bool IsPartial);

/// <summary>
/// Describes the complete declaration shape required to reopen a consumer type.
/// </summary>
/// <param name="DeclarationKind">The consumer declaration kind.</param>
/// <param name="Name">The escaped consumer identifier.</param>
/// <param name="Namespace">The escaped namespace name.</param>
/// <param name="Arity">The number of consumer type parameters.</param>
/// <param name="TypeParameters">The escaped consumer type parameter names.</param>
/// <param name="TypeParameterList">The source type parameter list, including variance and attributes.</param>
/// <param name="ConstraintClauses">The source constraint clauses.</param>
/// <param name="IsPartial">Whether the consumer declaration is partial.</param>
/// <param name="IsSealed">Whether the consumer declaration is sealed.</param>
/// <param name="IsStatic">Whether the consumer declaration is static.</param>
/// <param name="ContainingTypes">The containing declarations from outermost to innermost.</param>
/// <param name="IsFileScoped">Whether the consumer uses a file-scoped namespace.</param>
public readonly record struct ConsumerDeclarationShape(
    ConsumerDeclarationKind DeclarationKind,
    string Name,
    string Namespace,
    int Arity,
    ImmutableArray<string> TypeParameters,
    string TypeParameterList,
    string ConstraintClauses,
    bool IsPartial,
    bool IsSealed,
    bool IsStatic,
    ImmutableArray<ConsumerContainingTypeShape> ContainingTypes,
    bool IsFileScoped)
{
    /// <summary>
    /// Creates a legacy top-level class shape for models constructed without declaration metadata.
    /// </summary>
    /// <param name="name">The consumer name.</param>
    /// <param name="namespaceName">The consumer namespace.</param>
    /// <param name="isPartial">Whether the consumer is partial.</param>
    /// <returns>A top-level class shape.</returns>
    public static ConsumerDeclarationShape CreateLegacy(
        string name,
        string namespaceName,
        bool isPartial)
    {
        return new ConsumerDeclarationShape(
            ConsumerDeclarationKind.Class,
            name,
            namespaceName,
            0,
            ImmutableArray<string>.Empty,
            string.Empty,
            string.Empty,
            isPartial,
            false,
            false,
            ImmutableArray<ConsumerContainingTypeShape>.Empty,
            false);
    }
}
