namespace SingletonDI.Attributes;

/// <summary>
/// Marks a class as a consumer of singleton providers.
/// </summary>
/// <remarks>
/// <para>
/// The marked class receives one generated property for each declared dependency. A dependency
/// can be a visible concrete provider or an interface or abstract service contract. Contract
/// dependencies are named from the contract identity and do not require a reference to the
/// provider implementation.
/// </para>
/// <para>
/// The class must be declared as <c>partial</c> to allow code generation.
/// </para>
/// <para>
/// Inherited is set to <c>true</c>, meaning derived classes automatically get the same dependencies.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class SingletonDIConsumeAttribute : Attribute
{
    /// <summary>
    /// Gets the concrete provider types and service contracts this consumer depends on.
    /// </summary>
    /// <remarks>
    /// A contract entry is validated by the executable composition root because the provider
    /// implementation can live in another assembly.
    /// </remarks>
    public Type[] Dependencies { get; }

    /// <summary>
    /// Creates a new SingletonDIConsumeAttribute with the specified dependencies.
    /// </summary>
    /// <param name="dependencies">The concrete provider types and supported service contracts consumed by the class.</param>
    public SingletonDIConsumeAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
