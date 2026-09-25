using System.Reflection;
using SingletonDI.Generated;
using Xunit;

namespace SingletonDI.Tests;

[Collection("SingletonDI runtime")]
public sealed class SingletonDIInitializerTests
{
    private static readonly SemaphoreSlim TestGate = new(1, 1);

    [Fact]
    public async Task ShutdownManager_CancelKeyPressCancelsAndTerminatesOnce()
    {
        var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCount = 0;
        var terminateCount = 0;
        var exitCode = 0;
        var manager = new ShutdownManager(
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                disposeStarted.TrySetResult(true);
                return new ValueTask(releaseDispose.Task);
            },
            code =>
            {
                Interlocked.Increment(ref terminateCount);
                Volatile.Write(ref exitCode, code);
            });
        var cancelled = false;

        var shutdown = manager.HandleCancelKeyPressForTesting(() => cancelled = true);
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.Equal(0, Volatile.Read(ref terminateCount));

        releaseDispose.TrySetResult(true);
        await shutdown;
        Assert.Equal(130, Volatile.Read(ref exitCode));

        var secondShutdown = manager.HandleCancelKeyPressForTesting(() => cancelled = true);
        await secondShutdown;

        Assert.Equal(1, Volatile.Read(ref disposeCount));
        Assert.Equal(1, Volatile.Read(ref terminateCount));
    }

    [Theory]
    [InlineData(130)]
    [InlineData(143)]
    [InlineData(131)]
    public async Task ShutdownManager_PosixSignalCancelsAndUsesExitCode(int expectedExitCode)
    {
        var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCount = 0;
        var terminateCount = 0;
        var exitCode = 0;
        var manager = new ShutdownManager(
            () =>
            {
                Interlocked.Increment(ref disposeCount);
                disposeStarted.TrySetResult(true);
                return new ValueTask(releaseDispose.Task);
            },
            code =>
            {
                Interlocked.Increment(ref terminateCount);
                Volatile.Write(ref exitCode, code);
            });
        var cancelled = false;

        var shutdown = manager.HandlePosixSignalForTesting(() => cancelled = true, expectedExitCode);
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.Equal(0, Volatile.Read(ref terminateCount));

        releaseDispose.TrySetResult(true);
        await shutdown;
        Assert.Equal(expectedExitCode, Volatile.Read(ref exitCode));

        var secondShutdown = manager.HandlePosixSignalForTesting(() => cancelled = true, expectedExitCode);
        await secondShutdown;

        Assert.Equal(1, Volatile.Read(ref disposeCount));
        Assert.Equal(1, Volatile.Read(ref terminateCount));
    }

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
    public async Task InitializeAsync_RegistersShutdownHandlersBeforeInitializationCompletes()
    {
        await TestGate.WaitAsync();
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            var initializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            __SingletonDIHost__.RegisterProvider<IInitializationHandlerService, InitializationHandlerService>(
                static () => new InitializationHandlerService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    initializerStarted.TrySetResult(true);
                    await releaseInitializer.Task;
                },
                null,
                null);

            var initialization = SingletonDIInitializer.InitializeAsync();
            await initializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdownManagerField = typeof(SingletonDIInitializer).GetField(
                "_shutdownManager",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(shutdownManagerField);
            Assert.NotNull(shutdownManagerField!.GetValue(null));
            releaseInitializer.TrySetResult(true);
            await initialization;
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
    public async Task DisposeAsync_SupersedesReinitializationQueuedBehindActiveDisposal()
    {
        await TestGate.WaitAsync();
        var disposalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCount = 0;
        var disposeCount = 0;

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            __SingletonDIHost__.RegisterProvider<ISupersededService, SupersededService>(
                () =>
                {
                    Interlocked.Increment(ref createCount);
                    return new SupersededService();
                },
                Array.Empty<Type>(),
                null,
                null,
                async _ =>
                {
                    disposalStarted.TrySetResult(true);
                    await releaseDisposal.Task;
                    Interlocked.Increment(ref disposeCount);
                });

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            var firstDisposal = SingletonDIInitializer.DisposeAsync().AsTask();
            await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queuedInitialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            var secondDisposal = SingletonDIInitializer.DisposeAsync().AsTask();

            releaseDisposal.TrySetResult(true);
            await Task.WhenAll(firstDisposal, secondDisposal);
            var supersededException = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await queuedInitialization);

            Assert.Equal("Initialization was superseded by a disposal request.", supersededException.Message);
            Assert.Equal(1, Volatile.Read(ref createCount));
            Assert.Equal(1, Volatile.Read(ref disposeCount));
            Assert.Throws<InvalidOperationException>(() => __SingletonDIHost__.Resolve<ISupersededService>());
        }
        finally
        {
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
    public async Task InitializeAsync_AfterSupersededQueueWaitsForLatestDisposalAndSucceeds()
    {
        await TestGate.WaitAsync();
        var disposalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCount = 0;
        var disposeCount = 0;

        try
        {
            await SingletonDIInitializer.DisposeAsync();
            __SingletonDIHost__.RegisterProvider<IReplacementSupersededService, ReplacementSupersededService>(
                () =>
                {
                    Interlocked.Increment(ref createCount);
                    return new ReplacementSupersededService();
                },
                Array.Empty<Type>(),
                null,
                null,
                async _ =>
                {
                    if (Interlocked.Increment(ref disposeCount) == 1)
                    {
                        disposalStarted.TrySetResult(true);
                        await releaseDisposal.Task;
                    }
                });

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            var firstDisposal = SingletonDIInitializer.DisposeAsync().AsTask();
            await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var supersededInitialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            var secondDisposal = SingletonDIInitializer.DisposeAsync().AsTask();
            var replacementInitialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            releaseDisposal.TrySetResult(true);
            await Task.WhenAll(firstDisposal, secondDisposal);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await supersededInitialization);
            await replacementInitialization;

            Assert.Equal(2, Volatile.Read(ref createCount));
            Assert.Equal(1, Volatile.Read(ref disposeCount));
            Assert.NotNull(__SingletonDIHost__.Resolve<IReplacementSupersededService>());
        }
        finally
        {
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

    private interface IInitializationHandlerService
    {
    }

    private sealed class InitializationHandlerService : IInitializationHandlerService
    {
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

    private interface ISupersededService
    {
    }

    private sealed class SupersededService : ISupersededService
    {
    }

    private interface IReplacementSupersededService
    {
    }

    private sealed class ReplacementSupersededService : IReplacementSupersededService
    {
    }

    private interface IRetryPublicService
    {
    }

    private sealed class RetryPublicService : IRetryPublicService
    {
    }
}
