using Xunit;
using Xunit.Abstractions;

namespace SingletonDI.Tests;

/// <summary>
/// Serializes the packaging tests against each other.
/// </summary>
/// <remarks>
/// Both packaging classes run <c>dotnet pack</c> on the same
/// <c>src/SingletonDI.Attributes/SingletonDI.Attributes.csproj</c>. They use different
/// <c>--artifacts-path</c> and <c>--output</c> values, so their package trees do not collide, but
/// the project shares one <c>obj/</c> directory, and two concurrent MSBuild processes writing the
/// same <c>project.assets.json</c> and intermediate assemblies make the run flaky.
/// </remarks>
internal static class PackageSmokeLock
{
    // Both packaging classes run dotnet pack and then build and run a consumer app against the
    // package, which takes well over two minutes on a cold SDK. The bound has to exceed the longest
    // operation it serializes, otherwise the class that waits reports a held lock on every run
    // rather than on the rare orphaned testhost this message is about.
    private const int LockAttempts = 3000;
    private const int LockLogInterval = 100;
    private const int LockRetryDelay = 100;

    /// <summary>
    /// Takes the packaging lock, waiting for a concurrent run to release it.
    /// </summary>
    /// <param name="output">Receives a line per wait interval so a queued run is visible.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="TimeoutException">The lock was still held after the bounded wait.</exception>
    internal static Task<IAsyncDisposable> AcquireAsync(
        ITestOutputHelper? output,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            output,
            Path.Combine(Path.GetTempPath(), "SingletonDI.PackageSmokeTests.lock"),
            LockAttempts,
            LockRetryDelay,
            cancellationToken);

    /// <summary>
    /// Takes a packaging lock at a caller-supplied path with a caller-supplied bound.
    /// </summary>
    /// <param name="output">Receives a line per wait interval so a queued run is visible.</param>
    /// <param name="lockPath">The file that represents the lock.</param>
    /// <param name="lockAttempts">How many times to try the file before giving up.</param>
    /// <param name="lockRetryDelayMilliseconds">How long to wait between attempts.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="TimeoutException">
    /// The lock was still held after <paramref name="lockAttempts"/> attempts.
    /// </exception>
    internal static async Task<IAsyncDisposable> AcquireAsync(
        ITestOutputHelper? output,
        string lockPath,
        int lockAttempts,
        int lockRetryDelayMilliseconds,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= lockAttempts; attempt++)
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
                // The last attempt leaves the loop through the shared throw, so a run that never
                // takes the lock reports the bounded wait it was promised rather than the file
                // sharing violation of that final attempt.
                if (attempt == lockAttempts)
                {
                    break;
                }

                // A testhost orphaned by a killed run holds the lock indefinitely.
                if (attempt % LockLogInterval == 0)
                {
                    output?.WriteLine(
                        $"Waiting for the package smoke lock at {lockPath}, attempt {attempt}/{lockAttempts}.");
                }

                await Task.Delay(lockRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"The package smoke lock at {lockPath} was still held after {lockAttempts} attempts. " +
            "A testhost orphaned by a killed run keeps it; delete the file once no test run is active.");
    }
}
