using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace SingletonDI.Tests;

internal sealed record RefactoringTestContext(
    AdhocWorkspace Workspace,
    Document Document);

internal static class RefactoringTestHarness
{
    internal static async Task<RefactoringTestContext> CreateAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = CreateReferences();
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId("RefactoringTestProject");
        var documentId = DocumentId.CreateNewId(projectId, "Source.cs");
        var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "RefactoringTestProject",
                "RefactoringTestAssembly",
                LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(parseOptions);
        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(documentId, "Source.cs", SourceText.From(source));
        var document = solution.GetDocument(documentId)!;
        await document.GetSyntaxRootAsync();
        return new RefactoringTestContext(workspace, document);
    }

    internal static async Task<ImmutableArray<CodeAction>> GetActionsAsync(
        RefactoringTestContext context,
        TextSpan span,
        CodeRefactoringProvider provider,
        CancellationToken cancellationToken = default)
    {
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var codeRefactoringContext = new CodeRefactoringContext(
            context.Document,
            span,
            action => actions.Add(action),
            cancellationToken);
        await provider.ComputeRefactoringsAsync(codeRefactoringContext);
        return actions.ToImmutable();
    }

    internal static async Task<Document> ApplyAsync(
        RefactoringTestContext context,
        CodeAction action,
        CancellationToken cancellationToken = default)
    {
        var operations = await action.GetOperationsAsync(cancellationToken);
        if (operations.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one code action operation, got {operations.Length}.");
        }

        operations[0].Apply(context.Workspace, cancellationToken);
        return context.Workspace.CurrentSolution.GetDocument(context.Document.Id)!;
    }

    /// <summary>
    /// Applies the only refactoring offered at <paramref name="span"/> and then runs the workspace
    /// formatter over the annotated ranges, the same way the IDE does when the refactoring is
    /// committed.
    /// </summary>
    /// <remarks>
    /// The formatting step is not optional: a refactoring that marks an enclosing node for
    /// formatting changes nothing until the IDE reformats that node, so skipping it would hide the
    /// very difference the assertion is meant to catch.
    /// </remarks>
    internal static async Task<Document> ApplyOnlyAndFormatAsync(
        string source,
        TextSpan span,
        CodeRefactoringProvider provider,
        string expectedTitle)
    {
        var context = await CreateAsync(source);
        var action = Assert.Single(await GetActionsAsync(context, span, provider));
        Assert.Equal(expectedTitle, action.Title);
        var changedDocument = await ApplyAsync(context, action);
        var formatted = await Formatter.FormatAsync(changedDocument, Formatter.Annotation);
        return formatted ?? throw new InvalidOperationException(
            "Formatting the refactored document produced no result.");
    }

    private static IReadOnlyList<MetadataReference> CreateReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDI.Attributes.SingletonDIProvideAttribute).Assembly.Location)
        };
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[]
                 {
                     "System.Runtime",
                     "System.Runtime.CompilerServices",
                     "System.Threading.Tasks",
                     "System.Collections",
                     "System.Linq",
                     "netstandard"
                 })
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return references;
    }
}
