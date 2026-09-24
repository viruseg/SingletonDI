using System.Diagnostics;
using Xunit;

namespace SingletonDI.Tests;

public sealed class PackageSmokeTests
{
    private const string PackageVersion = "1.1.0";

    private static readonly (string Directory, string Version)[] SdkDirectories =
    [
        ("Sdk8", "8.0.131"),
        ("Sdk9", "9.0.121"),
        ("Sdk10", "10.0.401"),
    ];

    [Fact]
    public async Task PackedConsumerBuildsAndRunsOnSupportedSdks()
    {
        using var packageSmokeLock = await AcquirePackageSmokeLock();
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

    private static async Task<FileStream> AcquirePackageSmokeLock()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "SingletonDI.PackageSmokeTests.lock");
        for (var attempt = 0; attempt < 1800; attempt++)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException("Timed out waiting for the package smoke test lock.");
    }

    private static async Task<ProcessResult> RunDotnetAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string? nugetPackages = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment.Remove("MSBuildSDKsPath");
        startInfo.Environment.Remove("MSBuildExtensionsPath");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (nugetPackages is not null)
        {
            startInfo.Environment["NUGET_PACKAGES"] = nugetPackages;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutputTask;
        var error = await standardErrorTask;
        return new ProcessResult(process.ExitCode, output + error);
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
