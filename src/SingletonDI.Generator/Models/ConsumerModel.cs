using System.Collections.Immutable;

namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable model representing a consumer of singleton services.
/// </summary>
/// <param name="FullyQualifiedName">The fully qualified consumer type name.</param>
/// <param name="ShortName">The consumer type short name.</param>
/// <param name="Namespace">The consumer namespace.</param>
/// <param name="IsPartial">Whether the consumer declaration is partial.</param>
/// <param name="Dependencies">The typed service references consumed by the consumer.</param>
/// <param name="DeclarationShape">The complete declaration shape used to reopen the consumer.</param>
/// <param name="ExistingMemberNames">Final property names that collide with existing consumer members.</param>
public readonly record struct ConsumerModel
(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    bool IsPartial,
    ImmutableArray<ServiceReferenceModel> Dependencies,
    ConsumerDeclarationShape? DeclarationShape = null,
    ImmutableHashSet<string>? ExistingMemberNames = null)
{
    /// <summary>
    /// Gets the fully qualified name of the consumer type.
    /// </summary>
    public string FullyQualifiedName { get; } = FullyQualifiedName;

    /// <summary>
    /// Gets the short name of the consumer type.
    /// </summary>
    public string ShortName { get; } = ShortName;

    /// <summary>
    /// Gets the namespace of the consumer type.
    /// </summary>
    public string Namespace { get; } = Namespace;

    /// <summary>
    /// Gets whether the consumer is declared as partial.
    /// </summary>
    public bool IsPartial { get; } = IsPartial;

    /// <summary>
    /// Gets the typed service references consumed by the consumer.
    /// </summary>
    public ImmutableArray<ServiceReferenceModel> Dependencies { get; } = Dependencies;

    /// <summary>
    /// Gets the complete declaration shape used to reopen the consumer.
    /// </summary>
    public ConsumerDeclarationShape Shape { get; } =
        DeclarationShape ?? ConsumerDeclarationShape.CreateLegacy(ShortName, Namespace, IsPartial);

    /// <summary>
    /// Gets final property names that collide with existing consumer members.
    /// </summary>
    public ImmutableHashSet<string> ExistingMemberNames { get; } =
        ExistingMemberNames ?? ImmutableHashSet<string>.Empty;
}
