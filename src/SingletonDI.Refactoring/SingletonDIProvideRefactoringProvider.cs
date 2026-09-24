using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SingletonDI.Refactoring;

/// <summary>
/// Provides a code refactoring to add InitializeAsync method to classes marked with [SingletonDIProvide] attribute.
/// </summary>
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(SingletonDIProvideRefactoringProvider))]
[Shared]
public class SingletonDIProvideRefactoringProvider : CodeRefactoringProvider
{
    private const string AttributeFullName = "SingletonDI.Attributes.SingletonDIProvideAttribute";
    private const string Title = "Add InitializeAsync method";

    /// <inheritdoc />
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var node = root.FindNode(context.Span);

        // Find the class declaration
        var classDeclaration = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDeclaration is null)
            return;

        // Check that cursor is on the class name identifier
        var identifierToken = classDeclaration.Identifier;
        if (!identifierToken.Span.Contains(context.Span))
            return;

        // Check if the class has the SingletonDIProvide attribute
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken);
        if (classSymbol is null)
            return;

        var hasAttribute = classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == AttributeFullName);

        if (!hasAttribute)
            return;

        // Check if InitializeAsync method already exists
        var hasInitializeAsync = classSymbol.GetMembers("InitializeAsync")
            .OfType<IMethodSymbol>()
            .Any(m => m.Parameters.Length == 0 && IsTaskType(m.ReturnType));

        if (hasInitializeAsync)
            return;

        // Register the refactoring
        var action = CodeAction.Create(
            Title,
            ct => AddInitializeAsyncMethodAsync(context.Document, classDeclaration, ct),
            Title);

        context.RegisterRefactoring(action);
    }

    private static bool IsTaskType(ITypeSymbol type)
    {
        var originalDefinition = type.OriginalDefinition;
        return originalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
            "global::System.Threading.Tasks.Task" or
            "global::System.Threading.Tasks.ValueTask";
    }

    private static async Task<Document> AddInitializeAsyncMethodAsync(
        Document document,
        ClassDeclarationSyntax classDeclaration,
        CancellationToken cancellationToken)
    {
        var taskCompletedTask = SyntaxFactory.ParseExpression(
            "global::System.Threading.Tasks.Task.CompletedTask");

        var returnStatement = SyntaxFactory.ReturnStatement(taskCompletedTask);

        var body = SyntaxFactory.Block(returnStatement);

        var method = SyntaxFactory.MethodDeclaration(
                                      SyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task"),
                                      "InitializeAsync")
                                  .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                                  .WithBody(body)
                                  // ВАЖНО: Добавляем аннотацию форматирования
                                  .WithAdditionalAnnotations(Formatter.Annotation);

        var newClassDeclaration = classDeclaration.AddMembers(method);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var newRoot = root.ReplaceNode(classDeclaration, newClassDeclaration);
        return document.WithSyntaxRoot(newRoot);
    }
}
