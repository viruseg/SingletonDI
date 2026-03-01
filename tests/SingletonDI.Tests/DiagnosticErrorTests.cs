using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

public class DiagnosticErrorTests
{
    [Fact]
    public void DM0012_InitializeAsyncCannotBeStatic()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService
                              {
                                  public static Task InitializeAsync() => Task.CompletedTask;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0012 = diagnostics.FirstOrDefault(d => d.Id == "DM0012");
        Assert.NotNull(dm0012);
        Assert.Equal("Method InitializeAsync in class 'MyService' is static. InitializeAsync must be an instance method.", dm0012.GetMessage());
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new SingletonDIGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDI.Attributes.SingletonDIProvideAttribute).Assembly.Location),
        };

        // Add all referenced assemblies
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[] { "System.Runtime", "System.Collections", "System.Linq", "netstandard" })
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}