using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

public class DiagnosticErrorTests
{
    [Fact]
    public void DM0001_ProvideOnStruct()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public struct MyStruct
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0001 = diagnostics.FirstOrDefault(d => d.Id == "DM0001");
        Assert.NotNull(dm0001);
        Assert.Equal("Cannot use [SingletonDIProvide] on struct", dm0001.Descriptor.Title);
    }

    [Fact]
    public void DM0002_ProvideOnAbstractClass()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public abstract class MyAbstractClass
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0002 = diagnostics.FirstOrDefault(d => d.Id == "DM0002");
        Assert.NotNull(dm0002);
        Assert.Equal("Cannot use [SingletonDIProvide] on abstract class", dm0002.Descriptor.Title);
    }

    [Fact]
    public void DM0002_ProvideOnInterface()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public interface IInterface
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0002 = diagnostics.FirstOrDefault(d => d.Id == "DM0002");
        Assert.NotNull(dm0002);
        Assert.Equal("Cannot use [SingletonDIProvide] on abstract class", dm0002.Descriptor.Title);
    }

    [Fact]
    public void DM0004_ProvideMissingParameterlessConstructor()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService
                              {
                                  public MyService(int value) { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0004 = diagnostics.FirstOrDefault(d => d.Id == "DM0004");
        Assert.NotNull(dm0004);
        Assert.Contains("MyService", dm0004.GetMessage());
        Assert.Contains("public parameterless constructor", dm0004.GetMessage());
    }

    [Fact]
    public void DM0004_ProvideWithPrivateConstructor()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService
                              {
                                  private MyService() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0004 = diagnostics.FirstOrDefault(d => d.Id == "DM0004");
        Assert.NotNull(dm0004);
        Assert.Contains("MyService", dm0004.GetMessage());
    }

    [Fact]
    public void DM0005_InitializeAsyncNotAccessible_Private()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService
                              {
                                  private Task InitializeAsync() => Task.CompletedTask;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0005 = diagnostics.FirstOrDefault(d => d.Id == "DM0005");
        Assert.NotNull(dm0005);
        Assert.Contains("private", dm0005.GetMessage());
    }

    [Fact]
    public void DM0005_InitializeAsyncNotAccessible_Protected()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService
                              {
                                  protected Task InitializeAsync() => Task.CompletedTask;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0005 = diagnostics.FirstOrDefault(d => d.Id == "DM0005");
        Assert.NotNull(dm0005);
        Assert.Contains("protected", dm0005.GetMessage());
    }

    [Fact]
    public void DM0006_ConsumeReferencesNonProvider()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              public class NonProviderService { }

                              [SingletonDIConsume(typeof(NonProviderService))]
                              public partial class MyConsumer
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0006 = diagnostics.FirstOrDefault(d => d.Id == "DM0006");
        Assert.NotNull(dm0006);
        Assert.Contains("NonProviderService", dm0006.GetMessage());
        Assert.Contains("does not have the [SingletonDIProvide] attribute", dm0006.GetMessage());
    }

    [Fact]
    public void DM0007_ConsumeNotPartial()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService { }

                              [SingletonDIConsume(typeof(MyService))]
                              public class MyConsumer
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0007 = diagnostics.FirstOrDefault(d => d.Id == "DM0007");
        Assert.NotNull(dm0007);
        Assert.Contains("MyConsumer", dm0007.GetMessage());
        Assert.Contains("must be declared as partial", dm0007.GetMessage());
    }

    [Fact]
    public void DM0009_CircularDependency()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceB))]
                              public partial class ServiceA
                              {
                              }

                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceA))]
                              public partial class ServiceB
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0009 = diagnostics.FirstOrDefault(d => d.Id == "DM0009");
        Assert.NotNull(dm0009);
        Assert.Contains("Circular dependency detected", dm0009.GetMessage());
    }

    [Fact]
    public void DM0009_CircularDependency_ThreeWay()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceB))]
                              public partial class ServiceA
                              {
                              }

                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceC))]
                              public partial class ServiceB
                              {
                              }

                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceA))]
                              public partial class ServiceC
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0009 = diagnostics.FirstOrDefault(d => d.Id == "DM0009");
        Assert.NotNull(dm0009);
        Assert.Contains("Circular dependency detected", dm0009.GetMessage());
    }

    [Fact]
    public void DM0010_ConsumeDuplicateTypes()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService { }

                              [SingletonDIConsume(typeof(MyService), typeof(MyService))]
                              public partial class MyConsumer
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0010 = diagnostics.FirstOrDefault(d => d.Id == "DM0010");
        Assert.NotNull(dm0010);
        Assert.Contains("MyService", dm0010.GetMessage());
        Assert.Contains("specified multiple times", dm0010.GetMessage());
    }

    [Fact]
    public void DM0011_ConsumeDuplicateInBaseClass()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class MyService { }

                              [SingletonDIConsume(typeof(MyService))]
                              public partial class BaseConsumer
                              {
                              }

                              [SingletonDIConsume(typeof(MyService))]
                              public partial class DerivedConsumer : BaseConsumer
                              {
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0011 = diagnostics.FirstOrDefault(d => d.Id == "DM0011");
        Assert.NotNull(dm0011);
        Assert.Contains("MyService", dm0011.GetMessage());
        Assert.Contains("BaseConsumer", dm0011.GetMessage());
        Assert.Contains("already declared in base class", dm0011.GetMessage());
    }

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
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attributes.SingletonDIProvideAttribute).Assembly.Location),
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
