using System.Collections.Concurrent;
using System.Diagnostics;

namespace SingletonDI.Tests;

/// <summary>
/// Captures the outcome of a child <c>dotnet</c> process started by a test.
/// </summary>
/// <param name="ExitCode">The exit code reported by the process.</param>
/// <param name="StandardOutput">
/// Standard output only, truncated to whatever had been read when the runner stopped waiting. Use it when the
/// caller parses the output as a protocol.
/// </param>
/// <param name="Output">
/// Standard output followed by standard error. Use it for diagnostics, where losing the distinction is harmless.
/// </param>
internal sealed record DotnetProcessResult(int ExitCode, string StandardOutput, string Output);

/// <summary>
/// Starts child <c>dotnet</c> processes under a hard timeout so a stuck build or fixture cannot hang the whole test run.
/// </summary>
internal static class DotnetProcessRunner
{
    /// <summary>
    /// Budget for MSBuild work such as <c>pack</c> and <c>build</c>, which is legitimately slow against a cold package cache.
    /// </summary>
    internal static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Budget for commands that only start an assembly that has already been built.
    /// </summary>
    internal static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan StreamDrainGrace = TimeSpan.FromSeconds(10);
    private static readonly string[] InheritedMsBuildVariables =
    [
        "MSBuildSDKsPath",
        "MSBuildExtensionsPath",
        "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
    ];

    /// <summary>
    /// Runs <c>dotnet</c> with the supplied arguments and returns its exit code and output.
    /// </summary>
    /// <param name="workingDirectory">Directory used as the working directory of the process.</param>
    /// <param name="arguments">Arguments passed to <c>dotnet</c>, without the executable name.</param>
    /// <param name="timeout">
    /// How long the process may run before it and its descendants are killed. Covers process exit only; reading the
    /// redirected output is separately bounded by <see cref="StreamDrainGrace"/>.
    /// </param>
    /// <param name="environment">Additional environment variables applied to the child process.</param>
    /// <returns>The exit code, standard output and combined output of the process.</returns>
    /// <exception cref="TimeoutException">
    /// The process was still running after <paramref name="timeout"/>. It is killed along with its descendants before
    /// the exception is thrown, so no orphan keeps the redirected pipes or the package smoke lock alive.
    /// </exception>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    internal static async Task<DotnetProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        using var process = Process.Start(CreateStartInfo(workingDirectory, arguments, environment))
            ?? throw new InvalidOperationException($"Could not start 'dotnet {string.Join(' ', arguments)}'.");

        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();
        var outputCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) => Capture(e.Data, standardOutput, outputCompleted);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, standardError, errorCompleted);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var expiry = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(expiry.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await DrainAsync(outputCompleted.Task, errorCompleted.Task);
            throw new TimeoutException(
                $"'dotnet {string.Join(' ', arguments)}' did not exit within {timeout} " +
                $"in '{workingDirectory}'." +
                $"{Environment.NewLine}{Join(standardOutput.Concat(standardError))}");
        }

        await DrainAsync(outputCompleted.Task, errorCompleted.Task);
        return new DotnetProcessResult(
            process.ExitCode,
            Join(standardOutput),
            Join(standardOutput.Concat(standardError)));
    }

    private static ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The enclosing dotnet test run exports these; a nested build has to resolve SDKs on its own.
        foreach (var variable in InheritedMsBuildVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        // Without this the build starts VBCSCompiler, which outlives dotnet, inherits the redirected pipes and
        // leaves ReadToEnd-style readers blocked long after the build itself finished.
        startInfo.Environment["UseSharedCompilation"] = "false";

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    private static void Capture(string? line, ConcurrentQueue<string> sink, TaskCompletionSource completed)
    {
        if (line is null)
        {
            completed.TrySetResult();
            return;
        }

        sink.Enqueue(line);
    }

    private static async Task DrainAsync(Task outputCompleted, Task errorCompleted)
    {
        // A descendant that inherited the output handles can keep them open after dotnet exited, so end-of-stream
        // is awaited only as a best effort and the captured output is used either way.
        await Task.WhenAny(
            Task.WhenAll(outputCompleted, errorCompleted),
            Task.Delay(StreamDrainGrace));
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string Join(IEnumerable<string> lines)
    {
        return string.Join(Environment.NewLine, lines);
    }
}
