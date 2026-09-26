using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;
using Xunit;

namespace SingletonDI.Tests;

public sealed class GeneratorIncrementalTests
{
    [Fact]
    public void NoOpRunCachesProviderAndConsumerStages()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                    public int Value => 1;
                }
            }
            """;
        var compilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var trackedSteps = runResult.Results
            .SelectMany(result => result.TrackedSteps)
            .Concat(runResult.Results.SelectMany(result => result.TrackedOutputSteps))
            .Where(pair => pair.Key is "ProviderModuleOutput" or "ConsumerOutput")
            .SelectMany(pair => pair.Value)
            .ToList();

        Assert.Equal(2, trackedSteps.Count);
        Assert.All(
            trackedSteps,
            step => Assert.Contains(
                step.Outputs,
                output => output.Reason == IncrementalStepRunReason.Cached));
    }

    [Fact]
    public void ConsumerBodyEditCachesConsumerOutput()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var providerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }
            """;
        var firstConsumerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                    public int Value => 1;
                }
            }
            """;
        var secondConsumerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                    public int Value => 2;
                }
            }
            """;
        var providerTree = CSharpSyntaxTree.ParseText(providerSource, parseOptions);
        var firstCompilation = CreateCompilation(
            [providerTree, CSharpSyntaxTree.ParseText(firstConsumerSource, parseOptions)],
            parseOptions);
        var secondCompilation = CreateCompilation(
            [providerTree, CSharpSyntaxTree.ParseText(secondConsumerSource, parseOptions)],
            parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);
        var runResult = driver.GetRunResult();
        var consumerSteps = runResult.Results
            .SelectMany(result => result.TrackedSteps)
            .Concat(runResult.Results.SelectMany(result => result.TrackedOutputSteps))
            .Where(pair => pair.Key == "ConsumerOutput")
            .SelectMany(pair => pair.Value)
            .ToList();

        var consumerStep = Assert.Single(consumerSteps);
        Assert.Contains(
            consumerStep.Outputs,
            output => output.Reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void ReferencedCompositionSnapshotCachesReferenceOnlyCompilation()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var firstSource = "public class First { public int Value => 1; }";
        var secondSource = "public class First { public int Value => 2; }";
        var references = CreateMetadataReferences();
        var firstCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(firstSource, parseOptions)],
            parseOptions,
            references);
        var secondCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(secondSource, parseOptions)],
            parseOptions,
            references);
        var changedOptionsCompilation = secondCompilation.WithOptions(
            secondCompilation.Options.WithOutputKind(OutputKind.ConsoleApplication));
        var changedReferencesCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(secondSource, parseOptions)],
            parseOptions,
            references.Append(MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location)));
        var collector = new CountingReferencedCompositionCollector();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SnapshotTrackingGenerator(collector).AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);
        Assert.Equal(1, collector.CallCount);

        driver = driver.RunGenerators(changedOptionsCompilation);
        Assert.Equal(2, collector.CallCount);

        driver = driver.RunGenerators(changedReferencesCompilation);
        Assert.Equal(3, collector.CallCount);
    }

    [Fact]
    public void ProviderBodyEditCachesProviderModuleOutput()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var firstSource = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
                public int Value => 1;
            }
            """;
        var secondSource = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
                public int Value => 2;
            }
            """;
        var firstCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(firstSource, parseOptions)],
            parseOptions);
        var secondCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(secondSource, parseOptions)],
            parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);
        var providerSteps = driver.GetRunResult().Results
            .SelectMany(result => result.TrackedSteps)
            .Where(pair => pair.Key == "ProviderModuleOutput")
            .SelectMany(pair => pair.Value)
            .ToList();
        var providerStep = Assert.Single(providerSteps);

        Assert.Contains(
            providerStep.Outputs,
            output => output.Reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void ConsumerDiagnosticCacheKeyIncludesSourceTreeIdentity()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var source = """
            using SingletonDI.Attributes;

            [SingletonDIConsume]
            public class Consumer
            {
            }
            """;
        var references = CreateMetadataReferences();
        var firstCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions, path: "First.cs")],
            parseOptions,
            references);
        var secondCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions, path: "Second.cs")],
            parseOptions,
            references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);
        var diagnostic = Assert.Single(
            driver.GetRunResult().Diagnostics,
            item => item.Id == "DM0007");

        Assert.Equal("Second.cs", diagnostic.Location.SourceTree?.FilePath);
    }

    [Fact]
    public void ReferencedCompositionCollector_SkipsFilteredCompilation()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText("public class LibraryType { }", parseOptions)],
            parseOptions);
        var collector = new CountingReferencedCompositionCollector();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SnapshotTrackingGenerator(collector, _ => false).AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(compilation);

        Assert.Equal(0, collector.CallCount);
    }

    [Fact]
    public void ProviderServiceTypeAssemblyChangePreservesCompositionValidation()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(Shared.IContract))]
                public sealed class Provider : Shared.IContract
                {
                }
            }
            """;
        var firstContract = CreateContractReference("Shared.Contracts.First");
        var secondContract = CreateContractReference("Shared.Contracts.Second");
        var firstConsumer = CreateConsumerReference("External.Consumer.First", firstContract);
        var secondConsumer = CreateConsumerReference("External.Consumer.Second", secondContract);
        var firstCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            parseOptions,
            CreateMetadataReferences().Append(firstContract).Append(firstConsumer));
        var secondCompilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            parseOptions,
            CreateMetadataReferences().Append(secondContract).Append(secondConsumer));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: new GeneratorTestAnalyzerConfigOptionsProvider(
                new GeneratorTestOptions(true, OutputKind.DynamicallyLinkedLibrary)),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);

        var runResult = driver.GetRunResult();
        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Id == "DM0018");
        var generated = string.Join(
            Environment.NewLine,
            runResult.Results
                .SelectMany(result => result.GeneratedSources)
                .Select(source => source.SourceText.ToString()));
        Assert.Contains("__SingletonDICompositionRootModule__", generated);
    }

    private sealed class SnapshotTrackingGenerator : IIncrementalGenerator
    {
        private readonly IReferencedCompositionCollector _collector;
        private readonly Func<Compilation, bool>? _shouldCollect;

        public SnapshotTrackingGenerator(
            IReferencedCompositionCollector collector,
            Func<Compilation, bool>? shouldCollect = null)
        {
            _collector = collector;
            _shouldCollect = shouldCollect;
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var snapshots = _shouldCollect is null
                ? ReferencedCompositionCollector.CreateProvider(
                    context.CompilationProvider,
                    _collector)
                : ReferencedCompositionCollector.CreateProvider(
                    context.CompilationProvider,
                    _collector,
                    _shouldCollect);
            context.RegisterSourceOutput(snapshots, static (_, _) => { });
        }
    }

    private sealed class CountingReferencedCompositionCollector : IReferencedCompositionCollector
    {
        public int CallCount { get; private set; }

        public ReferencedCompositionSnapshot Collect(
            Compilation compilation,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ReferencedCompositionSnapshot.Empty;
        }
    }

    private static MetadataReference CreateContractReference(string assemblyName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                "namespace Shared { public interface IContract { } }",
                parseOptions)],
            CreateMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success);
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference CreateConsumerReference(
        string assemblyName,
        MetadataReference contractReference)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                "using SingletonDI.Attributes; [SingletonDIConsume(typeof(Shared.IContract))] public class ExternalConsumer { }",
                parseOptions)],
            CreateMetadataReferences().Append(contractReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    [Fact]
    public void ConsumerDeclarationMoveInvalidatesConsumerOutput()
    {
        // The declaration location anchors the diagnostics reported from the consumer model, but
        // ConsumerCandidate compares its cache key alone, so a candidate whose key carries no
        // location compared equal after the declaration moved and the diagnostics kept pointing at
        // the previous span.
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var providerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }

                public interface IUnmapped
                {
                }
            }
            """;
        var consumerBody = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(IUnmapped))]
                public partial class Consumer
                {
                }
            }
            """;
        var providerTree = CSharpSyntaxTree.ParseText(providerSource, parseOptions);
        var firstCompilation = CreateCompilation(
            [providerTree, CSharpSyntaxTree.ParseText(consumerBody, parseOptions)],
            parseOptions,
            outputKind: OutputKind.ConsoleApplication);
        var shiftedCompilation = CreateCompilation(
            [
                providerTree,
                CSharpSyntaxTree.ParseText(
                    "// shifted" + Environment.NewLine + consumerBody,
                    parseOptions)
            ],
            parseOptions,
            outputKind: OutputKind.ConsoleApplication);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGenerators(firstCompilation);
        var firstDiagnostic = Assert.Single(
            driver.GetRunResult().Diagnostics,
            diagnostic => diagnostic.Id == "DM0017");
        driver = driver.RunGenerators(shiftedCompilation);
        var runResult = driver.GetRunResult();
        var secondDiagnostic = Assert.Single(
            runResult.Diagnostics,
            diagnostic => diagnostic.Id == "DM0017");
        var consumerSteps = runResult.Results
            .SelectMany(result => result.TrackedSteps)
            .Concat(runResult.Results.SelectMany(result => result.TrackedOutputSteps))
            .Where(pair => pair.Key == "ConsumerOutput")
            .SelectMany(pair => pair.Value)
            .ToList();

        var consumerStep = Assert.Single(consumerSteps);
        Assert.DoesNotContain(
            consumerStep.Outputs,
            output => output.Reason == IncrementalStepRunReason.Cached);
        Assert.Equal(
            firstDiagnostic.Location.SourceSpan.Start + "// shifted".Length + Environment.NewLine.Length,
            secondDiagnostic.Location.SourceSpan.Start);
    }

    private static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        CSharpParseOptions parseOptions,
        IEnumerable<MetadataReference>? references = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        return CSharpCompilation.Create(
            "GeneratorIncrementalTests",
            syntaxTrees,
            references ?? CreateMetadataReferences(),
            new CSharpCompilationOptions(outputKind));
    }

    private static MetadataReference[] CreateMetadataReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDI.Attributes.SingletonDIProvideAttribute).Assembly.Location),
        };
        var runtimePath = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll");
        if (File.Exists(runtimePath))
        {
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        }

        return references.ToArray();
    }
}
