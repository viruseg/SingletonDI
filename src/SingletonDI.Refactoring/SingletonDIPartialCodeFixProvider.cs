using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SingletonDI.Refactoring;

/// <summary>
/// Provides a code fix for DM0007: Adds the 'partial' modifier to types marked with [SingletonDIConsume] attribute.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingletonDIPartialCodeFixProvider))]
[Shared]
public class SingletonDIPartialCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "DM0007";
    private const string Title = "Add 'partial' modifier";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticId];

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id != DiagnosticId)
                continue;

            var node = root.FindNode(diagnostic.Location.SourceSpan);

            // Try to find the type declaration (class, record, or record struct)
            var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (typeDeclaration is null)
                continue;

            // Check if already has partial modifier
            if (typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                continue;

            var action = CodeAction.Create(
                Title,
                ct => AddPartialModifierAsync(context.Document, typeDeclaration, ct),
                Title);

            context.RegisterCodeFix(action, diagnostic);
        }
    }

    private static async Task<Document> AddPartialModifierAsync(
        Document document,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken cancellationToken)
    {
        // Find the position where 'partial' should be inserted
        // It should come after any access modifiers (public, internal, private, protected)
        // but before other modifiers (static, sealed, abstract, etc.)
        var modifiers = typeDeclaration.Modifiers;

        var insertIndex = 0;
        var foundAccessModifier = false;

        for (var i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (IsAccessModifier(modifier.Kind()))
            {
                insertIndex = i + 1;
                foundAccessModifier = true;
            }
            else if (foundAccessModifier)
            {
                // Found a non-access modifier after access modifiers, insert before it
                insertIndex = i;
                break;
            }
        }

        // If no access modifiers found, insert at the beginning
        if (!foundAccessModifier && modifiers.Count > 0)
        {
            // Check if first modifier is static - partial should come after static
            if (modifiers[0].IsKind(SyntaxKind.StaticKeyword))
            {
                insertIndex = 1;
            }
        }

        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = modifiers.Insert(insertIndex, partialToken);

        var newTypeDeclaration = typeDeclaration.WithModifiers(newModifiers)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsAccessModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword;
    }
}
