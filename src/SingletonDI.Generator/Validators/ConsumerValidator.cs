using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates consumer types with [SingletonDIConsume] attribute and creates ConsumerModel.
/// </summary>
internal static class ConsumerValidator
{
    /// <summary>
    /// Validates a type with [SingletonDIConsume] attribute and creates a ConsumerModel.
    /// Returns null if validation fails.
    /// </summary>
    public static ConsumerModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        HashSet<string> allProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // DM0007: Must be partial
        if (!typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConsumeNotPartial,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return null;
        }

        // Get dependencies from [SingletonDIConsume] attribute
        var consumeAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.SingletonDIConsumeAttribute");

        if (consumeAttr == null)
        {
            return null; // Not a consumer
        }

        // Get the attribute syntax for location information
        var consumeAttrSyntax = GetAttributeSyntax(typeDecl, "SingletonDIConsume");

        // Handle params Type[] - it comes as a single array argument
        var dependencyTypes = new List<(ITypeSymbol Type, Location? Location)>();

        if (consumeAttr.ConstructorArguments.Length > 0)
        {
            var firstArg = consumeAttr.ConstructorArguments[0];

            // Check if it's an array of types (params Type[])
            if (firstArg.Kind == TypedConstantKind.Array)
            {
                var argumentLocations = GetArgumentLocations(consumeAttrSyntax);

                for (int i = 0; i < firstArg.Values.Length; i++)
                {
                    var element = firstArg.Values[i];
                    if (element.Kind == TypedConstantKind.Type && element.Value is ITypeSymbol depTypeSymbol)
                    {
                        var location = i < argumentLocations.Count ? argumentLocations[i] : null;
                        dependencyTypes.Add((depTypeSymbol, location));
                    }
                }
            }
            else if (firstArg.Kind == TypedConstantKind.Type && firstArg.Value is ITypeSymbol singleType)
            {
                // Single type argument (not using params)
                var argumentLocations = GetArgumentLocations(consumeAttrSyntax);
                var location = argumentLocations.Count > 0 ? argumentLocations[0] : null;
                dependencyTypes.Add((singleType, location));
            }
        }

        // DM0010: Check for duplicate types in attribute arguments
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        var duplicateTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (depType, _) in dependencyTypes)
        {
            var depFqn = depType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenTypes.Add(depFqn))
            {
                duplicateTypes.Add(depFqn);
            }
        }

        // Report DM0010 for each duplicate
        foreach (var (depType, location) in dependencyTypes)
        {
            var depFqn = depType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (duplicateTypes.Contains(depFqn) && seenTypes.Remove(depFqn))
            {
                // Only report for the second (and subsequent) occurrences
                continue;
            }
            if (duplicateTypes.Contains(depFqn))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeDuplicateTypes,
                    location ?? typeDecl.Identifier.GetLocation(),
                    depType.Name));
            }
        }

        // DM0011: Check for duplicates in base classes
        var baseClassTypes = GetTypesFromBaseClasses(typeSymbol);
        var dependencies = ImmutableArray.CreateBuilder<string>();

        foreach (var (depType, location) in dependencyTypes)
        {
            var depFqn = depType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Skip duplicates in attribute arguments (already reported as DM0010)
            if (duplicateTypes.Contains(depFqn))
            {
                continue;
            }

            // DM0011: Check if type is already declared in base class
            if (baseClassTypes.TryGetValue(depFqn, out var baseClassName))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeDuplicateInBaseClass,
                    location ?? typeDecl.Identifier.GetLocation(),
                    depType.Name,
                    baseClassName));
                continue;
            }

            // DM0006: Reference must have [SingletonDIProvide]
            if (!allProviderFullyQualifiedNames.Contains(depFqn))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeReferencesNonProvider,
                    location ?? typeDecl.Identifier.GetLocation(),
                    depType.Name));
                continue;
            }

            // DM0008: Cannot consume itself
            if (depFqn == fqn)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeSelfReference,
                    location ?? typeDecl.Identifier.GetLocation(),
                    typeSymbol.Name));
                continue;
            }

            dependencies.Add(depFqn);
        }

        return new ConsumerModel
        {
            FullyQualifiedName = fqn,
            ShortName = typeSymbol.Name,
            Namespace = typeSymbol.ContainingNamespace.ToDisplayString(),
            IsPartial = true,
            Dependencies = dependencies.ToImmutable()
        };
    }

    /// <summary>
    /// Gets the attribute syntax for the specified attribute name.
    /// </summary>
    private static AttributeSyntax? GetAttributeSyntax(TypeDeclarationSyntax typeDecl, string attributeName)
    {
        foreach (var attributeList in typeDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                // Handle both "SingletonDIConsume" and "SingletonDIConsumeAttribute" forms
                if (name == attributeName || name == attributeName + "Attribute")
                {
                    return attribute;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the locations of type arguments in the attribute.
    /// </summary>
    private static List<Location> GetArgumentLocations(AttributeSyntax? attributeSyntax)
    {
        var locations = new List<Location>();

        if (attributeSyntax?.ArgumentList == null)
        {
            return locations;
        }

        foreach (var argument in attributeSyntax.ArgumentList.Arguments)
        {
            // For typeof(T) expressions, get the location of the type
            if (argument.Expression is TypeOfExpressionSyntax typeOfExpr)
            {
                locations.Add(typeOfExpr.Type.GetLocation());
            }
            else
            {
                locations.Add(argument.GetLocation());
            }
        }

        return locations;
    }

    /// <summary>
    /// Gets all types declared in [SingletonDIConsume] attributes on base classes.
    /// </summary>
    private static Dictionary<string, string> GetTypesFromBaseClasses(INamedTypeSymbol typeSymbol)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var currentBase = typeSymbol.BaseType;

        while (currentBase != null)
        {
            var baseConsumeAttr = currentBase.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.SingletonDIConsumeAttribute");

            if (baseConsumeAttr != null && baseConsumeAttr.ConstructorArguments.Length > 0)
            {
                var firstArg = baseConsumeAttr.ConstructorArguments[0];

                if (firstArg.Kind == TypedConstantKind.Array)
                {
                    foreach (var element in firstArg.Values)
                    {
                        if (element is { Kind: TypedConstantKind.Type, Value: ITypeSymbol depType })
                        {
                            var depFqn = depType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (!result.ContainsKey(depFqn))
                            {
                                result[depFqn] = currentBase.Name;
                            }
                        }
                    }
                }
                else if (firstArg is { Kind: TypedConstantKind.Type, Value: ITypeSymbol singleType })
                {
                    var depFqn = singleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (!result.ContainsKey(depFqn))
                    {
                        result[depFqn] = currentBase.Name;
                    }
                }
            }

            currentBase = currentBase.BaseType;
        }

        return result;
    }
}
