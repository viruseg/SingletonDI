using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator;

namespace SingletonDI.Tests;

internal sealed record CodeFixApplicationResult(
    Document Document,
    CodeAction Action,
    ImmutableArray<CodeAction> RegisteredActions);

internal static class CodeFixTestHarness
{
    internal static async Task<CodeFixApplicationResult> ApplyFirstAsync(
        string source,
        string diagnosticId,
        CodeFixProvider provider,
        string expectedTitle)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = CreateReferences();
        var compilation = CSharpCompilation.Create(
            "CodeFixTestAssembly",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false,
                baseDirectory: null));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);
        var diagnostic = generatorDiagnostics.Single(item => item.Id == diagnosticId);

        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId("CodeFixTestProject");
        var documentId = DocumentId.CreateNewId(projectId, "Source.cs");
        var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "CodeFixTestProject",
                "CodeFixTestAssembly",
                LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(parseOptions);
        var solution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(documentId, "Source.cs", SourceText.From(source));
        var document = solution.GetDocument(documentId)!;
        var registeredActions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => registeredActions.Add(action),
            CancellationToken.None);

        await provider.RegisterCodeFixesAsync(context);
        if (registeredActions.Count == 0)
        {
            throw new InvalidOperationException($"No code fix was registered for {diagnosticId}.");
        }

        var action = registeredActions[0];
        if (!string.Equals(action.Title, expectedTitle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected code fix title. Expected '{expectedTitle}', got '{action.Title}'.");
        }

        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var operation = operations.Single();
        operation.Apply(workspace, CancellationToken.None);
        var changedDocument = workspace.CurrentSolution.GetDocument(documentId)!;
        return new CodeFixApplicationResult(changedDocument, action, registeredActions.ToImmutable());
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
