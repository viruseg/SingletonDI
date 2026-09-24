using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

public sealed class GeneratorIncrementalTests
{
    [Fact]
    public void NoOpRunCachesProviderAndConsumerStages()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET8_0_OR_GREATER", "NET5_0_OR_GREATER"]);
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
            preprocessorSymbols: ["NET8_0_OR_GREATER", "NET5_0_OR_GREATER"]);
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

    private static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        CSharpParseOptions parseOptions)
    {
        return CSharpCompilation.Create(
            "GeneratorIncrementalTests",
            syntaxTrees,
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SingletonDI.Attributes.SingletonDIProvideAttribute).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
