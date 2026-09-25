using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Attributes;
using SingletonDI.Generator;

namespace SingletonDI.Tests.Coverage;

internal sealed class MatrixRunOptions
{
    internal bool CompositionRoot { get; init; }

    internal OutputKind OutputKind { get; init; } = OutputKind.DynamicallyLinkedLibrary;

    internal LanguageVersion LanguageVersion { get; init; } = LanguageVersion.Latest;

    internal bool NullableContextProviderEnabled { get; init; } = true;

    internal bool IncludeOutputTypeProperty { get; init; } = true;

    internal ImmutableArray<string> AdditionalPreprocessorSymbols { get; init; } =
        ImmutableArray<string>.Empty;
}

internal sealed record MatrixRunResult(
    ImmutableArray<string> GeneratorErrorIds,
    ImmutableArray<Diagnostic> CompilerErrors,
    string GeneratedSource,
    bool EmitSucceeded)
{
    internal ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; } =
        ImmutableArray<Diagnostic>.Empty;

    internal CSharpCompilation OutputCompilation { get; init; } = null!;
}

internal static class DeclarationMatrixHarness
{
    private static readonly string[] RuntimeAssemblyNames =
    [
        "System.Runtime",
        "System.Runtime.CompilerServices",
        "System.Threading.Tasks",
        "System.Runtime.InteropServices",
        "System.Collections",
        "System.Linq",
        "netstandard",
    ];

    private static readonly ImmutableArray<MetadataReference> DefaultReferences = BuildDefaultReferences();

    internal static MatrixRunResult Run(
        string source,
        bool compositionRoot,
        OutputKind outputKind,
        LanguageVersion languageVersion)
    {
        return Run(
            source,
            new MatrixRunOptions
            {
                CompositionRoot = compositionRoot,
                OutputKind = outputKind,
                LanguageVersion = languageVersion,
            });
    }

    internal static MatrixRunResult Run(string source, MatrixRunOptions options)
    {
        var compilation = Compile(source, options);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: CreateParseOptions(options),
            optionsProvider: new GeneratorTestAnalyzerConfigOptionsProvider(
                new GeneratorTestOptions(options.CompositionRoot, options.OutputKind, options.IncludeOutputTypeProperty)),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false,
                baseDirectory: null));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);
        var output = (CSharpCompilation)outputCompilation;

        var inputTrees = compilation.SyntaxTrees.ToImmutableArray();
        var generatedSource = string.Join(
            Environment.NewLine,
            output.SyntaxTrees
                .Where(tree => !inputTrees.Contains(tree))
                .Select(tree => tree.ToString()));

        var errors = output.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        var generatorErrorIds = errors
            .Where(diagnostic => diagnostic.Descriptor.Category == "SingletonDI")
            .Select(diagnostic => diagnostic.Id)
            .ToImmutableArray();
        var compilerErrors = errors
            .Where(diagnostic => diagnostic.Descriptor.Category != "SingletonDI")
            .ToImmutableArray();

        using var stream = new MemoryStream();
        var emitResult = output.Emit(stream);

        return new MatrixRunResult(
            generatorErrorIds,
            compilerErrors,
            generatedSource,
            emitResult.Success)
        {
            GeneratorDiagnostics = generatorDiagnostics,
            OutputCompilation = output,
        };
    }

    internal static CSharpCompilation Compile(string source, MatrixRunOptions options)
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, CreateParseOptions(options))],
            DefaultReferences,
            new CSharpCompilationOptions(
                options.OutputKind,
                nullableContextOptions: options.NullableContextProviderEnabled
                    ? NullableContextOptions.Enable
                    : NullableContextOptions.Disable));
    }

    internal static CSharpParseOptions CreateParseOptions(MatrixRunOptions options)
    {
        var symbols = ImmutableArray.CreateBuilder<string>();
        if (!options.AdditionalPreprocessorSymbols.IsDefault)
        {
            symbols.AddRange(options.AdditionalPreprocessorSymbols);
        }

        symbols.Add("NET10_0_OR_GREATER");
        symbols.Add("NET5_0_OR_GREATER");
        return new CSharpParseOptions(
            options.LanguageVersion,
            preprocessorSymbols: symbols.ToImmutable());
    }

    internal static byte[] EmitAssembly(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            throw new InvalidOperationException($"Compilation failed:{Environment.NewLine}{diagnostics}");
        }

        return stream.ToArray();
    }

    private static ImmutableArray<MetadataReference> BuildDefaultReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Task).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(RuntimeHelpers).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(RuntimeInformation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(SingletonDIProvideAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(SingletonDIProviderModuleAttribute).Assembly.Location));

        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in RuntimeAssemblyNames)
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return references.ToImmutable();
    }
}
