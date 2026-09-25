using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed partial class ProviderDeclarationMatrixTests
{
    public static IEnumerable<object[]> VisibilityCases =>
        VisibilityRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(VisibilityCases))]
    public void Visibility(string id)
    {
        DeclarationCaseVerifier.Verify(VisibilityRows.Single(row => row.Id == id));
    }

    private const string ServiceRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Service, global::App.Service>(";

    internal static readonly DeclarationCase[] VisibilityRows =
    [
        new(
            "PROV-VIS-01",
            "PROVIDER_VISIBILITY",
            "public class provider",
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
            [ServiceRegistration],
            []),
        new(
            "PROV-VIS-02",
            "PROVIDER_VISIBILITY",
            "public sealed class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceRegistration],
            []),
        new(
            "PROV-VIS-03",
            "PROVIDER_VISIBILITY",
            "internal class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                internal class Service
                {
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceRegistration],
            []),
        new(
            "PROV-VIS-04",
            "PROVIDER_VISIBILITY",
            "protected internal class provider nested in an internal class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                internal sealed class Outer
                {
                    [SingletonDIProvide]
                    protected internal class Service
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [NestedRegistration],
            []),
        new(
            "PROV-VIS-05",
            "PROVIDER_VISIBILITY",
            "file-local class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                file class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            [ServiceRegistration]),
        new(
            "PROV-VIS-06",
            "PROVIDER_VISIBILITY",
            "public abstract class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public abstract class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0002"]),
            [],
            []),
        new(
            "PROV-VIS-07",
            "PROVIDER_VISIBILITY",
            "internal abstract class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                internal abstract class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0002"]),
            [],
            []),
        new(
            "PROV-VIS-08",
            "PROVIDER_VISIBILITY",
            "generic class provider",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service<T>
                {
                }
            }
            """,
            new RejectedExpectation(["DM0015"]),
            [],
            []),
        new(
            "PROV-VIS-09",
            "PROVIDER_VISIBILITY",
            "top-level class provider without a namespace",
            """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
            }
            """,
            new SupportedExpectation(),
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::Service, global::Service>("],
            []),
        new(
            "PROV-VIS-10",
            "PROVIDER_VISIBILITY",
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
            [ServiceRegistration],
            []),
    ];

    public static IEnumerable<object[]> NestingCases =>
        NestingRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(NestingCases))]
    public void Nesting(string id)
    {
        DeclarationCaseVerifier.Verify(NestingRows.Single(row => row.Id == id));
    }

    private const string NestedRegistration =
        "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Outer.Service, global::App.Outer.Service>(";

    internal static readonly DeclarationCase[] NestingRows =
    [
        new(
            "PROV-NST-01",
            "PROVIDER_NESTING",
            "public class nested in a public class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    [SingletonDIProvide]
                    public class Service
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [NestedRegistration],
            []),
        new(
            "PROV-NST-02",
            "PROVIDER_NESTING",
            "internal class nested in an internal class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                internal class Outer
                {
                    [SingletonDIProvide]
                    internal class Service
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [NestedRegistration],
            []),
        new(
            "PROV-NST-03",
            "PROVIDER_NESTING",
            "protected internal class nested in a public class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    [SingletonDIProvide]
                    protected internal class Service
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [NestedRegistration],
            []),
        new(
            "PROV-NST-04",
            "PROVIDER_NESTING",
            "private class nested in a public class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    [SingletonDIProvide]
                    private class Service
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            [NestedRegistration]),
        new(
            "PROV-NST-05",
            "PROVIDER_NESTING",
            "protected class nested in a public class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    [SingletonDIProvide]
                    protected class Service
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            [NestedRegistration]),
        new(
            "PROV-NST-06",
            "PROVIDER_NESTING",
            "private protected class nested in a public class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    [SingletonDIProvide]
                    private protected class Service
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            [NestedRegistration]),
        new(
            "PROV-NST-07",
            "PROVIDER_NESTING",
            "public class nested in an internal class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                internal class Outer
                {
                    [SingletonDIProvide]
                    public class Service
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [NestedRegistration],
            []),
        new(
            "PROV-NST-08",
            "PROVIDER_NESTING",
            "internal class nested in a private class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer
                {
                    private class Middle
                    {
                        [SingletonDIProvide]
                        internal class Service
                        {
                        }
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0022"]),
            [],
            []),
        new(
            "PROV-NST-09",
            "PROVIDER_NESTING",
            "non-generic class nested in a generic class, rejected",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                public class Outer<T>
                {
                    [SingletonDIProvide]
                    public class Service
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0015"]),
            [],
            ["global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Outer<T>.Service, global::App.Outer<T>.Service>("]),
    ];

    public static IEnumerable<object[]> TypeKindCases =>
        TypeKindRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(TypeKindCases))]
    public void TypeKind(string id)
    {
        DeclarationCaseVerifier.Verify(TypeKindRows.Single(row => row.Id == id));
    }

    private const string AsyncDisposeDelegate =
        "static value => ((global::System.IAsyncDisposable)value).DisposeAsync().AsTask());";

    internal static readonly DeclarationCase[] TypeKindRows =
    [
        new(
            "PROV-KND-01",
            "PROVIDER_TYPE_KIND",
            "record without positional parameters",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public record Service;
            }
            """,
            new SupportedExpectation(),
            [ServiceRegistration],
            []),
        new(
            "PROV-KND-02",
            "PROVIDER_TYPE_KIND",
            "record with positional parameters",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public record Service(int Value);
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceRegistration]),
        new(
            "PROV-KND-03",
            "PROVIDER_TYPE_KIND",
            "sealed record",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed record Service;
            }
            """,
            new SupportedExpectation(),
            [ServiceRegistration],
            []),
        new(
            "PROV-KND-04",
            "PROVIDER_TYPE_KIND",
            "abstract record",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public abstract record Service;
            }
            """,
            new RejectedExpectation(["DM0002"]),
            [],
            []),
        new(
            "PROV-KND-05",
            "PROVIDER_TYPE_KIND",
            "static class",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public static class Service
                {
                }
            }
            """,
            new RejectedExpectation(["DM0002"]),
            [],
            [ServiceRegistration]),
        new(
            "PROV-KND-06",
            "PROVIDER_TYPE_KIND",
            "record with an explicit public parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public record Service
                {
                    public Service()
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceRegistration],
            []),
        new(
            "PROV-KND-07",
            "PROVIDER_TYPE_KIND",
            "record implementing IAsyncDisposable",
            """
            using System;
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public record Service : IAsyncDisposable
                {
                    public ValueTask DisposeAsync() => default;
                }
            }
            """,
            new SupportedExpectation(),
            [AsyncDisposeDelegate],
            []),
    ];

    public static IEnumerable<object[]> ConstructorCases =>
        ConstructorRows.Select(row => new object[] { row.Id });

    [Theory]
    [MemberData(nameof(ConstructorCases))]
    public void Constructor(string id)
    {
        DeclarationCaseVerifier.Verify(ConstructorRows.Single(row => row.Id == id));
    }

    private const string ServiceFactory = "() => new global::App.Service(),";

    internal static readonly DeclarationCase[] ConstructorRows =
    [
        new(
            "PROV-CTOR-01",
            "PROVIDER_CONSTRUCTOR",
            "implicit public parameterless constructor",
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
            [ServiceFactory],
            []),
        new(
            "PROV-CTOR-02",
            "PROVIDER_CONSTRUCTOR",
            "explicit public parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public Service()
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "PROV-CTOR-03",
            "PROVIDER_CONSTRUCTOR",
            "only an internal parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    internal Service()
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-04",
            "PROVIDER_CONSTRUCTOR",
            "only a private parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    private Service()
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-05",
            "PROVIDER_CONSTRUCTOR",
            "only a protected parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    protected Service()
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-06",
            "PROVIDER_CONSTRUCTOR",
            "only a protected internal parameterless constructor",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    protected internal Service()
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-07",
            "PROVIDER_CONSTRUCTOR",
            "two constructors, one of them parameterless",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public Service()
                    {
                    }

                    public Service(int value)
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "PROV-CTOR-08",
            "PROVIDER_CONSTRUCTOR",
            "only a constructor with optional parameters",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public Service(int value = 0)
                    {
                    }
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-09",
            "PROVIDER_CONSTRUCTOR",
            "primary constructor with parameters",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service(int value)
                {
                }
            }
            """,
            new RejectedExpectation(["DM0004"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-10",
            "PROVIDER_CONSTRUCTOR",
            "required members with SetsRequiredMembers on the constructor",
            """
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

                    public required string Name { get; set; }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "PROV-CTOR-11",
            "PROVIDER_CONSTRUCTOR",
            "required members without SetsRequiredMembers",
            """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    public Service()
                    {
                    }

                    public required string Name { get; set; }
                }
            }
            """,
            new RejectedExpectation(["DM0032"]),
            [],
            [ServiceFactory]),
        new(
            "PROV-CTOR-12",
            "PROVIDER_CONSTRUCTOR",
            "required members on the base type, SetsRequiredMembers on the derived constructor",
            """
            using System.Diagnostics.CodeAnalysis;
            using SingletonDI.Attributes;

            namespace App
            {
                public abstract class BaseService
                {
                    public required string Name { get; set; }
                }

                [SingletonDIProvide]
                public sealed class Service : BaseService
                {
                    [SetsRequiredMembers]
                    public Service()
                    {
                        Name = string.Empty;
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
        new(
            "PROV-CTOR-13",
            "PROVIDER_CONSTRUCTOR",
            "constructor marked Obsolete",
            """
            using System;
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class Service
                {
                    [Obsolete("Use the other constructor.")]
                    public Service()
                    {
                    }
                }
            }
            """,
            new SupportedExpectation(),
            [ServiceFactory],
            []),
    ];
}
