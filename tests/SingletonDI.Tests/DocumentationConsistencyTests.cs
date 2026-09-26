using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using SingletonDI.Generator;
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
        var documentsTestingDependency = agents.Contains(
            "Microsoft.CodeAnalysis.Testing",
            StringComparison.Ordinal);

        Assert.Equal(hasTestingDependency, documentsTestingDependency);
    }

    [Fact]
    public void EveryGeneratorDiagnosticTitleMatchesItsDocumentedHeading()
    {
        // Comparing id strings alone missed nine headings that had drifted from the descriptor
        // title they document.
        var root = FindRepositoryRoot();
        var descriptors = ReadDescriptors();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        var headings = Regex.Matches(readme, @"^### (DM\d{4}): (.+)$", RegexOptions.Multiline)
            .Select(match => (Id: match.Groups[1].Value, Title: match.Groups[2].Value.Trim()))
            .ToDictionary(entry => entry.Id, entry => entry.Title, StringComparer.Ordinal);

        var mismatches = descriptors
            .Where(descriptor => !headings.TryGetValue(descriptor.Id, out var title) ||
                                !string.Equals(title, descriptor.Title.ToString(), StringComparison.Ordinal))
            .Select(descriptor =>
                $"{descriptor.Id}: descriptor '{descriptor.Title}', documented '{headings.GetValueOrDefault(descriptor.Id, "<missing>")}'")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            mismatches.Length == 0,
            "Diagnostic headings diverge from the descriptor titles:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void EveryGeneratorDiagnosticCarriesADescriptionAndADocumentationLink()
    {
        // The IDE shows the description in the diagnostics list and links the help entry from it.
        // Without either, all of them arrived blank even though the readme documents every one.
        var descriptors = ReadDescriptors();

        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, descriptor => Assert.False(
            string.IsNullOrWhiteSpace(descriptor.Description.ToString()),
            $"{descriptor.Id} has no description."));
        Assert.All(descriptors, descriptor => Assert.False(
            string.IsNullOrWhiteSpace(descriptor.HelpLinkUri),
            $"{descriptor.Id} has no help link."));
    }

    [Fact]
    public void EveryGeneratorDiagnosticIsAnError()
    {
        // The documented severity column is only correct because nothing pinned it.
        var descriptors = ReadDescriptors();

        Assert.NotEmpty(descriptors);
        Assert.All(
            descriptors,
            descriptor => Assert.Equal(
                DiagnosticSeverity.Error,
                descriptor.DefaultSeverity));
    }

    /// <summary>
    /// Reads the descriptors the generator actually exposes, rather than scraping the source file.
    /// </summary>
    private static ImmutableArray<DiagnosticDescriptor> ReadDescriptors()
    {
        return typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .OrderBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToImmutableArray();
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

    [Fact]
    public void ShippedConsumeAttributeDocumentSaysInheritanceIsAboutAccess()
    {
        // The shipped XML documentation promises more than the generator delivers: a derived
        // declaration is never validated, so DM0007, DM0026 and DM0029 cannot fire on it.
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SingletonDI.Attributes",
            "SingletonDIConsumeAttribute.cs"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains(
            "carries access to the base type's generated properties",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "a derived declaration is not itself validated",
            readme,
            StringComparison.OrdinalIgnoreCase);
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
