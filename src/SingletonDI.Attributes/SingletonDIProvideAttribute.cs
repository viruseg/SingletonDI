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
    /// <summary>
    /// Gets the custom property name for the singleton instance.
    /// </summary>
    /// <remarks>
    /// If not specified, the default property name will be <c>{TypeName}Instance</c>.
    /// When specified, this value will be used directly as the property name in consumers.
    /// </remarks>
    public string? PropertyName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingletonDIProvideAttribute"/> class.
    /// </summary>
    /// <param name="propertyName">
    /// Optional custom property name for the singleton instance.
    /// If not specified, the default name <c>{TypeName}Instance</c> will be used.
    /// </param>
    public SingletonDIProvideAttribute(string? propertyName = null)
    {
        PropertyName = propertyName;
    }
}
