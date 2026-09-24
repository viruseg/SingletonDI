using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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
