using SingletonDI.InterProjectFixtures.RootApp;

namespace SingletonDI.Tests;

internal sealed record ProcessScenarioResult(
    bool ContractAndConcreteSame,
    bool ServiceDisposedBeforeDispose,
    bool ServiceDisposedAfterDispose,
    bool ExternalServiceDisposedAfterDispose,
    IReadOnlyList<string> LifecycleEvents);

internal static class ProcessScenarioRunner
{
    internal static async Task<ProcessScenarioResult> RunAsync()
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        var workingDirectory = Path.GetDirectoryName(assemblyPath)!;
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
