using Xunit;

namespace SingletonDI.Tests;

public sealed class PackageSmokeLockTests
{
    [Fact]
    public async Task AcquireAsync_ReportsTimeoutWhenTheLockIsStillHeldAfterTheBoundedWait()
    {
        var lockPath = CreateLockPath();

        await using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => PackageSmokeLock.AcquireAsync(
                    output: null,
                    lockPath,
                    lockAttempts: 3,
                    lockRetryDelayMilliseconds: 10));

            Assert.Contains(lockPath, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AcquireAsync_TakesTheLockOnceTheConcurrentHolderReleasesIt()
    {
        var lockPath = CreateLockPath();
        var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var queued = PackageSmokeLock.AcquireAsync(
            output: null,
            lockPath,
            lockAttempts: 200,
            lockRetryDelayMilliseconds: 10);
        await held.DisposeAsync();

        await using var acquired = await queued;
        Assert.NotNull(acquired);
    }

    private static string CreateLockPath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SingletonDI.PackageSmokeLockTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "packaging.lock");
    }
}
