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
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

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
        // The 'partial' modifier must appear immediately before 'class', 'struct', 'record', or 'interface'.
        // According to C# specification, it should be the last modifier before the type keyword.
        // Correct order: public sealed partial class MyClass
        // Incorrect order: public partial sealed class MyClass
        var modifiers = typeDeclaration.Modifiers;

        // Insert 'partial' at the end of the modifiers list (right before the type keyword)
        var insertIndex = modifiers.Count;

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
}
