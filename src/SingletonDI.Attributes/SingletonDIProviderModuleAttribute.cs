namespace SingletonDI.Attributes;

/// <summary>
/// Marks an assembly as a singleton provider module.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SingletonDIProviderModuleAttribute : Attribute
{
}
