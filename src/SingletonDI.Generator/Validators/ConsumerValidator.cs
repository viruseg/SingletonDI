using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates consumer types with [Consume] attribute and creates ConsumerModel.
/// </summary>
internal static class ConsumerValidator
{
    /// <summary>
    /// Validates a type with [Consume] attribute and creates a ConsumerModel.
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
        if (!typeDecl.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConsumeNotPartial,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return null;
        }

        // Get dependencies from [Consume] attribute
        var consumeAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.ConsumeAttribute");

        if (consumeAttr == null)
        {
            return null; // Not a consumer
        }

        // Handle params Type[] - it comes as a single array argument
        var dependencyTypes = new List<ITypeSymbol>();

        if (consumeAttr.ConstructorArguments.Length > 0)
        {
            var firstArg = consumeAttr.ConstructorArguments[0];

            // Check if it's an array of types (params Type[])
            if (firstArg.Kind == TypedConstantKind.Array)
            {
                foreach (var element in firstArg.Values)
                {
                    if (element.Kind == TypedConstantKind.Type && element.Value is ITypeSymbol depTypeSymbol)
                    {
                        dependencyTypes.Add(depTypeSymbol);
                    }
                }
            }
            else if (firstArg.Kind == TypedConstantKind.Type && firstArg.Value is ITypeSymbol singleType)
            {
                // Single type argument (not using params)
                dependencyTypes.Add(singleType);
            }
        }

        var dependencies = ImmutableArray.CreateBuilder<string>();

        foreach (var depType in dependencyTypes)
        {
            var depFqn = depType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // DM0006: Reference must have [Provide]
            if (!allProviderFullyQualifiedNames.Contains(depFqn))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeReferencesNonProvider,
                    typeDecl.Identifier.GetLocation(),
                    depType.Name));
                continue;
            }

            // DM0008: Cannot consume itself
            if (depFqn == fqn)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConsumeSelfReference,
                    typeDecl.Identifier.GetLocation(),
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
}
