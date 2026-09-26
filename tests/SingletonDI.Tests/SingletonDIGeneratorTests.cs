using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SingletonDI.Generator.Emitters;
using SingletonDI.Generator.Validators;
using Xunit;
using SingletonDI.Generator;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for the SingletonDI source generator.
/// </summary>
public class SingletonDIGeneratorTests
{
    [Fact]
    public void ProviderModel_WithNullPropertyName_UsesDefault()
    {
        // Arrange & Act
        var model = new ProviderModel(fullyQualifiedName : "Test.UserService",
                                      shortName : "UserService",
                                      @namespace : "Test",
                                      assemblyIdentity : "Test",
                                      hasInitializeAsyncMethod : false,
                                      isDisposable : false,
                                      isAsyncDisposable : false,
                                      dependencies : ImmutableArray<string>.Empty,
                                      serviceTypeFullyQualifiedName : null,
                                      serviceTypeShortName : null,
                                      serviceTypeNamespace : null,
                                      propertyName : null,
                                      location : Location.None,
                                      propertyNameLocation : null);

        // Assert
        Assert.Null(model.PropertyName);
    }

    [Fact]
    public void ProviderValidator_SymbolOnlyOverload_ReadsServiceType()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract { }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class Service : IContract { }
            }
            """, "App.Service");

        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
        Assert.Equal("global::App.IContract", model!.Value.ServiceTypeFullyQualifiedName);
        Assert.Equal("IContract", model.Value.ServiceTypeShortName);
        Assert.Equal("App", model.Value.ServiceTypeNamespace);
    }

    [Fact]
    public void ProviderValidator_AcceptsServiceTypeAssignableThroughGenericVariance()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IBox<out T> { }

                [SingletonDIProvide(ServiceType = typeof(IBox<object>))]
                public class StringBox : IBox<string> { }
            }
            """, "App.StringBox");

        var provider = compilation.GetTypeByMetadataName("App.StringBox")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
        Assert.Equal("global::App.IBox<global::System.Object>", model!.Value.ServiceTypeFullyQualifiedName);
    }

    [Fact]
    public void ProviderValidator_RejectsGenericVarianceWithValueTypeArgument()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IBox<out T> { }

                [SingletonDIProvide(ServiceType = typeof(IBox<object>))]
                public class IntBox : IBox<int> { }
            }
            """, "App.IntBox");

        var provider = compilation.GetTypeByMetadataName("App.IntBox")!;
        var compilationDiagnostics = new List<Diagnostic>();
        var compilationModel = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            compilationDiagnostics.Add);
        var symbolDiagnostics = new List<Diagnostic>();
        var symbolModel = ProviderValidator.Validate(
            provider,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            symbolDiagnostics.Add);

        // IBox<int> converts to IBox<object> only explicitly: the instance is not an IBox<object> at
        // run time, so the cast in Resolve<IBox<object>> would throw.
        Assert.Null(compilationModel);
        Assert.Contains(compilationDiagnostics, diagnostic => diagnostic.Id == "DM0016");
        Assert.Null(symbolModel);
        Assert.Contains(symbolDiagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void ProviderValidator_AcceptsInterfaceTypeArgumentThroughVarianceToObject()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IValue { }
                public interface IBox<out T> { }

                [SingletonDIProvide(ServiceType = typeof(IBox<object>))]
                public class ValueBox : IBox<IValue> { }
            }
            """, "App.ValueBox");

        var provider = compilation.GetTypeByMetadataName("App.ValueBox")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ProviderValidator_AcceptsObjectServiceType()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract { }

                [SingletonDIProvide(ServiceType = typeof(object))]
                public class Service : IContract { }
            }
            """, "App.Service");

        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ProviderValidator_RejectsUserDefinedConversionOnly()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public class Contract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(Contract))]
                public class Service
                {
                    public static implicit operator Contract(Service service) => new Contract();
                }
            }
            """, "App.Service");

        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var compilationDiagnostics = new List<Diagnostic>();
        var compilationModel = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            compilationDiagnostics.Add);
        var symbolDiagnostics = new List<Diagnostic>();
        var symbolModel = ProviderValidator.Validate(
            provider,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            symbolDiagnostics.Add);

        Assert.Null(compilationModel);
        Assert.Contains(compilationDiagnostics, diagnostic => diagnostic.Id == "DM0016");
        Assert.Null(symbolModel);
        Assert.Contains(symbolDiagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void ProviderValidator_MetadataProvider_RejectsUserDefinedConversionWithCompilation()
    {
        var (providerCompilation, _) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace ProviderAssembly
            {
                public class Contract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(Contract))]
                public class Service
                {
                    public static implicit operator Contract(Service service) => new Contract();
                }
            }
            """, "ProviderAssembly.Service");
        using var stream = new MemoryStream();
        var emitResult = providerCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var metadataReference = MetadataReference.CreateFromImage(stream.ToArray());
        var consumerCompilation = CSharpCompilation.Create(
            "ConsumerAssembly",
            [],
            providerCompilation.References.Append(metadataReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var provider = consumerCompilation.GetTypeByMetadataName("ProviderAssembly.Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            consumerCompilation,
            Location.None,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.Null(model);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void ProviderValidator_ReportsUserDefinedTaskAsUnsupportedReturnType()
    {
        var (compilation, declaration) = CreateCompilation(
            """
            using SingletonDI.Attributes;

            public sealed class Task
            {
            }

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                    public Task InitializeAsync() => new Task();
                }
            }
            """,
            "App.Service");
        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.False(model!.Value.HasInitializeAsyncMethod);
        var diagnostic = Assert.Single(diagnostics, item => item.Id == "DM0034");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void ProviderValidator_RejectsInaccessibleConsumeDependency()
    {
        var (compilation, declaration) = CreateCompilation(
            """
            using SingletonDI.Attributes;

            file sealed class FileService
            {
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(FileService))]
            public sealed class Service
            {
            }
            """,
            "Service");
        var provider = compilation.GetTypeByMetadataName("Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.Null(model);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0022");
    }

    [Fact]
    public void ProviderValidator_RejectsOpenGenericConsumeDependency()
    {
        var (compilation, declaration) = CreateCompilation(
            """
            using SingletonDI.Attributes;

            public interface IContract<T>
            {
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(IContract<>))]
            public sealed class Service
            {
            }
            """,
            "Service");
        var provider = compilation.GetTypeByMetadataName("Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            compilation,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.Null(model);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0024");
    }

    [Fact]
    public void ProviderValidator_RejectsServiceTypeThatProviderDoesNotImplement()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract { }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class Service { }
            }
            """, "App.Service");

        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.Null(model);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void ConsumerValidator_AllowsExternalContractAndCarriesTypedReference()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace Contracts
            {
                public interface IContract { }
            }

            namespace App
            {
                [SingletonDIConsume(typeof(Contracts.IContract))]
                public partial class Consumer { }
            }
            """, "App.Consumer");

        var consumer = compilation.GetTypeByMetadataName("App.Consumer")!;
        var diagnostics = new List<Diagnostic>();
        var model = ConsumerValidator.Validate(
            declaration,
            consumer,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
        var reference = Assert.Single(model!.Value.Dependencies);
        Assert.Equal("global::Contracts.IContract", reference.FullyQualifiedName);
        Assert.True(reference.IsContract);
        Assert.Null(reference.PropertyName);
    }

    [Fact]
    public void ProviderAndConsumerValidators_ReportSingleMissingDependencyDiagnostic()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace Contracts
            {
                public interface IExternalService { }
                public class MissingService { }
            }

            namespace App
            {
                [SingletonDIProvide]
                [SingletonDIConsume(typeof(Contracts.IExternalService), typeof(Contracts.MissingService))]
                public partial class Provider { }
            }
            """, "App.Provider");

        var provider = compilation.GetTypeByMetadataName("App.Provider")!;
        var diagnostics = new List<Diagnostic>();
        var model = ProviderValidator.Validate(
            provider,
            declaration.GetLocation(),
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);
        var consumerModel = ConsumerValidator.Validate(
            declaration,
            provider,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.NotNull(consumerModel);
        Assert.Equal(
            new[] { "global::Contracts.IExternalService", "global::Contracts.MissingService" },
            model!.Value.Dependencies.ToArray());
        Assert.Equal("global::Contracts.IExternalService", Assert.Single(consumerModel!.Value.Dependencies).FullyQualifiedName);
        var missingDependencyDiagnostic = Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == "DM0006");
        Assert.Contains("valid interface or abstract contract", missingDependencyDiagnostic.GetMessage());
    }

    [Fact]
    public void ConsumerValidator_AcceptsProviderMarkedOutsideKnownSet()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace ProviderAssembly
            {
                [SingletonDIProvide("ExternalService")]
                public class ExternalService { }
            }

            namespace App
            {
                [SingletonDIConsume(typeof(ProviderAssembly.ExternalService))]
                public partial class Consumer { }
            }
            """, "App.Consumer");

        var consumer = compilation.GetTypeByMetadataName("App.Consumer")!;
        var diagnostics = new List<Diagnostic>();
        var model = ConsumerValidator.Validate(
            declaration,
            consumer,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
        var reference = Assert.Single(model!.Value.Dependencies);
        Assert.False(reference.IsContract);
        Assert.Equal("ExternalService", reference.PropertyName);
    }

    [Fact]
    public void ProviderValidator_MetadataProvider_UsesNoneLocationAndAssemblyIdentity()
    {
        var (providerCompilation, _) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace ProviderAssembly
            {
                [SingletonDIProvide]
                public class ExternalProvider { }
            }
            """, "ProviderAssembly.ExternalProvider");
        using var stream = new MemoryStream();
        var emitResult = providerCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var metadataReference = MetadataReference.CreateFromImage(stream.ToArray());
        var consumerCompilation = CSharpCompilation.Create(
            "ConsumerAssembly",
            [],
            providerCompilation.References.Append(metadataReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var provider = consumerCompilation.GetTypeByMetadataName("ProviderAssembly.ExternalProvider")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            Location.None,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(diagnostics);
        Assert.Equal(Location.None, model!.Value.Location);
        Assert.StartsWith("SingletonDIGeneratorTests,", model.Value.AssemblyIdentity, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderValidator_MetadataProvider_RejectsUserDefinedConversion()
    {
        var (providerCompilation, _) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace ProviderAssembly
            {
                public class Contract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(Contract))]
                public class Service
                {
                    public static implicit operator Contract(Service service) => new Contract();
                }
            }
            """, "ProviderAssembly.Service");
        using var stream = new MemoryStream();
        var emitResult = providerCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        var metadataReference = MetadataReference.CreateFromImage(stream.ToArray());
        var consumerCompilation = CSharpCompilation.Create(
            "ConsumerAssembly",
            [],
            providerCompilation.References.Append(metadataReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var provider = consumerCompilation.GetTypeByMetadataName("ProviderAssembly.Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            provider,
            Location.None,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        Assert.Null(model);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void ProviderValidator_UsesExplicitLocationForPartialDeclarationDiagnostics()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public abstract partial class Service { }

                public abstract partial class Service { }
            }
            """, "App.Service", 1);

        var provider = compilation.GetTypeByMetadataName("App.Service")!;
        var diagnostics = new List<Diagnostic>();

        var model = ProviderValidator.Validate(
            declaration,
            provider,
            ImmutableHashSet<string>.Empty,
            diagnostics.Add);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Null(model);
        Assert.Equal("DM0002", diagnostic.Id);
        Assert.Equal(declaration.GetLocation().SourceSpan, diagnostic.Location.SourceSpan);
        Assert.Same(declaration.SyntaxTree, diagnostic.Location.SourceTree);
    }

    [Fact]
    public void ServiceTypeMap_MapsContractAndImplementationToOneProvider()
    {
        var provider = new ProviderModel(
            "global::App.DatabaseService",
            "DatabaseService",
            "App",
            "App, Version=1.0.0.0",
            true,
            false,
            false,
            ImmutableArray<string>.Empty,
            "global::Contracts.IDatabaseService",
            "IDatabaseService",
            "Contracts",
            null,
            Location.None,
            null);

        var result = ServiceTypeResolver.BuildServiceTypeMap([provider]);
        var map = result.Map;

        Assert.Equal(provider.FullyQualifiedName, map["global::App.DatabaseService"]);
        Assert.Equal(provider.FullyQualifiedName, map["global::Contracts.IDatabaseService"]);
    }

    [Fact]
    public void ServiceTypeMap_ReportsAllProvidersForContractConflict()
    {
        var first = new ProviderModel(
            "global::App.FirstDatabaseService",
            "FirstDatabaseService",
            "App",
            "App, Version=1.0.0.0",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            "global::Contracts.IDatabaseService",
            "IDatabaseService",
            "Contracts",
            null,
            Location.None,
            null);
        var second = new ProviderModel(
            "global::App.SecondDatabaseService",
            "SecondDatabaseService",
            "App",
            "App, Version=1.0.0.0",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            "global::Contracts.IDatabaseService",
            "IDatabaseService",
            "Contracts",
            null,
            Location.None,
            null);

        var result = ServiceTypeResolver.BuildServiceTypeMap([first, second]);
        var conflict = Assert.Single(result.Conflicts);

        Assert.Equal("global::Contracts.IDatabaseService", conflict.ServiceTypeFullyQualifiedName);
        Assert.Equal(
            new[] { first.FullyQualifiedName, second.FullyQualifiedName },
            conflict.ProviderFullyQualifiedNames);
    }

    [Fact]
    public void ServiceTypeMap_DoesNotMergeSameFqnAcrossAssemblies()
    {
        var first = new ProviderModel(
            "global::App.Service",
            "Service",
            "App",
            "FirstAssembly",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            "global::Contracts.IService",
            "IService",
            "Contracts",
            null,
            Location.None,
            null,
            serviceTypeIdentity: new ServiceTypeIdentity(
                "global::Contracts.IService",
                "ContractsA"));
        var second = new ProviderModel(
            "global::App.Service",
            "Service",
            "App",
            "SecondAssembly",
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            "global::Contracts.IService",
            "IService",
            "Contracts",
            null,
            Location.None,
            null,
            serviceTypeIdentity: new ServiceTypeIdentity(
                "global::Contracts.IService",
                "ContractsB"));

        var result = ServiceTypeResolver.BuildServiceTypeMap([first, second]);

        Assert.Equal(2, result.Conflicts.Length);
        var providerConflict = Assert.Single(
            result.Conflicts,
            conflict => conflict.ServiceTypeFullyQualifiedName == "global::App.Service");
        Assert.Equal(2, providerConflict.ServiceTypeIdentities.Length);
        Assert.Contains(
            providerConflict.ServiceTypeIdentities,
            identity => identity.AssemblyIdentity == "FirstAssembly");
        Assert.Contains(
            providerConflict.ServiceTypeIdentities,
            identity => identity.AssemblyIdentity == "SecondAssembly");
        Assert.Equal(4, result.IdentityMap.Count);
        Assert.Contains(
            new ServiceTypeIdentity("global::Contracts.IService", "ContractsA"),
            result.IdentityMap.Keys);
        Assert.Contains(
            new ServiceTypeIdentity("global::Contracts.IService", "ContractsB"),
            result.IdentityMap.Keys);
    }

    [Fact]
    public void TopologicalSorter_DoesNotCollapseSameNamedProvidersFromDifferentAssemblies()
    {
        // Two providers share a fully qualified name and differ only by assembly. Collapsing them
        // into one graph node would hide a cycle between them.
        var firstProvider = CreateProvider("global::App.Service", "App", "FirstAssembly");
        var secondProvider = CreateProvider("global::App.Service", "App", "SecondAssembly");

        var cycle = TopologicalSorter.TryFindCycle(
            [firstProvider, secondProvider],
            new Dictionary<ServiceTypeIdentity, ServiceTypeIdentity>());

        Assert.Empty(cycle);
    }

    [Fact]
    public void TopologicalSorter_DetectsCycleBetweenSameNamedProvidersFromDifferentAssemblies()
    {
        var firstProvider = CreateProvider(
            "global::App.Service",
            "App",
            "FirstAssembly",
            [new ServiceTypeIdentity("global::App.Service", "SecondAssembly")]);
        var secondProvider = CreateProvider(
            "global::App.Service",
            "App",
            "SecondAssembly",
            [new ServiceTypeIdentity("global::App.Service", "FirstAssembly")]);
        var map = ServiceTypeResolver.BuildServiceTypeMap([firstProvider, secondProvider]).IdentityMap;

        var cycle = TopologicalSorter.TryFindCycle([firstProvider, secondProvider], map);

        // The DFS closes the cycle by repeating the start node, so assert on the identities that
        // participate: both same-named providers must appear, which is what collapsing them into
        // one node would prevent.
        var assemblies = cycle.Select(identity => identity.AssemblyIdentity).Distinct().ToArray();
        Assert.Equal(2, assemblies.Length);
    }

    [Fact]
    public void TopologicalSorter_ResolvesContractAliasThroughServiceTypeMap()
    {
        var implementation = CreateProvider(
            "global::App.DatabaseService",
            "App",
            "App",
            serviceTypeFullyQualifiedName: "global::Contracts.IDatabaseService");
        var dependent = CreateProvider(
            "global::App.OrderService",
            "App",
            "App",
            [new ServiceTypeIdentity("global::Contracts.IDatabaseService", "Contracts")]);
        var map = ServiceTypeResolver.BuildServiceTypeMap([implementation, dependent]).IdentityMap;

        var cycle = TopologicalSorter.TryFindCycle([implementation, dependent], map);

        Assert.Empty(cycle);
    }

    private static ProviderModel CreateProvider(
        string fullyQualifiedName,
        string @namespace,
        string assemblyIdentity,
        ServiceTypeIdentity[]? dependencyIdentities = null,
        string? serviceTypeFullyQualifiedName = null) =>
        new(
            fullyQualifiedName,
            fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..],
            @namespace,
            assemblyIdentity,
            false,
            false,
            false,
            ImmutableArray<string>.Empty,
            serviceTypeFullyQualifiedName,
            serviceTypeFullyQualifiedName is null
                ? null
                : serviceTypeFullyQualifiedName[(serviceTypeFullyQualifiedName.LastIndexOf('.') + 1)..],
            serviceTypeFullyQualifiedName is null ? null : "Contracts",
            null,
            Location.None,
            null,
            dependencyIdentities is null ? ImmutableArray<ServiceTypeIdentity>.Empty : dependencyIdentities.ToImmutableArray());

    [Fact]
    public void PropertyNameResolver_ResolvesNamesWithinConsumerOnly()
    {
        var references = new[]
        {
            new ServiceReferenceModel("global::A.Service", "Service", "A", "CustomService", false),
            new ServiceReferenceModel("global::B.Service", "Service", "B", null, true),
            new ServiceReferenceModel("global::C.Service", "Service", "C", null, true),
            new ServiceReferenceModel("global::D.Unique", "Unique", "D", null, true)
        };

        var names = PropertyNameResolver.ResolveConsumerPropertyNames(references);

        Assert.Equal("CustomService", names["global::A.Service"]);
        Assert.Equal("B_ServiceInstance", names["global::B.Service"]);
        Assert.Equal("C_ServiceInstance", names["global::C.Service"]);
        Assert.Equal("UniqueInstance", names["global::D.Unique"]);
    }

    [Fact]
    public void ConsumerValidator_UsesIdentityForProviderClassification()
    {
        var (compilation, declaration) = CreateCompilation("""
            using SingletonDI.Attributes;

            namespace App
            {
                public sealed class Service { }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer { }
            }
            """, "App.Consumer");

        var consumer = compilation.GetTypeByMetadataName("App.Consumer")!;
        var diagnostics = new List<Diagnostic>();
        var model = ConsumerValidator.Validate(
            declaration,
            consumer,
            ImmutableHashSet.Create(new ServiceTypeIdentity("global::App.Service", "OtherAssembly")),
            diagnostics.Add);

        Assert.NotNull(model);
        Assert.Empty(model!.Value.Dependencies);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0006");
    }

    [Fact]
    public void ServiceTypeIdentity_DistinguishesConstructedTypeArgumentAssemblies()
    {
        var contractCompilation = CSharpCompilation.Create(
            "Contracts",
            [CSharpSyntaxTree.ParseText("namespace Shared { public interface IEnvelope<out T> { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var contractReference = CreateMetadataReference(contractCompilation);
        var firstPayloadCompilation = CSharpCompilation.Create(
            "FirstPayload",
            [CSharpSyntaxTree.ParseText("namespace Shared { public sealed class Payload { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var secondPayloadCompilation = CSharpCompilation.Create(
            "SecondPayload",
            [CSharpSyntaxTree.ParseText("namespace Shared { public sealed class Payload { } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var firstPayloadReference = CreateMetadataReference(firstPayloadCompilation);
        var secondPayloadReference = CreateMetadataReference(secondPayloadCompilation);
        var objectReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var firstConsumerCompilation = CSharpCompilation.Create(
            "FirstConsumer",
            [],
            [contractReference, firstPayloadReference, objectReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var secondConsumerCompilation = CSharpCompilation.Create(
            "SecondConsumer",
            [],
            [contractReference, secondPayloadReference, objectReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var firstEnvelope = firstConsumerCompilation
            .GetTypeByMetadataName("Shared.IEnvelope`1")!
            .Construct(firstConsumerCompilation.GetTypeByMetadataName("Shared.Payload")!);
        var secondEnvelope = secondConsumerCompilation
            .GetTypeByMetadataName("Shared.IEnvelope`1")!
            .Construct(secondConsumerCompilation.GetTypeByMetadataName("Shared.Payload")!);

        var firstIdentity = ServiceTypeIdentity.FromSymbol(firstEnvelope);
        var secondIdentity = ServiceTypeIdentity.FromSymbol(secondEnvelope);

        Assert.Equal(firstIdentity.FullyQualifiedName, secondIdentity.FullyQualifiedName);
        Assert.Equal(firstIdentity.AssemblyIdentity, secondIdentity.AssemblyIdentity);
        Assert.NotEqual(firstIdentity, secondIdentity);
    }

    [Fact]
    public void PropertyNameResolver_UsesServiceIdentityAsKey()
    {
        var first = new ServiceReferenceModel(
            "global::App.Service",
            "Service",
            "App",
            null,
            false,
            new ServiceTypeIdentity("global::App.Service", "FirstAssembly"));
        var second = new ServiceReferenceModel(
            "global::App.Service",
            "Service",
            "App",
            null,
            false,
            new ServiceTypeIdentity("global::App.Service", "SecondAssembly"));

        var names = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity([first, second]);

        Assert.Equal(2, names.Count);
        Assert.Equal(2, names.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PropertyNameResolver_UsesProviderIdentityForSameFqn()
    {
        var first = new ProviderModel(
            "global::App.Service",
            "Service",
            "App",
            "FirstAssembly",
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
        var second = new ProviderModel(
            "global::App.Service",
            "Service",
            "App",
            "SecondAssembly",
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

        var names = PropertyNameResolver.ResolvePropertyNamesByIdentity([first, second]);

        Assert.Equal(2, names.Count);
        Assert.Equal(2, names.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ConsumerEmitter_EmitsDistinctPropertiesForSameFqnDependencies()
    {
        var first = new ServiceReferenceModel(
            "global::App.Service",
            "Service",
            "App",
            null,
            false,
            new ServiceTypeIdentity("global::App.Service", "FirstAssembly"));
        var second = new ServiceReferenceModel(
            "global::App.Service",
            "Service",
            "App",
            null,
            false,
            new ServiceTypeIdentity("global::App.Service", "SecondAssembly"));
        var consumer = new ConsumerModel(
            "global::App.Consumer",
            "Consumer",
            "App",
            true,
            [first, second]);

        var source = ConsumerEmitter.GenerateByIdentity([consumer])
            .Values
            .Single();

        Assert.Equal(
            2,
            source.Split("Resolve<global::App.Service>()", StringSplitOptions.None).Length - 1);
        Assert.Contains("App_ServiceInstance", source);
        Assert.Contains("App_ServiceInstance_2", source);
    }

    private static PortableExecutableReference CreateMetadataReference(
        CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static (CSharpCompilation Compilation, TypeDeclarationSyntax Declaration) CreateCompilation(
        string source,
        string typeMetadataName,
        int declarationIndex = 0)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.Latest,
                preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]));
        var compilation = CSharpCompilation.Create(
            "SingletonDIGeneratorTests",
            [syntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(global::SingletonDI.Generated.__SingletonDIHost__).Assembly.Location),
                MetadataReference.CreateFromFile(
                    Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll"))
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var declaration = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(node => compilation.GetSemanticModel(syntaxTree).GetDeclaredSymbol(node) is INamedTypeSymbol symbol &&
                           symbol.ToDisplayString() == typeMetadataName)
            .ElementAt(declarationIndex);

        return (compilation, declaration);
    }
}
