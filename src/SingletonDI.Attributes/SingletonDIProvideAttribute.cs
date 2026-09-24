namespace SingletonDI.Attributes;

/// <summary>
/// Marks a class as a singleton provider that can be injected into consumers.
/// </summary>
/// <remarks>
/// <para>
/// The provider is registered by its concrete type. When <see cref="ServiceType"/> is specified,
/// the same instance is also registered by that contract so a consumer can depend on the contract
/// without referencing the implementation assembly.
/// </para>
/// <para>
/// Initialization uses the parameterless constructor or a parameterless <c>InitializeAsync</c>
/// method returning <see cref="System.Threading.Tasks.Task"/> or <see cref="System.Threading.Tasks.ValueTask"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    /// <summary>
    /// Gets the custom property name for concrete access to the singleton instance.
    /// </summary>
    /// <remarks>
    /// If not specified, the default property name is <c>{TypeName}Instance</c>.
    /// The name is used only for a concrete provider dependency. A contract dependency is named
    /// from the contract identity and the consumer's dependency set, so the name is stable even
    /// when the provider implementation is in another assembly.
    /// </remarks>
    public string? PropertyName { get; }

    /// <summary>
    /// Gets or sets the service contract exposed by the provider.
    /// </summary>
    /// <remarks>
    /// The value must be a reference type to which the provider is assignable and which is
    /// visible to the provider assembly. The contract and concrete registrations resolve to
    /// the same singleton instance. A consumer may use the contract without a reference to
    /// this provider type; a composition root must provide exactly one mapping for it.
    /// </remarks>
    public Type? ServiceType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingletonDIProvideAttribute"/> class.
    /// </summary>
    /// <param name="propertyName">
    /// Optional custom property name for concrete access to the singleton instance.
    /// If not specified, the default name <c>{TypeName}Instance</c> is used.
    /// </param>
    public SingletonDIProvideAttribute(string? propertyName = null)
    {
        PropertyName = propertyName;
    }
}
