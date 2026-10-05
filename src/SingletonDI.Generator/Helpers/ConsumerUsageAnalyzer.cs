using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Models;
using SingletonDI.Generator.Validators;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Reports the dependencies a consumer declares and never reads.
/// </summary>
/// <remarks>
/// <para>
/// A dependency counts as read when anything in the consumer's declaration names either the generated
/// property that resolves it or the declared type itself, because a provider is a plain class whose
/// members can be reached through directly.
/// </para>
/// <para>
/// The scan covers every part of a partial consumer, the types nested in it, and every type of the
/// same assembly that derives from it: the consume attribute is inherited, so a derived declaration
/// reads the same generated properties without repeating the attribute. A derived type from another
/// assembly is not visible here, which is why the diagnostic stays a suggestion.
/// </para>
/// <para>
/// Nothing but the consumer symbol and its dependencies crosses into this method, and both are read
/// back from the compilation being analyzed. A report built here therefore always points into the
/// compilation that produced it, which a cached candidate carrying its own locations could not
/// promise.
/// </para>
/// </remarks>
internal static class ConsumerUsageAnalyzer
{
    /// <summary>
    /// Reports DM0036 for every dependency of <paramref name="consumer"/> that nothing in its
    /// declaration reads.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="consumer">The consumer whose dependencies are judged.</param>
    /// <param name="dependencies">The validated dependencies of the consumer.</param>
    /// <param name="reportDiagnostic">Receives one diagnostic per unread dependency.</param>
    internal static void ReportUnusedDependencies(
        Compilation compilation,
        INamedTypeSymbol consumer,
        ImmutableArray<ServiceReferenceModel> dependencies,
        Action<Diagnostic> reportDiagnostic)
    {
        if (dependencies.IsEmpty)
        {
            return;
        }

        var declaredTypes = GetDeclaredTypes(consumer);
        var propertyNames = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity(dependencies, out _);
        var usages = new List<DependencyUsage>(dependencies.Length);

        foreach (var dependency in dependencies)
        {
            var declaredIndex = IndexOfDeclaredType(declaredTypes, dependency);
            if (declaredIndex < 0)
            {
                continue;
            }

            var identity = dependency.Identity is { } identityValue &&
                           !string.IsNullOrEmpty(identityValue.FullyQualifiedName)
                ? identityValue
                : new ServiceTypeIdentity(dependency.FullyQualifiedName, string.Empty);
            if (!propertyNames.TryGetValue(identity, out var propertyName))
            {
                continue;
            }

            var declared = declaredTypes[declaredIndex];
            usages.Add(new DependencyUsage(declared.Type, propertyName, declared.Location));
        }

        if (usages.Count == 0)
        {
            return;
        }

        foreach (var declaration in GetDeclarationsToScan(consumer))
        {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var candidatesByName = BuildCandidateIndex(usages, model, declaration);
            if (MarkUsages(declaration, model, usages, candidatesByName))
            {
                break;
            }
        }

        foreach (var usage in usages)
        {
            if (usage.IsUsed)
            {
                continue;
            }

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConsumeDependencyUnused,
                usage.Location,
                usage.Type.Name,
                consumer.Name));
        }
    }

    /// <summary>
    /// Reads every type the consumer's consume attributes declare, paired with the location of the
    /// name that declares it. A type declared without a location in source cannot be pointed at, so
    /// it is left out.
    /// </summary>
    private static ImmutableArray<DeclaredType> GetDeclaredTypes(INamedTypeSymbol consumer)
    {
        var builder = ImmutableArray.CreateBuilder<DeclaredType>();

        foreach (var (type, location) in ConsumerValidator.GetTypeArguments(
                     ConsumerValidator.FindConsumeAttributes(consumer)))
        {
            if (type is INamedTypeSymbol namedType && location is { IsInSource: true } sourceLocation)
            {
                builder.Add(new DeclaredType(namedType, sourceLocation));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Finds the declared type a validated dependency stands for. A dependency that validation
    /// rejected is absent from both collections and is skipped rather than reported.
    /// </summary>
    private static int IndexOfDeclaredType(
        ImmutableArray<DeclaredType> declaredTypes,
        ServiceReferenceModel dependency)
    {
        for (var index = 0; index < declaredTypes.Length; index++)
        {
            if (declaredTypes[index].Type.ToDisplayString(SymbolDisplayFormats.CodeGeneration) ==
                dependency.FullyQualifiedName)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Maps every text a reference to one of the dependencies can carry to the dependencies it
    /// would satisfy, so a body is walked once no matter how many dependencies it declares.
    /// </summary>
    private static Dictionary<string, List<DependencyUsage>> BuildCandidateIndex(
        List<DependencyUsage> usages,
        SemanticModel semanticModel,
        TypeDeclarationSyntax declaration)
    {
        var index = new Dictionary<string, List<DependencyUsage>>(StringComparer.Ordinal);
        foreach (var usage in usages)
        {
            Add(index, usage.Type.Name, usage);
            if (!string.Equals(usage.PropertyName, usage.Type.Name, StringComparison.Ordinal))
            {
                Add(index, usage.PropertyName, usage);
            }
        }

        foreach (var directive in GetAliasDirectives(declaration, usages))
        {
            var name = directive.Alias!.Name.Identifier.ValueText;
            var symbol = directive.Name is null
                ? null
                : semanticModel.GetSymbolInfo(directive.Name).Symbol as INamedTypeSymbol;
            if (symbol is null)
            {
                continue;
            }

            foreach (var usage in usages)
            {
                if (!usage.IsUsed && SymbolEqualityComparer.Default.Equals(symbol, usage.Type))
                {
                    Add(index, name, usage);
                }
            }
        }

        return index;
    }

    private static void Add(
        Dictionary<string, List<DependencyUsage>> index,
        string name,
        DependencyUsage usage)
    {
        if (!index.TryGetValue(name, out var usages))
        {
            usages = new List<DependencyUsage>(1);
            index.Add(name, usages);
        }

        usages.Add(usage);
    }

    /// <summary>
    /// Returns the using aliases of the declaration's file, because an alias is a second text the
    /// declared type can be reached through.
    /// </summary>
    private static IEnumerable<UsingDirectiveSyntax> GetAliasDirectives(
        TypeDeclarationSyntax declaration,
        List<DependencyUsage> usages)
    {
        if (usages.TrueForAll(usage => usage.IsUsed))
        {
            return [];
        }

        return declaration.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(directive => directive.Alias is not null);
    }

    /// <summary>
    /// Marks every dependency the declaration reads and reports whether all of them are now read.
    /// </summary>
    private static bool MarkUsages(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        List<DependencyUsage> usages,
        Dictionary<string, List<DependencyUsage>> candidatesByName)
    {
        foreach (var node in EnumerateBodyNodes(declaration))
        {
            if (!TryGetSimpleName(node, out var name) ||
                !candidatesByName.TryGetValue(name, out var candidates))
            {
                continue;
            }

            foreach (var usage in candidates)
            {
                if (string.Equals(usage.PropertyName, name, StringComparison.Ordinal))
                {
                    usage.IsUsed = true;
                }
            }

            if (candidates.TrueForAll(usage => usage.IsUsed))
            {
                continue;
            }

            var symbol = ResolveTypeSymbol(node, semanticModel);
            if (symbol is not null)
            {
                foreach (var usage in candidates)
                {
                    if (SymbolEqualityComparer.Default.Equals(symbol, usage.Type))
                    {
                        usage.IsUsed = true;
                    }
                }
            }
        }

        return usages.TrueForAll(usage => usage.IsUsed);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(SyntaxNode node, SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(node).Symbol as INamedTypeSymbol;
        return symbol ?? semanticModel.GetTypeInfo(node).Type as INamedTypeSymbol;
    }

    /// <summary>
    /// Walks the body of a declaration, skipping attribute lists: a consume attribute names the very
    /// dependencies this diagnostic is about, and a nested consumer's attribute says nothing about
    /// what the containing consumer reads.
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateBodyNodes(TypeDeclarationSyntax declaration)
    {
        var pending = new Stack<SyntaxNode>();
        PushMembers(pending, declaration);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;

            if (node is TypeDeclarationSyntax nested)
            {
                PushMembers(pending, nested);
                continue;
            }

            foreach (var child in node.ChildNodes())
            {
                pending.Push(child);
            }
        }
    }

    private static void PushMembers(Stack<SyntaxNode> pending, TypeDeclarationSyntax declaration)
    {
        for (var index = declaration.Members.Count - 1; index >= 0; index--)
        {
            pending.Push(declaration.Members[index]);
        }
    }

    private static bool TryGetSimpleName(SyntaxNode node, out string name)
    {
        switch (node)
        {
            case SimpleNameSyntax simple:
                name = simple.Identifier.ValueText;
                return true;
            case QualifiedNameSyntax qualified:
                return TryGetSimpleName(qualified.Right, out name);
            case AliasQualifiedNameSyntax alias:
                return TryGetSimpleName(alias.Name, out name);
            default:
                name = string.Empty;
                return false;
        }
    }

    private static IEnumerable<TypeDeclarationSyntax> GetDeclarationsToScan(INamedTypeSymbol consumer)
    {
        foreach (var declaration in GetDeclarations(consumer))
        {
            yield return declaration;
        }

        if (!CanBeInherited(consumer))
        {
            yield break;
        }

        foreach (var type in EnumerateTypes(consumer.ContainingAssembly.GlobalNamespace))
        {
            if (!IsDerivedFrom(type, consumer))
            {
                continue;
            }

            foreach (var declaration in GetDeclarations(type))
            {
                yield return declaration;
            }
        }
    }

    private static IEnumerable<TypeDeclarationSyntax> GetDeclarations(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration)
            {
                yield return declaration;
            }
        }
    }

    /// <summary>
    /// A sealed or static consumer and every struct have no derived declaration that could read an
    /// inherited generated property, so nothing has to be searched for them.
    /// </summary>
    private static bool CanBeInherited(INamedTypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class && !type.IsSealed && !type.IsStatic;
    }

    private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (var type in EnumerateTypes(childNamespace))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;

                foreach (var nested in EnumerateTypes(type))
                {
                    yield return nested;
                }
            }
        }
    }

    private sealed class DependencyUsage
    {
        internal DependencyUsage(ITypeSymbol type, string propertyName, Location location)
        {
            Type = type;
            PropertyName = propertyName;
            Location = location;
        }

        internal ITypeSymbol Type { get; }

        internal string PropertyName { get; }

        internal Location Location { get; }

        internal bool IsUsed { get; set; }
    }

    private readonly record struct DeclaredType(ITypeSymbol Type, Location Location);
}