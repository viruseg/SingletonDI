using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Verifies generation and compilation of the legacy dependency-order fixtures.
/// Runtime lifecycle execution belongs to the Task 5 RootApp fixture.
/// </summary>
public class SingletonDisposeOrderTests
{
    private const string ThreeThreadsSource =
        """
        using System;
        using System.Collections.Generic;
        using SingletonDI.Attributes;

        namespace TestApp
        {
            [SingletonDIProvide]
            public class DatabaseConfigA : IDisposable
            {
                public DatabaseConfigA() => ActionLog.Add("Register Level 0 DatabaseConfigA");
                public void Dispose() => ActionLog.Add("Dispose Level 0 DatabaseConfigA");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(DatabaseConfigA))]
            public partial class DatabaseConnectionA : IDisposable
            {
                public DatabaseConnectionA() => ActionLog.Add("Register Level 1 DatabaseConnectionA");
                public void Dispose() => ActionLog.Add("Dispose Level 1 DatabaseConnectionA");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(DatabaseConnectionA))]
            public partial class RepositoryA : IDisposable
            {
                public RepositoryA() => ActionLog.Add("Register Level 2 RepositoryA");
                public void Dispose() => ActionLog.Add("Dispose Level 2 RepositoryA");
            }

            [SingletonDIProvide]
            public class CacheConfigB : IDisposable
            {
                public CacheConfigB() => ActionLog.Add("Register Level 0 CacheConfigB");
                public void Dispose() => ActionLog.Add("Dispose Level 0 CacheConfigB");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(CacheConfigB))]
            public partial class CacheConnectionB : IDisposable
            {
                public CacheConnectionB() => ActionLog.Add("Register Level 1 CacheConnectionB");
                public void Dispose() => ActionLog.Add("Dispose Level 1 CacheConnectionB");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(CacheConnectionB))]
            public partial class CacheRepositoryB : IDisposable
            {
                public CacheRepositoryB() => ActionLog.Add("Register Level 2 CacheRepositoryB");
                public void Dispose() => ActionLog.Add("Dispose Level 2 CacheRepositoryB");
            }

            [SingletonDIProvide]
            public class QueueConfigC : IDisposable
            {
                public QueueConfigC() => ActionLog.Add("Register Level 0 QueueConfigC");
                public void Dispose() => ActionLog.Add("Dispose Level 0 QueueConfigC");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(QueueConfigC))]
            public partial class QueueConnectionC : IDisposable
            {
                public QueueConnectionC() => ActionLog.Add("Register Level 1 QueueConnectionC");
                public void Dispose() => ActionLog.Add("Dispose Level 1 QueueConnectionC");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(QueueConnectionC))]
            public partial class QueueProcessorC : IDisposable
            {
                public QueueProcessorC() => ActionLog.Add("Register Level 2 QueueProcessorC");
                public void Dispose() => ActionLog.Add("Dispose Level 2 QueueProcessorC");
            }

            public static class ActionLog
            {
                private static readonly List<string> _actions = new();

                public static void Add(string action)
                {
                    lock (_actions)
                    {
                        _actions.Add(action);
                    }
                }
            }
        }
        """;

    [Fact]
    public void DisposeOrder_ThreeIndependentThreads_GeneratesRuntimeRegistrationOutput()
    {
        var (outputCompilation, generatedSource, diagnostics) = CompileWithGenerator(ThreeThreadsSource);

        AssertNoErrors(diagnostics);
        Assert.Contains("__SingletonDIProviderModule__", generatedSource);
        Assert.DoesNotContain("__SingletonDIContainer__", generatedSource);

        foreach (var providerName in new[]
                 {
                     "DatabaseConfigA",
                     "DatabaseConnectionA",
                     "RepositoryA",
                     "CacheConfigB",
                     "CacheConnectionB",
                     "CacheRepositoryB",
                     "QueueConfigC",
                     "QueueConnectionC",
                     "QueueProcessorC"
                 })
        {
            Assert.Contains(
                $"RegisterProvider<global::TestApp.{providerName}, global::TestApp.{providerName}>",
                generatedSource);
        }

        AssertNoErrors(outputCompilation.GetDiagnostics());
    }

    [Fact]
    public void DisposeOrder_SingleThread_GeneratesRuntimeRegistrationOutput()
    {
        const string source =
            """
            using System;
            using SingletonDI.Attributes;

            namespace TestApp
            {
                [SingletonDIProvide]
                public class Config : IDisposable
                {
                    public Config() { }
                    public void Dispose() { }
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(Config))]
                public partial class Connection : IDisposable
                {
                    public Connection() { }
                    public void Dispose() { }
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(Connection))]
                public partial class Repository : IDisposable
                {
                    public Repository() { }
                    public void Dispose() { }
                }
            }
            """;

        var (outputCompilation, generatedSource, diagnostics) = CompileWithGenerator(source);

        AssertNoErrors(diagnostics);
        Assert.Contains("__SingletonDIProviderModule__", generatedSource);
        Assert.DoesNotContain("__SingletonDIContainer__", generatedSource);
        Assert.Contains(
            "RegisterProvider<global::TestApp.Config, global::TestApp.Config>",
            generatedSource);
        Assert.Contains(
            "typeof(global::TestApp.Config)",
            generatedSource);
        Assert.Contains(
            "RegisterProvider<global::TestApp.Connection, global::TestApp.Connection>",
            generatedSource);
        Assert.Contains(
            "typeof(global::TestApp.Connection)",
            generatedSource);
        AssertNoErrors(outputCompilation.GetDiagnostics());
    }

    private static (CSharpCompilation OutputCompilation, string GeneratedSource, ImmutableArray<Diagnostic> Diagnostics) CompileWithGenerator(
        string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SingletonDIGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .ToList();
        var outputCompilation = compilation.AddSyntaxTrees(
            generatedSources.Select(generated => generated.SyntaxTree));
        var generatedSource = string.Join(
            Environment.NewLine,
            generatedSources.Select(generated => generated.SourceText.ToString()));

        return (outputCompilation, generatedSource, runResult.Diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var runtimeAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IDisposable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::SingletonDI.Generated.__SingletonDIHost__).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeAssemblyPath, "System.Runtime.dll")),
        };

        return CSharpCompilation.Create(
            "SingletonDisposeOrderTests_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(
                    LanguageVersion.Latest,
                    preprocessorSymbols: ["NET8_0_OR_GREATER", "NET5_0_OR_GREATER"]))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
