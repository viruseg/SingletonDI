using System.Reflection;
using System.Runtime.InteropServices;
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
    public async Task ShutdownManager_OneInterruptOnPosixPlatformAwaitsDisposalInsteadOfTerminatingImmediately()
    {
        var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var termination = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminateCount = 0;
        var signalSource = new CapturingShutdownSignalSource();
        var manager = new ShutdownManager(
            () =>
            {
                disposeStarted.TrySetResult(true);
                return new ValueTask(releaseDispose.Task);
            },
            code =>
            {
                Interlocked.Increment(ref terminateCount);
                termination.TrySetResult(code);
            },
            signalSource,
            TimeSpan.FromSeconds(5));

        manager.Register();

        // A single Ctrl+C reaches every handler the platform registered, so the same interrupt is
        // delivered to more than one of them. A shutdown must not read the second delivery as the
        // user insisting on an immediate exit while the first disposal is still in flight.
        signalSource.RaiseInterrupt();

        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref terminateCount));

        releaseDispose.TrySetResult(true);

        Assert.Equal(130, await termination.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref terminateCount));
    }

    [Fact]
    public void ShutdownManager_Register_RollsBackEveryHandlerAfterPartialFailure()
    {
        var signalSource = new FailingShutdownSignalSource(PosixSignal.SIGTERM);
        var manager = new ShutdownManager(
            static () => ValueTask.CompletedTask,
            static _ => { },
            signalSource);

        var exception = Assert.Throws<InvalidOperationException>(() => manager.Register());

        Assert.Equal("POSIX registration failed", exception.Message);
        Assert.Equal(0, signalSource.ActiveRegistrations);

        signalSource.FailOnSignal = null;
        manager.Register();

        // Process exit plus SIGINT, SIGTERM and SIGQUIT. A POSIX source needs no console handler,
        // so the count is the signal registrations only.
        Assert.Equal(4, signalSource.ActiveRegistrations);

        manager.Dispose();
        Assert.Equal(0, signalSource.ActiveRegistrations);
    }

    [Fact]
    public async Task ShutdownManager_TerminatesWhenDisposalExceedsTimeout()
    {
        var disposalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var termination = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminateCount = 0;
        var manager = new ShutdownManager(
            async () =>
            {
                disposalStarted.TrySetResult(true);
                await releaseDisposal.Task;
            },
            code =>
            {
                Interlocked.Increment(ref terminateCount);
                termination.TrySetResult(code);
            },
            new FailingShutdownSignalSource(null),
            TimeSpan.FromMilliseconds(50));

        var shutdown = manager.HandlePosixSignalForTesting(
            static () => { },
            143);

        await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(143, await termination.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await shutdown;

        releaseDisposal.TrySetResult(true);
        Assert.Equal(1, Volatile.Read(ref terminateCount));
    }

    [Fact]
    public async Task ShutdownManager_SecondSignalTerminatesImmediatelyAndOnlyOnce()
    {
        var disposalStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var termination = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminateCount = 0;
        var manager = new ShutdownManager(
            async () =>
            {
                disposalStarted.TrySetResult(true);
                await releaseDisposal.Task;
            },
            code =>
            {
                Interlocked.Increment(ref terminateCount);
                termination.TrySetResult(code);
            },
            new FailingShutdownSignalSource(null),
            TimeSpan.FromSeconds(5));

        var firstShutdown = manager.HandlePosixSignalForTesting(static () => { }, 130);
        await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondShutdown = manager.HandlePosixSignalForTesting(static () => { }, 143);

        Assert.Equal(130, await termination.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        releaseDisposal.TrySetResult(true);
        await Task.WhenAll(firstShutdown, secondShutdown);

        Assert.Equal(1, Volatile.Read(ref terminateCount));
    }

    [Fact]
    public async Task InitializeAsync_TrueRequestRegistersShutdownHandlers()
    {
        await TestGate.WaitAsync();
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            __SingletonDIHost__.ResetForTesting();
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

            var initialization = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: true);
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
    public async Task InitializeAsync_RegistersShutdownHandlersBeforeInitializationCompletes()
    {
        await TestGate.WaitAsync();
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            __SingletonDIHost__.ResetForTesting();
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
    public async Task InitializeAsync_RejectsEveryCallAfterTheFirst()
    {
        // A singleton exists for the lifetime of the application. A second initialization would
        // build a second graph over the same registrations, and returning a task instead of
        // throwing would let the caller believe the container was rebuilt.
        await TestGate.WaitAsync();
        var createCount = 0;

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<IOverlapService, OverlapService>(
                () =>
                {
                    Interlocked.Increment(ref createCount);
                    return new OverlapService();
                },
                Array.Empty<Type>(),
                null,
                null,
                null);

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            var afterSuccess = Record.Exception(
                () => { _ = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false); });
            Assert.IsType<InvalidOperationException>(afterSuccess);
            Assert.Equal("The singleton container has already been initialized.", afterSuccess!.Message);

            await SingletonDIInitializer.DisposeAsync();
            var afterDisposal = Record.Exception(
                () => { _ = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false); });
            Assert.IsType<InvalidOperationException>(afterDisposal);
            Assert.Equal("The singleton container has already been initialized.", afterDisposal!.Message);

            Assert.Equal(1, Volatile.Read(ref createCount));
            Assert.Throws<InvalidOperationException>(() => __SingletonDIHost__.Resolve<IOverlapService>());
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

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_RejectsDisposeFromProviderInitializer()
    {
        await TestGate.WaitAsync();
        var rejected = false;

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<IReentrantInitializerService, ReentrantInitializerService>(
                static () => new ReentrantInitializerService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    try
                    {
                        var disposal = SingletonDIInitializer.DisposeAsync().AsTask();
                        if (await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromMilliseconds(250))) == disposal)
                        {
                            await disposal;
                        }
                    }
                    catch (InvalidOperationException exception)
                        when (exception.Message == "Lifecycle operations cannot be called from provider callbacks.")
                    {
                        rejected = true;
                    }
                },
                null,
                null);

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.True(rejected);
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

    [Fact]
    public async Task InitializeAsync_RejectsInitializeFromProviderInitializer()
    {
        await TestGate.WaitAsync();
        var rejected = false;

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<IReentrantInitializeService, ReentrantInitializeService>(
                static () => new ReentrantInitializeService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    try
                    {
                        var initialization = SingletonDIInitializer.InitializeAsync(
                            registerShutdownHandlers: false);
                        if (await Task.WhenAny(initialization, Task.Delay(TimeSpan.FromMilliseconds(250))) ==
                            initialization)
                        {
                            await initialization;
                        }
                    }
                    catch (InvalidOperationException exception)
                        when (exception.Message == "Lifecycle operations cannot be called from provider callbacks.")
                    {
                        rejected = true;
                    }
                },
                null,
                null);

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.True(rejected);
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

    // Registrations survive ResetForTesting because a generated module fills them once per process,
    // so a provider that reads startup data or fails on purpose is registered once and armed only
    // while the test that wants it runs.
    private static int _retryFailureArmed;
    private static int _probeRegistered;
    private static int _probeReadsStartupData;
    private static int _probeInitializerFailure;
    private static string? _valueSeenInFactory;
    private static string? _valueSeenInInitializer;

    private static void EnsureStartupDataProbe()
    {
        if (Interlocked.Exchange(ref _probeRegistered, 1) != 0)
        {
            return;
        }

        __SingletonDIHost__.RegisterProvider<IStartupDataProbeService, StartupDataProbeService>(
            static () => new StartupDataProbeService(),
            Array.Empty<Type>(),
            async probe => await probe.InitializeAsync(),
            null,
            null);
    }

    [Fact]
    public async Task InitializeAsync_LetsAProviderReadStartupDataFromItsFactoryAndInitializer()
    {
        await TestGate.WaitAsync();

        try
        {
            __SingletonDIHost__.ResetForTesting();
            EnsureStartupDataProbe();
            Volatile.Write(ref _probeReadsStartupData, 1);
            SingletonDIStartupData.Set("connection", "server=db");

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.NotNull(SingletonDIInitializer.Resolve<IStartupDataProbeService>());
            Assert.Equal("server=db", _valueSeenInFactory);
            Assert.Equal("server=db", _valueSeenInInitializer);
        }
        finally
        {
            Volatile.Write(ref _probeReadsStartupData, 0);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_ReleasesStartupDataAndDisposesItsValuesWhenItCompletes()
    {
        await TestGate.WaitAsync();
        var synchronous = new StartupDataDisposable();
        var asynchronous = new StartupDataAsyncDisposable();

        try
        {
            __SingletonDIHost__.ResetForTesting();
            EnsureStartupDataProbe();
            SingletonDIStartupData.Set("sync", synchronous);
            SingletonDIStartupData.Set("async", asynchronous);
            SingletonDIStartupData.Set("plain", 42);

            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

            Assert.Equal(1, synchronous.DisposeCount);
            Assert.Equal(1, asynchronous.AsyncDisposeCount);
            Assert.Equal(0, asynchronous.DisposeCount);
            Assert.Throws<InvalidOperationException>(() => SingletonDIStartupData.Get<int>("plain"));
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

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_ReleasesStartupDataWhenInitializationFails()
    {
        await TestGate.WaitAsync();
        var startupValue = new StartupDataDisposable();

        try
        {
            __SingletonDIHost__.ResetForTesting();
            EnsureStartupDataProbe();
            SingletonDIStartupData.Set("value", startupValue);
            Volatile.Write(ref _probeInitializerFailure, 1);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));

            Assert.Contains("failed to initialize", exception.Message);
            Assert.Equal(1, startupValue.DisposeCount);
            Assert.Throws<InvalidOperationException>(
                () => SingletonDIStartupData.Get<StartupDataDisposable>("value"));
        }
        finally
        {
            Volatile.Write(ref _probeInitializerFailure, 0);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_KeepsTheInitializationFailureWhenReleasingStartupDataFails()
    {
        await TestGate.WaitAsync();

        try
        {
            __SingletonDIHost__.ResetForTesting();
            EnsureStartupDataProbe();
            SingletonDIStartupData.Set("value", new StartupDataDisposable(failOnDispose: true));
            Volatile.Write(ref _probeInitializerFailure, 1);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));

            Assert.Contains("failed to initialize", exception.Message);
            var cleanupFailure = Assert.IsType<InvalidOperationException>(
                exception.Data[SingletonDIStartupData.CleanupFailureKey]);
            Assert.Contains("boom", cleanupFailure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Volatile.Write(ref _probeInitializerFailure, 0);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_RejectsSetWhileProviderCodeIsRunning()
    {
        await TestGate.WaitAsync();
        var factoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<ILateWriteFactoryService, LateWriteFactoryService>(
                () =>
                {
                    factoryEntered.TrySetResult(true);
                    releaseFactory.Task.GetAwaiter().GetResult();
                    return new LateWriteFactoryService();
                },
                Array.Empty<Type>(),
                null,
                null,
                null);

            var initialization = Task.Run(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));
            await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var exception = Assert.Throws<InvalidOperationException>(
                () => SingletonDIStartupData.Set("late", 1));
            Assert.Contains("initialization", exception.Message, StringComparison.OrdinalIgnoreCase);

            releaseFactory.TrySetResult(true);
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseFactory.TrySetResult(true);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    [Fact]
    public async Task InitializeAsync_FailedInitializationCannotBeRetried()
    {
        await TestGate.WaitAsync();
        var attempts = 0;

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<IRetryPublicService, RetryPublicService>(
                static () => new RetryPublicService(),
                Array.Empty<Type>(),
                async _ =>
                {
                    Interlocked.Increment(ref attempts);
                    if (Volatile.Read(ref _retryFailureArmed) != 0)
                    {
                        throw new InvalidOperationException("first initialization failed");
                    }
                },
                null,
                null);

            Volatile.Write(ref _retryFailureArmed, 1);
            var firstException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));
            Assert.Contains("failed to initialize", firstException.Message);
            Assert.Equal("first initialization failed", firstException.InnerException?.Message);

            var retryException = Record.Exception(
                () => { _ = SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false); });
            Assert.IsType<InvalidOperationException>(retryException);
            Assert.Equal("Initialization failed and cannot be retried.", retryException!.Message);
            Assert.Equal(1, attempts);
        }
        finally
        {
            Volatile.Write(ref _retryFailureArmed, 0);
            try
            {
                await SingletonDIInitializer.DisposeAsync();
            }
            catch
            {
            }

            __SingletonDIHost__.ResetForTesting();
            TestGate.Release();
        }
    }

    private sealed class FailingShutdownSignalSource : IShutdownSignalSource
    {
        private int _activeRegistrations;

        internal FailingShutdownSignalSource(PosixSignal? failOnSignal)
        {
            FailOnSignal = failOnSignal;
        }

        internal PosixSignal? FailOnSignal { get; set; }

        internal int ActiveRegistrations => Volatile.Read(ref _activeRegistrations);

        public bool SupportsPosixSignals => true;

        public IDisposable RegisterProcessExit(Action handler)
        {
            return Register();
        }

        public IDisposable RegisterCancelKeyPress(Action<ConsoleCancelEventArgs> handler)
        {
            return Register();
        }

        public IDisposable RegisterPosixSignal(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            if (signal == FailOnSignal)
            {
                throw new InvalidOperationException("POSIX registration failed");
            }

            return Register();
        }

        private IDisposable Register()
        {
            Interlocked.Increment(ref _activeRegistrations);
            return new CallbackRegistration(() => Interlocked.Decrement(ref _activeRegistrations));
        }

        private sealed class CallbackRegistration : IDisposable
        {
            private Action? _dispose;

            internal CallbackRegistration(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
            }
        }
    }

    private sealed class CapturingShutdownSignalSource : IShutdownSignalSource
    {
        private readonly List<Action<ConsoleCancelEventArgs>> _cancelKeyPressHandlers = [];
        private readonly Dictionary<PosixSignal, List<Action<PosixSignalContext>>> _posixHandlers = [];

        internal int RegisteredHandlerCount =>
            _cancelKeyPressHandlers.Count + _posixHandlers.Values.Sum(handlers => handlers.Count);

        public bool SupportsPosixSignals => true;

        public IDisposable RegisterProcessExit(Action handler) => new CallbackRegistration(() => { });

        public IDisposable RegisterCancelKeyPress(Action<ConsoleCancelEventArgs> handler)
        {
            _cancelKeyPressHandlers.Add(handler);
            return new CallbackRegistration(
                () => _cancelKeyPressHandlers.Remove(handler));
        }

        public IDisposable RegisterPosixSignal(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            if (!_posixHandlers.TryGetValue(signal, out var handlers))
            {
                handlers = [];
                _posixHandlers[signal] = handlers;
            }

            handlers.Add(handler);
            return new CallbackRegistration(() => handlers.Remove(handler));
        }

        /// <summary>
        /// Delivers one interrupt to every handler a platform would route it to, which on a POSIX
        /// platform is both the console cancel-key handler and the registered signal handler.
        /// </summary>
        internal void RaiseInterrupt()
        {
            foreach (var handler in _cancelKeyPressHandlers.ToArray())
            {
                handler((ConsoleCancelEventArgs)RuntimeHelpers.GetUninitializedObject(
                    typeof(ConsoleCancelEventArgs)));
            }

            if (!_posixHandlers.TryGetValue(PosixSignal.SIGINT, out var interruptHandlers))
            {
                return;
            }

            foreach (var handler in interruptHandlers.ToArray())
            {
                handler((PosixSignalContext)RuntimeHelpers.GetUninitializedObject(
                    typeof(PosixSignalContext)));
            }
        }

        private sealed class CallbackRegistration : IDisposable
        {
            private Action? _dispose;

            internal CallbackRegistration(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_KeepsTheContainerUsableWhenShutdownHandlerRegistrationFails()
    {
        await TestGate.WaitAsync();

        try
        {
            __SingletonDIHost__.ResetForTesting();
            SetShutdownSignalSource(new FailingShutdownSignalSource(PosixSignal.SIGINT));
            __SingletonDIHost__.RegisterProvider<ISignalSubscriptionService, SignalSubscriptionService>(
                static () => new SignalSubscriptionService(),
                Array.Empty<Type>(),
                null,
                null,
                null);

            // The graph is already built by the time the handlers are subscribed, so failing here
            // would report a failed initialization for a container that is serving instances.
            await SingletonDIInitializer.InitializeAsync();
            Assert.NotNull(SingletonDIInitializer.Resolve<ISignalSubscriptionService>());

            await SingletonDIInitializer.DisposeAsync();
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

            SetShutdownSignalSource(null);
            TestGate.Release();
        }
    }

    private static void SetShutdownSignalSource(IShutdownSignalSource? signalSource)
    {
        typeof(SingletonDIInitializer)
            .GetField("_signalSource", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, signalSource ?? PlatformShutdownSignalSource.Instance);
    }

    private interface IInitializationHandlerService
    {
    }

    private sealed class InitializationHandlerService : IInitializationHandlerService
    {
    }

    private interface ISignalSubscriptionService
    {
    }

    private sealed class SignalSubscriptionService : ISignalSubscriptionService
    {
    }

    private interface IInitializerService
    {
    }

    private sealed class InitializerService : IInitializerService
    {
    }

    [Fact]
    public async Task InitializeAsync_DoesNotHoldProcessLockWhileRunningProviderCode()
    {
        // A provider factory is user code and may take seconds - a database or HTTP connect is
        // ordinary. Holding the process-wide lifecycle lock across it blocked every other lifecycle
        // call in the process, stalled the main thread inside AppDomain.ProcessExit, and would
        // deadlock against a factory that starts a thread which does not flow the execution context.
        await TestGate.WaitAsync();
        var factoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            __SingletonDIHost__.ResetForTesting();
            __SingletonDIHost__.RegisterProvider<IBlockedFactoryService, BlockedFactoryService>(
                () =>
                {
                    factoryEntered.TrySetResult(true);
                    releaseFactory.Task.GetAwaiter().GetResult();
                    return new BlockedFactoryService();
                },
                Array.Empty<Type>(),
                null,
                null,
                null);

            // The factory blocks its caller, so initialization runs off the test thread. Reaching
            // this point proves the registry is already in its Initializing state.
            var initialization = Task.Run(
                () => SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false));
            await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Only the synchronous part of the call is under test: whether taking the process-wide
            // lifecycle lock has to wait for the factory that is currently running. Awaiting the
            // returned ValueTask would instead wait for the pending initialization, which is correct
            // in both revisions and would tell us nothing.
            var disposeCall = Task.Run(() => SingletonDIInitializer.DisposeAsync());
            var returnedInTime = await Task.WhenAny(disposeCall, Task.Delay(TimeSpan.FromSeconds(5)))
                == disposeCall;

            releaseFactory.TrySetResult(true);
            Assert.True(
                returnedInTime,
                "DisposeAsync() blocked on the lifecycle lock while a provider factory was running.");
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));
            await (await disposeCall).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseFactory.TrySetResult(true);
            await SingletonDIInitializer.DisposeAsync();
            TestGate.Release();
        }
    }

    private interface IBlockedFactoryService
    {
    }

    private sealed class BlockedFactoryService : IBlockedFactoryService
    {
    }

    private interface IOverlapService
    {
    }

    private sealed class OverlapService : IOverlapService
    {
    }

    private interface IReentrantInitializerService
    {
    }

    private sealed class ReentrantInitializerService : IReentrantInitializerService
    {
    }

    private interface IReentrantInitializeService
    {
    }

    private sealed class ReentrantInitializeService : IReentrantInitializeService
    {
    }

    private interface IRetryPublicService
    {
    }

    private sealed class RetryPublicService : IRetryPublicService
    {
    }

    private interface IStartupDataProbeService
    {
    }

    private sealed class StartupDataProbeService : IStartupDataProbeService
    {
        internal StartupDataProbeService()
        {
            // A factory reads the data before the graph has finished creating instances, which is
            // exactly the case the channel exists for. A registration outlives a single test, so
            // the read only happens while a test armed it.
            if (Volatile.Read(ref _probeReadsStartupData) == 0)
            {
                return;
            }

            _valueSeenInFactory = SingletonDIStartupData.Get<string>("connection");
        }

        internal async Task InitializeAsync()
        {
            await Task.Yield();
            if (Volatile.Read(ref _probeReadsStartupData) != 0)
            {
                _valueSeenInInitializer = SingletonDIStartupData.Get<string>("connection");
            }

            if (Volatile.Read(ref _probeInitializerFailure) != 0)
            {
                throw new InvalidOperationException("initializer failed");
            }
        }
    }

    private interface ILateWriteFactoryService
    {
    }

    private sealed class LateWriteFactoryService : ILateWriteFactoryService
    {
    }

    private sealed class StartupDataDisposable : IDisposable
    {
        private readonly bool _failOnDispose;

        internal StartupDataDisposable(bool failOnDispose = false)
        {
            _failOnDispose = failOnDispose;
        }

        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (_failOnDispose)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }

    private sealed class StartupDataAsyncDisposable : IDisposable, IAsyncDisposable
    {
        internal int DisposeCount { get; private set; }

        internal int AsyncDisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return default;
        }
    }
}
