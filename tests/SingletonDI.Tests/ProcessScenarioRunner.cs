namespace SingletonDI.Tests;

internal sealed record ProcessScenarioResult(
    bool ContractAndConcreteSame,
    bool ServiceDisposedBeforeDispose,
    bool ServiceDisposedAfterDispose,
    bool ExternalServiceDisposedAfterDispose,
    IReadOnlyList<string> LifecycleEvents);

internal static class ProcessScenarioRunner
{
    private const string FixtureAssemblyName = "RootApp.dll";

    internal static async Task<ProcessScenarioResult> RunAsync()
    {
        // The fixture is located by file name rather than through typeof(Program). Touching the type
        // would run the composition root's module initializer inside the test host, where the
        // process-wide container may already be initialized or disposed by an earlier test.
        var workingDirectory = AppContext.BaseDirectory;
        var assemblyPath = Path.Combine(workingDirectory, FixtureAssemblyName);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"{FixtureAssemblyName} was not found next to the test assembly.", assemblyPath);
        }

        var result = await DotnetProcessRunner.RunAsync(
            workingDirectory,
            [assemblyPath],
            DotnetProcessRunner.RunTimeout);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"RootApp fixture exited with code {result.ExitCode}.{Environment.NewLine}{result.Output}");
        }

        return Parse(result.StandardOutput);
    }

    private static ProcessScenarioResult Parse(string output)
    {
        var lifecycleEvents = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("EVENT|", StringComparison.Ordinal))
            .Select(line => line[6..])
            .ToArray();
        var identityLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("IDENTITY|", StringComparison.Ordinal));
        var resultLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("RESULT|", StringComparison.Ordinal));
        var identityParts = identityLine[9..].Split(',');
        var resultParts = resultLine[7..].Split(',');
        if (identityParts.Length != 1 || resultParts.Length != 3)
        {
            throw new InvalidOperationException("RootApp fixture returned malformed scenario data.");
        }

        return new ProcessScenarioResult(
            bool.Parse(identityParts[0]),
            bool.Parse(resultParts[0]),
            bool.Parse(resultParts[1]),
            bool.Parse(resultParts[2]),
            lifecycleEvents);
    }
}
