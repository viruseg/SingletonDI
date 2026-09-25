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
    private const int LockAttempts = 600;
    private const int LockLogInterval = 100;
    private const int LockRetryDelay = 100;

    /// <summary>
    /// Takes the packaging lock, waiting for a concurrent run to release it.
    /// </summary>
    /// <param name="output">Receives a line per wait interval so a queued run is visible.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="TimeoutException">The lock was still held after the bounded wait.</exception>
    internal static async Task<IAsyncDisposable> AcquireAsync(
        ITestOutputHelper? output,
        CancellationToken cancellationToken = default)
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "SingletonDI.PackageSmokeTests.lock");
        for (var attempt = 1; attempt <= LockAttempts; attempt++)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (attempt < LockAttempts)
            {
                // A testhost orphaned by a killed run holds the lock indefinitely.
                if (attempt % LockLogInterval == 0)
                {
                    output?.WriteLine(
                        $"Waiting for the package smoke lock at {lockPath}, attempt {attempt}/{LockAttempts}.");
                }

                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"The package smoke lock at {lockPath} was still held after {LockAttempts} attempts. " +
            "A testhost orphaned by a killed run keeps it; delete the file once no test run is active.");
    }
}
