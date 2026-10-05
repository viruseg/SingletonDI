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

namespace SingletonDI.Refactoring;

/// <summary>
/// Provides code fixes for Consumer-related diagnostics:
/// - DM0010: Removes duplicate type from SingletonDIConsume attribute
/// - DM0036: Removes a dependency the consumer never uses from SingletonDIConsume attribute
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingletonDIConsumerCodeFixProvider))]
[Shared]
public class SingletonDIConsumerCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Diagnostic IDs that this provider can fix.
    /// </summary>
    private const string DM0010 = "DM0010";
    private const string DM0036 = "DM0036";

    private const string DM0010Title = "Remove duplicate type";
    private const string DM0036Title = "Remove unused dependency";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DM0010, DM0036);

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
            else if (diagnostic.Id == DM0036)
            {
                var action = CodeAction.Create(
                    DM0036Title,
                    ct => RemoveDependencyAsync(context.Document, attributeSyntax, attributeArgument, ct),
                    DM0036Title);

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
        var newAttributeList = CodeFixFormatting.RemoveArgument(argumentList, argumentToRemove);

        var newAttribute = attributeSyntax.WithArgumentList(newAttributeList);

        var newRoot2 = root.ReplaceNode(attributeSyntax, newAttribute);
        return document.WithSyntaxRoot(newRoot2);
    }

    /// <summary>
    /// Removes one dependency from a consume attribute, and the attribute itself once it declares
    /// nothing: a consumer that lists no type is a consumer that generates nothing, so leaving an
    /// empty attribute behind would trade one redundant type for a redundant attribute.
    /// </summary>
    private static async Task<Document> RemoveDependencyAsync(
        Document document,
        AttributeSyntax attributeSyntax,
        AttributeArgumentSyntax argumentToRemove,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var typeDeclaration = attributeSyntax.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        var attributeList = attributeSyntax.Parent as AttributeListSyntax;
        if (typeDeclaration is null || attributeList is null)
            return document;

        var argumentList = attributeSyntax.ArgumentList;
        if (argumentList is null || argumentList.Arguments.IndexOf(argumentToRemove) < 0)
            return document;

        if (argumentList.Arguments.Count > 1)
        {
            var newAttribute = attributeSyntax.WithArgumentList(
                CodeFixFormatting.RemoveArgument(argumentList, argumentToRemove));
            return document.WithSyntaxRoot(root.ReplaceNode(attributeSyntax, newAttribute));
        }

        var newAttributeList = attributeList.Attributes.Count > 1
            ? CodeFixFormatting.RemoveAttribute(attributeList, attributeSyntax)
            : null;
        var newTypeDeclaration = newAttributeList is null
            ? CodeFixFormatting.RemoveAttributeList(typeDeclaration, attributeList)
            : typeDeclaration.WithAttributeLists(
                typeDeclaration.AttributeLists.Replace(attributeList, newAttributeList));

        return document.WithSyntaxRoot(root.ReplaceNode(typeDeclaration, newTypeDeclaration));
    }
}
