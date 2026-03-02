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
/// - DM0011: Removes type that is already provided by base class
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingletonDIConsumerCodeFixProvider))]
[Shared]
public class SingletonDIConsumerCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Diagnostic IDs that this provider can fix.
    /// </summary>
    private const string DM0010 = "DM0010";
    private const string DM0011 = "DM0011";

    private const string DM0010Title = "Remove duplicate type";
    private const string DM0011Title = "Remove type from attribute";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [DM0010, DM0011];

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

            switch (diagnostic.Id)
            {
                case DM0010:
                    {
                        var action = CodeAction.Create(
                            DM0010Title,
                            ct => RemoveArgumentFromAttributeAsync(context.Document, attributeSyntax, attributeArgument, ct),
                            DM0010Title);

                        context.RegisterCodeFix(action, diagnostic);
                    }
                    break;

                case DM0011:
                    {
                        var action = CodeAction.Create(
                            DM0011Title,
                            ct => RemoveArgumentFromAttributeAsync(context.Document, attributeSyntax, attributeArgument, ct),
                            DM0011Title);

                        context.RegisterCodeFix(action, diagnostic);
                    }
                    break;
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

        // Create new argument list without the removed argument
        var newArguments = argumentList.Arguments.Remove(argumentToRemove);

        // If no arguments remain, remove the entire attribute
        if (newArguments.Count == 0)
        {
            // Find the attribute list that contains this attribute
            var attributeList = attributeSyntax.Parent as AttributeListSyntax;
            if (attributeList is null)
                return document;

            // If this is the only attribute in the list, remove the entire list
            // But preserve the leading trivia (comments, XML docs) of the next node
            if (attributeList.Attributes.Count == 1)
            {
                // Get the parent node (usually a class declaration)
                var parentNode = attributeList.Parent;
                if (parentNode is null)
                    return document;

                // Get the leading trivia from the attribute list (which includes comments before it)
                var attributeListLeadingTrivia = attributeList.GetLeadingTrivia();

                // Remove the attribute list but keep leading trivia
                var updatedRoot = root.RemoveNode(attributeList, SyntaxRemoveOptions.KeepLeadingTrivia);
                if (updatedRoot is null)
                    return document;

                // If there was leading trivia (comments/XML docs), we need to preserve them
                // by attaching them to the next token
                if (attributeListLeadingTrivia.Any())
                {
                    // Find the same parent in the updated tree
                    var updatedParent = updatedRoot.FindNode(parentNode.Span).FirstAncestorOrSelf<TypeDeclarationSyntax>();
                    if (updatedParent is not null)
                    {
                        // Get the first token of the parent (usually the keyword like "class")
                        var firstToken = updatedParent.GetFirstToken();
                        var newLeadingTrivia = firstToken.LeadingTrivia;

                        // Prepend the preserved trivia
                        var combinedTrivia = attributeListLeadingTrivia.AddRange(newLeadingTrivia);
                        var newFirstToken = firstToken.WithLeadingTrivia(combinedTrivia);

                        updatedRoot = updatedRoot.ReplaceToken(firstToken, newFirstToken);
                    }
                }

                return document.WithSyntaxRoot(updatedRoot);
            }

            // Otherwise, remove just this attribute from the list
            var newAttributeList = attributeList.RemoveNode(attributeSyntax, SyntaxRemoveOptions.KeepNoTrivia);
            if (newAttributeList is null)
                return document;

            var updatedRoot2 = root.ReplaceNode(attributeList, newAttributeList);
            return document.WithSyntaxRoot(updatedRoot2);
        }

        // Create new argument list with remaining arguments
        var newArgumentList = argumentList.WithArguments(newArguments)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newAttribute = attributeSyntax.WithArgumentList(newArgumentList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot2 = root.ReplaceNode(attributeSyntax, newAttribute);
        return document.WithSyntaxRoot(newRoot2);
    }
}
