using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Tests.Coverage;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests that every generator diagnostic is anchored to the smallest piece of source that the
/// reader has to change, rather than to a whole type declaration or to a whole file.
/// </summary>
public class DiagnosticAnchorTests
{
    [Fact]
    public void DM0002_AnchorsToAbstractModifier()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public abstract class Repository
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0002");

        Assert.Equal("abstract", GetSpanText(diagnostic));
        AssertSpansOneLine(diagnostic);
    }

    [Fact]
    public void DM0002_AnchorsToStaticModifier()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public static class Repository
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0002");

        Assert.Equal("static", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0004_AnchorsToTheConstructorThatRejectsIt()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Repository
            {
                public Repository(int id)
                {
                }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0004");

        Assert.Equal("Repository", GetSpanText(diagnostic));
        AssertSpansOneLine(diagnostic);
    }

    [Fact]
    public void DM0004_AnchorsToTheDeclaredParameterlessConstructor()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Repository
            {
                internal Repository()
                {
                }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0004");

        Assert.Equal("Repository", GetSpanText(diagnostic));
        Assert.Equal(GetLineOf(source, "internal Repository()"), GetStartLine(diagnostic));
    }

    [Fact]
    public void DM0009_AnchorsToTheProviderTheCycleStartsFrom()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(BetaService))]
            public class AlphaService
            {
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(AlphaService))]
            public class BetaService
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0009");

        var cycleStart = diagnostic.GetMessage()
            .Split("Circular dependency detected: ", 2)[1]
            .Split(" -> ", 2)[0]
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        Assert.Equal(
            cycleStart[(cycleStart.LastIndexOf('.') + 1)..],
            GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0015_AnchorsToTypeParameterList()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Repository<T>
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0015");

        Assert.Equal("<T>", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0018_AnchorsToTheDependencyArgumentOfTheProvider()
    {
        const string source = """
            using SingletonDI.Attributes;

            public class MissingService
            {
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(MissingService))]
            public class Repository
            {
            }

            public static class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(
            RunGenerator(source, compositionRoot: true),
            "DM0018");

        Assert.Equal("MissingService", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0019_AnchorsToTheFirstConflictingProviderName()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IStorage
            {
            }

            [SingletonDIProvide(ServiceType = typeof(IStorage))]
            public class FileStorage : IStorage
            {
            }

            [SingletonDIProvide(ServiceType = typeof(IStorage))]
            public class MemoryStorage : IStorage
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0019");

        Assert.Contains(GetSpanText(diagnostic), new[] { "FileStorage", "MemoryStorage" });
        AssertSpansOneLine(diagnostic);
    }

    [Fact]
    public void DM0022_AnchorsToTheInaccessibleProviderName()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            private class Repository
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0022");

        Assert.Equal("Repository", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0028_AnchorsToFileScopedNamespaceName()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App;

            [SingletonDIConsume]
            public partial class Consumer
            {
            }
            """;

        var diagnostic = AssertSingleDiagnostic(
            RunGenerator(source, languageVersion: LanguageVersion.CSharp9),
            "DM0028");

        Assert.Equal("App", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0032_AnchorsToTheRequiredMember()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Repository
            {
                public required string ConnectionString { get; set; }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0032");

        Assert.Equal("ConnectionString", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0035_AnchorsToTheContainingGenericTypeName()
    {
        const string source = """
            using SingletonDI.Attributes;

            public partial class Outer<T>
            {
                [SingletonDIProvide]
                public class Inner
                {
                }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0035");

        Assert.Equal("Outer", GetSpanText(diagnostic));
    }

    [Fact]
    public void DM0001_AnchorsBothDiagnosticsToThePropertyNameArguments()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide("Shared")]
            public class FirstService
            {
            }

            [SingletonDIProvide("Shared")]
            public class SecondService
            {
            }
            """;

        var diagnostics = RunGenerator(source)
            .Where(diagnostic => diagnostic.Id == "DM0001")
            .ToImmutableArray();

        Assert.Equal(2, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            Assert.Equal("\"Shared\"", GetSpanText(diagnostic));
        }
    }

    [Fact]
    public void DM0017_AnchorsToTheUnmappedDependencyArgument()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IClock
            {
            }

            [SingletonDIConsume(typeof(IClock))]
            public partial class Consumer
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostic = AssertSingleDiagnostic(
            RunGenerator(source, outputKind: OutputKind.ConsoleApplication),
            "DM0017");

        Assert.Equal("IClock", GetSpanText(diagnostic));
    }

    [Fact]
    public void ProviderDiagnosticNeverCoversTheWholeDeclaration()
    {
        const string source = """
            using SingletonDI.Attributes;

            /// <summary>
            /// A provider nothing can register.
            /// </summary>
            [SingletonDIProvide]
            public abstract class Repository
            {
                public int Id => 1;
            }
            """;

        var diagnostic = AssertSingleDiagnostic(RunGenerator(source), "DM0002");

        AssertSpansOneLine(diagnostic);
        Assert.DoesNotContain('\n', GetSpanText(diagnostic));
    }

    private static ImmutableArray<Diagnostic> RunGenerator(
        string source,
        bool compositionRoot = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        return DeclarationMatrixHarness
            .Run(
                source,
                new MatrixRunOptions
                {
                    CompositionRoot = compositionRoot,
                    OutputKind = outputKind,
                    LanguageVersion = languageVersion,
                    NullableContextProviderEnabled = false,
                    IncludeOutputTypeProperty = true,
                })
            .GeneratorDiagnostics;
    }

    private static Diagnostic AssertSingleDiagnostic(
        ImmutableArray<Diagnostic> diagnostics,
        string id)
    {
        var diagnostic = Assert.Single(diagnostics, item => item.Id == id);
        Assert.True(diagnostic.Location.IsInSource);
        return diagnostic;
    }

    private static string GetSpanText(Diagnostic diagnostic)
    {
        return diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static int GetStartLine(Diagnostic diagnostic)
    {
        return diagnostic.Location.GetLineSpan().StartLinePosition.Line;
    }

    private static int GetLineOf(string source, string lineContent)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.Equals(lines[index].Trim(), lineContent, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new Xunit.Sdk.XunitException($"'{lineContent}' is not a line of the source.");
    }

    private static void AssertSpansOneLine(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        Assert.Equal(lineSpan.StartLinePosition.Line, lineSpan.EndLinePosition.Line);
    }
}