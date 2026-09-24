using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            typeDecl.GetLocation(),
            compilation: null,
            knownProviderFullyQualifiedNames,
            reportDiagnostic);
    }

    internal static ProviderModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            typeDecl.GetLocation(),
            compilation,
            knownProviderFullyQualifiedNames,
            reportDiagnostic);
    }

    internal static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        Location location,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            location,
            compilation: null,
            knownProviderFullyQualifiedNames,
            reportDiagnostic);
    }

    internal static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        Location location,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeSymbol,
            location,
            compilation,
            knownProviderFullyQualifiedNames,
            reportDiagnostic);
    }

    private static ProviderModel? Validate(
        INamedTypeSymbol typeSymbol,
        Location location,
        Compilation? compilation,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        var provideAttribute = FindAttribute(typeSymbol, ProvideAttributeName);
        if (provideAttribute == null)
        {
            return null;
        }

        var attributeLocation = GetAttributeLocation(provideAttribute) ?? location;
        var declarationLocation = location;
        var providerDisplayName = GetProviderDisplayName(typeSymbol, location);

        if (typeSymbol.IsGenericType || typeSymbol.TypeParameters.Length > 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GenericTypeNotSupported,
                declarationLocation,
                providerDisplayName));
            return null;
        }

        if (typeSymbol.IsAbstract)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideOnAbstractClass,
                declarationLocation));
            return null;
        }

        var constructor = typeSymbol.InstanceConstructors
            .FirstOrDefault(candidate => candidate.Parameters.IsEmpty &&
                                          candidate.DeclaredAccessibility == Accessibility.Public);

        if (constructor == null)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideMissingParameterlessConstructor,
                declarationLocation,
                providerDisplayName));
            return null;
        }

        var initializeAsyncMethod = FindInitializeAsyncMethod(typeSymbol);

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
                    initializeAsyncMethod.Locations.FirstOrDefault() ?? location,
                    accessibility.ToString().ToLowerInvariant()));
                return null;
            }
        }

        if (initializeAsyncMethod is { IsStatic: true })
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InitializeAsyncIsStatic,
                initializeAsyncMethod.Locations.FirstOrDefault() ?? location,
                providerDisplayName));
            return null;
        }

        var serviceType = GetServiceType(provideAttribute);
        if (serviceType == null && HasInvalidServiceTypeValue(provideAttribute))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidServiceType,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                serviceType == null
                    ? "<invalid>"
                    : GetTypeDisplayName(serviceType, location),
                providerDisplayName));
            return null;
        }

        if (serviceType != null &&
            (!serviceType.IsReferenceType || !IsAssignableTo(typeSymbol, serviceType, compilation)))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidServiceType,
                GetServiceTypeArgumentLocation(provideAttribute) ?? attributeLocation,
                GetTypeDisplayName(serviceType, location),
                providerDisplayName));
            return null;
        }

        var dependencyIdentities = GetDependencyIdentities(
            typeSymbol,
            knownProviderFullyQualifiedNames);
        var dependencies = dependencyIdentities
            .Select(identity => identity.FullyQualifiedName)
            .ToImmutableArray();

        var (propertyName, propertyNameLocation) = GetPropertyName(
            provideAttribute,
            location,
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
            location: location,
            propertyNameLocation: propertyNameLocation,
            dependencyIdentities: dependencyIdentities,
            serviceTypeIdentity: serviceTypeIdentity);
    }

    private static IMethodSymbol? FindInitializeAsyncMethod(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IMethodSymbol { Name: "InitializeAsync", Parameters.IsEmpty: true } method)
            {
                continue;
            }

            var returnTypeName = method.ReturnType.ToDisplayString();
            if (returnTypeName is
                "System.Threading.Tasks.Task" or
                "Task" or
                "System.Threading.Tasks.ValueTask" or
                "ValueTask")
            {
                return method;
            }
        }

        return null;
    }

    private static ImmutableArray<ServiceTypeIdentity> GetDependencyIdentities(
        INamedTypeSymbol typeSymbol,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames)
    {
        var consumeAttribute = FindAttribute(typeSymbol, ConsumeAttributeName);
        if (consumeAttribute == null)
        {
            return ImmutableArray<ServiceTypeIdentity>.Empty;
        }

        var consumeAttributeSyntax = GetAttributeSyntax(consumeAttribute);
        var argumentLocations = GetArgumentLocations(consumeAttributeSyntax);
        var dependencies = ImmutableArray.CreateBuilder<ServiceTypeIdentity>();

        foreach (var (dependencyType, _) in GetTypeArguments(consumeAttribute, argumentLocations))
        {
            dependencies.Add(ServiceTypeIdentity.FromSymbol(dependencyType));
        }

        return dependencies.ToImmutable();
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
            var namedArgument = provideAttribute.NamedArguments
                .FirstOrDefault(argument => argument.Key == "PropertyName");

            if (!string.IsNullOrEmpty(namedArgument.Key) &&
                namedArgument.Value is { Kind: TypedConstantKind.Primitive, Value: string namedValue })
            {
                propertyName = namedValue;
                propertyNameWasSpecified = true;
                propertyNameLocation = GetNamedArgumentLocation(provideAttribute, "PropertyName");
            }
        }

        if (!propertyNameWasSpecified)
        {
            return (null, null);
        }

        var diagnosticLocation = propertyNameLocation ?? GetAttributeLocation(provideAttribute) ?? location;
        if (string.IsNullOrEmpty(propertyName) || !SyntaxFacts.IsValidIdentifier(propertyName!))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidPropertyName,
                diagnosticLocation,
                propertyName));
            return (null, null);
        }

        if (SyntaxFacts.GetKeywordKind(propertyName!) != SyntaxKind.None)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.PropertyNameIsReservedKeyword,
                diagnosticLocation,
                propertyName));
            return (null, null);
        }

        return (propertyName, propertyNameLocation);
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

    private static bool HasProvideAttribute(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(attribute => IsAttribute(attribute, ProvideAttributeName));
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        var attributeName = attribute.AttributeClass?.ToDisplayString();
        return attributeName == metadataName || attributeName == "global::" + metadataName;
    }

    private static bool IsAssignableTo(
        INamedTypeSymbol provider,
        ITypeSymbol serviceType,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var conversion = compilation.ClassifyCommonConversion(provider, serviceType);
            return conversion.Exists &&
                   !conversion.IsUserDefined &&
                   (conversion.IsIdentity || conversion.IsReference || conversion.IsImplicit);
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
                VarianceKind.Out => HasCommonConversion(sourceArgument, targetArgument),
                VarianceKind.In => HasCommonConversion(targetArgument, sourceArgument),
                _ => SymbolEqualityComparer.Default.Equals(sourceArgument, targetArgument),
            };
            if (!converted)
            {
                return false;
            }
        }

        return true;
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, metadataName));
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

    private static Location? GetNamedArgumentLocation(AttributeData attribute, string argumentName)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        if (syntax?.ArgumentList == null)
        {
            return null;
        }

        var argument = syntax.ArgumentList.Arguments.FirstOrDefault(candidate =>
            candidate.NameColon?.Name.Identifier.ValueText == argumentName);
        return argument?.GetLocation();
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
        var displayName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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

        return $"{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, " +
               typeSymbol.ContainingAssembly.Identity;
    }

    private static string GetFullyQualifiedName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
