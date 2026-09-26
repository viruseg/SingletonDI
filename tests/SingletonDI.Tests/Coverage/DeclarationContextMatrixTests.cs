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
}
