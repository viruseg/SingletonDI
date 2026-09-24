namespace SingletonDI.Generator.Models;

/// <summary>
/// Immutable metadata for a service referenced by a consumer.
/// </summary>
/// <param name="FullyQualifiedName">The fully qualified service type name.</param>
/// <param name="ShortName">The service type short name.</param>
/// <param name="Namespace">The service type namespace.</param>
/// <param name="PropertyName">The visible provider's custom property name, if one is available.</param>
/// <param name="IsContract">Whether the reference is an interface or abstract contract.</param>
/// <param name="Identity">The assembly-qualified service identity.</param>
/// <param name="CanUseProtectedProperty">Whether a protected generated property can expose the service type.</param>
public readonly record struct ServiceReferenceModel(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    string? PropertyName,
    bool IsContract,
    ServiceTypeIdentity? Identity = null,
    bool CanUseProtectedProperty = true)
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

    /// <summary>
    /// Gets whether a protected generated property can expose the referenced type without
    /// inconsistent accessibility.
    /// </summary>
    public bool CanUseProtectedProperty { get; } = CanUseProtectedProperty;
}
