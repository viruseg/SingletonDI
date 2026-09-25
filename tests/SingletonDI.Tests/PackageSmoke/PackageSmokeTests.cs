using Xunit;
using Xunit.Abstractions;

namespace SingletonDI.Tests;

[Trait("Category", "Packaging")]
public sealed class PackageSmokeTests
{
    private const string PackageVersion = "1.1.0";
    private static readonly (string Directory, string Version)[] SdkDirectories =
    [
        ("Sdk10", "10.0.401"),
    ];

    private readonly ITestOutputHelper _output;

    public PackageSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task PackedConsumerBuildsAndRunsOnSupportedSdks()
    {
        await using var packageSmokeLock = await PackageSmokeLock.AcquireAsync(_output);
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "SingletonDI.PackageSmokeTests",
            Guid.NewGuid().ToString("N"));
        var packageOutput = Path.Combine(temporaryRoot, "package");
        var packArtifacts = Path.Combine(temporaryRoot, "pack-artifacts");
        var packagePath = Path.Combine(packageOutput, $"SingletonDI.{PackageVersion}.nupkg");
        var runtimeProject = Path.Combine(
            repositoryRoot,
            "src",
            "SingletonDI.Attributes",
            "SingletonDI.Attributes.csproj");
        var consumerFixture = Path.Combine(
            repositoryRoot,
            "tests",
            "SingletonDI.Tests",
            "PackageSmoke",
            "ConsumerApp");

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var packResult = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    runtimeProject,
                    "--configuration",
                    "Release",
                    "--artifacts-path",
                    packArtifacts,
                    "--output",
                    packageOutput,
                    "--nologo",
                ]);
            Assert.True(
                packResult.ExitCode == 0,
                $"dotnet pack failed with exit code {packResult.ExitCode}.\n{packResult.Output}");
            Assert.True(File.Exists(packagePath), $"Package was not created at {packagePath}.");

            foreach (var (sdkDirectory, expectedSdkVersion) in SdkDirectories)
            {
                var consumerRoot = Path.Combine(temporaryRoot, sdkDirectory);
                var consumerProjectRoot = Path.Combine(consumerRoot, "ConsumerApp");
                CopyConsumerFixture(consumerFixture, consumerProjectRoot);

                var localFeed = Path.Combine(consumerProjectRoot, "local-feed");
                Directory.CreateDirectory(localFeed);
                File.Copy(
                    packagePath,
                    Path.Combine(localFeed, $"SingletonDI.{PackageVersion}.nupkg"));

                var nugetPackages = Path.Combine(temporaryRoot, $"{sdkDirectory}-packages");
                var sdkWorkingDirectory = Path.Combine(consumerProjectRoot, sdkDirectory);
                var versionResult = await RunDotnetAsync(
                    sdkWorkingDirectory,
                    ["--version"],
                    nugetPackages);
                Assert.True(
                    versionResult.ExitCode == 0,
                    $"{sdkDirectory} version query failed with exit code {versionResult.ExitCode}.\n{versionResult.Output}");
                Assert.Equal(expectedSdkVersion, versionResult.Output.Trim());

                var buildResult = await RunDotnetAsync(
                    sdkWorkingDirectory,
                    [
                        "build",
                        "../ConsumerApp.csproj",
                        "--configuration",
                        "Release",
                        "--force",
                        "--nologo",
                    ],
                    nugetPackages);
                Assert.True(
                    buildResult.ExitCode == 0,
                    $"{sdkDirectory} build failed with exit code {buildResult.ExitCode}.\n{buildResult.Output}");

                var runResult = await RunDotnetAsync(
                    sdkWorkingDirectory,
                    [
                        "run",
                        "--project",
                        "../ConsumerApp.csproj",
                        "--configuration",
                        "Release",
                        "--no-build",
                        "--no-restore",
                        "--nologo",
                    ],
                    nugetPackages);
                Assert.True(
                    runResult.ExitCode == 0,
                    $"{sdkDirectory} run failed with exit code {runResult.ExitCode}.\n{runResult.Output}");
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

    private static void CopyConsumerFixture(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var fileName in new[]
                 {
                     "ConsumerApp.csproj",
                     "Program.cs",
                     "NuGet.Config",
                 })
        {
            File.Copy(
                Path.Combine(sourceRoot, fileName),
                Path.Combine(destinationRoot, fileName));
        }

        foreach (var (sdkDirectory, _) in SdkDirectories)
        {
            var destinationSdkDirectory = Path.Combine(destinationRoot, sdkDirectory);
            Directory.CreateDirectory(destinationSdkDirectory);
            File.Copy(
                Path.Combine(sourceRoot, sdkDirectory, "global.json"),
                Path.Combine(destinationSdkDirectory, "global.json"));
        }
    }

    private static async Task<ProcessResult> RunDotnetAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? nugetPackages = null)
    {
        var environment = nugetPackages is null
            ? null
            : new Dictionary<string, string?> { ["NUGET_PACKAGES"] = nugetPackages };
        var result = await DotnetProcessRunner.RunAsync(
            workingDirectory,
            arguments,
            arguments.Contains("run") ? DotnetProcessRunner.RunTimeout : DotnetProcessRunner.BuildTimeout,
            environment);

        return new ProcessResult(result.ExitCode, result.Output);
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

    private sealed record ProcessResult(int ExitCode, string Output);
}
