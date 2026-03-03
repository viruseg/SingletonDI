using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator;

/// <summary>
/// Contains all diagnostic descriptors for the SingletonDI source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "SingletonDI";

    /// <summary>
    /// DM0001: Duplicate property name in [SingletonDIProvide] attributes.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyNameConflict = Create(
        "DM0001",
        "Duplicate property name",
        "Property name '{0}' is specified in multiple [SingletonDIProvide] attributes. " +
        "Providers '{1}' and '{2}' have the same property name. " +
        "Each provider must have a unique property name.",
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
    /// DM0004: [SingletonDIProvide] class must have a public parameterless constructor.
    /// </summary>
    public static readonly DiagnosticDescriptor ProvideMissingParameterlessConstructor = Create(
        "DM0004",
        "Missing parameterless constructor",
        "[SingletonDIProvide] class '{0}' must have a public parameterless constructor.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0005: InitializeAsync method has inaccessible access modifier.
    /// </summary>
    public static readonly DiagnosticDescriptor InitializeAsyncNotAccessible = Create(
        "DM0005",
        "InitializeAsync method has inaccessible access modifier",
        "Method InitializeAsync has access modifier '{0}' which makes it inaccessible from generated code. Use 'public', 'internal', or 'protected internal'.",
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

    /// <summary>
    /// DM0010: Duplicate type in SingletonDIConsume attribute arguments.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeDuplicateTypes = Create(
        "DM0010",
        "Duplicate type in SingletonDIConsume attribute arguments",
        "Type '{0}' is specified multiple times in SingletonDIConsume attribute. Each type should be specified only once.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0011: Type already declared in base class.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeDuplicateInBaseClass = Create(
        "DM0011",
        "Type already declared in base class",
        "Type '{0}' is already declared in base class '{1}'. Remove the duplicate declaration.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0012: InitializeAsync method cannot be static.
    /// </summary>
    public static readonly DiagnosticDescriptor InitializeAsyncIsStatic = Create(
        "DM0012",
        "InitializeAsync method cannot be static",
        "Method InitializeAsync in class '{0}' is static. InitializeAsync must be an instance method.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0013: Property name is not a valid C# identifier.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidPropertyName = Create(
        "DM0013",
        "Invalid property name",
        "Property name '{0}' is not a valid C# identifier. Property names must start with a letter or underscore and contain only letters, digits, or underscores.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0014: Property name is a C# reserved keyword.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyNameIsReservedKeyword = Create(
        "DM0014",
        "Property name is a reserved keyword",
        "Property name '{0}' is a C# reserved keyword. Use a different name or prefix with '@' in your code.",
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
