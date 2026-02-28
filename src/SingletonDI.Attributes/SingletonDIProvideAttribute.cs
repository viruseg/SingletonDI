namespace SingletonDI.Attributes;

/// <summary>
/// Marks a class as a singleton provider that can be injected into consumers.
/// </summary>
/// <remarks>
/// <para>
/// The marked class will be registered in the DI container as a singleton.
/// </para>
/// <para>
/// Initialization options:
/// <list type="bullet">
/// <item><description>Use parameterless constructor for synchronous initialization</description></item>
/// <item><description>Implement <c>Task InitializeAsync()</c> method for asynchronous initialization (will be called automatically if present)</description></item>
/// </list>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
}
