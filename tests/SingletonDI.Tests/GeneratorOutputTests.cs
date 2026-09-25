using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator;
using SingletonDI.Generator.Emitters;
using SingletonDI.Generator.Models;
using Xunit;

namespace SingletonDI.Tests;

public sealed class GeneratorOutputTests
{
    [Fact]
    public void Generator_EmitsProviderModuleAndRuntimeConsumerProperties()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIConsume(typeof(FirstService), typeof(SecondService))]
                public partial class Consumer
                {
                }
            }
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generated = GetGeneratedSource(runResult);
        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("SingletonDIProviderModuleAttribute", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.Contains("public static class __SingletonDIProviderModule__", generated);
        Assert.Contains("public static void Bootstrap()", generated);
        Assert.Contains("Interlocked.Exchange", generated);
        Assert.Contains("Provides generated provider registrations", generated);
        Assert.Contains("Registers generated providers exactly once", generated);
        Assert.DoesNotContain("RunClassConstructor", generated);
        Assert.Contains("RegisterProvider<global::App.FirstService, global::App.FirstService>", generated);
        Assert.Contains("global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.FirstService>()", generated);
        Assert.DoesNotContain("__SingletonDIContainer__", generated);
        Assert.DoesNotContain("class SingletonDIInitializer", generated);
    }

    [Fact]
    public void Generator_ReleasesBootstrapGuardWhenRegistrationFails()
    {
        // The guard used to be claimed before any registration ran, so a failure left it claimed
        // for good: every later Bootstrap() silently returned and initialization then built a graph
        // over a half-registered set, failing with a misleading missing-dependency error.
        var compilation = CreateCompilation(
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class FirstService
                {
                }

                [SingletonDIProvide]
                public sealed class SecondService
                {
                }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var generated = GetGeneratedSource(driver.GetRunResult());

        const string claim = "if (global::System.Threading.Interlocked.Exchange(ref _bootstrapState, 1) != 0)";
        const string release = "global::System.Threading.Interlocked.Exchange(ref _bootstrapState, 0);";
        var claimIndex = generated.IndexOf(claim, StringComparison.Ordinal);
        var releaseIndex = generated.IndexOf(release, StringComparison.Ordinal);
        var firstRegistration = generated.IndexOf("__SingletonDIHost__.RegisterProvider", StringComparison.Ordinal);
        var rethrowIndex = generated.IndexOf("throw;", StringComparison.Ordinal);

        Assert.True(claimIndex >= 0, "The bootstrap guard is missing.");
        Assert.True(releaseIndex > claimIndex, "The guard is never released.");
        Assert.True(
            firstRegistration > claimIndex && firstRegistration < releaseIndex,
            "Registrations must run inside the guarded region.");
        Assert.True(rethrowIndex > releaseIndex, "The original error must be rethrown after releasing.");
    }

    [Fact]
    public void Generator_EmitsFileScopedConsumerNamespace()
    {
        var compilation = CreateCompilation(
            """
            using SingletonDI.Attributes;

            namespace App;

            [SingletonDIProvide]
            public sealed class Service { }

            [SingletonDIConsume(typeof(Service))]
            public partial record struct Consumer
            {
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var generated = GetGeneratedSource(driver.GetRunResult());

        Assert.Contains("namespace App;", generated);
        Assert.Contains("partial record struct Consumer", generated);
    }

    [Fact]
    public void Generator_EmitsSameNullableContextForBothConsumerShapes()
    {
        // The file-scoped shape used to skip the directive, so identical consumer source produced a
        // different nullable context per shape and any annotation the emitter adds later would fail
        // with CS8632 on one of them.
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());
        var generated = new List<string>();

        foreach (var source in new[]
                 {
                     """
                     using SingletonDI.Attributes;

                     namespace App;

                     [SingletonDIProvide]
                     public sealed class Service { }

                     [SingletonDIConsume(typeof(Service))]
                     public partial class FileScopedConsumer
                     {
                     }
                     """,
                     """
                     using SingletonDI.Attributes;

                     namespace App
                     {
                         [SingletonDIProvide]
                         public sealed class Service { }

                         [SingletonDIConsume(typeof(Service))]
                         public partial class BlockScopedConsumer
                         {
                         }
                     }
                     """
                 })
        {
            driver = driver.RunGenerators(CreateCompilation(source));
            generated.Add(GetGeneratedSource(driver.GetRunResult()));
        }

        Assert.All(generated, source => Assert.Contains("#nullable enable", source));
        Assert.Equal(
            generated[0].Split('\n').Count(line => line.Trim() == "#nullable enable"),
            generated[1].Split('\n').Count(line => line.Trim() == "#nullable enable"));
    }

    [Fact]
    public void Generator_PreservesGenericConsumerConstraintSymbols()
    {
        var compilation = CreateCompilation(
            """
            using System;
            using System.Collections.Generic;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T>
                    where T : Exception, IEnumerable<T>
                {
                }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);
        var generated = GetGeneratedSource(driver.GetRunResult());

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("partial class Consumer<T>", generated);
        Assert.Contains("where T : global::System.Exception,global::System.Collections.Generic.IEnumerable<T>", generated);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.Service>()",
            generated);
    }

    [Fact]
    public void Generator_OmitsConsumerPropertyWhenMemberAlreadyExists()
    {
        var compilation = CreateCompilation(
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                    public int ServiceInstance => 0;
                }
            }
            """);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var outputCompilation = compilation.AddSyntaxTrees(GetGeneratedSyntaxTrees(runResult));
        var generated = GetGeneratedSource(runResult);

        Assert.Contains(runResult.Diagnostics, diagnostic => diagnostic.Id == "DM0025");
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Id == "CS0102");
        Assert.DoesNotContain(generated, "Resolve<global::App.Service>()", StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ReportsUnsupportedCSharpVersionBeforeAddingSource()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp7_3,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var compilation = CreateCompilation(
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diagnostic = Assert.Single(runResult.Diagnostics, item => item.Id == "DM0027");
        Assert.Equal("Generated code requires C# 9 or newer", diagnostic.Descriptor.Title);
        Assert.Contains("CSharp7_3", diagnostic.GetMessage());
        Assert.Empty(runResult.Results.SelectMany(result => result.GeneratedSources));
    }

    [Fact]
    public void Generator_ReportsFileScopedConsumerRequiresCSharp10()
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp9,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        var compilation = CreateCompilation(
            """
            using SingletonDI.Attributes;

            namespace App;

            [SingletonDIProvide]
            public sealed class Service
            {
            }

            [SingletonDIConsume(typeof(Service))]
            public partial class Consumer
            {
            }
            """,
            parseOptions);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var diagnostic = Assert.Single(runResult.Diagnostics, item => item.Id == "DM0028");
        Assert.Equal("File-scoped consumers require C# 10", diagnostic.Descriptor.Title);
        Assert.Contains("CSharp9", diagnostic.GetMessage());
        Assert.Empty(runResult.Results.SelectMany(result => result.GeneratedSources));
    }

    [Fact]
    public void Generator_GeneratedSourcesCompileAndExposeTypedRegistration()
    {
        const string source = """
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
                }
            }
            """;
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var outputCompilation = compilation.AddSyntaxTrees(GetGeneratedSyntaxTrees(runResult));

        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var registration = outputCompilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Single(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                                   memberAccess.Name.Identifier.Text == "RegisterProvider");
        var registrationMember = Assert.IsType<MemberAccessExpressionSyntax>(registration.Expression);
        var registrationName = Assert.IsType<GenericNameSyntax>(registrationMember.Name);
        Assert.Equal(
            "global::App.Service",
            registrationName.TypeArgumentList.Arguments[0].ToString());
        Assert.Equal(
            "global::App.Service",
            registrationName.TypeArgumentList.Arguments[1].ToString());

        var resolution = outputCompilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Single(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                                   memberAccess.Name.Identifier.Text == "Resolve");
        var resolutionMember = Assert.IsType<MemberAccessExpressionSyntax>(resolution.Expression);
        var resolutionName = Assert.IsType<GenericNameSyntax>(resolutionMember.Name);
        Assert.Equal(
            "global::App.Service",
            resolutionName.TypeArgumentList.Arguments[0].ToString());
    }

    [Fact]
    public void Generator_EmitsContractRegistrationAndConsumerResolution()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IService
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IService))]
                public class Service : IService
                {
                    public System.Threading.Tasks.ValueTask InitializeAsync() => System.Threading.Tasks.ValueTask.CompletedTask;
                }

                [SingletonDIConsume(typeof(IService))]
                public partial class Consumer
                {
                }
            }
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generated = GetGeneratedSource(runResult);
        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "RegisterProvider<global::App.IService, global::App.Service>",
            generated);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.IService>()",
            generated);
        Assert.Contains(".AsTask()", generated);
    }

    [Fact]
    public void Generator_EmitsCompilableValueTaskAndAsyncDisposalDelegates()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service : System.IDisposable, System.IAsyncDisposable
                {
                    public System.Threading.Tasks.ValueTask InitializeAsync() => System.Threading.Tasks.ValueTask.CompletedTask;

                    public void Dispose()
                    {
                    }

                    public System.Threading.Tasks.ValueTask DisposeAsync() => System.Threading.Tasks.ValueTask.CompletedTask;
                }
            }
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generatedTrees = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => CSharpSyntaxTree.ParseText(
                generated.SourceText.ToString(),
                new CSharpParseOptions(
                    LanguageVersion.Latest,
                    preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"])));
        var outputCompilation = compilation.AddSyntaxTrees(generatedTrees);
        var generated = GetGeneratedSource(runResult);

        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Service, global::App.Service>",
            generated);
        Assert.Contains("static value => ToTask(value.InitializeAsync()),", generated);
        Assert.Contains(
            "static value => ((global::System.IAsyncDisposable)value).DisposeAsync().AsTask());",
            generated);
        Assert.DoesNotContain("((global::System.IDisposable)value).Dispose()", generated);
    }

    [Fact]
    public void Generator_DoesNotEmitProviderModuleForConsumerOnlyLibrary()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IService
                {
                }

                [SingletonDIConsume(typeof(IService))]
                public partial class Consumer
                {
                }
            }
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());

        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generated = GetGeneratedSource(runResult);
        Assert.DoesNotContain(
            runResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("ModuleInitializer", generated);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.IService>()",
            generated);
    }

    [Fact]
    public void Generator_ReportsCollisionBetweenProviderCustomNameAndContractDefault()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IService
                {
                }

                [SingletonDIProvide("IServiceInstance")]
                public class Service : IService
                {
                }

                [SingletonDIConsume(typeof(Service), typeof(IService))]
                public partial class Consumer
                {
                }
            }
            """;

        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generated = GetGeneratedSource(runResult);

        var conflict = Assert.Single(
            runResult.Diagnostics,
            diagnostic => diagnostic.Id == "DM0003");
        Assert.Equal("Consumer property name conflict", conflict.Descriptor.Title);
        Assert.Contains("same consumer dependency set", conflict.GetMessage());
        Assert.Contains(
            "protected static global::App.Service IServiceInstance",
            generated);
        Assert.Contains(
            "protected static global::App.IService App_IServiceInstance",
            generated);
    }

    [Fact]
    public void Generator_ReusesProviderModuleOutputWhenOnlyConsumerChanges()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }
            """;
        const string firstConsumerSource = """
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
        const string secondConsumerSource = """
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

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        var providerTree = CSharpSyntaxTree.ParseText(providerSource, parseOptions);
        var firstCompilation = CreateCompilation(
            [providerTree, CSharpSyntaxTree.ParseText(firstConsumerSource, parseOptions)],
            parseOptions);
        var secondCompilation = CreateCompilation(
            [providerTree, CSharpSyntaxTree.ParseText(secondConsumerSource, parseOptions)],
            parseOptions);
        driver = driver.RunGenerators(firstCompilation);
        driver = driver.RunGenerators(secondCompilation);
        var runResult = driver.GetRunResult();
        var providerSteps = runResult.Results
            .SelectMany(result => result.TrackedSteps)
            .Concat(runResult.Results.SelectMany(result => result.TrackedOutputSteps))
            .Where(pair => pair.Key == "ProviderModuleOutput")
            .SelectMany(pair => pair.Value)
            .ToList();

        var providerStep = Assert.Single(providerSteps);
        Assert.Contains(
            providerStep.Outputs,
            output => output.Reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void ProviderModuleEmitter_EmitsDependenciesAndPrefersAsyncDisposal()
    {
        var provider = new ProviderModel(
            "global::App.Service",
            "Service",
            "App",
            "App, Version=1.0.0.0",
            true,
            true,
            true,
            ["global::Contracts.IDependency"],
            null,
            null,
            null,
            null,
            Location.None,
            null);

        var generated = ProviderModuleEmitter.Generate(
            [provider],
            ImmutableArray<ProviderAssemblyModel>.Empty,
            isCompositionRoot: false);

        Assert.Contains("#if !NET5_0_OR_GREATER", generated);
        Assert.Contains("new global::System.Type[]", generated);
        Assert.Contains("typeof(global::Contracts.IDependency)", generated);
        Assert.Contains("ToTask(value.InitializeAsync())", generated);
        Assert.Contains(
            "((global::System.IAsyncDisposable)value).DisposeAsync().AsTask()",
            generated);
        Assert.DoesNotContain(
            "((global::System.IDisposable)value).Dispose()",
            generated);
    }

    [Fact]
    public void ConsumerEmitter_UsesSafeUniqueHintNames()
    {
        var first = new ConsumerModel(
            "global::App.Consumer",
            "Consumer",
            "App",
            true,
            ImmutableArray<ServiceReferenceModel>.Empty);
        var second = new ConsumerModel(
            "global::App.Consumer",
            "Consumer",
            "App",
            true,
            ImmutableArray<ServiceReferenceModel>.Empty);

        var generated = ConsumerEmitter.Generate([first, second]);

        Assert.Equal(2, generated.Count);
        Assert.Contains(generated.Keys, key => key.Contains('_'));
        Assert.All(generated.Keys, key =>
        {
            var stem = key[..^5];
            Assert.All(stem, character =>
                Assert.True(
                    char.IsLetterOrDigit(character) || character == '_',
                    $"Invalid hint-name character '{character}' in '{key}'."));
        });
    }

    [Fact]
    public void ProviderModuleEmitter_DeduplicatesDependencyIdentities()
    {
        var dependencyIdentity = new ServiceTypeIdentity(
            "global::Contracts.IDependency",
            "Contracts, Version=1.0.0.0");
        var provider = new ProviderModel(
            "global::App.Provider",
            "Provider",
            "App",
            "App",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            null,
            Location.None,
            null,
            [dependencyIdentity, dependencyIdentity]);

        var generated = ProviderModuleEmitter.Generate(
            [provider],
            ImmutableArray<ProviderAssemblyModel>.Empty,
            isCompositionRoot: false);

        Assert.Equal(
            1,
            generated.Split("typeof(global::Contracts.IDependency)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ProviderModuleEmitter_BootstrapsEachMarkedExternalAssemblyOnce()
    {
        var localProvider = new ProviderModel(
            "global::App.LocalService",
            "LocalService",
            "App",
            "App, Version=1.0.0.0",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            null,
            Location.None,
            null);
        var externalProvider = new ProviderAssemblyModel(
            "External, Version=1.0.0.0",
            true,
            new ServiceTypeIdentity(
                "global::SingletonDI.Generated.__SingletonDIProviderModule__",
                "External, Version=1.0.0.0"),
            true);
        var duplicateExternalProvider = new ProviderAssemblyModel(
            "External, Version=1.0.0.0",
            true,
            new ServiceTypeIdentity(
                "global::SingletonDI.Generated.__SingletonDIProviderModule__",
                "External, Version=1.0.0.0"),
            true);
        var unmarkedProvider = new ProviderAssemblyModel(
            "Unmarked, Version=1.0.0.0",
            false);

        var generated = ProviderModuleEmitter.Generate(
            [localProvider],
            [externalProvider, duplicateExternalProvider, unmarkedProvider],
            isCompositionRoot: true);

        Assert.Equal(
            1,
            generated.Split(
                "global::SingletonDI.Generated.__SingletonDIProviderModule__",
                StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            generated);
        Assert.DoesNotContain("RunClassConstructor", generated);
        Assert.DoesNotContain("global::App.LocalService).TypeHandle", generated);
        Assert.DoesNotContain("global::Unmarked.UnmarkedService", generated);
    }

    [Fact]
    public void ProviderModuleEmitter_ReturnsEmptyForNoProviders()
    {
        var generated = ProviderModuleEmitter.Generate(
            ImmutableArray<ProviderModel>.Empty,
            ImmutableArray<ProviderAssemblyModel>.Empty,
            isCompositionRoot: false);

        Assert.Equal(string.Empty, generated);
    }

    [Fact]
    public void ProviderModuleEmitter_SortsLocalProvidersDeterministically()
    {
        var secondProvider = new ProviderModel(
            "global::App.SecondService",
            "SecondService",
            "App",
            "App",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            null,
            Location.None,
            null);
        var firstProvider = new ProviderModel(
            "global::App.FirstService",
            "FirstService",
            "App",
            "App",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            null,
            Location.None,
            null);

        var generated = ProviderModuleEmitter.Generate(
            [secondProvider, firstProvider],
            ImmutableArray<ProviderAssemblyModel>.Empty,
            isCompositionRoot: false);

        var firstIndex = generated.IndexOf(
            "RegisterProvider<global::App.FirstService, global::App.FirstService>",
            StringComparison.Ordinal);
        var secondIndex = generated.IndexOf(
            "RegisterProvider<global::App.SecondService, global::App.SecondService>",
            StringComparison.Ordinal);
        Assert.True(firstIndex >= 0);
        Assert.True(secondIndex > firstIndex);
    }

    [Fact]
    public void ProviderModuleEmitter_DoesNotEmitExternalBootstrapOutsideCompositionRoot()
    {
        var localProvider = new ProviderModel(
            "global::App.LocalService",
            "LocalService",
            "App",
            "App, Version=1.0.0.0",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            null,
            Location.None,
            null);
        var externalProvider = new ProviderAssemblyModel(
            "External, Version=1.0.0.0",
            true,
            new ServiceTypeIdentity(
                "global::SingletonDI.Generated.__SingletonDIProviderModule__",
                "External, Version=1.0.0.0"),
            true);

        var generated = ProviderModuleEmitter.Generate(
            [localProvider],
            [externalProvider],
            isCompositionRoot: false);

        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            generated);
        Assert.DoesNotContain("RunClassConstructor", generated);
        Assert.Contains("RegisterProvider<global::App.LocalService, global::App.LocalService>", generated);
    }

    private static IReadOnlyList<SyntaxTree> GetGeneratedSyntaxTrees(
        GeneratorDriverRunResult runResult)
    {
        return runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generated => CSharpSyntaxTree.ParseText(
                generated.SourceText.ToString(),
                CreateParseOptions()))
            .ToArray();
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult runResult)
    {
        return string.Join(
            Environment.NewLine,
            runResult.Results
                .SelectMany(result => result.GeneratedSources)
                .Select(generated => generated.SourceText.ToString()));
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        CSharpParseOptions? parseOptions = null)
    {
        parseOptions ??= CreateParseOptions();
        return CreateCompilation(
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            parseOptions);
    }

    private static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        CSharpParseOptions parseOptions)
    {
        return CSharpCompilation.Create(
            "GeneratorOutputTests",
            syntaxTrees,
            CreateReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpParseOptions CreateParseOptions()
    {
        return new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);
    }

    private static List<MetadataReference> CreateReferences()
    {
        var runtimeAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IDisposable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attributes.SingletonDIProviderModuleAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyPath, "System.Runtime.dll")),
        ];
    }
}
