using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator;

/// <summary>
/// Contains all diagnostic descriptors for the SingletonDI source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "SingletonDI";

    /// <summary>
    /// DM0001: [Provide] attribute cannot be used on struct or record struct.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnStruct = Create(
        "DM0001",
        "Cannot use [Provide] on struct",
        "[Provide] attribute cannot be applied to struct or record struct. Only classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0002: [Provide] attribute cannot be used on abstract class.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnAbstractClass = Create(
        "DM0002",
        "Cannot use [Provide] on abstract class",
        "[Provide] attribute cannot be applied to abstract class. Only concrete classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0003: [Provide] attribute cannot be used on interface.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideOnInterface = Create(
        "DM0003",
        "Cannot use [Provide] on interface",
        "[Provide] attribute cannot be applied to interface. Only classes are supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0004: [Provide] class must have a public parameterless constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideMissingParameterlessConstructor = Create(
        "DM0004",
        "Missing parameterless constructor",
        "[Provide] class '{0}' must have a public parameterless constructor.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0005: [Provide] class should implement IInitializeSync or IInitializeAsync.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideMissingInitialization = Create(
        "DM0005",
        "Missing initialization interface",
        "[Provide] class '{0}' should implement IInitializeSync or IInitializeAsync for proper initialization.",
        Category,
        DiagnosticSeverity.Warning);

    /// <summary>
    /// DM0006: [Consume] references a type that does not have [Provide] attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeReferencesNonProvider = Create(
        "DM0006",
        "Referenced type is not a provider",
        "[Consume] references '{0}' which does not have the [Provide] attribute. All dependencies must be marked with [Provide].",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0007: [Consume] consumer must be partial.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeNotPartial = Create(
        "DM0007",
        "Consumer must be partial",
        "[Consume] class '{0}' must be declared as partial to allow code generation.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0008: [Consume] consumer cannot consume itself.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeSelfReference = Create(
        "DM0008",
        "Self-reference not allowed",
        "[Consume] class '{0}' cannot consume itself. Remove '{0}' from the dependencies.",
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

    /// <summary>
    /// DM0010: [Provide] class cannot have both Initialize() and InitializeAsync().
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideBothInitializationMethods = Create(
        "DM0010",
        "Cannot have both initialization methods",
        "[Provide] class '{0}' cannot implement both IInitializeSync and IInitializeAsync. Choose only one.",
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
