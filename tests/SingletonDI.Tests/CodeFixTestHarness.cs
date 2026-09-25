using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator;
using Xunit;

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
        var workspace = await CreateWorkspaceAsync(source, diagnosticId);
        var document = workspace.Document;
        var diagnostic = Assert.Single(workspace.Diagnostics);
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
        operation.Apply(workspace.Workspace, CancellationToken.None);
        var changedDocument = workspace.Workspace.CurrentSolution.GetDocument(document.Id)!;
        return new CodeFixApplicationResult(changedDocument, action, registeredActions.ToImmutable());
    }

    /// <summary>
    /// Applies the first registered code fix and then runs the workspace formatter over the
    /// annotated ranges, the same way the IDE does when the fix is committed.
    /// </summary>
    /// <remarks>
    /// Use this instead of <see cref="ApplyFirstAsync"/> when the assertion depends on
    /// comments, documentation or formatting: <c>NormalizeWhitespace</c>-style comparison
    /// discards all of them and passes even when a fix deleted the documentation.
    /// </remarks>
    internal static async Task<Document> ApplyFirstAndFormatAsync(
        string source,
        string diagnosticId,
        CodeFixProvider provider,
        string expectedTitle)
    {
        var result = await ApplyFirstAsync(source, diagnosticId, provider, expectedTitle);
        var formatted = await Formatter.FormatAsync(result.Document, Formatter.Annotation)
            .ConfigureAwait(false);
        return formatted ?? throw new InvalidOperationException(
            "Formatting the fixed document produced no result.");
    }

    /// <summary>
    /// Produces the text changes that a fix-all run would have to merge for every
    /// diagnostic of <paramref name="diagnosticId"/> in the document.
    /// </summary>
    /// <remarks>
    /// The batch fixer itself is internal Roslyn API and cannot be executed from a test.
    /// Non-overlapping changes are the precondition it needs to apply all fixes in one
    /// pass, so this is the property the tests can assert.
    /// </remarks>
    internal static async Task<ImmutableArray<TextChange>> GetFixAllTextChangesAsync(
        string source,
        string diagnosticId,
        CodeFixProvider provider)
    {
        var workspace = await CreateWorkspaceAsync(source, diagnosticId);
        var document = workspace.Document;
        var registered = new List<(CodeAction Action, Diagnostic Diagnostic)>();

        foreach (var diagnostic in workspace.Diagnostics)
        {
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, fixedDiagnostics) => registered.Add((action, fixedDiagnostics.Single())),
                CancellationToken.None);
            await provider.RegisterCodeFixesAsync(context);
        }

        var changes = ImmutableArray.CreateBuilder<TextChange>();
        foreach (var (action, _) in registered)
        {
            var operations = await action.GetOperationsAsync(CancellationToken.None);
            var applyChanges = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
            if (applyChanges is null)
            {
                throw new InvalidOperationException(
                    $"Code fix '{action.Title}' did not produce a single document change.");
            }

            var changedDocument = applyChanges.ChangedSolution.GetDocument(document.Id);
            if (changedDocument is null)
            {
                throw new InvalidOperationException(
                    $"Code fix '{action.Title}' did not change the source document.");
            }

            changes.AddRange(await changedDocument.GetTextChangesAsync(document));
        }

        Assert.NotEmpty(changes);
        return changes.ToImmutable();
    }

    private static async Task<CodeFixWorkspace> CreateWorkspaceAsync(
        string source,
        string diagnosticId)
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
        var diagnostics = generatorDiagnostics
            .Where(item => item.Id == diagnosticId)
            .ToImmutableArray();

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
        await document.GetSyntaxRootAsync();
        return new CodeFixWorkspace(workspace, document, diagnostics);
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

    private sealed record CodeFixWorkspace(
        AdhocWorkspace Workspace,
        Document Document,
        ImmutableArray<Diagnostic> Diagnostics);
}
