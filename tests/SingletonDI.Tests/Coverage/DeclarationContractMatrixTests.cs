using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed partial class DeclarationContractMatrixTests
{
    private const string Registration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Service, global::App.Service>(";

    private const string RepoProperty = "protected static global::App.Service Repo";

    private const string LongName = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    public static IEnumerable<object[]> AttributeFormCases =>
        AttributeFormRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(AttributeFormCases))]
    public void AttributeForm(string id)
    {
        DeclarationCaseVerifier.Verify(AttributeFormRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] AttributeFormRows =
    [
        new(
            "ATTR-01",
            "ATTRIBUTE_FORM",
            "attribute applied through a using directive",
            """
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
            [Registration],
            []),
        new(
            "ATTR-02",
            "ATTRIBUTE_FORM",
            "attribute applied by its fully qualified name",
            """
            namespace App
            {
                [SingletonDI.Attributes.SingletonDIProvide]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [Registration],
            []),
        new(
            "ATTR-03",
            "ATTRIBUTE_FORM",
            "attribute applied by its global-qualified name",
            """
            namespace App
            {
                [global::SingletonDI.Attributes.SingletonDIProvide]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [Registration],
            []),
        new(
            "ATTR-04",
            "ATTRIBUTE_FORM",
            "attribute applied through a using alias",
            """
            using ProvideAttribute = SingletonDI.Attributes.SingletonDIProvideAttribute;

            namespace App
            {
                [ProvideAttribute]
                public class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [Registration],
            []),
        new(
            "ATTR-05",
            "ATTRIBUTE_FORM",
            "attribute on the second partial declaration",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public partial class Service
                {
                }

                [SingletonDIProvide]
                public partial class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [Registration],
            []),
        new(
            "ATTR-06",
            "ATTRIBUTE_FORM",
            "consume attribute with several type arguments",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class First
                {
                }

                [SingletonDIProvide]
                public class Second
                {
                }

                [SingletonDIConsume(typeof(First), typeof(Second))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.First FirstInstance"],
            []),
        new(
            "ATTR-07",
            "ATTRIBUTE_FORM",
            "consume attribute applied several times",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class First
                {
                }

                [SingletonDIProvide]
                public class Second
                {
                }

                [SingletonDIConsume(typeof(First))]
                [SingletonDIConsume(typeof(Second))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            [
                "protected static global::App.First FirstInstance",
                "protected static global::App.Second SecondInstance",
            ],
            []),
        new(
            "ATTR-08",
            "ATTRIBUTE_FORM",
            "the same consume type in two attributes",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0010"]),
            [],
            []),
    ];

    public static IEnumerable<object[]> PropertyNameCases =>
        PropertyNameRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(PropertyNameCases))]
    public void PropertyName(string id)
    {
        DeclarationCaseVerifier.Verify(PropertyNameRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] PropertyNameRows =
    [
        new(
            "PNAME-01",
            "PROPERTY_NAME",
            "positional constructor argument",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Repo")]
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
            [RepoProperty],
            []),
        new(
            "PNAME-02",
            "PROPERTY_NAME",
            "constructor argument named by its parameter name",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(propertyName: "Repo")]
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
            [RepoProperty],
            []),
        new(
            "PNAME-04",
            "PROPERTY_NAME",
            "empty string",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("")]
                public class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0013"]),
            [],
            []),
        new(
            "PNAME-05",
            "PROPERTY_NAME",
            "reserved keyword",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("class")]
                public class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0014"]),
            [],
            []),
        new(
            "PNAME-06",
            "PROPERTY_NAME",
            "not an identifier",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("1Repo")]
                public class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0013"]),
            [],
            []),
        new(
            "PNAME-07",
            "PROPERTY_NAME",
            "escaped keyword as the name",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("@class")]
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
            ["protected static global::App.Service @class"],
            []),
        new(
            "PNAME-08",
            "PROPERTY_NAME",
            "non-ASCII name",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Репозиторий")]
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
            ["protected static global::App.Service Репозиторий"],
            []),
        new(
            "PNAME-09",
            "PROPERTY_NAME",
            "name collides with an existing consumer member",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Repo")]
                public class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                    protected static global::App.Service Repo => null!;
                }
            }
            """,
            new RejectedExpectation(["DM0025"]),
            [],
            [RepoProperty]),
        new(
            "PNAME-10",
            "PROPERTY_NAME",
            "two providers share a name inside one consumer",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Shared")]
                public class First
                {
                }

                [SingletonDIProvide("Shared")]
                public class Second
                {
                }

                [SingletonDIConsume(typeof(First), typeof(Second))]
                public partial class Consumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0001", "DM0003"]),
            [],
            []),
        new(
            "PNAME-11",
            "PROPERTY_NAME",
            "two providers share a name with no consumer",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Shared")]
                public class First
                {
                }

                [SingletonDIProvide("Shared")]
                public class Second
                {
                }
            }
            """,
            new RejectedExpectation(["DM0001"]),
            [],
            []),
        new(
            "PNAME-12",
            "PROPERTY_NAME",
            "same name on two providers in different consumer sets",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Shared")]
                public class First
                {
                }

                [SingletonDIProvide("Shared")]
                public class Second
                {
                }

                [SingletonDIConsume(typeof(First))]
                public partial class FirstConsumer
                {
                }

                [SingletonDIConsume(typeof(Second))]
                public partial class SecondConsumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0001"]),
            [],
            []),
        new(
            "PNAME-13",
            "PROPERTY_NAME",
            "name equal to the provider type name",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("Service")]
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
            ["protected static global::App.Service Service"],
            []),
        new(
            "PNAME-14",
            "PROPERTY_NAME",
            "name of 204 characters",
            $$"""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("{{LongName}}")]
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
            ["protected static global::App.Service " + LongName],
            []),
        new(
            "PNAME-15",
            "PROPERTY_NAME",
            "rejected name next to a consumer, the consumer falls back to the default name",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide("1Repo")]
                public class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0013"]),
            ["protected static global::App.Service ServiceInstance"],
            []),
    ];
}
