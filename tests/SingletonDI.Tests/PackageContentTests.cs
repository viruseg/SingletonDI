using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;

namespace SingletonDI.Tests;

/// <summary>
/// Serializes the packaging tests against each other in this process, so neither waits on the lock
/// the other holds.
/// </summary>
/// <remarks>
/// Both classes pack the same project and then build against the package, which takes minutes. Run
/// in parallel they spent the whole of one class's pack waiting for the lock, so the file lock is
/// left to do the job it exists for: separating two concurrent test processes.
/// </remarks>
[CollectionDefinition("SingletonDI packaging", DisableParallelization = true)]
public sealed class SingletonDIPackagingCollection
{
}

[Collection("SingletonDI packaging")]
[Trait("Category", "Packaging")]
public sealed class PackageContentTests
{
    private readonly ITestOutputHelper _output;

    private static readonly string[] AnalyzerAssemblyNames =
    [
        "SingletonDI.Generator.dll",
        "SingletonDI.Refactoring.dll",
    ];

    public PackageContentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void RuntimeProjectUsesTargetPathForAnalyzerAssets()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "src",
            "SingletonDI.Attributes",
            "SingletonDI.Attributes.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain(
            @"..\SingletonDI.Generator\bin",
            project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            @"..\SingletonDI.Refactoring\bin",
            project,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BeforeTargets=\"_GetPackageFiles\"", project);
        Assert.Contains("GetTargetPath", project);
    }

    [Fact]
    public async Task PackUsesArtifactsPathForAnalyzerAssets()
    {
        // Packs the same project as PackageSmokeTests, and the two share one obj/ directory.
        await using var packageSmokeLock = await PackageSmokeLock.AcquireAsync(_output);
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "src",
            "SingletonDI.Attributes",
            "SingletonDI.Attributes.csproj");
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "SingletonDI.PackageContentTests",
            Guid.NewGuid().ToString("N"));
        var artifactsPath = Path.Combine(temporaryRoot, "artifacts");
        var packagePath = Path.Combine(temporaryRoot, "package");

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var packResult = await RunDotnetPackAsync(
                root,
                projectPath,
                artifactsPath,
                packagePath);
            Assert.True(
                packResult.ExitCode == 0,
                $"dotnet pack failed with exit code {packResult.ExitCode}.\n{packResult.Output}");

            var package = Assert.Single(
                Directory.EnumerateFiles(packagePath),
                path => string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase));
            using var archive = ZipFile.OpenRead(package);
            foreach (var assemblyName in AnalyzerAssemblyNames)
            {
                var packageEntry = archive.GetEntry($"analyzers/dotnet/cs/{assemblyName}");
                Assert.NotNull(packageEntry);

                var targetFile = Assert.Single(
                    Directory.EnumerateFiles(
                        Path.Combine(artifactsPath, "bin"),
                        assemblyName,
                        SearchOption.AllDirectories));
                await using var packageStream = packageEntry!.Open();
                var packageHash = await ComputeHashAsync(packageStream);
                var targetHash = await ComputeHashAsync(targetFile);
                Assert.Equal(targetHash, packageHash);
            }
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetPackAsync(
        string workingDirectory,
        string projectPath,
        string artifactsPath,
        string packagePath)
    {
        var result = await DotnetProcessRunner.RunAsync(
            workingDirectory,
            [
                "pack",
                projectPath,
                "--configuration",
                "Release",
                "--artifacts-path",
                artifactsPath,
                "--output",
                packagePath,
                "--nologo",
            ],
            DotnetProcessRunner.BuildTimeout);

        return (result.ExitCode, result.Output);
    }

    private static async Task<string> ComputeHashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await ComputeHashAsync(stream);
    }

    private static async Task<string> ComputeHashAsync(Stream stream)
    {
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
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
