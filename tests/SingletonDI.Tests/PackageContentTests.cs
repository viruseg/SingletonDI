using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;

namespace SingletonDI.Tests;

public sealed class PackageContentTests
{
    private static readonly string[] AnalyzerAssemblyNames =
    [
        "SingletonDI.Generator.dll",
        "SingletonDI.Refactoring.dll",
    ];

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
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(artifactsPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("--nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet pack.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, output + error);
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
