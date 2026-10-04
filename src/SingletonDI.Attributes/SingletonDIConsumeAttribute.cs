namespace SingletonDI.Attributes;

/// <summary>
/// Marks a class, struct, record, or record struct as a consumer of singleton providers.
/// </summary>
/// <remarks>
/// <para>
/// The marked type receives one generated property for each declared dependency. A dependency
/// can be a visible concrete provider or an interface or abstract service contract. Contract
/// dependencies are named from the contract identity and do not require a reference to the
/// provider implementation.
/// </para>
/// <para>
/// The attribute can be applied more than once. Every occurrence contributes its dependencies in
/// declaration order, so several attributes are equivalent to a single attribute listing all of
/// their types. A type repeated across attributes is a duplicate and is reported as DM0010.
/// </para>
/// <para>
/// The type and any containing types that receive generated members must be declared as
/// <c>partial</c>. File-scoped declarations require C# 10 or later.
/// </para>
/// <para>
/// The attribute is inherited, so derived types receive the same generated dependencies without
/// repeating the attribute: inheritance carries access to the base type's generated properties, and
/// nothing else. A derived declaration is not validated and receives no generated members of its
/// own. Generated properties are protected for an unsealed class and private for a sealed class, a
/// static class, a struct, or a record struct.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
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
