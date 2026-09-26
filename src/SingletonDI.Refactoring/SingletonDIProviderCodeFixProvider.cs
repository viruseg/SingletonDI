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
/// Provides code fixes for Provider-related diagnostics:
/// - DM0004: Adds a public parameterless constructor
/// - DM0005: Makes InitializeAsync method public
/// - DM0012: Removes static modifier from InitializeAsync method
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SingletonDIProviderCodeFixProvider))]
[Shared]
public class SingletonDIProviderCodeFixProvider : CodeFixProvider
{
    /// <summary>
    /// Diagnostic IDs that this provider can fix.
    /// </summary>
    private const string DM0004 = "DM0004";
    private const string DM0005 = "DM0005";
    private const string DM0012 = "DM0012";

    private const string DM0004Title = "Add public parameterless constructor";
    private const string DM0005Title = "Make method public";
    private const string DM0012Title = "Remove static modifier";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DM0004, DM0005, DM0012);

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
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            switch (diagnostic.Id)
            {
                case DM0004:
                    {
                        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                        if (typeDeclaration is null)
                            continue;

                        var action = CodeAction.Create(
                            DM0004Title,
                            ct => AddParameterlessConstructorAsync(context.Document, typeDeclaration, ct),
                            DM0004Title);

                        context.RegisterCodeFix(action, diagnostic);
                    }
                    break;

                case DM0005:
                    {
                        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                        if (methodDeclaration is null)
                            continue;

                        var action = CodeAction.Create(
                            DM0005Title,
                            ct => MakeMethodPublicAsync(context.Document, methodDeclaration, ct),
                            DM0005Title);

                        context.RegisterCodeFix(action, diagnostic);
                    }
                    break;

                case DM0012:
                    {
                        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                        if (methodDeclaration is null)
                            continue;

                        var action = CodeAction.Create(
                            DM0012Title,
                            ct => RemoveStaticModifierAsync(context.Document, methodDeclaration, ct),
                            DM0012Title);

                        context.RegisterCodeFix(action, diagnostic);
                    }
                    break;
            }
        }
    }

    private static async Task<Document> AddParameterlessConstructorAsync(
        Document document,
        TypeDeclarationSyntax typeDeclaration,
        CancellationToken cancellationToken)
    {
        // A static constructor has no parameters either, but it is not the constructor DM0004 is
        // about and it may not carry an access modifier, so it is excluded by keyword rather than
        // by parameter count.
        var existingParameterlessCtor = typeDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c =>
                c.ParameterList.Parameters.Count == 0 &&
                !c.Modifiers.Any(SyntaxKind.StaticKeyword));

        if (existingParameterlessCtor is not null)
        {
            // Change the existing constructor's accessibility to public
            // Preserve the leading trivia (comments, XML docs) from the original constructor
            var leadingTrivia = existingParameterlessCtor.GetLeadingTrivia();

            var newModifiers = AnnotateModifiers(MakePublicModifiers(existingParameterlessCtor.Modifiers));

            var newConstructor = existingParameterlessCtor
                .WithModifiers(newModifiers)
                .WithLeadingTrivia(leadingTrivia);

            var newMembers = typeDeclaration.Members.Replace(existingParameterlessCtor, newConstructor);
            var newTypeDeclaration = typeDeclaration.WithMembers(newMembers);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
                return document;

            var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }
        else
        {
            // No parameterless constructor exists, create a new public one
            var constructor = SyntaxFactory.ConstructorDeclaration(typeDeclaration.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithBody(SyntaxFactory.Block())
                .WithAdditionalAnnotations(Formatter.Annotation);

            // Insert the constructor at the beginning of the members
            var newMembers = typeDeclaration.Members.Insert(0, constructor);

            var newTypeDeclaration = typeDeclaration.WithMembers(newMembers);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
                return document;

            var newRoot = root.ReplaceNode(typeDeclaration, newTypeDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }
    }

    private static SyntaxTokenList MakePublicModifiers(SyntaxTokenList existingModifiers)
    {
        // Remove all access modifiers and add public at the beginning
        var newModifiers = SyntaxFactory.TokenList();

        foreach (var modifier in existingModifiers)
        {
            if (!IsAccessModifier(modifier.Kind()))
            {
                newModifiers = newModifiers.Add(modifier);
            }
        }

        // Insert public at the beginning
        newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        return newModifiers;
    }

    /// <summary>
    /// Marks a rewritten modifier list for formatting.
    /// </summary>
    /// <remarks>
    /// The formatter re-lays out every token inside an annotated range. Annotating the enclosing
    /// declaration or method instead would reformat the whole type or method body, so a fix that
    /// changes one modifier produced a diff across hundreds of unrelated lines. The first modifier
    /// is the token that needs spacing, because a freshly inserted one carries no trivia.
    /// </remarks>
    private static SyntaxTokenList AnnotateModifiers(SyntaxTokenList modifiers)
    {
        if (modifiers.Count == 0)
        {
            return modifiers;
        }

        return modifiers.Replace(
            modifiers[0],
            modifiers[0].WithAdditionalAnnotations(Formatter.Annotation));
    }

    private static async Task<Document> MakeMethodPublicAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        // Preserve the leading trivia (comments, XML docs) from the original method
        var leadingTrivia = methodDeclaration.GetLeadingTrivia();

        // Get existing modifiers
        var modifiers = methodDeclaration.Modifiers;

        // Find and replace access modifier with public, or add public if no access modifier exists
        var newModifiers = SyntaxFactory.TokenList();
        var hasAccessModifier = false;

        foreach (var modifier in modifiers)
        {
            if (IsAccessModifier(modifier.Kind()))
            {
                if (!hasAccessModifier)
                {
                    // Replace first access modifier with public
                    newModifiers = newModifiers.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                    hasAccessModifier = true;
                }
                // Skip other access modifiers (e.g., protected internal -> public)
            }
            else
            {
                newModifiers = newModifiers.Add(modifier);
            }
        }

        // If no access modifier was found, add public at the beginning
        if (!hasAccessModifier)
        {
            newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        }

        var newMethodDeclaration = methodDeclaration
            .WithModifiers(AnnotateModifiers(newModifiers))
            .WithLeadingTrivia(leadingTrivia);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> RemoveStaticModifierAsync(
        Document document,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellationToken)
    {
        // Preserve the leading trivia (comments, XML docs) from the original method
        var leadingTrivia = methodDeclaration.GetLeadingTrivia();

        // Remove static modifier from the method
        var newModifiers = AnnotateModifiers(SyntaxFactory.TokenList(
            methodDeclaration.Modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword))));

        var newMethodDeclaration = methodDeclaration
            .WithModifiers(newModifiers)
            .WithLeadingTrivia(leadingTrivia);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsAccessModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword
            or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword
            or SyntaxKind.InternalKeyword;
    }
}
