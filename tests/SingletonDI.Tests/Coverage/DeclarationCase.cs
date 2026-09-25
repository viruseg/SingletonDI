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
            FormatCompilerErrors(declarationCase, result));

        AssertFragments(declarationCase, result, mustBePresent: true);
        AssertFragments(declarationCase, result, mustBePresent: false);

        if (declarationCase.Expectation is RejectedExpectation)
        {
            return;
        }

        Assert.True(
            result.EmitSucceeded,
            $"{declarationCase.Id} ({declarationCase.Variant}): a " +
            $"{declarationCase.Expectation.GetType().Name} row requires the compilation to emit, " +
            $"actual EmitSucceeded {result.EmitSucceeded}.");
    }

    internal static void VerifyData(IEnumerable<DeclarationCase> cases, string axis)
    {
        var all = cases.ToImmutableArray();
        var prefix = IdPrefix(axis);
        foreach (var declarationCase in all)
        {
            Assert.True(
                string.Equals(axis, declarationCase.Axis, StringComparison.Ordinal),
                $"{declarationCase.Id}: the axis must be {axis}, actual {declarationCase.Axis}.");
            Assert.False(
                string.IsNullOrWhiteSpace(declarationCase.Variant),
                $"{declarationCase.Id}: the variant must not be blank.");
            Assert.False(
                string.IsNullOrWhiteSpace(declarationCase.Source),
                $"{declarationCase.Id}: the source must not be blank.");
            Assert.True(
                declarationCase.Id.StartsWith(prefix + "-", StringComparison.Ordinal),
                $"{declarationCase.Id}: the id must start with {prefix}-.");
            Assert.True(
                declarationCase.Id.Length == prefix.Length + 3,
                $"{declarationCase.Id}: the id must be {prefix}- plus two digits, " +
                $"expected length {prefix.Length + 3}, actual {declarationCase.Id.Length}.");
            Assert.All(
                declarationCase.Id[(prefix.Length + 1)..],
                character => Assert.True(
                    character is >= '0' and <= '9',
                    $"{declarationCase.Id}: every character after {prefix}- must be a digit, " +
                    $"actual '{character}'."));

            foreach (var fragment in declarationCase.ExpectedFragments
                         .Concat(declarationCase.AbsentFragments))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(fragment),
                    $"{declarationCase.Id}: a fragment must not be blank.");
            }

            var overlap = declarationCase.ExpectedFragments
                .Intersect(declarationCase.AbsentFragments, StringComparer.Ordinal)
                .ToImmutableArray();
            Assert.True(
                overlap.IsEmpty,
                $"{declarationCase.Id}: a fragment cannot be expected and absent at once, " +
                $"actual [{string.Join(" | ", overlap)}].");

            var expectedDistinct = declarationCase.ExpectedFragments
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.True(
                expectedDistinct == declarationCase.ExpectedFragments.Length,
                $"{declarationCase.Id}: expected fragments must be distinct, " +
                $"{declarationCase.ExpectedFragments.Length} given, {expectedDistinct} distinct.");

            var absentDistinct = declarationCase.AbsentFragments
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.True(
                absentDistinct == declarationCase.AbsentFragments.Length,
                $"{declarationCase.Id}: absent fragments must be distinct, " +
                $"{declarationCase.AbsentFragments.Length} given, {absentDistinct} distinct.");

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

        var duplicates = all
            .GroupBy(declarationCase => declarationCase.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToImmutableArray();
        Assert.True(
            duplicates.IsEmpty,
            $"Axis {axis}: every matrix id must be unique, " +
            $"actual duplicates [{string.Join(", ", duplicates)}].");
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

    private static string FormatCompilerErrors(
        DeclarationCase declarationCase,
        MatrixRunResult result)
    {
        var sourceErrors = result.CompilerErrors
            .Where(error => !IsInGeneratedFile(error))
            .ToImmutableArray();
        var generatedErrors = result.CompilerErrors
            .Where(IsInGeneratedFile)
            .ToImmutableArray();

        if (generatedErrors.IsEmpty)
        {
            return $"{declarationCase.Id} ({declarationCase.Variant}): the row's own source does not " +
                   $"compile, so the row proves nothing. Compiler errors in the row's own source:" +
                   Environment.NewLine + Format(sourceErrors);
        }

        var headline = sourceErrors.IsEmpty
            ? "the row's own source compiled, but the generated code does not, so the row is pointing at a " +
              "generator defect and must not be edited"
            : "the row's own source does not compile either, so the row proves nothing on both counts";
        return $"{declarationCase.Id} ({declarationCase.Variant}): {headline}. " +
               $"Compiler errors in generated files:" + Environment.NewLine + Format(generatedErrors) +
               (sourceErrors.IsEmpty
                   ? string.Empty
                   : Environment.NewLine + "Compiler errors in the row's own source:" + Environment.NewLine +
                     Format(sourceErrors));
    }

    private static string Format(ImmutableArray<Diagnostic> errors) =>
        string.Join(Environment.NewLine, errors.Select(error => error.ToString()));

    private static bool IsInGeneratedFile(Diagnostic diagnostic) =>
        !string.IsNullOrEmpty(diagnostic.Location.SourceTree?.FilePath);

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
