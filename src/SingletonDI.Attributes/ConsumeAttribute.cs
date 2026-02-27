namespace SingletonDI.Attributes;

/// <summary>
/// Attribute to mark a class as a consumer of singleton providers.
/// Inherited = true means derived classes automatically get the same dependencies.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class ConsumeAttribute : Attribute
{
    /// <summary>
    /// The types of singleton providers this consumer depends on.
    /// </summary>
    public Type[] Dependencies { get; }

    /// <summary>
    /// Creates a new ConsumeAttribute with the specified dependencies.
    /// </summary>
    /// <param name="dependencies">The types of singleton providers.</param>
    public ConsumeAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
