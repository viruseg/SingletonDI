using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Tests.Coverage;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Covers a consumer that declares its dependencies in several separate
/// <c>SingletonDIConsume</c> attributes instead of one attribute listing every type.
/// </summary>
public sealed class MultipleConsumeAttributeTests
{
    [Fact]
    public void SeparateConsumeAttributes_ProduceTheSameOutputAsOneAttributeWithEveryType()
    {
        const string singleAttribute = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIConsume(typeof(FirstService), typeof(SecondService))]
                public partial class Consumer
                {
                }
            }
            """;

        const string separateAttributes = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIConsume(typeof(FirstService))]
                [SingletonDIConsume(typeof(SecondService))]
                public partial class Consumer
                {
                }
            }
            """;

        var single = Run(singleAttribute);
        var separate = Run(separateAttributes);

        Assert.Empty(single.GeneratorErrorIds);
        Assert.Empty(separate.GeneratorErrorIds);
        Assert.Empty(separate.CompilerErrors);
        Assert.Equal(single.GeneratedSource, separate.GeneratedSource);
    }

    [Fact]
    public void ThreeSeparateConsumeAttributesOnAStaticClass_GenerateEveryDependency()
    {
        var result = Run("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class DetectPeopleService
                {
                }

                [SingletonDIProvide]
                public class ImageQualityIQAService
                {
                }

                [SingletonDIProvide]
                public class FaceDetectionService
                {
                }

                [SingletonDIConsume(typeof(DetectPeopleService))]
                [SingletonDIConsume(typeof(ImageQualityIQAService))]
                [SingletonDIConsume(typeof(FaceDetectionService))]
                public static partial class CropAllImgsInPreset
                {
                }
            }
            """);

        Assert.Empty(result.GeneratorErrorIds);
        Assert.Empty(result.CompilerErrors);
        Assert.Contains("Resolve<global::App.DetectPeopleService>()", result.GeneratedSource);
        Assert.Contains("Resolve<global::App.ImageQualityIQAService>()", result.GeneratedSource);
        Assert.Contains("Resolve<global::App.FaceDetectionService>()", result.GeneratedSource);
    }

    [Fact]
    public void SeparateConsumeAttributes_KeepTheirDeclarationOrder()
    {
        var result = Run("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIConsume(typeof(SecondService))]
                [SingletonDIConsume(typeof(FirstService))]
                public partial class Consumer
                {
                }
            }
            """);

        var firstDependency = result.GeneratedSource.IndexOf(
            "Resolve<global::App.SecondService>()",
            StringComparison.Ordinal);
        var secondDependency = result.GeneratedSource.IndexOf(
            "Resolve<global::App.FirstService>()",
            StringComparison.Ordinal);

        Assert.True(firstDependency >= 0, "The first declared dependency was not generated.");
        Assert.True(
            secondDependency > firstDependency,
            "The dependencies must follow the order the attributes declare them in.");
    }

    [Fact]
    public void RepeatedTypeInSeparateConsumeAttributes_ReportsDm0010OnTheSecondAttribute()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIConsume(typeof(FirstService))]
                [SingletonDIConsume(typeof(SecondService), typeof(FirstService))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = Run(source);

        var dm0010 = Assert.Single(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "DM0010");
        Assert.Equal(DiagnosticSeverity.Error, dm0010.Severity);
        Assert.Contains("'FirstService'", dm0010.GetMessage());

        // The repeat is reported where it was written, so the diagnostic has to point into the second
        // attribute rather than at the first occurrence.
        var secondAttribute = source.IndexOf(
            "[SingletonDIConsume(typeof(SecondService)",
            StringComparison.Ordinal);
        Assert.Equal(
            "FirstService",
            source.Substring(
                dm0010.Location.SourceSpan.Start,
                dm0010.Location.SourceSpan.Length));
        Assert.True(
            dm0010.Location.SourceSpan.Start > secondAttribute,
            "DM0010 must be reported on the occurrence inside the second attribute.");
    }

    [Fact]
    public void ProviderWithSeparateConsumeAttributes_RegistersEveryDependency()
    {
        var result = Run("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(FirstService))]
                [SingletonDIConsume(typeof(SecondService))]
                public partial class CompositeService
                {
                }
            }
            """);

        Assert.Empty(result.GeneratorErrorIds);

        var registration = GetRegistration(result.GeneratedSource, "global::App.CompositeService");
        Assert.Contains("typeof(global::App.FirstService)", registration);
        Assert.Contains("typeof(global::App.SecondService)", registration);
    }

    [Fact]
    public void DerivedProviderWithoutItsOwnConsumeAttributes_UsesEveryBaseTypeAttribute()
    {
        var result = Run("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(FirstService))]
                [SingletonDIConsume(typeof(SecondService))]
                public partial class BaseService
                {
                }

                [SingletonDIProvide]
                public class DerivedService : BaseService
                {
                }
            }
            """);

        Assert.Empty(result.GeneratorErrorIds);

        var registration = GetRegistration(result.GeneratedSource, "global::App.DerivedService");
        Assert.Contains("typeof(global::App.FirstService)", registration);
        Assert.Contains("typeof(global::App.SecondService)", registration);
    }

    [Fact]
    public void DerivedProviderWithItsOwnConsumeAttributes_IgnoresTheBaseTypeAttributes()
    {
        var result = Run("""
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public class FirstService
                {
                }

                [SingletonDIProvide]
                public class SecondService
                {
                }

                [SingletonDIProvide]
                public class ThirdService
                {
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(FirstService))]
                [SingletonDIConsume(typeof(SecondService))]
                public partial class BaseService
                {
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(ThirdService))]
                public partial class DerivedService : BaseService
                {
                }
            }
            """);

        Assert.Empty(result.GeneratorErrorIds);

        var derived = GetRegistration(result.GeneratedSource, "global::App.DerivedService");
        Assert.Contains("typeof(global::App.ThirdService)", derived);
        Assert.DoesNotContain("typeof(global::App.FirstService)", derived);
        Assert.DoesNotContain("typeof(global::App.SecondService)", derived);

        var declared = GetRegistration(result.GeneratedSource, "global::App.BaseService");
        Assert.Contains("typeof(global::App.FirstService)", declared);
        Assert.Contains("typeof(global::App.SecondService)", declared);
    }

    private static MatrixRunResult Run(string source)
    {
        return DeclarationMatrixHarness.Run(
            source,
            new MatrixRunOptions
            {
                CompositionRoot = false,
                OutputKind = OutputKind.DynamicallyLinkedLibrary,
                LanguageVersion = LanguageVersion.Latest,
                NullableContextProviderEnabled = false,
            });
    }

    private static string GetRegistration(string generatedSource, string implementationType)
    {
        const string marker = "__SingletonDIHost__.RegisterProvider<";
        var registration = generatedSource.IndexOf(
            $"{marker}{implementationType}, {implementationType}>(",
            StringComparison.Ordinal);
        Assert.True(registration >= 0, $"No registration was emitted for {implementationType}.");

        var next = generatedSource.IndexOf(
            marker,
            registration + 1,
            StringComparison.Ordinal);

        return generatedSource.Substring(
            registration,
            (next < 0 ? generatedSource.Length : next) - registration);
    }
}