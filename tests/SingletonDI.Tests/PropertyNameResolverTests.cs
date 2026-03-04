using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for property name resolution in SingletonDI.Generator.
/// These tests verify that property names are correctly generated when providers have conflicting ShortNames.
/// </summary>
public class PropertyNameResolverTests
{
    /// <summary>
    /// Test 1: Two providers with the same ShortName without custom names.
    /// Both should get namespace-prefixed property names.
    /// </summary>
    [Fact]
    public void TwoProvidersWithSameShortName_NoCustomNames_GeneratesNamesWithNamespacePrefix()
    {
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace TestNamespace0
                              {
                                  [SingletonDIConsume(typeof(SampleNamespace0.ProvideA), typeof(SampleNamespace1.ProvideA))]
                                  public partial class Test
                                  {
                                  }
                              }

                              namespace SampleNamespace0
                              {
                                  [SingletonDIProvide]
                                  public class ProvideA
                                  {
                                      public string Prop { get; set; } = "SampleNamespace0 ProvideA Test";
                                  }
                              }

                              namespace SampleNamespace1
                              {
                                  [SingletonDIProvide]
                                  public class ProvideA
                                  {
                                      public string Prop { get; set; } = "SampleNamespace1 ProvideA Test";
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - both providers should have namespace-prefixed property names in Container
        Assert.Contains("SampleNamespace0_ProvideA", generatedCode);
        Assert.Contains("SampleNamespace1_ProvideA", generatedCode);
    }

    /// <summary>
    /// Test 2: Two providers with the same ShortName, one with custom name.
    /// The one with the custom name should use it, the other one should get the name without the namespace prefix.
    /// </summary>
    [Fact]
    public void TwoProvidersWithSameShortName_OneCustomName_GeneratesCorrectNames()
    {
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace TestNamespace1
                              {
                                  [SingletonDIConsume(typeof(SampleNamespace2.ProvideA), typeof(SampleNamespace3.ProvideA))]
                                  public partial class Test
                                  {
                                  }
                              }

                              namespace SampleNamespace2
                              {
                                  [SingletonDIProvide("ProvideAName")]
                                  public class ProvideA
                                  {
                                      public string Prop { get; set; } = "SampleNamespace2 ProvideA Test";
                                  }
                              }

                              namespace SampleNamespace3
                              {
                                  [SingletonDIProvide]
                                  public class ProvideA
                                  {
                                      public string Prop { get; set; } = "SampleNamespace3 ProvideA Test";
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - provider with custom name uses it, other gets namespace prefix
        Assert.Contains("ProvideAName", generatedCode);
        Assert.Contains("ProvideAInstance", generatedCode);
        Assert.DoesNotContain("SampleNamespace3_ProvideAInstance", generatedCode);
    }

    /// <summary>
    /// Test 3: Single provider without name conflict.
    /// Should use ShortName without namespace prefix.
    /// </summary>
    [Fact]
    public void SingleProvider_NoConflict_UsesShortName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace MyApp.Services
                              {
                                  [SingletonDIProvide]
                                  public class MyService
                                  {
                                      public void DoWork() { }
                                  }

                                  [SingletonDIConsume(typeof(MyService))]
                                  public partial class MyConsumer
                                  {
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - single provider uses ShortName without namespace prefix
        Assert.Contains("MyServiceInstance", generatedCode);
        // Should NOT have namespace prefix
        Assert.DoesNotContain("MyApp_Services_MyServiceInstance", generatedCode);
    }

    /// <summary>
    /// Test 4: Single provider with custom property name.
    /// Should use the custom name.
    /// </summary>
    [Fact]
    public void SingleProvider_WithCustomName_UsesCustomName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace MyApp.Services
                              {
                                  [SingletonDIProvide("CustomService")]
                                  public class MyService
                                  {
                                      public void DoWork() { }
                                  }

                                  [SingletonDIConsume(typeof(MyService))]
                                  public partial class MyConsumer
                                  {
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - custom name is used
        Assert.Contains("CustomService", generatedCode);
        Assert.DoesNotContain("CustomServiceInstance", generatedCode);
    }

    /// <summary>
    /// Test 5: Three providers with the same ShortName.
    /// All three should get namespace-prefixed property names.
    /// </summary>
    [Fact]
    public void ThreeProvidersWithSameShortName_NoCustomNames_AllGetNamespacePrefix()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace ConsumerNamespace
                              {
                                  [SingletonDIConsume(typeof(NamespaceA.Service), typeof(NamespaceB.Service), typeof(NamespaceC.Service))]
                                  public partial class Consumer
                                  {
                                  }
                              }

                              namespace NamespaceA
                              {
                                  [SingletonDIProvide]
                                  public class Service
                                  {
                                      public string Name => "A";
                                  }
                              }

                              namespace NamespaceB
                              {
                                  [SingletonDIProvide]
                                  public class Service
                                  {
                                      public string Name => "B";
                                  }
                              }

                              namespace NamespaceC
                              {
                                  [SingletonDIProvide]
                                  public class Service
                                  {
                                      public string Name => "C";
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - all three get namespace-prefixed names
        Assert.Contains("NamespaceA_ServiceInstance", generatedCode);
        Assert.Contains("NamespaceB_ServiceInstance", generatedCode);
        Assert.Contains("NamespaceC_ServiceInstance", generatedCode);
    }

    /// <summary>
    /// Test 6: Three providers, two with custom names, one without.
    /// Custom names should be used for those that have them, namespace prefix for the one without.
    /// </summary>
    [Fact]
    public void ThreeProviders_TwoWithCustomNames_OneWithout_GeneratesCorrectNames()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace ConsumerNamespace
                              {
                                  [SingletonDIConsume(typeof(NamespaceX.Provider), typeof(NamespaceY.Provider), typeof(NamespaceZ.Provider))]
                                  public partial class Consumer
                                  {
                                  }
                              }

                              namespace NamespaceX
                              {
                                  [SingletonDIProvide("ProviderX")]
                                  public class Provider
                                  {
                                  }
                              }

                              namespace NamespaceY
                              {
                                  [SingletonDIProvide("ProviderY")]
                                  public class Provider
                                  {
                                  }
                              }

                              namespace NamespaceZ
                              {
                                  [SingletonDIProvide]
                                  public class Provider
                                  {
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - custom names used, third gets namespace prefix
        Assert.Contains("ProviderX", generatedCode);
        Assert.DoesNotContain("NamespaceX_ProviderInstance", generatedCode);
        Assert.Contains("ProviderY", generatedCode);
        Assert.DoesNotContain("NamespaceY_ProviderInstance", generatedCode);
        Assert.Contains("ProviderInstance", generatedCode);
    }

    /// <summary>
    /// Test 7: Provider in global namespace (no namespace).
    /// Should use ShortName directly.
    /// </summary>
    [Fact]
    public void Provider_InGlobalNamespace_UsesShortName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              [SingletonDIProvide]
                              public class GlobalService
                              {
                                  public void DoWork() { }
                              }

                              namespace MyApp
                              {
                                  [SingletonDIConsume(typeof(GlobalService))]
                                  public partial class Consumer
                                  {
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - global namespace provider uses ShortName
        Assert.Contains("GlobalServiceInstance", generatedCode);
        Assert.DoesNotContain("MyApp_GlobalServiceInstance", generatedCode);
    }

    /// <summary>
    /// Test 8: Provider with nested namespace.
    /// Should use the full namespace path converted to underscores when there's a conflict.
    /// </summary>
    [Fact]
    public void Provider_WithNestedNamespace_GeneratesCorrectPrefix()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace ConsumerNamespace
                              {
                                  [SingletonDIConsume(typeof(Company.Project.Services.Repository), typeof(Company.Project.Data.Repository))]
                                  public partial class Consumer
                                  {
                                  }
                              }

                              namespace Company.Project.Services
                              {
                                  [SingletonDIProvide]
                                  public class Repository
                                  {
                                      public string Type => "Services";
                                  }
                              }

                              namespace Company.Project.Data
                              {
                                  [SingletonDIProvide]
                                  public class Repository
                                  {
                                      public string Type => "Data";
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - nested namespaces are converted to underscores
        Assert.Contains("Company_Project_Services_RepositoryInstance", generatedCode);
        Assert.Contains("Company_Project_Data_RepositoryInstance", generatedCode);
    }

    /// <summary>
    /// Test 9: Mixed scenario - some providers with conflicts, some without.
    /// Only conflicting providers should get namespace prefixes.
    /// </summary>
    [Fact]
    public void MixedProviders_WithAndWithoutConflicts_GeneratesCorrectNames()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace ConsumerNamespace
                              {
                                  [SingletonDIConsume(typeof(NamespaceA.UniqueService), typeof(NamespaceB.ConflictingService), typeof(NamespaceC.ConflictingService))]
                                  public partial class Consumer
                                  {
                                  }
                              }

                              namespace NamespaceA
                              {
                                  [SingletonDIProvide]
                                  public class UniqueService
                                  {
                                      // No conflict - should use ShortName
                                  }
                              }

                              namespace NamespaceB
                              {
                                  [SingletonDIProvide]
                                  public class ConflictingService
                                  {
                                      // Conflict with NamespaceC - should get prefix
                                  }
                              }

                              namespace NamespaceC
                              {
                                  [SingletonDIProvide]
                                  public class ConflictingService
                                  {
                                      // Conflict with NamespaceB - should get prefix
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - UniqueService has no prefix, ConflictingService has prefix
        Assert.Contains("UniqueServiceInstance", generatedCode);
        Assert.DoesNotContain("NamespaceA_UniqueServiceInstance", generatedCode);
        Assert.Contains("NamespaceB_ConflictingServiceInstance", generatedCode);
        Assert.Contains("NamespaceC_ConflictingServiceInstance", generatedCode);
    }

    /// <summary>
    /// Test 10: Provider with custom name that matches another provider's ShortName.
    /// Should not cause conflict - custom name takes precedence.
    /// </summary>
    [Fact]
    public void ProviderWithCustomName_MatchesOtherProviderShortName_NoConflict()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace ConsumerNamespace
                              {
                                  [SingletonDIConsume(typeof(NamespaceA.ServiceA), typeof(NamespaceB.ServiceB))]
                                  public partial class Consumer
                                  {
                                  }
                              }

                              namespace NamespaceA
                              {
                                  [SingletonDIProvide("ServiceB")]  // Custom name matches ServiceB's ShortName
                                  public class ServiceA
                                  {
                                  }
                              }

                              namespace NamespaceB
                              {
                                  [SingletonDIProvide]
                                  public class ServiceB
                                  {
                                  }
                              }
                              """;

        // Act
        var generatedCode = RunGeneratorAndGetGeneratedCode(SOURCE);

        // Assert - ServiceA uses custom name "ServiceB", ServiceB uses its ShortName
        // Note: This is a valid scenario - custom names are independent of ShortNames
        Assert.Contains("ServiceB", generatedCode);
        Assert.Contains("ServiceBInstance", generatedCode);
        Assert.DoesNotContain("NamespaceA_ServiceBInstance", generatedCode);
        Assert.DoesNotContain("NamespaceB_ServiceBInstance", generatedCode);
    }

    #region Helper Methods

    private static string RunGeneratorAndGetGeneratedCode(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new SingletonDIGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        // Get generated code from the output compilation
        var generatedFiles = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("SingletonDI.Generator"))
            .ToList();

        // Combine all generated code into one string for easier assertions
        return string.Join("\n", generatedFiles.Select(t => t.ToString()));
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

    #endregion
}
