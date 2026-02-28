using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator;

/// <summary>
/// Contains all diagnostic descriptors for the SingletonDI source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "SingletonDI";

    /// <summary>
    /// DM0001: [SingletonDIProvide] attribute cannot be used on struct or record struct.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnStruct = Create(
        "DM0001",
        "Cannot use [SingletonDIProvide] on struct",
        "[SingletonDIProvide] attribute cannot be applied to struct or record struct. Only classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0002: [SingletonDIProvide] attribute cannot be used on abstract class.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnAbstractClass = Create(
        "DM0002",
        "Cannot use [SingletonDIProvide] on abstract class",
        "[SingletonDIProvide] attribute cannot be applied to abstract class. Only concrete classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0003: [SingletonDIProvide] attribute cannot be used on interface.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnInterface = Create(
        "DM0003",
        "Cannot use [SingletonDIProvide] on interface",
        "[SingletonDIProvide] attribute cannot be applied to interface. Only classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0004: [SingletonDIProvide] class must have a public parameterless constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideMissingParameterlessConstructor = Create(
        "DM0004",
        "Missing parameterless constructor",
        "[SingletonDIProvide] class '{0}' must have a public parameterless constructor.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0006: [SingletonDIConsume] references a type that does not have [SingletonDIProvide] attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeReferencesNonProvider = Create(
        "DM0006",
        "Referenced type is not a provider",
        "[SingletonDIConsume] references '{0}' which does not have the [SingletonDIProvide] attribute. All dependencies must be marked with [SingletonDIProvide].",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0007: [SingletonDIConsume] consumer must be partial.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeNotPartial = Create(
        "DM0007",
        "Consumer must be partial",
        "[SingletonDIConsume] class '{0}' must be declared as partial to allow code generation.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0008: [SingletonDIConsume] consumer cannot consume itself.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeSelfReference = Create(
        "DM0008",
        "Self-reference not allowed",
        "[SingletonDIConsume] class '{0}' cannot consume itself. Remove '{0}' from the dependencies.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0009: Circular dependency between singleton providers.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularDependency = Create(
        "DM0009",
        "Circular dependency detected",
        "Circular dependency detected: {0}. Please resolve the dependency cycle between singleton providers.",
        Category,
        DiagnosticSeverity.Error);

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity severity)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            severity,
            isEnabledByDefault: true);
    }
}
