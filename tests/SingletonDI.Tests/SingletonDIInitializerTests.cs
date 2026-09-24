using System.Reflection;
using SingletonDI.Generated;
using Xunit;

namespace SingletonDI.Tests;

[Collection("SingletonDI runtime")]
public sealed class SingletonDIInitializerTests
{
    private static readonly SemaphoreSlim TestGate = new(1, 1);

    [Fact]
    public async Task InitializeAsync_ConcurrentTrueRequestRegistersShutdownHandlers()
    {
        await TestGate.WaitAsync();
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            var initializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            __SingletonDIHost__.RegisterProvider<IInitializerService, InitializerService>(
                static () => new InitializerService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    initializerStarted.TrySetResult(true);
                    await releaseInitializer.Task;
                },
                null,
                null);

            var withoutHandlers = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            await initializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var withHandlers = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: true);
            releaseInitializer.TrySetResult(true);
            await Task.WhenAll(withoutHandlers, withHandlers);

            var shutdownManagerField = typeof(SingletonDIInitializer).GetField(
                "_shutdownManager",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(shutdownManagerField);
            Assert.NotNull(shutdownManagerField!.GetValue(null));
        }
        finally
        {
            releaseInitializer.TrySetResult(true);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_QueuesReinitializationUntilDisposalCompletes()
    {
        await TestGate.WaitAsync();
        var initializationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitialization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCount = 0;

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            __SingletonDIHost__.RegisterProvider<IOverlapService, OverlapService>(
                () =>
                {
                    Interlocked.Increment(ref createCount);
                    return new OverlapService();
                },
                Array.Empty<Type>(),
                async _ =>
                {
                    initializationStarted.TrySetResult(true);
                    await releaseInitialization.Task;
                },
                _ =>
                {
                    disposalStarted.TrySetResult(true);
                    releaseDisposal.Task.GetAwaiter().GetResult();
                },
                null);

            var initialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            await initializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = SingletonDIInitializer.DisposeAsync().AsTask();
            var reinitialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.False(reinitialization.IsCompleted);
            releaseInitialization.TrySetResult(true);
            await initialization;
            Assert.False(reinitialization.IsCompleted);

            await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseDisposal.TrySetResult(true);
            await disposal;
            await reinitialization;

            Assert.Equal(2, createCount);
        }
        finally
        {
            releaseInitialization.TrySetResult(true);
            releaseDisposal.TrySetResult(true);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_FailedInitializationCanRetry()
    {
        await TestGate.WaitAsync();
        var attempts = 0;

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            __SingletonDIHost__.RegisterProvider<IRetryPublicService, RetryPublicService>(
                static () => new RetryPublicService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    Interlocked.Increment(ref attempts);
                    if (attempts == 1)
                    {
                        throw new InvalidOperationException("first initialization failed");
                    }
                },
                null,
                null);

            var firstException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));
            Assert.Equal("first initialization failed", firstException.Message);

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.Equal(2, attempts);
        }
        finally
        {
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            TestGate.Release();
        }
    }

    private interface IInitializerService
    {
    }

    private sealed class InitializerService : IInitializerService
    {
    }

    private interface IOverlapService
    {
    }

    private sealed class OverlapService : IOverlapService
    {
    }

    private interface IRetryPublicService
    {
    }

    private sealed class RetryPublicService : IRetryPublicService
    {
    }
}
