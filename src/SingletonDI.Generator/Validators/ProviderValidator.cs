using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates provider types with <c>SingletonDIProvideAttribute</c> and creates provider models.
/// </summary>
internal static class ProviderValidator
{
    private const string ProvideAttributeName = "SingletonDI.Attributes.SingletonDIProvideAttribute";
    private const string ConsumeAttributeName = "SingletonDI.Attributes.SingletonDIConsumeAttribute";

    internal static ProviderModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            CreateAnchor(typeDecl),
            compilation: null,
            reportDiagnostic);
    }

    internal static ProviderModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            CreateAnchor(typeDecl),
            compilation,
            reportDiagnostic);
    }

    internal static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            ProviderAnchor.None,
            compilation: null,
            reportDiagnostic,
            location);
    }

    internal static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            ProviderAnchor.None,
            compilation,
            reportDiagnostic,
            location);
    }

    private static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        ProviderAnchor anchor,
        Compilation? compilation,
        Action<Diagnostic> reportDiagnostic,
        Location? fallbackLocation = null)
    {
        var provideAttribute = FindAttribute(typeSymbol, ProvideAttributeName);
        if (provideAttribute == null)
        {
            return null;
        }

        // A source provider is anchored to the name in its declaration, so a diagnostic underlines
        // the type and not its whole body. A provider reached through a reference has no source
        // declaration to point at, so it keeps the location it was given.
        var declarationLocation = anchor.FallbackLocation(fallbackLocation);
        var attributeLocation = GetAttributeLocation(provideAttribute) ?? declarationLocation;
        var providerDisplayName = GetProviderDisplayName(typeSymbol, declarationLocation);

        if (!IsAccessibleFromGeneratedCode(typeSymbol, typeSymbol.ContainingAssembly))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProviderTypeNotAccessible,
                declarationLocation,
                GetTypeDisplayName(typeSymbol, declarationLocation)));
            return null;
        }

        if (typeSymbol.TypeParameters.Length > 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GenericTypeNotSupported,
                anchor.TypeParameterList ?? declarationLocation,
                providerDisplayName));
            return null;
        }

        if (GetContainingGenericType(typeSymbol) is { } containingGenericType)
        {
            // A provider nested in a generic type cannot be named by generated code, because
            // the only fully qualified name it has is not a legal C# source. The rejection is
            // separate from DM0015 because the provider itself has no type parameters.
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProviderInGenericTypeNotSupported,
                containingGenericType.Location ?? declarationLocation,
                providerDisplayName,
                GetFullyQualifiedName(containingGenericType.Symbol)));
            return null;
        }

        // A source static class reports IsAbstract and IsSealed as false, so both flags are needed to
        // keep a static provider out of the constructor check, where DM0004 would invite a code fix
        // that inserts an instance constructor and breaks the build with CS0710.
        if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideOnAbstractClass,
                anchor.AbstractOrStaticModifier ?? declarationLocation));
            return null;
        }

        var constructor = typeSymbol.InstanceConstructors
            .FirstOrDefault(candidate => candidate.Parameters.IsEmpty &&
                                          candidate.DeclaredAccessibility == Accessibility.Public);

        if (constructor == null)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideMissingParameterlessConstructor,
                anchor.ConstructorName ?? declarationLocation,
                providerDisplayName));
            return null;
        }

        if (HasRequiredMembers(typeSymbol) && !HasSetsRequiredMembers(constructor))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProviderRequiredMembersNotSupported,
                GetRequiredMemberLocation(typeSymbol) ?? declarationLocation,
                providerDisplayName));
            return null;
        }

        var initializeAsyncMethod = FindInitializeAsyncMethod(typeSymbol, compilation, reportDiagnostic);

        if (initializeAsyncMethod?.ReturnType.NullableAnnotation == NullableAnnotation.Annotated)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NullableInitializerNotSupported,
                initializeAsyncMethod.Locations.FirstOrDefault() ?? declarationLocation,
                providerDisplayName));
            return null;
        }

        if (initializeAsyncMethod is { Arity: > 0 })
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GenericInitializerNotSupported,
                initializeAsyncMethod.Locations.FirstOrDefault() ?? declarationLocation,
                initializeAsyncMethod.ToDisplayString()));
            return null;
        }

        if (initializeAsyncMethod != null)
        {
            var accessibility = initializeAsyncMethod.DeclaredAccessibility;
            if (accessibility is
                Accessibility.Private or
                Accessibility.Protected or
                Accessibility.ProtectedAndInternal)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InitializeAsyncNotAccessible,
                    initializeAsyncMethod.Locations.FirstOrDefault() ?? declarationLocation,
                    GetCSharpAccessModifier(accessibility)));
                return null;
            }
        }

        if (initializeAsyncMethod is { IsStatic: true })
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InitializeAsyncIsStatic,
                initializeAsyncMethod.Locations.FirstOrDefault() ?? declarationLocation,
                providerDisplayName));
            return null;
        }

        var serviceType = GetServiceType(provideAttribute);
        if (serviceType == null && HasInvalidServiceTypeValue(provideAttribute))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidServiceType,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                "<invalid>",
                providerDisplayName));
            return null;
        }

        if (serviceType is not null && HasAliasQualifiedType(provideAttribute))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AliasedServiceTypeNotSupported,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                GetTypeDisplayName(serviceType, declarationLocation)));
            return null;
        }

        if (serviceType is INamedTypeSymbol { IsUnboundGenericType: true })
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.OpenGenericDependencyNotSupported,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                GetTypeDisplayName(serviceType, declarationLocation)));
            return null;
        }

        if (serviceType != null &&
            !IsAccessibleFromGeneratedCode(serviceType, typeSymbol.ContainingAssembly))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProviderTypeNotAccessible,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                GetTypeDisplayName(serviceType, declarationLocation)));
            return null;
        }

        if (serviceType != null &&
            (!serviceType.IsReferenceType || !IsAssignableTo(typeSymbol, serviceType, compilation)))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidServiceType,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                GetTypeDisplayName(serviceType, declarationLocation),
                providerDisplayName));
            return null;
        }

        var dependencyResolution = GetDependencyIdentities(
            typeSymbol,
            compilation,
            declarationLocation,
            reportDiagnostic);
        if (dependencyResolution is null)
        {
            return null;
        }

        var validDependencyIdentities = dependencyResolution.Value.Identities;
        var dependencies = validDependencyIdentities
            .Select(identity => identity.FullyQualifiedName)
            .ToImmutableArray();

        var (propertyName, propertyNameLocation) = GetPropertyName(
            provideAttribute,
            declarationLocation,
            reportDiagnostic);
        ServiceTypeIdentity? serviceTypeIdentity = serviceType == null
            ? null
            : ServiceTypeIdentity.FromSymbol(serviceType);

        var isDisposable = typeSymbol.AllInterfaces.Any(interfaceSymbol =>
            interfaceSymbol.OriginalDefinition.ToDisplayString() == "System.IDisposable");
        var isAsyncDisposable = typeSymbol.AllInterfaces.Any(interfaceSymbol =>
            interfaceSymbol.OriginalDefinition.ToDisplayString() == "System.IAsyncDisposable");

        return new ProviderModel(
            fullyQualifiedName: GetFullyQualifiedName(typeSymbol),
            shortName: typeSymbol.Name,
            @namespace: typeSymbol.ContainingNamespace.ToDisplayString(),
            assemblyIdentity: typeSymbol.ContainingAssembly.Identity.ToString(),
            hasInitializeAsyncMethod: initializeAsyncMethod != null,
            isDisposable: isDisposable,
            isAsyncDisposable: isAsyncDisposable,
            dependencies: dependencies,
            serviceTypeFullyQualifiedName: serviceType == null ? null : GetFullyQualifiedName(serviceType),
            serviceTypeShortName: serviceType?.Name,
            serviceTypeNamespace: serviceType?.ContainingNamespace.ToDisplayString(),
            propertyName: propertyName,
            location: declarationLocation,
            propertyNameLocation: propertyNameLocation,
            dependencyIdentities: validDependencyIdentities,
            serviceTypeIdentity: serviceTypeIdentity,
            dependencyLocations: dependencyResolution.Value.Locations);
    }

    private static IMethodSymbol? FindInitializeAsyncMethod(
        INamedTypeSymbol typeSymbol,
        Compilation? compilation,
        Action<Diagnostic> reportDiagnostic)
    {
        for (INamedTypeSymbol? current = typeSymbol;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            IMethodSymbol? usable = null;
            IMethodSymbol? unsupportedReturnType = null;
            IMethodSymbol? explicitImplementation = null;
            foreach (var member in current.GetMembers())
            {
                if (member is not IMethodSymbol { Parameters.IsEmpty: true } method ||
                    GetSimpleMemberName(method) != "InitializeAsync")
                {
                    continue;
                }

                if (!IsSupportedInitializerReturnType(method.ReturnType, compilation))
                {
                    // Remembered instead of returned, so a usable method declared later in the type
                    // still wins. Taking the first match decided the outcome by declaration order.
                    unsupportedReturnType ??= method;
                    continue;
                }

                if (method.ExplicitInterfaceImplementations.Length > 0)
                {
                    // Never reached by the generated call, which binds by member lookup, so a method
                    // that is not an explicit implementation wins over it whatever the order. It
                    // stays a candidate so a type whose only initializer is an explicit
                    // implementation is still rejected as inaccessible.
                    explicitImplementation ??= method;
                    continue;
                }

                usable = method;
                break;
            }

            var declared = usable ?? unsupportedReturnType ?? explicitImplementation;
            if (declared is null)
            {
                continue;
            }

            // The generated call binds by member lookup, which stops at the most derived type that
            // declares the member. Returning a base declaration hidden by this one would emit a call
            // that resolves to a different method than the one registered here.
            if (usable is null && unsupportedReturnType is not null)
            {
                if (!SymbolEqualityComparer.Default.Equals(current, typeSymbol))
                {
                    return null;
                }

                // Dropping the method silently left the provider registered with no initializer and
                // no diagnostic, so the rejection is reported instead.
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InitializerReturnTypeNotSupported,
                    declared.Locations.FirstOrDefault() ?? Location.None,
                    declared.ToDisplayString(),
                    typeSymbol.Name));
                return null;
            }

            if (SymbolEqualityComparer.Default.Equals(current, typeSymbol))
            {
                return declared;
            }

            // An inherited declaration was invisible before the base walk, so it is used only when it
            // is callable as the initializer. Anything else keeps the previous outcome, a provider
            // with no initializer, instead of reporting a diagnostic about a member the provider's
            // author does not own and cannot rename.
            return IsCallableInheritedInitializer(declared) ? declared : null;
        }

        return null;
    }

    private static bool IsCallableInheritedInitializer(IMethodSymbol method) =>
        method.Arity == 0 &&
        !method.IsStatic &&
        method.ReturnType.NullableAnnotation != NullableAnnotation.Annotated &&
        method.DeclaredAccessibility is not (
            Accessibility.Private or
            Accessibility.Protected or
            Accessibility.ProtectedAndInternal);

    private static string GetSimpleMemberName(IMethodSymbol method)
    {
        // An explicit interface implementation is named "IContract.InitializeAsync", so matching the
        // bare name is what lets the accessibility check in Validate report DM0005 for it.
        var name = method.Name;
        var lastDot = name.LastIndexOf('.');
        return lastDot < 0 ? name : name.Substring(lastDot + 1);
    }

    private static bool IsSupportedInitializerReturnType(
        ITypeSymbol returnType,
        Compilation? compilation)
    {
        var definition = UnwrapNullable(returnType).OriginalDefinition;
        if (compilation is not null)
        {
            return SymbolEqualityComparer.Default.Equals(
                       definition,
                       compilation.GetTypeByMetadataName("System.Threading.Tasks.Task")) ||
                   SymbolEqualityComparer.Default.Equals(
                       definition,
                       compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"));
        }

        var assemblyName = definition.ContainingAssembly?.Identity.Name;
        return definition.MetadataName is "Task" or "ValueTask" &&
               assemblyName is "System.Runtime" or "System.Private.CoreLib" or "netstandard";
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        // ValueTask? is System.Nullable<ValueTask>, whose original definition is not ValueTask. The
        // unwrapped type has to reach the supported-return-type check for DM0033 to be reported.
        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;
    }

    private static DependencyResolution? GetDependencyIdentities(
        INamedTypeSymbol typeSymbol,
        Compilation? compilation,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        var consumeAttributes = FindConsumeAttributesIncludingBaseTypes(typeSymbol);
        if (consumeAttributes.IsEmpty)
        {
            return new DependencyResolution(
                ImmutableArray<ServiceTypeIdentity>.Empty,
                ImmutableDictionary<string, Location>.Empty);
        }

        var dependencies = ImmutableArray.CreateBuilder<ServiceTypeIdentity>();
        var dependencyLocations = ImmutableDictionary.CreateBuilder<string, Location>(StringComparer.Ordinal);
        var hasInvalidDependency = false;

        foreach (var consumeAttribute in consumeAttributes)
        {
            var consumeAttributeSyntax = GetAttributeSyntax(consumeAttribute);
            var argumentLocations = GetArgumentLocations(consumeAttributeSyntax);
            foreach (var (dependencyType, dependencyLocation) in GetTypeArguments(
                         consumeAttribute,
                         argumentLocations))
            {
                var diagnosticLocation = dependencyLocation ??
                                        GetAttributeLocation(consumeAttribute) ??
                                        location;
                if (dependencyType is INamedTypeSymbol { IsUnboundGenericType: true })
                {
                    reportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.OpenGenericDependencyNotSupported,
                        diagnosticLocation,
                        GetTypeDisplayName(dependencyType, diagnosticLocation)));
                    hasInvalidDependency = true;
                    continue;
                }

                if (!IsAccessibleFromGeneratedCode(dependencyType, typeSymbol.ContainingAssembly))
                {
                    reportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ProviderTypeNotAccessible,
                        diagnosticLocation,
                        GetTypeDisplayName(dependencyType, diagnosticLocation)));
                    hasInvalidDependency = true;
                    continue;
                }

                var identity = ServiceTypeIdentity.FromSymbol(dependencyType);
                dependencies.Add(identity);
                if (dependencyLocation is not null && !dependencyLocations.ContainsKey(identity.CanonicalIdentity))
                {
                    dependencyLocations.Add(identity.CanonicalIdentity, dependencyLocation);
                }
            }
        }

        return hasInvalidDependency
            ? null
            : new DependencyResolution(dependencies.ToImmutable(), dependencyLocations.ToImmutable());
    }

    private static IEnumerable<(ITypeSymbol Type, Location? Location)> GetTypeArguments(
        AttributeData attribute,
        IReadOnlyList<Location> argumentLocations)
    {
        for (var argumentIndex = 0; argumentIndex < attribute.ConstructorArguments.Length; argumentIndex++)
        {
            var argument = attribute.ConstructorArguments[argumentIndex];
            if (argument.Kind == TypedConstantKind.Array)
            {
                for (var elementIndex = 0; elementIndex < argument.Values.Length; elementIndex++)
                {
                    if (argument.Values[elementIndex] is
                        { Kind: TypedConstantKind.Type, Value: ITypeSymbol dependencyType })
                    {
                        var location = elementIndex < argumentLocations.Count
                            ? argumentLocations[elementIndex]
                            : null;
                        yield return (dependencyType, location);
                    }
                }
            }
            else if (argument is { Kind: TypedConstantKind.Type, Value: ITypeSymbol singleType })
            {
                var location = argumentIndex < argumentLocations.Count
                    ? argumentLocations[argumentIndex]
                    : null;
                yield return (singleType, location);
            }
        }
    }

    private static (string? propertyName, Location? propertyNameLocation) GetPropertyName(
        AttributeData provideAttribute,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        string? propertyName = null;
        Location? propertyNameLocation = null;
        var propertyNameWasSpecified = false;

        if (provideAttribute.ConstructorArguments.Length > 0)
        {
            var argument = provideAttribute.ConstructorArguments[0];
            if (argument is { Kind: TypedConstantKind.Primitive, Value: string constructorValue })
            {
                propertyName = constructorValue;
                propertyNameWasSpecified = true;
                propertyNameLocation = GetConstructorArgumentLocation(provideAttribute);
            }
        }

        if (!propertyNameWasSpecified)
        {
            return (null, null);
        }

        var diagnosticLocation = propertyNameLocation ?? GetAttributeLocation(provideAttribute) ?? location;
        switch (PropertyNameSyntax.Classify(propertyName))
        {
            case PropertyNameKind.Invalid:
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidPropertyName,
                    diagnosticLocation,
                    propertyName));
                return (null, null);

            case PropertyNameKind.ReservedKeyword:
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PropertyNameIsReservedKeyword,
                    diagnosticLocation,
                    propertyName));
                return (null, null);
        }

        return (propertyName, propertyNameLocation);
    }

    private static bool HasAliasQualifiedType(AttributeData attribute)
    {
        var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        return attributeSyntax?
            .DescendantNodes()
            .OfType<TypeOfExpressionSyntax>()
            .Any(typeOfExpression => typeOfExpression.Type
                .DescendantNodesAndSelf()
                .OfType<AliasQualifiedNameSyntax>()
                .Any()) == true;
    }

    private static ITypeSymbol? GetServiceType(AttributeData provideAttribute)
    {
        var namedArgument = provideAttribute.NamedArguments
            .FirstOrDefault(argument => argument.Key == "ServiceType");

        if (string.IsNullOrEmpty(namedArgument.Key) || namedArgument.Value.IsNull)
        {
            return null;
        }

        return namedArgument.Value is { Kind: TypedConstantKind.Type, Value: ITypeSymbol serviceType }
            ? serviceType
            : null;
    }

    private static bool HasInvalidServiceTypeValue(AttributeData provideAttribute)
    {
        var namedArgument = provideAttribute.NamedArguments
            .FirstOrDefault(argument => argument.Key == "ServiceType");

        return !string.IsNullOrEmpty(namedArgument.Key) &&
               !namedArgument.Value.IsNull &&
               namedArgument.Value is not { Kind: TypedConstantKind.Type, Value: ITypeSymbol };
    }

    private static bool HasRequiredMembers(INamedTypeSymbol typeSymbol)
    {
        for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
            {
                break;
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } ||
                    member is IFieldSymbol { IsRequired: true })
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSetsRequiredMembers(IMethodSymbol constructor)
    {
        return constructor.GetAttributes().Any(attribute =>
            IsAttribute(attribute, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"));
    }

    /// <summary>
    /// Collects the places a provider diagnostic can be anchored to, so that each one underlines the
    /// token the reader has to change instead of the whole declaration.
    /// </summary>
    /// <param name="typeDecl">The declaration of the provider being validated.</param>
    private static ProviderAnchor CreateAnchor(TypeDeclarationSyntax typeDecl)
    {
        return new ProviderAnchor(
            typeDecl.Identifier.GetLocation(),
            GetModifierLocation(typeDecl, SyntaxKind.AbstractKeyword) ??
            GetModifierLocation(typeDecl, SyntaxKind.StaticKeyword),
            typeDecl.TypeParameterList?.GetLocation(),
            GetConstructorNameLocation(typeDecl));
    }

    private static Location? GetModifierLocation(TypeDeclarationSyntax typeDecl, SyntaxKind kind)
    {
        foreach (var modifier in typeDecl.Modifiers)
        {
            if (modifier.IsKind(kind))
            {
                return modifier.GetLocation();
            }
        }

        return null;
    }

    /// <summary>
    /// Prefers a constructor that takes arguments, because that is the one the author has to change.
    /// A provider with only an implicit constructor has nothing to point at.
    /// </summary>
    private static Location? GetConstructorNameLocation(TypeDeclarationSyntax typeDecl)
    {
        ConstructorDeclarationSyntax? withParameters = null;
        ConstructorDeclarationSyntax? declared = null;
        foreach (var member in typeDecl.Members)
        {
            if (member is not ConstructorDeclarationSyntax constructor)
            {
                continue;
            }

            declared ??= constructor;
            if (constructor.ParameterList.Parameters.Count > 0)
            {
                withParameters ??= constructor;
            }
        }

        return (withParameters ?? declared)?.Identifier.GetLocation();
    }

    /// <summary>
    /// Finds the required member that makes a provider unregistrable, which is the declaration the
    /// author has to change to keep it. A required member inherited from a base type in another
    /// assembly has no location to report.
    /// </summary>
    private static Location? GetRequiredMemberLocation(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not (IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                {
                    continue;
                }

                var location = member.Locations.FirstOrDefault(candidate => candidate.IsInSource);
                if (location is not null)
                {
                    return location;
                }
            }
        }

        return null;
    }

    private static ContainingGenericType? GetContainingGenericType(INamedTypeSymbol typeSymbol)
    {
        for (INamedTypeSymbol? container = typeSymbol.ContainingType;
             container is not null;
             container = container.ContainingType)
        {
            if (container.Arity > 0)
            {
                return new ContainingGenericType(
                    container.Locations.FirstOrDefault(candidate => candidate.IsInSource),
                    container);
            }
        }

        return null;
    }

    private static bool HasProvideAttribute(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(attribute => IsAttribute(attribute, ProvideAttributeName));
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        var attributeName = attribute.AttributeClass?.ToDisplayString();
        return attributeName == metadataName || attributeName == "global::" + metadataName;
    }

    /// <summary>
    /// Reports whether generated SingletonDI code can name <paramref name="typeSymbol"/>.
    /// </summary>
    /// <param name="typeSymbol">The type the generated code has to name.</param>
    /// <param name="declaringAssembly">
    /// The assembly the generated code is emitted into, which is the assembly that declares the
    /// provider. Assembly accessibility is resolved against it rather than against the compilation
    /// being built, because a provider discovered through a reference is validated while some other
    /// project is being compiled while its own module is emitted into its own assembly.
    /// </param>
    /// <summary>
    /// Renders an accessibility the way the C# language spells it, for a message the reader has to
    /// act on.
    /// </summary>
    /// <remarks>
    /// The enum names are the CLR ones, so <c>ProtectedAndInternal</c> would be lowercased to
    /// "protectedandinternal", which is not a modifier anyone can type. <c>ProtectedOrInternal</c> is
    /// the only case that differs from its own lowercased name.
    /// </remarks>
    private static string GetCSharpAccessModifier(Accessibility accessibility) =>
        accessibility == Accessibility.ProtectedAndInternal
            ? "private protected"
            : accessibility.ToString().ToLowerInvariant();

    private static bool IsAccessibleFromGeneratedCode(
        ITypeSymbol typeSymbol,
        IAssemblySymbol? declaringAssembly)
    {
        var isSameAssembly = declaringAssembly is null ||
            string.Equals(
                typeSymbol.ContainingAssembly?.Identity.ToString(),
                declaringAssembly.Identity.ToString(),
                StringComparison.Ordinal);

        for (INamedTypeSymbol? current = typeSymbol as INamedTypeSymbol;
             current is not null;
             current = current.ContainingType)
        {
            if (current.IsFileLocal ||
                !IsAccessibleFromGeneratedContext(current.DeclaredAccessibility, isSameAssembly))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAccessibleFromGeneratedContext(
        Accessibility accessibility,
        bool isSameAssembly)
    {
        return accessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal or Accessibility.ProtectedOrInternal => isSameAssembly,
            _ => false,
        };
    }

    private static bool IsAssignableTo(
        INamedTypeSymbol provider,
        ITypeSymbol serviceType,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var conversion = compilation.ClassifyCommonConversion(provider, serviceType);

            // A downcast to an interface the provider does not implement classifies as a reference
            // conversion, so IsImplicit is what separates a usable contract from a legal cast.
            return conversion.Exists &&
                   !conversion.IsUserDefined &&
                   (conversion.IsIdentity || conversion.IsImplicit);
        }

        return HasCommonConversion(provider, serviceType);
    }

    private static bool HasCommonConversion(ITypeSymbol source, ITypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
        {
            return true;
        }

        if (source is not INamedTypeSymbol sourceType || target is not INamedTypeSymbol targetType)
        {
            return false;
        }

        if (targetType.SpecialType == SpecialType.System_Object &&
            (sourceType.IsReferenceType || sourceType.IsValueType))
        {
            return true;
        }

        if (sourceType.TypeKind == TypeKind.Interface &&
            targetType.TypeKind == TypeKind.Interface &&
            sourceType.IsGenericType &&
            targetType.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(
                sourceType.OriginalDefinition,
                targetType.OriginalDefinition))
        {
            return HasVarianceConversion(sourceType, targetType);
        }

        for (var baseType = sourceType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (HasCommonConversion(baseType, target))
            {
                return true;
            }
        }

        return sourceType.AllInterfaces.Any(interfaceType => HasCommonConversion(interfaceType, target));
    }

    private static bool HasVarianceConversion(
        INamedTypeSymbol sourceType,
        INamedTypeSymbol targetType)
    {
        if (sourceType.TypeArguments.Length != targetType.TypeArguments.Length)
        {
            return false;
        }

        for (var index = 0; index < sourceType.TypeArguments.Length; index++)
        {
            var sourceArgument = sourceType.TypeArguments[index];
            var targetArgument = targetType.TypeArguments[index];
            var variance = targetType.OriginalDefinition.TypeParameters[index].Variance;
            var converted = variance switch
            {
                VarianceKind.Out => HasImplicitVarianceConversion(sourceArgument, targetArgument),
                VarianceKind.In => HasImplicitVarianceConversion(targetArgument, sourceArgument),
                _ => SymbolEqualityComparer.Default.Equals(sourceArgument, targetArgument),
            };
            if (!converted)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasImplicitVarianceConversion(ITypeSymbol source, ITypeSymbol target)
    {
        // The language makes a variance conversion implicit only when its type arguments are related
        // by an implicit reference conversion, so a value type on the narrowing side leaves it
        // explicit. An explicit conversion still compiles, but the instance is not the target type at
        // run time, so the cast in Resolve<T> would fail.
        return SymbolEqualityComparer.Default.Equals(source, target) ||
               (source.IsReferenceType && HasCommonConversion(source, target));
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, metadataName));
    }

    /// <summary>
    /// Reads every consume attribute declared on the type itself, or on the nearest base type that
    /// declares any.
    /// </summary>
    /// <remarks>
    /// <see cref="ISymbol.GetAttributes"/> only reports directly declared attributes, so an
    /// attribute declared with <c>Inherited = true</c> is invisible on a derived symbol even though
    /// the runtime applies it. A declaration on the most derived type wins, and every attribute it
    /// declares is returned, so the returned data still points at the declaration sites a dependency
    /// was actually written on.
    /// </remarks>
    private static ImmutableArray<AttributeData> FindConsumeAttributesIncludingBaseTypes(
        INamedTypeSymbol typeSymbol)
    {
        for (INamedTypeSymbol? current = typeSymbol;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            var declared = current.GetAttributes()
                .Where(attribute => IsAttribute(attribute, ConsumeAttributeName))
                .ToImmutableArray();
            if (!declared.IsEmpty)
            {
                return declared;
            }
        }

        return ImmutableArray<AttributeData>.Empty;
    }

    private static Location? GetAttributeLocation(AttributeData attribute)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
    }

    private static Location? GetServiceTypeArgumentLocation(AttributeData attribute)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        if (syntax?.ArgumentList == null)
        {
            return null;
        }

        var argument = syntax.ArgumentList.Arguments.FirstOrDefault(candidate =>
            candidate.NameColon?.Name.Identifier.ValueText == "ServiceType");
        return argument?.GetLocation();
    }

    private static Location? GetConstructorArgumentLocation(AttributeData attribute)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        return syntax?.ArgumentList?.Arguments.FirstOrDefault()?.GetLocation();
    }

    private static AttributeSyntax? GetAttributeSyntax(AttributeData attribute)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
    }

    private static IReadOnlyList<Location> GetArgumentLocations(AttributeSyntax? attributeSyntax)
    {
        if (attributeSyntax?.ArgumentList == null)
        {
            return Array.Empty<Location>();
        }

        return attributeSyntax.ArgumentList.Arguments
            .Select(argument => argument.Expression is TypeOfExpressionSyntax typeOfExpression
                ? typeOfExpression.Type.GetLocation()
                : argument.GetLocation())
            .ToImmutableArray();
    }

    private static string GetTypeDisplayName(ITypeSymbol typeSymbol, Location location)
    {
        var displayName = typeSymbol.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
        return location.IsInSource
            ? displayName
            : $"{displayName}, {typeSymbol.ContainingAssembly.Identity}";
    }

    private static string GetProviderDisplayName(INamedTypeSymbol typeSymbol, Location location)
    {
        if (location.IsInSource)
        {
            return typeSymbol.Name;
        }

        return $"{typeSymbol.ToDisplayString(SymbolDisplayFormats.CodeGeneration)}, " +
               typeSymbol.ContainingAssembly.Identity;
    }

    private static string GetFullyQualifiedName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormats.CodeGeneration);
    }

    /// <summary>
    /// The token a provider diagnostic points at, with the provider name as the last fallback.
    /// </summary>
    /// <param name="Name">The provider name in its declaration.</param>
    /// <param name="AbstractOrStaticModifier">The <c>abstract</c> or <c>static</c> modifier.</param>
    /// <param name="TypeParameterList">The provider's own type parameter list.</param>
    /// <param name="ConstructorName">The name of a declared constructor.</param>
    private readonly record struct ProviderAnchor(
        Location? Name,
        Location? AbstractOrStaticModifier,
        Location? TypeParameterList,
        Location? ConstructorName)
    {
        internal static ProviderAnchor None => default;

        /// <summary>
        /// Resolves the anchor a provider without a source declaration falls back to.
        /// </summary>
        internal Location FallbackLocation(Location? fallbackLocation) =>
            (Name ?? fallbackLocation) is { IsInSource: true } location
                ? location
                : Location.None;
    }

    /// <param name="Location">Where the containing type is declared, or <see langword="null"/> when it is metadata.</param>
    /// <param name="Symbol">The containing generic type itself.</param>
    private readonly record struct ContainingGenericType(Location? Location, INamedTypeSymbol Symbol);

    /// <param name="Identities">Every dependency the provider declares.</param>
    /// <param name="Locations">Where each dependency is named, keyed by its canonical identity.</param>
    private readonly record struct DependencyResolution(
        ImmutableArray<ServiceTypeIdentity> Identities,
        ImmutableDictionary<string, Location> Locations);
}
