using Microsoft.CodeAnalysis;
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

    private const string ConsumerProperty = "protected static global::App.Service ServiceInstance";

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
            ["partial class Consumer<T>", ConsumerProperty],
            []),
    ];

    public static IEnumerable<object[]> InheritanceCases =>
        InheritanceRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(InheritanceCases))]
    public void Inheritance(string id)
    {
        DeclarationCaseVerifier.Verify(InheritanceRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] InheritanceRows =
    [
        new(
            "CONS-INH-01",
            "CONSUMER_INHERITANCE",
            "derived type without the attribute reads the inherited property",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class BaseConsumer
                {
                }

                public sealed class DerivedConsumer : BaseConsumer
                {
                    public global::App.Service Resolve() => ServiceInstance;
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-INH-02",
            "CONSUMER_INHERITANCE",
            "derived type hides the base property with new",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class BaseConsumer
                {
                }

                public partial class DerivedConsumer : BaseConsumer
                {
                    protected new static global::App.Service ServiceInstance =>
                        global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.Service>();
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class BaseConsumer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-INH-03",
            "CONSUMER_INHERITANCE",
            "derived type declares its own attribute with a different dependency set",
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
                public partial class BaseConsumer
                {
                }

                [SingletonDIConsume(typeof(Second))]
                public partial class DerivedConsumer : BaseConsumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            [
                "partial class BaseConsumer",
                "protected static global::App.First FirstInstance",
                "partial class DerivedConsumer",
                "protected static global::App.Second SecondInstance",
            ],
            []),
        new(
            "CONS-INH-04",
            "CONSUMER_INHERITANCE",
            "three-level chain",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class First
                {
                }

                public partial class Second : First
                {
                }

                public sealed class Third : Second
                {
                    public global::App.Service Resolve() => ServiceInstance;
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-INH-05",
            "CONSUMER_INHERITANCE",
            "sealed derived type",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class BaseConsumer
                {
                }

                public sealed partial class DerivedConsumer : BaseConsumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-INH-06",
            "CONSUMER_INHERITANCE",
            "derived type hides the base property with a wider accessibility",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class BaseConsumer
                {
                }

                public partial class DerivedConsumer : BaseConsumer
                {
                    public new static global::App.Service ServiceInstance =>
                        global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.Service>();
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class BaseConsumer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-INH-07",
            "CONSUMER_INHERITANCE",
            "interface declares a property with the same name",
            ProviderSource + """

            namespace App
            {
                public interface IHasService
                {
                    static global::App.Service ServiceInstance => null!;
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer : IHasService
                {
                }
            }
            """,
            new RejectedExpectation(["DM0025"]),
            [],
            ["protected static global::App.Service ServiceInstance"]),
        new(
            "CONS-INH-08",
            "CONSUMER_INHERITANCE",
            "same-named property declared on the base class",
            ProviderSource + """

            namespace App
            {
                public class BaseConsumer
                {
                    protected static global::App.Service ServiceInstance => null!;
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer : BaseConsumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0025"]),
            [],
            ["protected static global::App.Service ServiceInstance"]),
    ];

    public static IEnumerable<object[]> NestingCases =>
        NestingRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(NestingCases))]
    public void Nesting(string id)
    {
        DeclarationCaseVerifier.Verify(NestingRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] NestingRows =
    [
        new(
            "CONS-NST-01",
            "CONSUMER_NESTING",
            "depth 1 inside a public partial class",
            ProviderSource + """

            namespace App
            {
                public partial class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Outer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-02",
            "CONSUMER_NESTING",
            "depth 1 inside an internal partial class",
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
            ["partial class Outer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-03",
            "CONSUMER_NESTING",
            "depth 2",
            ProviderSource + """

            namespace App
            {
                public partial class First
                {
                    public partial class Second
                    {
                        [SingletonDIConsume(typeof(Service))]
                        public partial class Consumer
                        {
                        }
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class First", "partial class Second", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-04",
            "CONSUMER_NESTING",
            "depth 3",
            ProviderSource + """

            namespace App
            {
                public partial class First
                {
                    public partial class Second
                    {
                        public partial class Third
                        {
                            [SingletonDIConsume(typeof(Service))]
                            public partial class Consumer
                            {
                            }
                        }
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class First", "partial class Second", "partial class Third", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-05",
            "CONSUMER_NESTING",
            "containing type is not partial",
            ProviderSource + """

            namespace App
            {
                public class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0026"]),
            [],
            ["protected static global::App.Service ServiceInstance"]),
        new(
            "CONS-NST-06",
            "CONSUMER_NESTING",
            "containing type is a record",
            ProviderSource + """

            namespace App
            {
                public partial record Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial record class Outer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-07",
            "CONSUMER_NESTING",
            "containing type is a sealed class",
            ProviderSource + """

            namespace App
            {
                public sealed partial class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Outer", "protected static global::App.Service ServiceInstance"],
            []),
        new(
            "CONS-NST-08",
            "CONSUMER_NESTING",
            "file-local containing type",
            ProviderSource + """

            namespace App
            {
                file partial class Outer
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0029"]),
            [],
            ["protected static global::App.Service ServiceInstance"]),
        new(
            "CONS-NST-09",
            "CONSUMER_NESTING",
            "file-local consumer",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                file partial class Consumer
                {
                }
            }
            """,
            new RejectedExpectation(["DM0029"]),
            [],
            ["protected static global::App.Service ServiceInstance"]),
    ];

    public static IEnumerable<object[]> NamespaceCases =>
        NamespaceRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(NamespaceCases))]
    public void Namespace(string id)
    {
        DeclarationCaseVerifier.Verify(NamespaceRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] NamespaceRows =
    [
        new(
            "CONS-NSM-01",
            "CONSUMER_NAMESPACE",
            "block namespace",
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
            ["namespace App", ConsumerProperty],
            []),
        new(
            "CONS-NSM-02",
            "CONSUMER_NAMESPACE",
            "file-scoped namespace",
            """
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
            """,
            new SupportedExpectation(),
            ["namespace App;", ConsumerProperty],
            []),
        new(
            "CONS-NSM-03",
            "CONSUMER_NAMESPACE",
            "global namespace",
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
            [ConsumerProperty, "partial class Consumer"],
            ["namespace", "namespace ;", "namespace App", "namespace App;"]),
        new(
            "CONS-NSM-04",
            "CONSUMER_NAMESPACE",
            "dotted name declared in one namespace",
            """
            using SingletonDI.Attributes;

            namespace App.Deep.Nested
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }

            namespace App.Deep.Nested
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["namespace App.Deep.Nested", "protected static global::App.Deep.Nested.Service ServiceInstance"],
            []),
        new(
            "CONS-NSM-05",
            "CONSUMER_NAMESPACE",
            "nested namespace blocks",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                namespace Deep
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
            }
            """,
            new SupportedExpectation(),
            ["namespace App.Deep", "protected static global::App.Deep.Service ServiceInstance"],
            []),
        new(
            "CONS-NSM-06",
            "CONSUMER_NAMESPACE",
            "namespace named with an escaped keyword",
            """
            using SingletonDI.Attributes;

            namespace @class
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }

            namespace @class
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["namespace @class", "protected static global::@class.Service ServiceInstance"],
            []),
        new(
            "CONS-NSM-07",
            "CONSUMER_NAMESPACE",
            "two namespaces in one source, consumer in the second",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                }
            }

            namespace Other
            {
                [SingletonDIConsume(typeof(App.Service))]
                public partial class Consumer
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["namespace Other", ConsumerProperty],
            []),
        new(
            "CONS-NSM-08",
            "CONSUMER_NAMESPACE",
            "top-level statements next to the consumer",
            """
            using SingletonDI.Attributes;

            return 0;

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
            ["namespace App", ConsumerProperty],
            [],
            OutputKind: OutputKind.ConsoleApplication),
    ];

    public static IEnumerable<object[]> GenericCases =>
        GenericRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(GenericCases))]
    public void Generic(string id)
    {
        DeclarationCaseVerifier.Verify(GenericRows.Single(row => row.Id == id));
    }

    internal static readonly DeclarationCase[] GenericRows =
    [
        new(
            "CONS-GEN-01",
            "CONSUMER_GENERIC",
            "unconstrained type parameter",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T>
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Consumer<T>", ConsumerProperty],
            []),
        new(
            "CONS-GEN-02",
            "CONSUMER_GENERIC",
            "class constraint",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T> where T : class
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["where T : class", ConsumerProperty],
            []),
        new(
            "CONS-GEN-03",
            "CONSUMER_GENERIC",
            "struct constraint",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T> where T : struct
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["where T : struct", ConsumerProperty],
            []),
        new(
            "CONS-GEN-04",
            "CONSUMER_GENERIC",
            "new() constraint",
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
            ["where T : new()", ConsumerProperty],
            []),
        new(
            "CONS-GEN-05",
            "CONSUMER_GENERIC",
            "notnull constraint",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T> where T : notnull
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["where T : notnull", ConsumerProperty],
            []),
        new(
            "CONS-GEN-06",
            "CONSUMER_GENERIC",
            "two type parameters",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<TFirst, TSecond>
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Consumer<TFirst, TSecond>", ConsumerProperty],
            []),
        new(
            "CONS-GEN-07",
            "CONSUMER_GENERIC",
            "several constraints in one clause",
            ProviderSource + """

            namespace App
            {
                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<T> where T : class, global::System.IDisposable, new()
                {
                }
            }
            """,
            new SupportedExpectation(),
            ["where T : class,global::System.IDisposable,new()", ConsumerProperty],
            []),
        new(
            "CONS-GEN-08",
            "CONSUMER_GENERIC",
            "consumer inside a generic containing type",
            ProviderSource + """

            namespace App
            {
                public partial class Outer<T>
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Outer<T>", ConsumerProperty],
            []),
        new(
            "CONS-GEN-09",
            "CONSUMER_GENERIC",
            "generic containing type with a constraint",
            ProviderSource + """

            namespace App
            {
                public partial class Outer<T> where T : class
                {
                    [SingletonDIConsume(typeof(Service))]
                    public partial class Consumer
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            ["partial class Outer<T>", "where T : class", ConsumerProperty],
            []),
        new(
            "CONS-GEN-10",
            "CONSUMER_GENERIC",
            "attribute on a type parameter",
            """
            using System;
            using SingletonDI.Attributes;

            namespace App
            {
                [AttributeUsage(AttributeTargets.GenericParameter)]
                public sealed class TypeParameterMarkerAttribute : Attribute
                {
                }

                [SingletonDIProvide]
                public class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class Consumer<[TypeParameterMarker] T>
                {
                }
            }
            """,
            new RejectedExpectation(["DM0031"]),
            [],
            [ConsumerProperty]),
    ];
}
