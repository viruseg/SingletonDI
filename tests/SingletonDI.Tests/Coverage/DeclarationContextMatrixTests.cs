using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed partial class DeclarationContextMatrixTests
{
    private const string ServiceFactory = "() => new global::App.Service(),";

    private const string ConsumerProperty = "protected static global::App.Service ServiceInstance";

    public static IEnumerable<object[]> NullableCases =>
        NullableRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(NullableCases))]
    public void Nullable(string id)
    {
        DeclarationCaseVerifier.Verify(NullableRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] NullableRows =
    [
        new(
            "NUL-01",
            "NULLABILITY",
            "provider inside #nullable enable",
            """
            #nullable enable

            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "NUL-02",
            "NULLABILITY",
            "consumer inside #nullable enable",
            """
            #nullable enable

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
            """,
            new SupportedExpectation(),
            [ConsumerProperty],
            []),
        new(
            "NUL-03",
            "NULLABILITY",
            "nullable member on the provider with a consumer, both annotated",
            """
            #nullable enable

            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public string? Name { get; set; }
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ConsumerProperty],
            []),
        new(
            "NUL-04",
            "NULLABILITY",
            "provider and consumer inside #nullable disable",
            """
            #nullable disable

            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public string Name { get; set; }
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ConsumerProperty],
            []),
        new(
            "NUL-05",
            "NULLABILITY",
            "required nullable member with SetsRequiredMembers",
            """
            #nullable enable

            using System.Diagnostics.CodeAnalysis;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    [SetsRequiredMembers]
                    public Service()
                    {
                    }

                    public required string? Name { get; set; }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "NUL-06",
            "NULLABILITY",
            "directive inside the namespace body rather than before it",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                #nullable enable

                [SingletonDIProvide]
                public class Service
                {
                    public string? Name { get; set; }
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ConsumerProperty],
            []),
    ];

    public static IEnumerable<object[]> LanguageVersionCases =>
        LanguageVersionRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(LanguageVersionCases))]
    public void LanguageVersion(string id)
    {
        DeclarationCaseVerifier.Verify(LanguageVersionRows.Single(row => row.Id == id));
    }

    private const string FileScopedSource = """
        using SingletonDI.Attributes;

        namespace App;

        [SingletonDIProvide]
        public class Service
        {
        }

        [SingletonDIConsume(typeof(Service))]
        public partial class Consumer
        {
        }
        """;

    private const string BlockScopedSource = """
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

    private const string ProviderOnlySource = """
        using SingletonDI.Attributes;

        namespace App
        {
            [SingletonDIProvide]
            public class Service
            {
            }
        }
        """;

    internal static readonly DeclarationCase[] LanguageVersionRows =
    [
        new(
            "LANG-01",
            "LANGUAGE_VERSION",
            "C# 9 with a block namespace",
            BlockScopedSource,
            new SupportedExpectation(),
            [ConsumerProperty],
            [],
            LanguageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp9),
        new(
            "LANG-03",
            "LANGUAGE_VERSION",
            "C# 10 with a file-scoped namespace",
            FileScopedSource,
            new SupportedExpectation(),
            [ConsumerProperty],
            [],
            LanguageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp10),
        new(
            "LANG-04",
            "LANGUAGE_VERSION",
            "C# 7.3",
            ProviderOnlySource,
            new RejectedExpectation(["DM0027"]),
            [],
            [ServiceFactory],
            LanguageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7_3,
            NullableContextProviderEnabled: false),
        new(
            "LANG-05",
            "LANGUAGE_VERSION",
            "C# 8",
            ProviderOnlySource,
            new RejectedExpectation(["DM0027"]),
            [],
            [ServiceFactory],
            LanguageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp8),
        new(
            "LANG-06",
            "LANGUAGE_VERSION",
            "latest",
            BlockScopedSource,
            new SupportedExpectation(),
            [ConsumerProperty],
            []),
    ];

    public static IEnumerable<object[]> ServiceTypeCases =>
        ServiceTypeRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(ServiceTypeCases))]
    public void ServiceType(string id)
    {
        DeclarationCaseVerifier.Verify(ServiceTypeRows.Single(row => row.Id == id));
    }

    private const string ContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.IContract, global::App.Service>(";

    private const string SelfRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Service, global::App.Service>(";

    private const string ClassContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Contract, global::App.Service>(";

    private const string ObjectContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::System.Object, global::App.Service>(";

    private const string GenericContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.IRepository<global::System.String>, global::App.Repository>(";

    private const string CovariantContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::System.Collections.Generic.IEnumerable<global::System.Object>, global::App.StringService>(";

    private const string ContravariantContractRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::System.Collections.Generic.IComparer<global::System.String>, global::App.Service>(";

    internal static readonly DeclarationCase[] ServiceTypeRows =
    [
        new(
            "SVC-01",
            "SERVICE_TYPE",
            "interface as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class Service : IContract
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ContractRegistration],
            []),
        new(
            "SVC-02",
            "SERVICE_TYPE",
            "abstract class as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public abstract class Contract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(Contract))]
                public class Service : Contract
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ClassContractRegistration],
            []),
        new(
            "SVC-03",
            "SERVICE_TYPE",
            "object as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(object))]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ObjectContractRegistration],
            []),
        new(
            "SVC-04",
            "SERVICE_TYPE",
            "the provider type itself as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(Service))]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [SelfRegistration],
            []),
        new(
            "SVC-05",
            "SERVICE_TYPE",
            "base class of the provider as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Contract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(Contract))]
                public class Service : Contract
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ClassContractRegistration],
            []),
        new(
            "SVC-06",
            "SERVICE_TYPE",
            "constructed generic contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IRepository<TEntity>
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IRepository<string>))]
                public class Repository : IRepository<string>
                {
                }
            }
            """,
            new SupportedExpectation(),
            [GenericContractRegistration],
            []),
        new(
            "SVC-07",
            "SERVICE_TYPE",
            "covariant variance through a narrower type argument",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(IEnumerable<object>))]
                public class StringService : IEnumerable<string>
                {
                    public IEnumerator<string> GetEnumerator() =>
                        throw new NotSupportedException();

                    IEnumerator IEnumerable.GetEnumerator() =>
                        throw new NotSupportedException();
                }
            }
            """,
            new SupportedExpectation(),
            [CovariantContractRegistration],
            []),
        new(
            "SVC-08",
            "SERVICE_TYPE",
            "contravariant variance",
            """
            using System.Collections.Generic;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(IComparer<string>))]
                public class Service : IComparer<object>
                {
                    public int Compare(object? left, object? right) => 0;
                }
            }
            """,
            new SupportedExpectation(),
            [ContravariantContractRegistration],
            []),
        new(
            "SVC-09",
            "SERVICE_TYPE",
            "internal interface in the same assembly",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                internal interface IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                internal class Service : IContract
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ContractRegistration],
            []),
        new(
            "SVC-10",
            "SERVICE_TYPE",
            "private interface",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    private interface IContract
                    {
                    }

                    [SingletonDIProvide(ServiceType = typeof(IContract))]
                    public class Service : IContract
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            []),
        new(
            "SVC-11",
            "SERVICE_TYPE",
            "explicit null ServiceType",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = null)]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [SelfRegistration],
            []),
        new(
            "SVC-12",
            "SERVICE_TYPE",
            "value type as the service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(int))]
                public class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0016"]),
            [],
            []),
        new(
            "SVC-13",
            "SERVICE_TYPE",
            "interface the provider does not implement",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0016"]),
            [],
            []),
        new(
            "SVC-14",
            "SERVICE_TYPE",
            "open generic service contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract<T>
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract<>))]
                public class Service : IContract<int>
                {
                }
            }
            """,
            new RejectedExpectation(["DM0024"]),
            [],
            []),
        new(
            "SVC-15",
            "SERVICE_TYPE",
            "two providers for one contract",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class First : IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public class Second : IContract
                {
                }
            }
            """,
            new RejectedExpectation(["DM0019"]),
            [],
            []),
        new(
            "SVC-16",
            "SERVICE_TYPE",
            "interface the provider does not implement, sealed provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                public sealed class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0016"]),
            [],
            []),
    ];

    public static IEnumerable<object[]> ConditionalCompilationCases =>
        ConditionalCompilationRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(ConditionalCompilationCases))]
    public void ConditionalCompilation(string id)
    {
        DeclarationCaseVerifier.Verify(ConditionalCompilationRows.Single(row => row.Id == id));
    }

    private const string ConditionalConsumerSource = """
        using SingletonDI.Attributes;

        namespace App
        {
            [SingletonDIProvide]
            public class Service
            {
            }

            #if FEATURE
            [SingletonDIConsume(typeof(Service))]
            public partial class Consumer
            {
            }
            #endif
        }
        """;

    internal static readonly DeclarationCase[] ConditionalCompilationRows =
    [
        new(
            "COND-01",
            "CONDITIONAL_COMPILATION",
            "provider inside #if with the symbol defined",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                #if FEATURE
                [SingletonDIProvide]
                public class Service
                {
                }
                #endif
            }
            """,
            new SupportedExpectation(),
            [SelfRegistration],
            [],
            PreprocessorSymbols: ["FEATURE"]),
        new(
            "COND-02",
            "CONDITIONAL_COMPILATION",
            "provider inside #if with the symbol undefined, second provider outside it",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                #if FEATURE
                [SingletonDIProvide]
                public class Conditional
                {
                }
                #endif

                [SingletonDIProvide]
                public class Always
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Always, global::App.Always>("],
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Conditional, global::App.Conditional>("]),
        new(
            "COND-03",
            "CONDITIONAL_COMPILATION",
            "consumer inside #if with the symbol undefined",
            ConditionalConsumerSource,
            new SupportedExpectation(),
            [SelfRegistration],
            [ConsumerProperty]),
        new(
            "COND-04",
            "CONDITIONAL_COMPILATION",
            "two providers, one in #if and one in #else, symbol defined",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                #if FEATURE
                [SingletonDIProvide]
                public class FeatureService
                {
                }
                #else
                [SingletonDIProvide]
                public class DefaultService
                {
                }
                #endif
            }
            """,
            new SupportedExpectation(),
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.FeatureService, global::App.FeatureService>("],
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.DefaultService, global::App.DefaultService>("],
            PreprocessorSymbols: ["FEATURE"]),
    ];
}
