namespace SingletonDI.Attributes;

/// <summary>
/// Marks an assembly as a singleton provider module.
/// </summary>
/// <remarks>
/// The generated module exposes an idempotent <c>Bootstrap()</c> method. A composition root
/// invokes that method for referenced provider assemblies before initialization.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SingletonDIProviderModuleAttribute : Attribute
{
}
