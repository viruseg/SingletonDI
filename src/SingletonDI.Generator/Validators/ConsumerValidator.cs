using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates consumer types with <c>SingletonDIConsumeAttribute</c> and creates consumer models.
/// </summary>
internal static class ConsumerValidator
{
    private const string ProvideAttributeName = "SingletonDI.Attributes.SingletonDIProvideAttribute";
    private const string ConsumeAttributeName = "SingletonDI.Attributes.SingletonDIConsumeAttribute";

    internal static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeDecl,
            typeSymbol,
            (identity, fullyQualifiedName) => knownProviderFullyQualifiedNames.Contains(fullyQualifiedName),
            reportDiagnostic);
    }

    internal static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        ImmutableHashSet<ServiceTypeIdentity> knownProviderIdentities,
        Action<Diagnostic> reportDiagnostic)
    {
        return Validate(
            typeDecl,
            typeSymbol,
            (identity, fullyQualifiedName) => knownProviderIdentities.Contains(identity),
            reportDiagnostic);
    }

    private static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        Func<ServiceTypeIdentity, string, bool> isKnownProvider,
        Action<Diagnostic> reportDiagnostic)
    {
        var fullyQualifiedName = GetFullyQualifiedName(typeSymbol);
        var consumerIdentity = CreateIdentity(typeSymbol);

        if (!typeDecl.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConsumeNotPartial,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return null;
        }

        var consumeAttribute = FindAttribute(typeSymbol, ConsumeAttributeName);
        if (consumeAttribute == null)
        {
            return null;
        }

        var consumeAttributeSyntax = consumeAttribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        var argumentLocations = GetArgumentLocations(consumeAttributeSyntax);
        var dependencyTypes = GetTypeArguments(consumeAttribute, argumentLocations);

        var seenTypes = new HashSet<ServiceTypeIdentity>();
        var duplicateTypes = new HashSet<ServiceTypeIdentity>();
        foreach (var (dependencyType, _) in dependencyTypes)
        {
            var dependencyIdentity = CreateIdentity(dependencyType);
            if (!seenTypes.Add(dependencyIdentity))
            {
                duplicateTypes.Add(dependencyIdentity);
            }
        }

        foreach (var (dependencyType, location) in dependencyTypes)
        {
            var dependencyIdentity = CreateIdentity(dependencyType);
            if (duplicateTypes.Contains(dependencyIdentity) &&
                seenTypes.Remove(dependencyIdentity))
            {
                continue;
            }

            if (duplicateTypes.Contains(dependencyIdentity))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeDuplicateTypes,
                    location ?? typeDecl.Identifier.GetLocation(),
                    dependencyType.Name));
            }
        }

        var dependencies = ImmutableArray.CreateBuilder<ServiceReferenceModel>();
        foreach (var (dependencyType, location) in dependencyTypes)
        {
            var dependencyFullyQualifiedName = GetFullyQualifiedName(dependencyType);
            var dependencyIdentity = CreateIdentity(dependencyType);

            if (duplicateTypes.Contains(dependencyIdentity))
            {
                continue;
            }

            var hasProvideAttribute = HasProvideAttribute(dependencyType);
            var isProvider = isKnownProvider(dependencyIdentity, dependencyFullyQualifiedName) ||
                             hasProvideAttribute;
            var isContract = !isProvider &&
                             (dependencyType.TypeKind == TypeKind.Interface ||
                              (dependencyType.TypeKind == TypeKind.Class &&
                               dependencyType.IsAbstract &&
                               !dependencyType.IsStatic));

            if (!isProvider && !isContract)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeReferencesNonProvider,
                    location ?? typeDecl.Identifier.GetLocation(),
                    dependencyType.Name));
                continue;
            }

            if (dependencyIdentity == consumerIdentity)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeSelfReference,
                    location ?? typeDecl.Identifier.GetLocation(),
                    typeSymbol.Name));
                continue;
            }

            dependencies.Add(new ServiceReferenceModel(
                dependencyFullyQualifiedName,
                dependencyType.Name,
                dependencyType.ContainingNamespace.ToDisplayString(),
                hasProvideAttribute ? GetProviderPropertyName(dependencyType) : null,
                isContract,
                dependencyIdentity));
        }

        return new ConsumerModel(
            FullyQualifiedName: fullyQualifiedName,
            ShortName: typeSymbol.Name,
            Namespace: typeSymbol.ContainingNamespace.ToDisplayString(),
            IsPartial: true,
            Dependencies: dependencies.ToImmutable());
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(attribute => IsAttribute(attribute, metadataName));
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

    private static string? GetProviderPropertyName(ITypeSymbol typeSymbol)
    {
        var attribute = FindAttribute(typeSymbol, ProvideAttributeName);
        if (attribute == null)
        {
            return null;
        }

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0] is
                { Kind: TypedConstantKind.Primitive, Value: string constructorValue })
        {
            return constructorValue;
        }

        var namedArgument = attribute.NamedArguments
            .FirstOrDefault(argument => argument.Key == "PropertyName");

        return namedArgument.Value is { Kind: TypedConstantKind.Primitive, Value: string namedValue }
            ? namedValue
            : null;
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

    private static ServiceTypeIdentity CreateIdentity(ITypeSymbol typeSymbol)
    {
        return ServiceTypeIdentity.FromSymbol(typeSymbol);
    }

    private static string GetFullyQualifiedName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
