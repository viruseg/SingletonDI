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
        var modifiers = typeDeclaration.Modifiers;

        // 'partial' goes last in the modifier list so the conventional
        // 'public sealed partial class' ordering is preserved.
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var newTypeDeclaration = typeDeclaration.WithModifiers(
            modifiers.Insert(modifiers.Count, partialToken));

        // A declaration's leading trivia - documentation comments, #region - hangs off its first
        // token. With no modifiers the inserted 'partial' token ends up in front of the keyword,
        // so the trivia has to follow it; leaving it behind strands the documentation between the
        // modifier and the keyword, detaches it from the type, and pushes a #region off the start
        // of its line, which the workspace formatter cannot repair.
        if (modifiers.Count == 0)
        {
            var keyword = newTypeDeclaration.Modifiers[0].GetNextToken();
            var declarationTrivia = keyword.LeadingTrivia;
            newTypeDeclaration = newTypeDeclaration.ReplaceToken(
                keyword,
                keyword.WithLeadingTrivia(SyntaxFactory.TriviaList()));
            var insertedPartial = newTypeDeclaration.Modifiers[0];
            newTypeDeclaration = newTypeDeclaration.ReplaceToken(
                insertedPartial,
                insertedPartial.WithLeadingTrivia(declarationTrivia));
        }

        newTypeDeclaration = newTypeDeclaration.WithAdditionalAnnotations(Formatter.Annotation);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }
}
