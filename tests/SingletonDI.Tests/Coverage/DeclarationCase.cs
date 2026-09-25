using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SingletonDI.Tests.Coverage;

internal abstract record MatrixExpectation;

internal sealed record SupportedExpectation : MatrixExpectation;

internal sealed record RejectedExpectation(string[] DiagnosticIds) : MatrixExpectation
{
    internal ImmutableArray<string> Ids => DiagnosticIds.ToImmutableArray();
}

internal sealed record SilentlyIgnoredExpectation : MatrixExpectation;

internal sealed record DeclarationCase(
    string Id,
    string Axis,
    string Variant,
    string Source,
    MatrixExpectation Expectation,
    string[] ExpectedFragments,
    string[] AbsentFragments,
    OutputKind OutputKind = OutputKind.DynamicallyLinkedLibrary,
    ImmutableArray<string> PreprocessorSymbols = default,
    LanguageVersion LanguageVersion = LanguageVersion.Latest)
{
    internal ImmutableArray<string> ResolvedPreprocessorSymbols =>
        PreprocessorSymbols.IsDefault ? ImmutableArray<string>.Empty : PreprocessorSymbols;
}

internal static class DeclarationCaseVerifier
{
    internal static void Verify(DeclarationCase declarationCase)
    {
        var result = DeclarationMatrixHarness.Run(
            declarationCase.Source,
            new MatrixRunOptions
            {
                OutputKind = declarationCase.OutputKind,
                LanguageVersion = declarationCase.LanguageVersion,
                AdditionalPreprocessorSymbols = declarationCase.ResolvedPreprocessorSymbols,
            });

        var expectedIds = (declarationCase.Expectation as RejectedExpectation)?.Ids
                         ?? ImmutableArray<string>.Empty;
        var actualIds = result.GeneratorErrorIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        var expected = expectedIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        Assert.True(
            expected.SequenceEqual(actualIds),
            $"{declarationCase.Id} ({declarationCase.Variant}): expected diagnostics " +
            $"[{string.Join(", ", expected)}], actual [{string.Join(", ", actualIds)}].");

        Assert.True(
            result.CompilerErrors.IsEmpty,
            $"{declarationCase.Id} ({declarationCase.Variant}): the source itself does not compile, " +
            $"so the row proves nothing. Compiler errors:{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, result.CompilerErrors.Select(error => error.ToString()))}");

        AssertFragments(declarationCase, result, mustBePresent: true);
        AssertFragments(declarationCase, result, mustBePresent: false);

        var expectsGeneration = declarationCase.Expectation is SupportedExpectation;
        Assert.True(
            expectsGeneration == result.EmitSucceeded,
            $"{declarationCase.Id} ({declarationCase.Variant}): expected Emit success to be " +
            $"{expectsGeneration}, actual {result.EmitSucceeded}.");
    }

    internal static void VerifyData(IEnumerable<DeclarationCase> cases, string axis)
    {
        var all = cases.ToImmutableArray();
        var prefix = IdPrefix(axis);
        foreach (var declarationCase in all)
        {
            Assert.Equal(axis, declarationCase.Axis);
            Assert.False(string.IsNullOrWhiteSpace(declarationCase.Variant));
            Assert.False(string.IsNullOrWhiteSpace(declarationCase.Source));
            Assert.StartsWith(prefix + "-", declarationCase.Id, StringComparison.Ordinal);
            Assert.Equal(prefix.Length + 4, declarationCase.Id.Length);
            Assert.All(
                declarationCase.Id[(prefix.Length + 1)..],
                character => Assert.True(character is >= '0' and <= '9'));

            foreach (var fragment in declarationCase.ExpectedFragments
                         .Concat(declarationCase.AbsentFragments))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(fragment),
                    $"{declarationCase.Id}: a fragment must not be blank.");
            }

            Assert.Empty(
                declarationCase.ExpectedFragments.Intersect(
                    declarationCase.AbsentFragments,
                    StringComparer.Ordinal));
            Assert.Equal(
                declarationCase.ExpectedFragments.Distinct(StringComparer.Ordinal).Count(),
                declarationCase.ExpectedFragments.Length);
            Assert.Equal(
                declarationCase.AbsentFragments.Distinct(StringComparer.Ordinal).Count(),
                declarationCase.AbsentFragments.Length);

            switch (declarationCase.Expectation)
            {
                case SupportedExpectation:
                    Assert.True(
                        declarationCase.ExpectedFragments.Length > 0 ||
                        declarationCase.AbsentFragments.Length > 0,
                        $"{declarationCase.Id}: a Supported row must assert something.");
                    break;
                case RejectedExpectation rejected:
                    Assert.NotEmpty(rejected.Ids);
                    Assert.Equal(
                        rejected.Ids.Distinct(StringComparer.Ordinal).Count(),
                        rejected.Ids.Length);
                    break;
                case SilentlyIgnoredExpectation:
                    Assert.NotEmpty(declarationCase.AbsentFragments);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"{declarationCase.Id}: unknown expectation {declarationCase.Expectation}.");
            }
        }

        Assert.Equal(
            all.Select(declarationCase => declarationCase.Id).Distinct(StringComparer.Ordinal).Count(),
            all.Length);
    }

    internal static string IdPrefix(string axis) => axis switch
    {
        "PROVIDER_VISIBILITY" => "PROV-VIS",
        "PROVIDER_NESTING" => "PROV-NST",
        "PROVIDER_TYPE_KIND" => "PROV-KND",
        "PROVIDER_CONSTRUCTOR" => "PROV-CTOR",
        "PROVIDER_INITIALIZER" => "PROV-INI",
        "CONSUMER_SHAPE" => "CONS-SHP",
        "CONSUMER_INHERITANCE" => "CONS-INH",
        "CONSUMER_NESTING" => "CONS-NST",
        "CONSUMER_NAMESPACE" => "CONS-NSM",
        "CONSUMER_GENERIC" => "CONS-GEN",
        "NULLABILITY" => "NUL",
        "LANGUAGE_VERSION" => "LANG",
        "ATTRIBUTE_FORM" => "ATTR",
        "PROPERTY_NAME" => "PNAME",
        "SERVICE_TYPE" => "SVC",
        "CONDITIONAL_COMPILATION" => "COND",
        _ => throw new InvalidOperationException($"Unknown axis {axis}."),
    };

    private static void AssertFragments(
        DeclarationCase declarationCase,
        MatrixRunResult result,
        bool mustBePresent)
    {
        var lines = result.GeneratedSource
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .ToImmutableHashSet(StringComparer.Ordinal);
        var fragments = mustBePresent
            ? declarationCase.ExpectedFragments
            : declarationCase.AbsentFragments;
        foreach (var fragment in fragments)
        {
            var trimmed = fragment.Trim();
            if (mustBePresent)
            {
                Assert.True(
                    lines.Contains(trimmed),
                    $"{declarationCase.Id} ({declarationCase.Variant}): the generated source has no " +
                    $"line [{trimmed}]. Generated:{Environment.NewLine}{result.GeneratedSource}");
            }
            else
            {
                Assert.False(
                    lines.Contains(trimmed),
                    $"{declarationCase.Id} ({declarationCase.Variant}): the generated source must have " +
                    $"no line [{trimmed}]. Generated:{Environment.NewLine}{result.GeneratedSource}");
            }
        }
    }
}
