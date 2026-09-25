using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Xunit;

namespace SingletonDI.Tests;

public sealed class DocumentationConsistencyTests
{
    private static readonly string[] RemovedNames =
    [
        "GeneratorPackage",
        "IInitializeSync",
        "IInitializeAsync",
        "Container",
        "ExceptionHelper"
    ];

    [Fact]
    public void RepositoryDocumentationDoesNotAdvertiseRemovedGeneratorNames()
    {
        var root = FindRepositoryRoot();
        var documentation = File.ReadAllText(Path.Combine(root, "README.md")) +
            File.ReadAllText(Path.Combine(root, "AGENTS.md"));

        Assert.All(RemovedNames, name =>
            Assert.DoesNotContain(name, documentation, StringComparison.Ordinal));
    }

    [Fact]
    public void RepositoryDocumentationDoesNotClaimUnusedTestingDependency()
    {
        var root = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        var hasTestingDependency = projectFiles
            .Select(File.ReadAllText)
            .Any(content => content.Contains(
                "Microsoft.CodeAnalysis.Testing",
                StringComparison.Ordinal));
        var agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));

        if (!hasTestingDependency)
        {
            Assert.DoesNotContain(
                "Microsoft.CodeAnalysis.Testing",
                agents,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryGeneratorDiagnosticIsDocumented()
    {
        var root = FindRepositoryRoot();
        var descriptorFile = Path.Combine(
            root,
            "src",
            "SingletonDI.Generator",
            "DiagnosticDescriptors.cs");
        var declaredIds = Regex.Matches(
                File.ReadAllText(descriptorFile),
                "\"(DM\\d{4})\"")
            .Select(match => match.Groups[1].Value)
            .ToImmutableHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declaredIds);
        foreach (var documentation in new[] { "README.md", "README.RU.md" })
        {
            var text = File.ReadAllText(Path.Combine(root, documentation));
            var missing = declaredIds
                .Where(id => !text.Contains(id, StringComparison.Ordinal))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                missing.Length == 0,
                $"{documentation} does not document: {string.Join(", ", missing)}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SingletonDI.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate SingletonDI repository root.");
    }
}
