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
        Action<Diagnostic> reportDiagnostic,
        SemanticModel? semanticModel = null)
    {
        return Validate(
            typeDecl,
            typeSymbol,
            ImmutableHashSet<ServiceTypeIdentity>.Empty,
            reportDiagnostic,
            semanticModel);
    }

    internal static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        ImmutableHashSet<string> knownProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic,
        SemanticModel? semanticModel = null)
    {
        return Validate(
            typeDecl,
            typeSymbol,
            (identity, fullyQualifiedName) => knownProviderFullyQualifiedNames.Contains(fullyQualifiedName),
            reportDiagnostic,
            semanticModel);
    }

    internal static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        ImmutableHashSet<ServiceTypeIdentity> knownProviderIdentities,
        Action<Diagnostic> reportDiagnostic,
        SemanticModel? semanticModel = null)
    {
        return Validate(
            typeDecl,
            typeSymbol,
            (identity, fullyQualifiedName) => knownProviderIdentities.Contains(identity),
            reportDiagnostic,
            semanticModel);
    }

    private static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        Func<ServiceTypeIdentity, string, bool> isKnownProvider,
        Action<Diagnostic> reportDiagnostic,
        SemanticModel? semanticModel)
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

        foreach (var containingType in typeDecl.Ancestors().OfType<TypeDeclarationSyntax>())
        {
            if (!containingType.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeContainingTypeNotPartial,
                    containingType.Identifier.GetLocation(),
                    containingType.Identifier.ValueText));
                return null;
            }
        }

        var consumeAttribute = FindAttribute(typeSymbol, ConsumeAttributeName);
        if (consumeAttribute == null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.FileLocalConsumerNotSupported,
                    typeDecl.Identifier.GetLocation(),
                    typeSymbol.Name));
                return null;
            }
        }

        var consumeAttributeSyntax = consumeAttribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        var aliasQualifiedType = consumeAttributeSyntax?
            .DescendantNodes()
            .OfType<TypeOfExpressionSyntax>()
            .FirstOrDefault(typeOfExpression => typeOfExpression.Type
                .DescendantNodesAndSelf()
                .OfType<AliasQualifiedNameSyntax>()
                .Any());
        if (aliasQualifiedType is not null)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AliasedServiceTypeNotSupported,
                aliasQualifiedType.GetLocation(),
                aliasQualifiedType.Type.ToString()));
            return null;
        }

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
            if (dependencyType is INamedTypeSymbol { IsUnboundGenericType: true })
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.OpenGenericDependencyNotSupported,
                    location ?? typeDecl.Identifier.GetLocation(),
                    GetFullyQualifiedName(dependencyType)));
                continue;
            }

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
                dependencyIdentity,
                CanUseProtectedProperty(dependencyType, typeSymbol.ContainingAssembly)));
        }

        return new ConsumerModel(
            FullyQualifiedName: fullyQualifiedName,
            ShortName: typeSymbol.Name,
            Namespace: typeSymbol.ContainingNamespace.ToDisplayString(),
            IsPartial: true,
            Dependencies: dependencies.ToImmutable(),
            DeclarationShape: CreateShape(typeDecl, typeSymbol, semanticModel));
    }

    private static ConsumerDeclarationShape CreateShape(
        TypeDeclarationSyntax typeDeclaration,
        INamedTypeSymbol typeSymbol,
        SemanticModel? semanticModel)
    {
        var typeParameters = GetTypeParameters(typeDeclaration);
        var containingTypes = typeDeclaration
            .Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(containingType => CreateContainingTypeShape(containingType, semanticModel))
            .ToImmutableArray();
        var namespaceDeclaration = typeDeclaration
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return new ConsumerDeclarationShape(
            GetDeclarationKind(typeDeclaration),
            typeDeclaration.Identifier.ToString(),
            GetEscapedNamespace(typeSymbol.ContainingNamespace),
            typeParameters.Length,
            typeParameters,
            typeDeclaration.TypeParameterList?.ToString() ?? string.Empty,
            GetConstraintClauses(typeDeclaration, semanticModel),
            true,
            typeSymbol.IsSealed,
            containingTypes,
            namespaceDeclaration is FileScopedNamespaceDeclarationSyntax);
    }

    private static ConsumerContainingTypeShape CreateContainingTypeShape(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel? semanticModel)
    {
        var typeParameters = GetTypeParameters(typeDeclaration);
        return new ConsumerContainingTypeShape(
            typeDeclaration.Identifier.ToString(),
            GetDeclarationKind(typeDeclaration),
            typeParameters.Length,
            typeParameters,
            typeDeclaration.TypeParameterList?.ToString() ?? string.Empty,
            GetConstraintClauses(typeDeclaration, semanticModel),
            typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
    }

    private static ImmutableArray<string> GetTypeParameters(TypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration.TypeParameterList?.Parameters
            .Select(parameter => parameter.Identifier.ToString())
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    private static string GetConstraintClauses(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel? semanticModel)
    {
        return string.Join(
            Environment.NewLine,
            typeDeclaration.ConstraintClauses.Select(constraint =>
            {
                if (semanticModel is null)
                {
                    return constraint.ToString();
                }

                var constraints = constraint.Constraints
                    .Select(limitation => limitation is TypeConstraintSyntax typeConstraint
                        ? typeConstraint.WithType(
                            GetQualifiedTypeSyntax(typeConstraint.Type, semanticModel))
                        : limitation)
                    .ToArray();
                return constraint.WithConstraints(SyntaxFactory.SeparatedList(constraints)).ToString();
            }));
    }

    private static TypeSyntax GetQualifiedTypeSyntax(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel)
    {
        var type = semanticModel.GetTypeInfo(typeSyntax).Type;
        return type is null
            ? typeSyntax
            : SyntaxFactory.ParseTypeName(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static ConsumerDeclarationKind GetDeclarationKind(TypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration switch
        {
            RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) =>
                ConsumerDeclarationKind.RecordStruct,
            RecordDeclarationSyntax => ConsumerDeclarationKind.RecordClass,
            StructDeclarationSyntax => ConsumerDeclarationKind.Struct,
            _ => ConsumerDeclarationKind.Class,
        };
    }

    private static string GetEscapedNamespace(INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var namespaceName = namespaceSymbol.ToDisplayString();
        if (namespaceName.StartsWith("global::", StringComparison.Ordinal))
        {
            namespaceName = namespaceName.Substring("global::".Length);
        }

        return string.Join(
            ".",
            namespaceName
                .Split('.')
                .Select(EscapeIdentifier));
    }

    private static string EscapeIdentifier(string identifier)
    {
        if (identifier.StartsWith("@", StringComparison.Ordinal))
        {
            return identifier;
        }

        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
               SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;
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

    private static bool CanUseProtectedProperty(
        ITypeSymbol typeSymbol,
        IAssemblySymbol consumerAssembly)
    {
        if (typeSymbol is ITypeParameterSymbol)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return CanUseProtectedProperty(arrayType.ElementType, consumerAssembly);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return CanUseProtectedProperty(pointerType.PointedAtType, consumerAssembly);
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var isSameAssembly = string.Equals(
            namedType.ContainingAssembly.Identity.ToString(),
            consumerAssembly.Identity.ToString(),
            StringComparison.Ordinal);

        for (INamedTypeSymbol? current = namedType;
             current is not null;
             current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }

            if (current.DeclaredAccessibility == Accessibility.Public ||
                (current.DeclaredAccessibility == Accessibility.ProtectedOrInternal && isSameAssembly))
            {
                continue;
            }

            return false;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            if (!CanUseProtectedProperty(typeArgument, consumerAssembly))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetFullyQualifiedName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
