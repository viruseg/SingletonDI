using System.Xml.Linq;
using Xunit;

namespace SingletonDI.Tests;

public sealed class PackageMetadataTests
{
    [Fact]
    public void RuntimePackageMetadataTargetsReleaseContract()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "src",
            "SingletonDI.Attributes",
            "SingletonDI.Attributes.csproj");
        var document = XDocument.Load(projectPath);

        Assert.Equal("1.2.0", ReadProperty(document, "Version"));
        Assert.Equal("1.2.0", ReadProperty(document, "AssemblyVersion"));
        Assert.Contains("1.2.0", ReadProperty(document, "PackageReleaseNotes"));
        Assert.Equal("true", ReadProperty(document, "IncludeSymbols"));
        Assert.Equal("snupkg", ReadProperty(document, "SymbolPackageFormat"));

        Assert.Equal("net10.0", ReadProperty(document, "TargetFramework"));
        Assert.Empty(ReadProperty(document, "TargetFrameworks"));

        var compatibilityReferences = document
            .Descendants("PackageReference")
            .Where(reference =>
                string.Equals(
                    reference.Attribute("Include")?.Value,
                    "System.Threading.Tasks.Extensions",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    reference.Attribute("Include")?.Value,
                    "Microsoft.Bcl.AsyncInterfaces",
                    StringComparison.OrdinalIgnoreCase));

        Assert.Empty(compatibilityReferences);
    }

    private static string ReadProperty(XDocument document, string name)
    {
        return document
            .Descendants("PropertyGroup")
            .Elements(name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0)
            ?? string.Empty;
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
