using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Validators;

/// <summary>
/// Validates provider types with [SingletonDIProvide] attribute and creates ProviderModel.
/// </summary>
internal static class ProviderValidator
{
    /// <summary>
    /// Validates a type with [SingletonDIProvide] attribute and creates a ProviderModel.
    /// Returns null if validation fails.
    /// </summary>
    public static ProviderModel? Validate(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        HashSet<string> allProviderFullyQualifiedNames,
        Action<Diagnostic> reportDiagnostic)
    {
        // DM0002: Cannot be abstract class
        if (typeSymbol.IsAbstract)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ProvideOnAbstractClass,
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

        // Check for InitializeAsync method with signature "Task InitializeAsync()"
        var initializeAsyncMethod = FindInitializeAsyncMethod(typeSymbol);

        // DM0005: Validate InitializeAsync access modifier
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
                    initializeAsyncMethod.Locations.FirstOrDefault(),
                    accessibility.ToString().ToLower()));
                return null;
            }
        }

        // DM0012: InitializeAsync cannot be static
        if (initializeAsyncMethod is { IsStatic: true })
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InitializeAsyncIsStatic,
                initializeAsyncMethod.Locations.FirstOrDefault(),
                typeSymbol.Name));
            return null;
        }

        // Check for IDisposable
        var isDisposable = typeSymbol.Interfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IDisposable");

        // Get dependencies (if this provider also has [SingletonDIConsume])
        var dependencies = GetDependencies(typeSymbol, allProviderFullyQualifiedNames);

        // Get custom property name from [SingletonDIProvide] attribute
        var propertyName = GetPropertyName(typeSymbol);

        var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new ProviderModel(fullyQualifiedName : fqn,
                                 shortName : typeSymbol.Name,
                                 @namespace : typeSymbol.ContainingNamespace.ToDisplayString(),
                                 hasInitializeAsyncMethod : initializeAsyncMethod != null,
                                 isDisposable : isDisposable,
                                 dependencies : dependencies,
                                 propertyName : propertyName);
    }

    /// <summary>
    /// Finds the InitializeAsync method with signature "Task InitializeAsync()".
    /// Returns null if not found.
    /// Note: This method finds both static and instance methods.
    /// Static methods are validated separately by DM0012 diagnostic.
    /// </summary>
    private static IMethodSymbol? FindInitializeAsyncMethod(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol { Name: "InitializeAsync", Parameters.IsEmpty: true } method)
            {
                // Check if return type is Task
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
        }

        return null;
    }

    private static ImmutableArray<string> GetDependencies(
        INamedTypeSymbol typeSymbol,
        HashSet<string> allProviderFullyQualifiedNames)
    {
        // Find [SingletonDIConsume] attribute on this type
        var consumeAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.SingletonDIConsumeAttribute");

        if (consumeAttr == null)
        {
            return ImmutableArray<string>.Empty;
        }

        var dependencies = new List<string>();
        foreach (var arg in consumeAttr.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array && arg.Values != null)
            {
                // params Type[] - массив типов
                foreach (var element in arg.Values)
                {
                    if (element.Kind == TypedConstantKind.Type && element.Value is ITypeSymbol dependencyType)
                    {
                        var fqn = dependencyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (allProviderFullyQualifiedNames.Contains(fqn))
                        {
                            dependencies.Add(fqn);
                        }
                    }
                }
            }
            else if (arg.Kind == TypedConstantKind.Type && arg.Value is ITypeSymbol singleType)
            {
                // Одиночный тип (если когда-либо будет использоваться без params)
                var fqn = singleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (allProviderFullyQualifiedNames.Contains(fqn))
                {
                    dependencies.Add(fqn);
                }
            }
        }
        return dependencies.ToImmutableArray();
    }

    /// <summary>
    /// Gets the custom property name from [SingletonDIProvide] attribute.
    /// Returns null if not specified.
    /// </summary>
    private static string? GetPropertyName(INamedTypeSymbol typeSymbol)
    {
        var provideAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "SingletonDI.Attributes.SingletonDIProvideAttribute");

        if (provideAttr == null)
        {
            return null;
        }

        // Check constructor arguments
        if (provideAttr.ConstructorArguments.Length > 0)
        {
            var arg = provideAttr.ConstructorArguments[0];
            if (arg.Kind == TypedConstantKind.Primitive && arg.Value is string propertyName && !string.IsNullOrEmpty(propertyName))
            {
                return propertyName;
            }
        }

        return null;
    }
}
