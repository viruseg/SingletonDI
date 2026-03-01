namespace SingletonDI.Attributes;

/// <summary>
/// Marks a class as a consumer of singleton providers.
/// </summary>
/// <remarks>
/// <para>
/// The marked class will receive dependencies through generated properties.
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
    /// The types of singleton providers this consumer depends on.
    /// </summary>
    public Type[] Dependencies { get; }

    /// <summary>
    /// Creates a new SingletonDIConsumeAttribute with the specified dependencies.
    /// </summary>
    /// <param name="dependencies">The types of singleton providers.</param>
    public SingletonDIConsumeAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
