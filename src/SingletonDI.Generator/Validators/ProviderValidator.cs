using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates provider types with [Provide] attribute and creates ProviderModel.
/// </summary>
internal static class ProviderValidator
{
    private const string InitializeSyncFullName = "SingletonDI.Attributes.IInitializeSync";
    private const string InitializeAsyncFullName = "SingletonDI.Attributes.IInitializeAsync";

    /// <summary>
    /// Validates a type with [Provide] attribute and creates a ProviderModel.
    /// Returns null if validation fails.
    /// </summary>
    public static ProviderModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        HashSet<string> allProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        // DM0001: Cannot be struct or record struct
        if (typeDecl.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructDeclaration))
        {
            var recordKeyword = typeDecl.ChildTokens()
                .Any(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordKeyword));

            if (recordKeyword)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ProvideOnStruct,
                    typeDecl.Identifier.GetLocation()));
                return null;
            }
        }

        // DM0002: Cannot be abstract class
        if (typeSymbol.IsAbstract)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideOnAbstractClass,
                typeDecl.Identifier.GetLocation()));
            return null;
        }

        // DM0003: Cannot be interface
        if (typeSymbol.TypeKind == TypeKind.Interface)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideOnInterface,
                typeDecl.Identifier.GetLocation()));
            return null;
        }

        // DM0004: Must have public parameterless constructor
        var constructor = typeSymbol.InstanceConstructors
            .FirstOrDefault(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);

        if (constructor == null)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideMissingParameterlessConstructor,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return null;
        }

        // Check for IInitializeSync and IInitializeAsync
        var hasSyncInit = typeSymbol.Interfaces.Any(i => i.OriginalDefinition.ToDisplayString() == InitializeSyncFullName);
        var hasAsyncInit = typeSymbol.Interfaces.Any(i => i.OriginalDefinition.ToDisplayString() == InitializeAsyncFullName);

        // DM0010: Cannot have both
        if (hasSyncInit && hasAsyncInit)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideBothInitializationMethods,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
            return null;
        }

        // DM0005: Warning if neither
        if (!hasSyncInit && !hasAsyncInit)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideMissingInitialization,
                typeDecl.Identifier.GetLocation(),
                typeSymbol.Name));
        }

        // Check for IDisposable
        var isDisposable = typeSymbol.Interfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IDisposable");

        // Get dependencies (if this provider also has [Consume])
        var dependencies = GetDependencies(typeSymbol, allProviderFullyQualifiedNames);

        var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new ProviderModel
        {
            FullyQualifiedName = fqn,
            ShortName = typeSymbol.Name,
            Namespace = typeSymbol.ContainingNamespace.ToDisplayString(),
            HasSyncInit = hasSyncInit,
            HasAsyncInit = hasAsyncInit,
            IsDisposable = isDisposable,
            Dependencies = dependencies
        };
    }

    private static ImmutableArray<string> GetDependencies(
        INamedTypeSymbol typeSymbol,
        HashSet<string> allProviderFullyQualifiedNames)
    {
        // Find [Consume] attribute on this type
        var consumeAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.ConsumeAttribute");

        if (consumeAttr == null)
        {
            return ImmutableArray<string>.Empty;
        }

        var dependencies = consumeAttr.ConstructorArguments
            .Where(arg => arg.Kind == TypedConstantKind.Type)
            .Select(arg => ((ITypeSymbol)arg.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Where(fqn => allProviderFullyQualifiedNames.Contains(fqn))
            .ToImmutableArray();

        return dependencies;
    }
}
