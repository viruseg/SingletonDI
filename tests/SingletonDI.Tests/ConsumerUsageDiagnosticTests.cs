using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator;
using SingletonDI.Tests.Coverage;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Covers DM0036, the suggestion that names a dependency a consumer declares and never uses.
/// </summary>
public sealed class ConsumerUsageDiagnosticTests
{
    private const string DiagnosticId = "DM0036";

    [Fact]
    public void UnusedDependency_AmongUsedOnes_IsReportedAlone()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class ProgramPaths
                {
                    public static void Start()
                    {
                    }
                }

                [SingletonDIProvide]
                public class DetectPeopleService
                {
                    public static void Start()
                    {
                    }
                }

                [SingletonDIConsume(typeof(ProgramPaths), typeof(DetectPeopleService))]
                public static partial class CropAllImgsInPreset
                {
                    public void Test()
                    {
                        DetectPeopleService.Start();
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == DiagnosticId);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("ProgramPaths", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("CropAllImgsInPreset", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("ProgramPaths", HighlightedType(result.Source, diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void DependencyReadThroughItsGeneratedProperty_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                public partial class Repository
                {
                    public string Read()
                    {
                        return DatabaseServiceInstance.ToString() + LoggerServiceInstance.ToString();
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyReachedThroughItsOwnStaticMembers_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class ProgramPaths
                {
                    public static string Root => "root";
                }

                [SingletonDIProvide]
                public class FileSystem
                {
                    public static void Copy()
                    {
                    }
                }

                [SingletonDIConsume(typeof(ProgramPaths), typeof(FileSystem))]
                public static partial class Installer
                {
                    public static void Run()
                    {
                        FileSystem.Copy();
                        System.Console.WriteLine(ProgramPaths.Root);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyOnlyNamedInATypePosition_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                public partial class Repository
                {
                    public bool IsDatabase(object value) => value is DatabaseService;

                    public Type Describe() => typeof(LoggerService);

                    public string Name => nameof(DatabaseService);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyUsedOnlyByANestedType_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                public partial class Repository
                {
                    public void Read()
                    {
                        System.Console.WriteLine(DatabaseServiceInstance);
                    }

                    private class Inner
                    {
                        public void Write()
                        {
                            System.Console.WriteLine(LoggerServiceInstance);
                        }
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyUsedOnlyByADerivedClass_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                public partial class BaseRepository
                {
                    public void Read()
                    {
                        System.Console.WriteLine(LoggerServiceInstance);
                    }
                }

                public partial class SpecializedRepository : BaseRepository
                {
                    public void Write()
                    {
                        System.Console.WriteLine(DatabaseServiceInstance);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyUsedOnlyInAnotherFileOfTheSamePartialType_IsNotReported()
    {
        var result = RunGenerator(
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                public partial class Repository
                {
                    public void Read()
                    {
                        System.Console.WriteLine(DatabaseServiceInstance);
                    }
                }
            }
            """,
            """
            namespace App
            {
                public partial class Repository
                {
                    public void Write()
                    {
                        System.Console.WriteLine(LoggerServiceInstance);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void UnusedContractDependency_IsReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IDatabaseService
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
                public class DatabaseService : IDatabaseService
                {
                }

                [SingletonDIConsume(typeof(IDatabaseService))]
                public partial class Repository
                {
                    public void Write()
                    {
                        System.Console.WriteLine("nothing injected");
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == DiagnosticId);
        Assert.Contains("IDatabaseService", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ContractDependencyReadThroughItsGeneratedProperty_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IDatabaseService
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
                public class DatabaseService : IDatabaseService
                {
                }

                [SingletonDIConsume(typeof(IDatabaseService))]
                public partial class Repository
                {
                    public void Write()
                    {
                        System.Console.WriteLine(IDatabaseServiceInstance);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DependencyReachedThroughAnAliasOfItsOwnType_IsNotReported()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;
            using Paths = App.ProgramPaths;

            namespace App
            {
                [SingletonDIProvide]
                public class ProgramPaths
                {
                    public static string Root => "root";
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(ProgramPaths), typeof(LoggerService))]
                public static partial class Installer
                {
                    public static void Run()
                    {
                        System.Console.WriteLine(Paths.Root);
                        System.Console.WriteLine(LoggerServiceInstance);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void EveryUnusedDependency_IsReportedOnItsOwnTypeName()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIProvide]
                public class MetricsService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService), typeof(LoggerService))]
                [SingletonDIConsume(typeof(MetricsService))]
                public partial class Repository
                {
                    public void Write()
                    {
                        System.Console.WriteLine(DatabaseServiceInstance);
                    }
                }
            }
            """);

        var reported = result.Diagnostics
            .Where(item => item.Id == DiagnosticId)
            .Select(item => HighlightedType(result.Source, item.Location.SourceSpan))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["LoggerService", "MetricsService"], reported);
    }

    [Fact]
    public void CustomProviderPropertyName_IsTheNameThatCountsAsUsage()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Paths")]
                public class ProgramPaths
                {
                }

                [SingletonDIProvide]
                public class LoggerService
                {
                }

                [SingletonDIConsume(typeof(ProgramPaths), typeof(LoggerService))]
                public partial class Installer
                {
                    public void Install()
                    {
                        System.Console.WriteLine(Paths);
                        System.Console.WriteLine(LoggerServiceInstance);
                    }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void DefaultProviderNameDoesNotCountWhenTheProviderRenamesItsProperty()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Paths")]
                public class ProgramPaths
                {
                }

                [SingletonDIConsume(typeof(ProgramPaths))]
                public partial class Installer
                {
                    public void Install()
                    {
                        System.Console.WriteLine(ProgramPathsInstance);
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == DiagnosticId);
        Assert.Contains("ProgramPaths", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumerThatFailedValidation_ReportsNothing()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService))]
                public class NotPartial
                {
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    [Fact]
    public void AttributeItselfIsNotAUsage()
    {
        var result = RunGenerator("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DatabaseService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService))]
                public sealed partial class Repository
                {
                }
            }
            """);

        Assert.Single(result.Diagnostics, item => item.Id == DiagnosticId);
    }

    private static string HighlightedType(string source, TextSpan span)
    {
        return source.Substring(span.Start, span.Length);
    }

    private static UsageRunResult RunGenerator(string source, string? additionalSource = null)
    {
        if (additionalSource is null)
        {
            return new UsageRunResult(Run(source).GeneratorDiagnostics, source);
        }

        // The declaration coverage harness compiles a single source, so a consumer split across two
        // files needs its own compilation here.
        var parseOptions = DeclarationMatrixHarness.CreateParseOptions(new MatrixRunOptions
        {
            CompositionRoot = false,
            OutputKind = OutputKind.DynamicallyLinkedLibrary,
            LanguageVersion = LanguageVersion.Latest,
            NullableContextProviderEnabled = false,
        });
        var compilation = CSharpCompilation.Create(
            "UsageTestAssembly",
            [CSharpSyntaxTree.ParseText(source, parseOptions), CSharpSyntaxTree.ParseText(additionalSource, parseOptions)],
            DeclarationMatrixHarness.DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: parseOptions,
            optionsProvider: new GeneratorTestAnalyzerConfigOptionsProvider(
                new GeneratorTestOptions(false, OutputKind.DynamicallyLinkedLibrary)),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false,
                baseDirectory: null));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        return new UsageRunResult(diagnostics, source);
    }

    private static MatrixRunResult Run(string source)
    {
        return DeclarationMatrixHarness.Run(
            source,
            new MatrixRunOptions
            {
                CompositionRoot = false,
                OutputKind = OutputKind.DynamicallyLinkedLibrary,
                LanguageVersion = LanguageVersion.Latest,
                NullableContextProviderEnabled = false,
            });
    }

    private sealed record UsageRunResult(
        ImmutableArray<Diagnostic> Diagnostics,
        string Source);
}