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
/// Provides code fixes for Consumer-related diagnostics:
/// - DM0010: Removes duplicate type from SingletonDIConsume attribute
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingletonDIConsumerCodeFixProvider))]
[Shared]
public class SingletonDIConsumerCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Diagnostic IDs that this provider can fix.
    /// </summary>
    private const string DM0010 = "DM0010";

    private const string DM0010Title = "Remove duplicate type";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DM0010);

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
            // Find the node at the diagnostic location
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            // The diagnostic location points to the type inside typeof expression
            // We need to find the containing attribute argument
            var typeOfExpression = node.FirstAncestorOrSelf<TypeOfExpressionSyntax>();
            if (typeOfExpression is null)
                continue;

            var attributeArgument = typeOfExpression.Parent as AttributeArgumentSyntax;
            if (attributeArgument is null)
                continue;

            var attributeSyntax = attributeArgument.FirstAncestorOrSelf<AttributeSyntax>();
            if (attributeSyntax is null)
                continue;

            if (diagnostic.Id == DM0010)
            {
                var action = CodeAction.Create(
                    DM0010Title,
                    ct => RemoveArgumentFromAttributeAsync(context.Document, attributeSyntax, attributeArgument, ct),
                    DM0010Title);

                context.RegisterCodeFix(action, diagnostic);
            }
        }
    }

    private static async Task<Document> RemoveArgumentFromAttributeAsync(
        Document document,
        AttributeSyntax attributeSyntax,
        AttributeArgumentSyntax argumentToRemove,
        CancellationToken cancellationToken)
    {
        var argumentList = attributeSyntax.ArgumentList;
        if (argumentList is null)
            return document;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // DM0010 is reported for every duplicate after the first one, so at least one
        // argument always survives and the attribute itself is never removed.
        var newAttributeList = argumentList
            .WithArguments(argumentList.Arguments.Remove(argumentToRemove))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newAttribute = attributeSyntax.WithArgumentList(newAttributeList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot2 = root.ReplaceNode(attributeSyntax, newAttribute);
        return document.WithSyntaxRoot(newRoot2);
    }
}
