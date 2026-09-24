namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable metadata for a service referenced by a consumer.
/// </summary>
public readonly record struct ServiceReferenceModel(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    string? PropertyName,
    bool IsContract,
    ServiceTypeIdentity? Identity = null)
{
    /// <summary>
    /// Gets the fully qualified name of the referenced service type.
    /// </summary>
    public string FullyQualifiedName { get; } = FullyQualifiedName;

    /// <summary>
    /// Gets the short name of the referenced service type.
    /// </summary>
    public string ShortName { get; } = ShortName;

    /// <summary>
    /// Gets the namespace of the referenced service type.
    /// </summary>
    public string Namespace { get; } = Namespace;

    /// <summary>
    /// Gets the visible provider's custom property name, if one is available.
    /// </summary>
    public string? PropertyName { get; } = PropertyName;

    /// <summary>
    /// Gets whether the reference is an interface or abstract contract.
    /// </summary>
    public bool IsContract { get; } = IsContract;

    /// <summary>
    /// Gets the assembly-qualified identity of the referenced service type.
    /// </summary>
    public ServiceTypeIdentity? Identity { get; } = Identity;
}
