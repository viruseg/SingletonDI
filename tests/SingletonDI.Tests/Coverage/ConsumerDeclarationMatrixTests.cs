using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed partial class ConsumerDeclarationMatrixTests
{
    internal const string ProviderSource = """
        using SingletonDI.Attributes;

        namespace App
        {
            [SingletonDIProvide]
            public class Service
            {
            }
        }
        """;

    public static IEnumerable<object[]> ShapeCases =>
        ShapeRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(ShapeCases))]
    public void Shape(string id)
    {
        DeclarationCaseVerifier.Verify(ShapeRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] ShapeRows =
    [
        new(
            "CONS-SHP-01",
            "CONSUMER_SHAPE",
            "public unsealed class, protected property",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-02",
            "CONSUMER_SHAPE",
            "public sealed class, private property",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public sealed partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-03",
            "CONSUMER_SHAPE",
            "internal class",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                internal partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-04",
            "CONSUMER_SHAPE",
            "class nested in a public class as private",
            ProviderSource + """

            namespace App
            {
                public partial class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    private partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-05",
            "CONSUMER_SHAPE",
            "class nested in an internal class",
            ProviderSource + """

            namespace App
            {
                internal partial class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    internal partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-06",
            "CONSUMER_SHAPE",
            "static class",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public static partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-07",
            "CONSUMER_SHAPE",
            "abstract class",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public abstract partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-08",
            "CONSUMER_SHAPE",
            "struct",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial struct Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-09",
            "CONSUMER_SHAPE",
            "readonly struct",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public readonly partial struct Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-10",
            "CONSUMER_SHAPE",
            "ref struct",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public ref partial struct Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-11",
            "CONSUMER_SHAPE",
            "record",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial record Consumer;
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-12",
            "CONSUMER_SHAPE",
            "record struct",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial record struct Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-13",
            "CONSUMER_SHAPE",
            "record class spelled out",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial record class Consumer;
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-14",
            "CONSUMER_SHAPE",
            "sealed record",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public sealed partial record Consumer;
            }
            """,
            new SupportedExpectation(),
            ["private static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-15",
            "CONSUMER_SHAPE",
            "record with positional parameters",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial record Consumer(int Value);
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-16",
            "CONSUMER_SHAPE",
            "class in the global namespace",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }

            [SingletonDIConsume(typeof(App.Service))]
            public partial class Consumer
            {
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-SHP-17",
            "CONSUMER_SHAPE",
            "class with a new() constraint",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T> where T : new()
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Consumer<T>"],
            []),
    ];
}
