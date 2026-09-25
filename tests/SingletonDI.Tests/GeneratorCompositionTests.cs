using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SingletonDI.Attributes;
using SingletonDI.Generator;
using SingletonDI.Generator.Helpers;
using Xunit;

namespace SingletonDI.Tests;

public sealed class GeneratorCompositionTests
{
    private const string ContractsSource = """
        namespace Shared.Contracts
        {
            public interface IDatabaseService
            {
            }

            public interface IAuditService
            {
            }
        }
        """;

    [Fact]
    public void Root_ImportsPublicProviderFromReferencedAssembly()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void Root_ImportsPublicProviderNestedInsidePublicContainingType()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                public partial class Container
                {
                    [SingletonDIProvide]
                    public sealed class Service
                    {
                    }
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void Root_ExternalBootstrapDoesNotRunUserProviderConstructor()
    {
        const string providerSource = """
            using System;
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class AlphaService
                {
                    static AlphaService()
                    {
                        throw new InvalidOperationException("provider constructor ran");
                    }
                }

                [SingletonDIProvide]
                public sealed class ZetaService
                {
                }
            }
            """;
        const string appSource = """
            namespace App
            {
                public static class Program
                {
                    public static bool MainExecuted;

                    public static void Main()
                    {
                        MainExecuted = true;
                    }
                }
            }
            """;

        var attributesImage = File.ReadAllBytes(typeof(SingletonDIProvideAttribute).Assembly.Location);
        var attributesReference = MetadataReference.CreateFromImage(attributesImage);
        var providerCompilation = CreateCompilation(
            "ProviderLibrary",
            providerSource,
            [],
            OutputKind.DynamicallyLinkedLibrary,
            attributesReference: attributesReference);
        var providerResult = RunGenerator(providerCompilation, compositionRoot: false);
        Assert.DoesNotContain(
            providerResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var providerImage = EmitImage(providerResult.OutputCompilation);
        var providerReference = MetadataReference.CreateFromImage(providerImage);

        var rootCompilation = CreateCompilation(
            "RootApp",
            appSource,
            [providerReference],
            OutputKind.ConsoleApplication,
            attributesReference: attributesReference);
        var rootResult = RunGenerator(rootCompilation, compositionRoot: true);
        Assert.DoesNotContain(
            rootResult.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var rootImage = EmitImage(rootResult.OutputCompilation);

        Exception? loadException = null;
        object? mainExecuted = null;
        var loadContext = new InMemoryAssemblyLoadContext(
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [typeof(SingletonDIProvideAttribute).Assembly.FullName!] = attributesImage,
                [providerResult.OutputCompilation.Assembly.Identity.ToString()] = providerImage
            });
        try
        {
            using var providerStream = new MemoryStream(providerImage);
            var providerAssembly = loadContext.LoadFromStream(providerStream);
            var bootstrapType = providerAssembly
                .GetTypes()
                .Single(type =>
                    type.Namespace == "SingletonDI.Generated" &&
                    type.Name.StartsWith(
                        "__SingletonDIProviderModule__",
                        StringComparison.Ordinal));
            var bootstrap = bootstrapType
                .GetMethod("Bootstrap", BindingFlags.Public | BindingFlags.Static)!;
            bootstrap.Invoke(null, null);
            bootstrap.Invoke(null, null);
            using var rootStream = new MemoryStream(rootImage);
            var rootAssembly = loadContext.LoadFromStream(rootStream);
            var programType = rootAssembly.GetType("App.Program", throwOnError: true)!;
            var main = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!;
            main.Invoke(null, null);
            mainExecuted = programType
                .GetField("MainExecuted", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null);
        }
        catch (Exception exception)
        {
            loadException = exception;
        }
        finally
        {
            loadContext.Unload();
        }

        Assert.Null(loadException);
        Assert.Equal(true, mainExecuted);
    }

    [Fact]
    public void Root_ImportsProviderAndConsumerReachableTransitively()
    {
        var result = RunTransitiveComposition();
        var consumerDependencies =
            ReferencedConsumerCollector.CollectReferencedConsumerDependencyIdentities(
                result.OutputCompilation,
                CancellationToken.None);

        Assert.Contains(
            consumerDependencies,
            dependencies => dependencies.Any(
                dependency => dependency.FullyQualifiedName == "global::Shared.Contracts.IDatabaseService"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0018");
    }

    [Fact]
    public void Root_EmitsOneModuleForLocalAndReferencedProviders()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        const string appSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class LocalService
                {
                }
            }
            """;

        var result = RunComposition(providerSource, appSource, compositionRoot: true);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, Count(result.GeneratedSources, "internal static void Initialize()"));
        Assert.Equal(
            1,
            Count(
                result.GeneratedSources,
                "global::SingletonDI.Generated.__SingletonDIProviderModule__"));
        Assert.Contains("RegisterProvider<global::App.LocalService, global::App.LocalService>", result.GeneratedSources);
    }

    [Fact]
    public void Root_IgnoresNonPublicReferencedProvider()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                internal sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0020");
        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void Root_ReportsContractWithMultipleProviders()
    {
        const string providerSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace Provider
            {
                [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
                public sealed class FirstService : IDatabaseService
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
                public sealed class SecondService : IDatabaseService
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "DM0019");
        Assert.Equal(Location.None, diagnostic.Location);
        Assert.Contains("ProviderLibrary", diagnostic.GetMessage());
        Assert.Contains("Version=", diagnostic.GetMessage());
    }

    [Fact]
    public void Root_ReportsSameFqnLocalAndExternalProvidersAsConflict()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;
        const string appSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(providerSource, appSource, compositionRoot: true);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "DM0019");
        Assert.Contains("App.Service", diagnostic.GetMessage());
        Assert.Contains("ProviderLibrary", diagnostic.GetMessage());
        Assert.Contains("RootApp", diagnostic.GetMessage());
        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("RegisterProvider<global::App.Service", result.GeneratedSources);
    }

    [Fact]
    public void LocalProviders_ReportEveryServiceTypeConflictWithAssemblyQualifiedIdentities()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IFirstService { }
                public interface ISecondService { }

                [SingletonDIProvide(ServiceType = typeof(IFirstService))]
                public sealed class FirstProvider : IFirstService { }

                [SingletonDIProvide(ServiceType = typeof(IFirstService))]
                public sealed class SecondProvider : IFirstService { }

                [SingletonDIProvide(ServiceType = typeof(ISecondService))]
                public sealed class ThirdProvider : ISecondService { }

                [SingletonDIProvide(ServiceType = typeof(ISecondService))]
                public sealed class FourthProvider : ISecondService { }
            }
            """;

        var result = RunSingleCompilation(
            source,
            compositionRoot: false,
            OutputKind.DynamicallyLinkedLibrary);
        var conflicts = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == "DM0019")
            .ToList();

        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, conflict =>
        {
            Assert.Contains("Version=", conflict.GetMessage());
            Assert.Contains("SingleCompilation", conflict.GetMessage());
        });
        Assert.DoesNotContain("RegisterProvider<", result.GeneratedSources);
    }

    [Fact]
    public void Root_AllowsUnrelatedExternalProvidersWithTheSameCustomName()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide("SharedName")]
                public sealed class FirstService
                {
                }
            }
            """;

        const string secondProviderSource = """
            using SingletonDI.Attributes;

            namespace OtherProvider
            {
                [SingletonDIProvide("SharedName")]
                public sealed class SecondService
                {
                }
            }
            """;

        const string appSource = """
            using Provider;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(FirstService))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            appSource,
            compositionRoot: true,
            secondProviderSource: secondProviderSource);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            2,
            Count(
                result.GeneratedSources,
                "global::SingletonDI.Generated.__SingletonDIProviderModule__"));
        Assert.Contains("protected static global::Provider.FirstService SharedName", result.GeneratedSources);
    }

    [Fact]
    public void Root_AllowsSameCustomNameAcrossDifferentConsumerDependencySets()
    {
        const string firstProviderSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide("SharedName")]
                public sealed class FirstService
                {
                }
            }
            """;

        const string secondProviderSource = """
            using SingletonDI.Attributes;

            namespace OtherProvider
            {
                [SingletonDIProvide("SharedName")]
                public sealed class SecondService
                {
                }
            }
            """;

        const string appSource = """
            using OtherProvider;
            using Provider;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(FirstService))]
                public partial class FirstConsumer
                {
                    public FirstService GetService() => SharedName;
                }

                [SingletonDIConsume(typeof(SecondService))]
                public partial class SecondConsumer
                {
                    public SecondService GetService() => SharedName;
                }
            }
            """;

        var result = RunComposition(
            firstProviderSource,
            appSource,
            compositionRoot: true,
            secondProviderSource: secondProviderSource);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Root_ReportsOneMissingDependencyForProviderAndConsumerRequests()
    {
        const string contractsSource = """
            namespace Shared.Contracts
            {
                public interface IMissingService { }
            }
            """;

        const string providerSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace Provider
            {
                [SingletonDIProvide]
                [SingletonDIConsume(typeof(IMissingService))]
                public sealed class Service
                {
                }
            }
            """;

        const string appSource = """
            using Shared.Contracts;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIConsume(typeof(IMissingService))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            appSource,
            compositionRoot: true,
            contractsSource: contractsSource);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0018");
        Assert.Contains("IMissingService", diagnostic.GetMessage());
    }

    [Fact]
    public void Root_DeduplicatesAProviderAssemblyReachedThroughTwoReferences()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            duplicateProviderReference: true);

        Assert.Equal(
            1,
            Count(
                result.GeneratedSources,
                "global::SingletonDI.Generated.__SingletonDIProviderModule__"));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0019");
    }

    [Fact]
    public void Root_InvalidExternalAssemblyDoesNotSuppressValidBootstrap()
    {
        const string validProviderSource = """
            using SingletonDI.Attributes;

            namespace ValidProvider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;
        const string invalidProviderSource = """
            using SingletonDI.Attributes;

            namespace InvalidProvider
            {
                [SingletonDIProvide(ServiceType = typeof(string))]
                public sealed class InvalidService
                {
                }
            }
            """;

        var result = RunComposition(
            validProviderSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            secondProviderSource: invalidProviderSource);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0016");
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
        Assert.DoesNotContain("InvalidProvider.InvalidService", result.GeneratedSources);
    }

    [Fact]
    public void Root_InvalidLocalProviderDoesNotSuppressValidExternalBootstrap()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;
        const string appSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(string))]
                public sealed class InvalidService
                {
                }
            }
            """;

        var result = RunComposition(providerSource, appSource, compositionRoot: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0016");
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void DM0017_DoesNotClassifyInvalidLocalProviderAsExternal()
    {
        var result = RunSingleCompilation(
            """
            using SingletonDI.Attributes;

            public sealed class InvalidService
            {
            }

            [SingletonDIProvide(ServiceType = typeof(string))]
            public sealed class AttributedInvalidService
            {
            }

            [SingletonDIConsume(typeof(AttributedInvalidService))]
            public partial class Consumer
            {
                public static void Main()
                {
                }
            }
            """,
            compositionRoot: false,
            OutputKind.ConsoleApplication);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0016");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void Root_ReportsReferencedConsumerDependencyWithoutProvider()
    {
        const string consumerSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace ConsumerLibrary
            {
                [SingletonDIConsume(typeof(IDatabaseService))]
                public partial class Repository
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource: null,
            consumerSource,
            compositionRoot: true);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "DM0018");
        Assert.Contains("Shared.Contracts", diagnostic.GetMessage());
        Assert.Contains("Version=", diagnostic.GetMessage());
    }

    [Fact]
    public void Root_ResolvesConstructedGenericContractIdentity()
    {
        const string contractsSource = """
            namespace Shared.Contracts
            {
                public interface IBox<out T> { }
                public sealed class Payload { }
            }
            """;
        const string providerSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace Provider
            {
                [SingletonDIProvide(ServiceType = typeof(IBox<Payload>))]
                public sealed class BoxProvider : IBox<Payload> { }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            contractsSource: contractsSource);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void Root_ReportsMissingConstructedGenericContractIdentity()
    {
        const string contractsSource = """
            namespace Shared.Contracts
            {
                public interface IBox<out T> { }
                public sealed class Payload { }
            }
            """;
        const string consumerSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace ConsumerLibrary
            {
                [SingletonDIConsume(typeof(IBox<Payload>))]
                public partial class Repository { }
            }
            """;

        var result = RunComposition(
            providerSource: null,
            consumerSource,
            compositionRoot: true,
            contractsSource: contractsSource);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "DM0018");
        Assert.Contains("Shared.Contracts.IBox", diagnostic.GetMessage());
        Assert.Contains("Version=", diagnostic.GetMessage());
    }

    [Fact]
    public void Root_StillEmitsConsumerSourceWhenRootGraphIsInvalid()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;
        const string appSource = """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IMissingService
                {
                }

                [SingletonDIConsume(typeof(IMissingService))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = RunComposition(providerSource, appSource, compositionRoot: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0018");
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.IMissingService>()",
            result.GeneratedSources);
        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void Root_ReportsCrossAssemblyProviderCycleWithAssemblyQualifiedNames()
    {
        const string contractsSource = """
            namespace Shared.Contracts
            {
                public interface IFirstService
                {
                }

                public interface ISecondService
                {
                }
            }
            """;
        const string firstProviderSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace FirstProvider
            {
                [SingletonDIProvide(ServiceType = typeof(ISecondService))]
                [SingletonDIConsume(typeof(IFirstService))]
                public sealed class FirstService : ISecondService
                {
                }
            }
            """;
        const string secondProviderSource = """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace SecondProvider
            {
                [SingletonDIProvide(ServiceType = typeof(IFirstService))]
                [SingletonDIConsume(typeof(ISecondService))]
                public sealed class SecondService : IFirstService
                {
                }
            }
            """;

        var result = RunComposition(
            firstProviderSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            contractsSource: contractsSource,
            secondProviderSource: secondProviderSource);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Id == "DM0009");
        Assert.Contains("FirstProvider", diagnostic.GetMessage());
        Assert.Contains("SecondProvider", diagnostic.GetMessage());
        Assert.Contains("Version=", diagnostic.GetMessage());
    }

    [Fact]
    public void DM0016_ReportsInvalidServiceType()
    {
        var result = RunSingleCompilation(
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(string))]
                public sealed class InvalidService
                {
                }
            }
            """,
            compositionRoot: false);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void DM0017_IsNotReportedForLibraryConsumer()
    {
        var result = RunSingleCompilation(
            ConsumerWithContractSource,
            compositionRoot: false,
            OutputKind.DynamicallyLinkedLibrary);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void DM0017_IsReportedForExecutableConsumer()
    {
        var result = RunSingleCompilation(
            ConsumerWithContractSource,
            compositionRoot: false,
            OutputKind.ConsoleApplication);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0017");
        Assert.Equal(
            "Composition root is required for an unmapped consumer dependency",
            diagnostic.Descriptor.Title);
        Assert.Contains("not mapped by a local singleton provider", diagnostic.GetMessage());
        Assert.Contains("SingletonDICompositionRoot=true", diagnostic.GetMessage());
    }

    [Fact]
    public void ExecutableWithoutRootValidatesReferencedConsumers()
    {
        const string contractsSource = """
            namespace Shared.Contracts
            {
                public interface ISatisfiedService
                {
                }

                public interface IMissingService
                {
                }
            }
            """;
        const string providerSource = """
            using Shared.Contracts;
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide(ServiceType = typeof(ISatisfiedService))]
                public sealed class SatisfiedService : ISatisfiedService
                {
                }
            }
            """;
        const string consumerSource = """
            using Shared.Contracts;
            using SingletonDI.Attributes;

            namespace ConsumerLibrary
            {
                [SingletonDIConsume(typeof(ISatisfiedService))]
                public partial class SatisfiedConsumer
                {
                }

                [SingletonDIConsume(typeof(IMissingService))]
                public partial class MissingConsumer
                {
                }
            }
            """;

        var contractsCompilation = CreateCompilation(
            "Shared.Contracts",
            contractsSource,
            [],
            OutputKind.DynamicallyLinkedLibrary);
        var contractsImage = EmitImage(contractsCompilation);
        var contractsReference = MetadataReference.CreateFromImage(contractsImage);

        var providerCompilation = CreateCompilation(
            "ProviderLibrary",
            providerSource,
            [contractsReference],
            OutputKind.DynamicallyLinkedLibrary);
        var providerResult = RunGenerator(providerCompilation, compositionRoot: false);
        Assert.DoesNotContain(providerResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var providerReference = MetadataReference.CreateFromImage(EmitImage(providerResult.OutputCompilation));

        var consumerCompilation = CreateCompilation(
            "ConsumerLibrary",
            consumerSource,
            [contractsReference],
            OutputKind.DynamicallyLinkedLibrary);
        var consumerResult = RunGenerator(consumerCompilation, compositionRoot: false);
        Assert.DoesNotContain(consumerResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var consumerReference = MetadataReference.CreateFromImage(EmitImage(consumerResult.OutputCompilation));

        var executableCompilation = CreateCompilation(
            "ExecutableWithoutRoot",
            "namespace App { public static class Program { public static void Main() { } } }",
            [contractsReference, providerReference, consumerReference],
            OutputKind.ConsoleApplication);
        var result = RunGenerator(
            executableCompilation,
            compositionRoot: false,
            OutputKind.ConsoleApplication);

        var missing = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "DM0018");
        Assert.Contains("IMissingService", missing.GetMessage());
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0017");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var libraryCompilation = CreateCompilation(
            "LibraryWithoutRoot",
            "namespace App { public sealed class LibraryMarker { } }",
            [contractsReference, providerReference, consumerReference],
            OutputKind.DynamicallyLinkedLibrary);
        var libraryResult = RunGenerator(
            libraryCompilation,
            compositionRoot: false,
            OutputKind.DynamicallyLinkedLibrary);

        Assert.DoesNotContain(libraryResult.Diagnostics, diagnostic => diagnostic.Id == "DM0018");
    }

    [Fact]
    public void DM0021_ReportsMarkedProviderAssemblyWithoutBootstrap()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            [assembly: SingletonDIProviderModule]
            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            generateProviderModule: false);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0021");
        Assert.Contains("ProviderLibrary", diagnostic.GetMessage());
        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    [Fact]
    public void DM0021_RejectsGenericBootstrapOnlyModule()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            [assembly: SingletonDIProviderModule]
            namespace SingletonDI.Generated
            {
                public static class __SingletonDIProviderModule__
                {
                    public static void Bootstrap<T>()
                    {
                    }
                }
            }

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            generateProviderModule: false);

        Assert.Contains(result.Diagnostics, item => item.Id == "DM0021");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DM0020_ReportsReferencedProviderAssemblyWithoutMarker()
    {
        const string providerSource = """
            using SingletonDI.Attributes;

            namespace Provider
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """;

        var result = RunComposition(
            providerSource,
            "namespace App { public sealed class AppMarker { } }",
            compositionRoot: true,
            generateProviderModule: false);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0020");
        Assert.DoesNotContain(
            "global::SingletonDI.Generated.__SingletonDIProviderModule__",
            result.GeneratedSources);
    }

    private const string ConsumerWithContractSource = """
        using SingletonDI.Attributes;

        public interface IExternalService
        {
        }

        [SingletonDIConsume(typeof(IExternalService))]
        public partial class Consumer
        {
            public static void Main()
            {
            }
        }
        """;

    private static CompositionRunResult RunComposition(
        string? providerSource,
        string appSource,
        bool compositionRoot,
        string? consumerSource = null,
        string? contractsSource = null,
        string? secondProviderSource = null,
        bool generateProviderModule = true,
        bool duplicateProviderReference = false)
    {
        var contractsCompilation = CreateCompilation(
            "Shared.Contracts",
            contractsSource ?? ContractsSource,
            [],
            OutputKind.DynamicallyLinkedLibrary);
        var contractsImage = EmitImage(contractsCompilation);
        var contractsReference = MetadataReference.CreateFromImage(contractsImage);

        var providerReferences = new List<MetadataReference> { contractsReference };
        MetadataReference? providerReference = null;
        byte[]? providerImage = null;
        if (providerSource is not null)
        {
            var providerCompilation = CreateCompilation(
                "ProviderLibrary",
                providerSource,
                providerReferences,
                OutputKind.DynamicallyLinkedLibrary);
            var providerResult = generateProviderModule
                ? RunGenerator(providerCompilation, compositionRoot: false)
                : new CompositionRunResult(
                    ImmutableArray<Diagnostic>.Empty,
                    string.Empty,
                    providerCompilation);
            providerImage = EmitImage(providerResult.OutputCompilation);
            providerReference = MetadataReference.CreateFromImage(providerImage);
            providerReferences.Add(providerReference);
        }

        if (secondProviderSource is not null)
        {
            var secondCompilation = CreateCompilation(
                "SecondProviderLibrary",
                secondProviderSource,
                [contractsReference],
                OutputKind.DynamicallyLinkedLibrary);
            var secondResult = RunGenerator(secondCompilation, compositionRoot: false);
            providerReferences.Add(MetadataReference.CreateFromImage(EmitImage(secondResult.OutputCompilation)));
        }

        MetadataReference? consumerReference = null;
        if (consumerSource is not null)
        {
            var consumerCompilation = CreateCompilation(
                "ConsumerLibrary",
                consumerSource,
                [contractsReference],
                OutputKind.DynamicallyLinkedLibrary);
            var consumerResult = RunGenerator(consumerCompilation, compositionRoot: false);
            consumerReference = MetadataReference.CreateFromImage(EmitImage(consumerResult.OutputCompilation));
            providerReferences.Add(consumerReference);
        }

        if (duplicateProviderReference && providerImage is not null)
        {
            providerReferences.Add(MetadataReference.CreateFromImage(providerImage));
        }

        return RunGenerator(
            CreateCompilation(
                "RootApp",
                appSource,
                providerReferences,
                OutputKind.DynamicallyLinkedLibrary),
            compositionRoot);
    }

    private static CompositionRunResult RunTransitiveComposition()
    {
        var contractsCompilation = CreateCompilation(
            "Shared.Contracts",
            ContractsSource,
            [],
            OutputKind.DynamicallyLinkedLibrary);
        var contractsImage = EmitImage(contractsCompilation);
        var contractsReference = MetadataReference.CreateFromImage(contractsImage);

        var providerCompilation = CreateCompilation(
            "TransitiveProvider",
            """
            using SingletonDI.Attributes;
            using Shared.Contracts;

            namespace Provider
            {
                [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
                public sealed class Service : IDatabaseService
                {
                }
            }
            """,
            [contractsReference],
            OutputKind.DynamicallyLinkedLibrary);
        var providerResult = RunGenerator(providerCompilation, compositionRoot: false);
        var providerImage = EmitImage(providerResult.OutputCompilation);
        var providerReference = MetadataReference.CreateFromImage(providerImage);

        var consumerCompilation = CreateCompilation(
            "TransitiveConsumer",
            """
            using SingletonDI.Attributes;
            using Shared.Contracts;
            using Provider;

            namespace ConsumerLibrary
            {
                public sealed class ProviderReference
                {
                    public Provider.Service? Value { get; }
                }

                [SingletonDIConsume(typeof(IDatabaseService))]
                public partial class Repository
                {
                }
            }
            """,
            [contractsReference, providerReference],
            OutputKind.DynamicallyLinkedLibrary);
        var consumerResult = RunGenerator(consumerCompilation, compositionRoot: false);
        var consumerImage = EmitImage(consumerResult.OutputCompilation);
        var consumerReference = MetadataReference.CreateFromImage(consumerImage);

        var wrapperCompilation = CreateCompilation(
            "TransitiveWrapper",
            """
            namespace Wrapper
            {
                public sealed class ConsumerReference
                {
                    public ConsumerLibrary.Repository? Value { get; }
                }
            }
            """,
            [consumerReference],
            OutputKind.DynamicallyLinkedLibrary);
        var wrapperImage = EmitImage(wrapperCompilation);
        var wrapperReference = MetadataReference.CreateFromImage(wrapperImage);
        var resolver = new InMemoryMetadataReferenceResolver(
            new Dictionary<string, PortableExecutableReference>(StringComparer.Ordinal)
            {
                [contractsCompilation.Assembly.Identity.ToString()] = contractsReference,
                [providerCompilation.Assembly.Identity.ToString()] = providerReference,
                [consumerCompilation.Assembly.Identity.ToString()] = consumerReference,
            });

        var appCompilation = CreateCompilation(
            "TransitiveRootApp",
            "namespace App { public sealed class AppMarker { } }",
            [wrapperReference],
            OutputKind.DynamicallyLinkedLibrary,
            resolver);

        return RunGenerator(appCompilation, compositionRoot: true);
    }

    [Fact]
    public void AliasedProviderServiceTypeIsRejected()
    {
        var contractsCompilation = CSharpCompilation.Create(
            "AliasContracts",
            [CSharpSyntaxTree.ParseText(
                "namespace AliasContracts { public interface IAliasService { } }",
                ParseOptions)],
            CreateReferences([], null),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var contractReference = MetadataReference.CreateFromImage(
            EmitImage(contractsCompilation),
            MetadataReferenceProperties.Assembly.WithAliases(new[] { "AliasContract" }));
        var source = """
            extern alias AliasContract;

            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(AliasContract::AliasContracts.IAliasService))]
                public sealed class AliasProvider : AliasContract::AliasContracts.IAliasService
                {
                }
            }
            """;
        var compilation = CreateCompilation(
            "AliasProviderApp",
            source,
            [contractReference],
            OutputKind.DynamicallyLinkedLibrary);
        var result = RunGenerator(compilation, compositionRoot: false);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0030");

        Assert.Equal("Aliased service type is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("IAliasService", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id is "CS0400" or "CS0311");
    }

    [Fact]
    public void AliasedConsumerDependencyIsRejected()
    {
        var contractsCompilation = CSharpCompilation.Create(
            "AliasContracts",
            [CSharpSyntaxTree.ParseText(
                "namespace AliasContracts { public interface IAliasService { } }",
                ParseOptions)],
            CreateReferences([], null),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var contractReference = MetadataReference.CreateFromImage(
            EmitImage(contractsCompilation),
            MetadataReferenceProperties.Assembly.WithAliases(new[] { "AliasContract" }));
        var source = """
            extern alias AliasContract;

            using SingletonDI.Attributes;

            [SingletonDIConsume(typeof(AliasContract::AliasContracts.IAliasService))]
            public partial class AliasConsumer
            {
            }
            """;
        var compilation = CreateCompilation(
            "AliasConsumerApp",
            source,
            [contractReference],
            OutputKind.DynamicallyLinkedLibrary);
        var result = RunGenerator(compilation, compositionRoot: false);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0030");

        Assert.Equal("Aliased service type is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("IAliasService", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id is "CS0400" or "CS0311");
    }

    private static CompositionRunResult RunSingleCompilation(
        string source,
        bool compositionRoot,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        return RunGenerator(
            CreateCompilation("SingleCompilation", source, [], outputKind),
            compositionRoot,
            outputKind);
    }

    private static CompositionRunResult RunGenerator(
        CSharpCompilation compilation,
        bool compositionRoot,
        OutputKind? outputKind = null)
    {
        var options = new GeneratorTestOptions(
            compositionRoot,
            outputKind ?? compilation.Options.OutputKind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: ParseOptions,
            optionsProvider: new GeneratorTestAnalyzerConfigOptionsProvider(options),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true,
                baseDirectory: null));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics);
        var generatedSources = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results
                .SelectMany(result => result.GeneratedSources)
                .Select(source => source.SourceText.ToString()));

        return new CompositionRunResult(
            diagnostics,
            generatedSources,
            (CSharpCompilation)outputCompilation);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference> additionalReferences,
        OutputKind outputKind,
        MetadataReferenceResolver? metadataReferenceResolver = null,
        MetadataReference? attributesReference = null)
    {
        var options = new CSharpCompilationOptions(
            outputKind,
            nullableContextOptions: NullableContextOptions.Enable,
            metadataReferenceResolver: metadataReferenceResolver);

        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            CreateReferences(additionalReferences, attributesReference),
            options);
    }

    private static ImmutableArray<MetadataReference> CreateReferences(
        IEnumerable<MetadataReference> additionalReferences,
        MetadataReference? attributesReference)
    {
        var references = new List<MetadataReference>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAssembly(string path)
        {
            if (File.Exists(path) && paths.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        AddAssembly(typeof(object).Assembly.Location);
        AddAssembly(typeof(Task).Assembly.Location);
        AddAssembly(typeof(ValueTask).Assembly.Location);
        AddAssembly(typeof(RuntimeHelpers).Assembly.Location);
        AddAssembly(typeof(RuntimeInformation).Assembly.Location);
        if (attributesReference is null)
        {
            AddAssembly(typeof(SingletonDIProvideAttribute).Assembly.Location);
            AddAssembly(typeof(SingletonDIProviderModuleAttribute).Assembly.Location);
        }
        else
        {
            references.Add(attributesReference);
        }
        AddAssembly(typeof(IAsyncDisposable).Assembly.Location);

        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[]
                 {
                     "System.Runtime",
                     "System.Runtime.CompilerServices",
                     "System.Threading.Tasks",
                     "System.Runtime.InteropServices",
                     "netstandard",
                 })
        {
            AddAssembly(Path.Combine(runtimePath, assemblyName + ".dll"));
        }

        foreach (var reference in additionalReferences)
        {
            references.Add(reference);
        }

        return [.. references];
    }

    private static byte[] EmitImage(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            throw new InvalidOperationException($"Compilation failed:{Environment.NewLine}{diagnostics}");
        }

        return stream.ToArray();
    }

    private static int Count(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }

    private sealed class InMemoryAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, byte[]> _assemblies;

        internal InMemoryAssemblyLoadContext(IReadOnlyDictionary<string, byte[]> assemblies)
            : base(isCollectible: true)
        {
            _assemblies = assemblies;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (_assemblies.TryGetValue(assemblyName.FullName ?? assemblyName.Name!, out var image))
            {
                using var stream = new MemoryStream(image);
                return LoadFromStream(stream);
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }
    }

    private static CSharpParseOptions ParseOptions { get; } = new(
        LanguageVersion.Latest,
        preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]);

    private sealed record CompositionRunResult(
        ImmutableArray<Diagnostic> Diagnostics,
        string GeneratedSources,
        CSharpCompilation OutputCompilation);
}

internal sealed class InMemoryMetadataReferenceResolver : MetadataReferenceResolver
{
    private readonly IReadOnlyDictionary<string, PortableExecutableReference> _references;

    public InMemoryMetadataReferenceResolver(
        IReadOnlyDictionary<string, PortableExecutableReference> references)
    {
        _references = references;
    }

    public override bool ResolveMissingAssemblies => true;

    public override PortableExecutableReference? ResolveMissingAssembly(
        MetadataReference definition,
        AssemblyIdentity referenceIdentity)
    {
        return _references.TryGetValue(referenceIdentity.ToString(), out var reference)
            ? reference
            : null;
    }

    public override ImmutableArray<PortableExecutableReference> ResolveReference(
        string reference,
        string? baseFilePath,
        MetadataReferenceProperties properties)
    {
        return _references.TryGetValue(reference, out var resolved)
            ? [resolved]
            : ImmutableArray<PortableExecutableReference>.Empty;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }
}

internal sealed class GeneratorTestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly GeneratorTestAnalyzerConfigOptions _options;

    public GeneratorTestAnalyzerConfigOptionsProvider(GeneratorTestOptions options)
    {
        _options = new GeneratorTestAnalyzerConfigOptions(options);
    }

    public override AnalyzerConfigOptions GlobalOptions => _options;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

    public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) => _options;
}

internal readonly record struct GeneratorTestOptions(
    bool IsCompositionRoot,
    OutputKind OutputKind,
    bool IncludeOutputType = true);

internal sealed class GeneratorTestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _values;

    public GeneratorTestAnalyzerConfigOptions(GeneratorTestOptions options)
    {
        _values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.SingletonDICompositionRoot"] = options.IsCompositionRoot ? "true" : "false",
        };
        if (options.IncludeOutputType)
        {
            _values["build_property.OutputType"] = options.OutputKind switch
            {
                OutputKind.ConsoleApplication => "Exe",
                OutputKind.WindowsApplication => "WinExe",
                _ => "Library",
            };
        }
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _values.TryGetValue(key, out value!);
    }
}
