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
    /// DM0003: A consumer property name conflicts with another generated property name.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyNameConflictsWithGenerated = Create(
        "DM0003",
        "Consumer property name conflict",
        "Consumer property '{0}' conflicts with the property resolved for service '{1}' " +
        "in the same consumer dependency set. Use distinct names or the namespace-qualified fallback.",
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
        "[SingletonDIConsume] references '{0}' which does not have the [SingletonDIProvide] attribute and is not a valid interface or abstract contract. Dependencies must be marked with [SingletonDIProvide] or use a valid contract.",
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

    /// <summary>
    /// DM0015: Generic types are not supported for singletons.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericTypeNotSupported = Create(
        "DM0015",
        "Generic types are not supported for singletons",
        "Generic type '{0}' cannot be a singleton. Generic types are not supported.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0016: The declared service contract is invalid for a provider.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidServiceType = Create(
        "DM0016",
        "Invalid ServiceType",
        "ServiceType '{0}' is invalid for provider '{1}'. It must be a reference type assignable to the provider.",
        Category,
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor MissingCompositionRoot = Create(
        "DM0017",
        "Composition root is required for an unmapped consumer dependency",
        "The executable project has a consumer dependency '{0}' that is not mapped by a local singleton provider. " +
        "Set SingletonDICompositionRoot=true when the dependency is supplied by another assembly or contract.",
        Category,
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor MissingExternalProvider = Create(
        "DM0018",
        "No provider for requested service",
        "No singleton provider is registered for requested service key '{0}'.",
        Category,
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ServiceTypeConflict = Create(
        "DM0019",
        "Multiple providers for service key",
        "Multiple singleton providers map to service key '{0}': {1}.",
        Category,
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor MissingProviderModuleMarker = Create(
        "DM0020",
        "Provider module marker is missing",
        "Referenced provider assembly '{0}' does not contain SingletonDIProviderModuleAttribute.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0021: A marked provider assembly does not expose the generated bootstrap method.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingProviderBootstrap = Create(
        "DM0021",
        "Provider module bootstrap is missing",
        "Referenced provider assembly '{0}' does not contain a public static parameterless Bootstrap() method in its generated provider module.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0022: A provider or service type cannot be named from generated code.
    /// </summary>
    public static readonly DiagnosticDescriptor ProviderTypeNotAccessible = Create(
        "DM0022",
        "Provider type is not accessible",
        "Type '{0}' used by [SingletonDIProvide] is not accessible from generated SingletonDI code. Use a public or assembly-accessible type and containing type.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0023: A provider initializer method has generic type parameters.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericInitializerNotSupported = Create(
        "DM0023",
        "Generic InitializeAsync is not supported",
        "InitializeAsync method '{0}' must not have type parameters.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0024: A consumer dependency uses an open generic type.
    /// </summary>
    public static readonly DiagnosticDescriptor OpenGenericDependencyNotSupported = Create(
        "DM0024",
        "Open generic dependency is not supported",
        "Dependency '{0}' must be a constructed type.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0025: A generated consumer property collides with an existing member.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumerPropertyNameAlreadyExists = Create(
        "DM0025",
        "Consumer property name already exists",
        "Consumer '{0}' already declares a member named '{1}'. The generated property was omitted.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0027: Generated source requires a newer C# language version.
    /// </summary>
    public static readonly DiagnosticDescriptor GeneratedLanguageVersionNotSupported = Create(
        "DM0027",
        "Generated code requires C# 9 or newer",
        "Generated SingletonDI code requires C# 9 or newer, but the effective language version is '{0}'.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0028: A file-scoped consumer requires C# 10 or newer.
    /// </summary>
    public static readonly DiagnosticDescriptor FileScopedConsumerLanguageVersionNotSupported = Create(
        "DM0028",
        "File-scoped consumers require C# 10",
        "File-scoped consumer declarations require C# 10 or newer, but the effective language version is '{0}'.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0029: A file-local consumer cannot be reopened by generated code.
    /// </summary>
    public static readonly DiagnosticDescriptor FileLocalConsumerNotSupported = Create(
        "DM0029",
        "File-local consumer is not supported",
        "Consumer '{0}' is file-local and cannot be reopened by generated code.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0030: An aliased service type cannot be represented in generated source.
    /// </summary>
    public static readonly DiagnosticDescriptor AliasedServiceTypeNotSupported = Create(
        "DM0030",
        "Aliased service type is not supported",
        "Service type '{0}' uses an extern alias that cannot be represented in generated SingletonDI source.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0031: Attributes on consumer type parameters cannot be reconstructed in generated source.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumerTypeParameterAttributesNotSupported = Create(
        "DM0031",
        "Consumer type parameter attributes are not supported",
        "Consumer '{0}' declares attributes on a type parameter, which cannot be represented in generated source.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0032: A provider has required members that generated construction cannot satisfy.
    /// </summary>
    public static readonly DiagnosticDescriptor ProviderRequiredMembersNotSupported = Create(
        "DM0032",
        "Provider required members are not supported",
        "Provider '{0}' has required members, but its public parameterless constructor does not declare [SetsRequiredMembers].",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0033: A provider initializer returns a nullable Task or ValueTask.
    /// </summary>
    public static readonly DiagnosticDescriptor NullableInitializerNotSupported = Create(
        "DM0033",
        "Nullable initializer return type is not supported",
        "InitializeAsync on provider '{0}' must return a non-nullable Task or ValueTask.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0034: InitializeAsync has an unsupported return type.
    /// </summary>
    public static readonly DiagnosticDescriptor InitializerReturnTypeNotSupported = Create(
        "DM0034",
        "InitializeAsync has an unsupported return type",
        "Method '{0}' on provider '{1}' must return a non-generic Task or ValueTask. The method was not registered as the initializer.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0035: Provider nested in a generic type is not supported.
    /// </summary>
    public static readonly DiagnosticDescriptor ProviderInGenericTypeNotSupported = Create(
        "DM0035",
        "Provider nested in a generic type is not supported",
        "Provider '{0}' is nested in generic type '{1}'. Generated code cannot name a provider nested in a generic type. Declare the provider in a non-generic type.",
        Category,
        DiagnosticSeverity.Error);

    /// <summary>
    /// DM0026: A nested consumer has a containing type that cannot be reopened.
    /// </summary>
    public static readonly DiagnosticDescriptor ConsumeContainingTypeNotPartial = Create(
        "DM0026",
        "Consumer containing type is not partial",
        "Containing type '{0}' of a nested consumer must be partial.",
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
