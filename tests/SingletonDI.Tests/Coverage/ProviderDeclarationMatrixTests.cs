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
            "public class nested in a public generic class",
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
            new SupportedExpectation(),
            [NestedRegistration],
            []),
    ];
}
