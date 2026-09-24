using System.Xml.Linq;
using Xunit;

namespace SingletonDI.Tests;

public sealed class PackageProjectSettingsTests
{
    [Fact]
    public void OnlyRuntimeProjectIsPackable()
    {
        var root = FindRepositoryRoot();
        var projects = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runtimeProject = Path.Combine(
            root,
            "src",
            "SingletonDI.Attributes",
            "SingletonDI.Attributes.csproj");

        Assert.Contains(runtimeProject, projects);
        foreach (var project in projects)
        {
            var document = XDocument.Load(project);
            var isPackable = ReadProperty(document, "IsPackable") ?? "true";
            var generatePackageOnBuild = ReadProperty(document, "GeneratePackageOnBuild") ?? "false";
            if (string.Equals(project, runtimeProject, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("true", isPackable);
                Assert.Equal("false", generatePackageOnBuild);
            }
            else
            {
                Assert.Equal("false", isPackable);
            }
        }
    }

    private static string? ReadProperty(XDocument document, string name)
    {
        return document
            .Descendants("PropertyGroup")
            .Elements(name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);
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
