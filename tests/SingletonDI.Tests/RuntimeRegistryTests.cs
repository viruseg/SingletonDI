using SingletonDI.Attributes;
using SingletonDI.Generated;
using Xunit;

namespace SingletonDI.Tests;

public sealed class RuntimeRegistryTests
{
    [Fact]
    public async Task RegisterProvider_ConcreteAndContractResolveSameInstance()
    {
        var createCount = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IThing, Thing>(
            () =>
            {
                createCount++;
                return new Thing();
            },
            Array.Empty<Type>(),
            null,
            null,
            null);

        await registry.InitializeAsync();

        Assert.Equal(1, createCount);
        Assert.Same(registry.Resolve<IThing>(), registry.Resolve<Thing>());
    }

    [Fact]
    public async Task Registry_UsesDependencyLevelsAndReverseDisposal()
    {
        var events = new List<string>();
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IBase, BaseService>(
            () => new BaseService(events),
            Array.Empty<Type>(),
            null,
            value => value.Dispose(),
            null);
        registry.RegisterProvider<IDependent, DependentService>(
            () => new DependentService(events),
            new[] { typeof(IBase) },
            null,
            value => value.Dispose(),
            null);

        await registry.InitializeAsync();
        await registry.DisposeAsync();

        Assert.Equal(
            new[]
            {
                "create:BaseService",
                "create:DependentService",
                "dispose:DependentService",
                "dispose:BaseService"
            },
            events);
    }

    [Fact]
    public async Task Registry_RejectsExternalResolveWhileInitializationIsInProgress()
    {
        var initializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IAsyncOne, AsyncOne>(
            static () => new AsyncOne(),
            Array.Empty<Type>(),
            async _ =>
            {
                initializerStarted.TrySetResult(true);
                await releaseInitializer.Task;
            },
            null,
            null);

        var initialization = registry.InitializeAsync();
        await initializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Resolve<IAsyncOne>());
        Assert.Contains("InitializeAsync", exception.Message);

        releaseInitializer.TrySetResult(true);
        await initialization;
    }

    [Fact]
    public async Task Registry_RejectsResolveFromPreviousInitializationGeneration()
    {
        var releaseStaleResolve = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleResolveSucceeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentInitializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCurrentInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawnerInitializationCount = 0;
        var blockerInitializationCount = 0;
        Task? staleResolveTask = null;
        var registry = new ServiceRegistry();

        registry.RegisterProvider<IStaleContextSpawner, StaleContextSpawner>(
            static () => new StaleContextSpawner(),
            Array.Empty<Type>(),
            async _ =>
            {
                if (Interlocked.Increment(ref spawnerInitializationCount) == 1)
                {
                    staleResolveTask = Task.Run(
                        async () =>
                        {
                            await releaseStaleResolve.Task;
                            try
                            {
                                registry.Resolve<IStaleContextTarget>();
                                staleResolveSucceeded.TrySetResult(true);
                            }
                            catch (InvalidOperationException)
                            {
                                staleResolveSucceeded.TrySetResult(false);
                            }
                        });
                }
            },
            null,
            null);
        registry.RegisterProvider<IStaleContextTarget, StaleContextTarget>(
            static () => new StaleContextTarget(),
            Array.Empty<Type>(),
            null,
            null,
            null);
        registry.RegisterProvider<ICurrentGenerationBlocker, CurrentGenerationBlocker>(
            static () => new CurrentGenerationBlocker(),
            Array.Empty<Type>(),
            async _ =>
            {
                if (Interlocked.Increment(ref blockerInitializationCount) == 2)
                {
                    currentInitializerStarted.TrySetResult(true);
                    await releaseCurrentInitializer.Task;
                }
            },
            null,
            null);

        await registry.InitializeAsync();
        await registry.DisposeAsync();
        var currentInitialization = registry.InitializeAsync();
        await currentInitializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseStaleResolve.TrySetResult(true);
        Assert.False(await staleResolveSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(staleResolveTask);

        releaseCurrentInitializer.TrySetResult(true);
        await currentInitialization;
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task Registry_AllowsFactoryToResolveDependencyDuringInitialization()
    {
        var events = new List<string>();
        IBase? resolvedDependency = null;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IBase, BaseService>(
            () => new BaseService(events),
            Array.Empty<Type>(),
            null,
            null,
            null);
        registry.RegisterProvider<IDependent, DependentService>(
            () =>
            {
                resolvedDependency = registry.Resolve<IBase>();
                return new DependentService(events);
            },
            new[] { typeof(IBase) },
            null,
            null,
            null);

        await registry.InitializeAsync();

        Assert.NotNull(resolvedDependency);
        Assert.Same(registry.Resolve<IBase>(), resolvedDependency);
    }

    [Fact]
    public async Task Registry_RunsInitializersOnSameLevelConcurrently()
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitializers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ServiceRegistry();

        registry.RegisterProvider<IAsyncOne, AsyncOne>(
            static () => new AsyncOne(),
            Array.Empty<Type>(),
            async _ =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.SetResult(true);
                }

                await releaseInitializers.Task;
            },
            null,
            null);
        registry.RegisterProvider<IAsyncTwo, AsyncTwo>(
            static () => new AsyncTwo(),
            Array.Empty<Type>(),
            async _ =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.SetResult(true);
                }

                await releaseInitializers.Task;
            },
            null,
            null);

        var initialization = registry.InitializeAsync();
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseInitializers.SetResult(true);
        await initialization;

        Assert.Equal(2, started);
    }

    [Fact]
    public async Task Registry_WaitsForStartedInitializersBeforeCleanupAfterSynchronousFailure()
    {
        var initializerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitializer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCalled = 0;
        var registry = new ServiceRegistry();

        registry.RegisterProvider<IAsyncOne, AsyncOne>(
            static () => new AsyncOne(),
            Array.Empty<Type>(),
            _ =>
            {
                initializerStarted.TrySetResult(true);
                return releaseInitializer.Task;
            },
            _ =>
            {
                Interlocked.Exchange(ref disposeCalled, 1);
                throw new InvalidOperationException("cleanup failed");
            },
            null);
        registry.RegisterProvider<IAsyncTwo, AsyncTwo>(
            static () => new AsyncTwo(),
            Array.Empty<Type>(),
            _ => throw new InvalidOperationException("initializer failed"),
            null,
            null);

        try
        {
            var initialization = registry.InitializeAsync();
            await initializerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.Equal(0, Volatile.Read(ref disposeCalled));

            releaseInitializer.TrySetResult(true);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initialization);
            Assert.Contains("failed to initialize", exception.Message);
            Assert.Equal("initializer failed", exception.InnerException?.Message);
            Assert.Equal(1, Volatile.Read(ref disposeCalled));
        }
        finally
        {
            releaseInitializer.TrySetResult(true);
            await registry.DisposeAsync();
        }
    }

    [Fact]
    public async Task Registry_PrefersAsynchronousDisposal()
    {
        var events = new List<string>();
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IDisposableService, DisposableService>(
            static () => new DisposableService(),
            Array.Empty<Type>(),
            null,
            _ => events.Add("sync"),
            async _ =>
            {
                events.Add("async:start");
                await Task.Yield();
                events.Add("async:end");
            });

        await registry.InitializeAsync();
        await registry.DisposeAsync();

        Assert.Equal(new[] { "async:start", "async:end" }, events);
    }

    [Fact]
    public async Task Registry_DisposesSameLevelSynchronousProvidersInParallel()
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisposers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ServiceRegistry();

        registry.RegisterProvider<IParallelOne, ParallelOne>(
            static () => new ParallelOne(),
            Array.Empty<Type>(),
            null,
            _ =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.TrySetResult(true);
                }

                releaseDisposers.Task.GetAwaiter().GetResult();
            },
            null);
        registry.RegisterProvider<IParallelTwo, ParallelTwo>(
            static () => new ParallelTwo(),
            Array.Empty<Type>(),
            null,
            _ =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.TrySetResult(true);
                }

                releaseDisposers.Task.GetAwaiter().GetResult();
            },
            null);

        await registry.InitializeAsync();
        var disposal = Task.Run(async () => await registry.DisposeAsync());
        try
        {
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseDisposers.TrySetResult(true);
        }

        await disposal;
        Assert.Equal(2, started);
    }

    [Fact]
    public async Task Registry_ContinuesDisposalAndInvalidatesAfterFailure()
    {
        var successfulDisposeCount = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IFailingDisposable, FailingDisposable>(
            static () => new FailingDisposable(),
            Array.Empty<Type>(),
            null,
            _ => throw new InvalidOperationException("disposal failed"),
            null);
        registry.RegisterProvider<IOtherDisposable, OtherDisposable>(
            static () => new OtherDisposable(),
            Array.Empty<Type>(),
            null,
            _ => Interlocked.Increment(ref successfulDisposeCount),
            null);

        await registry.InitializeAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.DisposeAsync());

        Assert.Equal("disposal failed", exception.Message);
        Assert.Equal(1, successfulDisposeCount);
        var resolveException = Assert.Throws<InvalidOperationException>(
            () => registry.Resolve<IFailingDisposable>());
        Assert.Contains("InitializeAsync", resolveException.Message);

        await registry.DisposeAsync();
        Assert.Equal(1, successfulDisposeCount);
    }

    [Fact]
    public async Task Registry_ReportsEveryFailingDisposer()
    {
        // Task.WhenAll surfaces one fault, so a second failing disposer in the same level used to
        // be dropped without ever being observed.
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ISecondFailingDisposable, SecondFailingDisposable>(
            static () => new SecondFailingDisposable(),
            Array.Empty<Type>(),
            null,
            _ => throw new InvalidOperationException("second disposal failed"),
            null);
        registry.RegisterProvider<IThirdFailingDisposable, ThirdFailingDisposable>(
            static () => new ThirdFailingDisposable(),
            Array.Empty<Type>(),
            null,
            _ => throw new NotSupportedException("third disposal failed"),
            null);

        await registry.InitializeAsync();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await registry.DisposeAsync());

        var messages = exception.InnerExceptions.Select(inner => inner.Message).ToArray();
        Assert.Contains("second disposal failed", messages);
        Assert.Contains("third disposal failed", messages);
    }

    [Fact]
    public async Task Registry_NamesTheProviderWhoseCreationFailed()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IWorkingService, WorkingService>(
            static () => new WorkingService(),
            Array.Empty<Type>(),
            null,
            null,
            null);
        registry.RegisterProvider<IFailingCreationService, FailingCreationService>(
            static () => throw new System.Net.Sockets.SocketException(10061),
            Array.Empty<Type>(),
            null,
            null,
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.InitializeAsync());

        Assert.Contains(nameof(FailingCreationService), exception.Message);
        Assert.IsType<System.Net.Sockets.SocketException>(exception.InnerException);
    }

    [Fact]
    public async Task Registry_ResolvesConcreteOnlyRegistration()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ConcreteOnlyService, ConcreteOnlyService>(
            static () => new ConcreteOnlyService(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        await registry.InitializeAsync();

        Assert.IsType<ConcreteOnlyService>(registry.Resolve<ConcreteOnlyService>());
    }

    [Fact]
    public async Task Registry_ReportsUnknownServiceKey()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IThing, Thing>(
            static () => new Thing(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        await registry.InitializeAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Resolve<IUnknown>());

        Assert.Contains(nameof(IUnknown), exception.Message);
    }

    [Fact]
    public async Task Registry_SecondSuccessfulDisposeIsNoOp()
    {
        var disposeCount = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ICountedDisposable, CountedDisposable>(
            static () => new CountedDisposable(),
            Array.Empty<Type>(),
            null,
            _ => Interlocked.Increment(ref disposeCount),
            null);

        await registry.InitializeAsync();
        await registry.DisposeAsync();
        await registry.DisposeAsync();

        Assert.Equal(1, disposeCount);
    }

    [Fact]
    public async Task Registry_RejectsMissingDependency()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IMissingDependencyConsumer, MissingDependencyConsumer>(
            static () => new MissingDependencyConsumer(),
            new[] { typeof(IMissingDependency) },
            null,
            null,
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.InitializeAsync());

        Assert.Contains(nameof(IMissingDependency), exception.Message);
    }

    [Fact]
    public async Task Registry_RejectsDependencyCycle()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ICycleFirst, CycleFirst>(
            static () => new CycleFirst(),
            new[] { typeof(ICycleSecond) },
            null,
            null,
            null);
        registry.RegisterProvider<ICycleSecond, CycleSecond>(
            static () => new CycleSecond(),
            new[] { typeof(ICycleFirst) },
            null,
            null,
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.InitializeAsync());

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_RejectsDuplicateServiceKey()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IThing, Thing>(
            static () => new Thing(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterProvider<IThing, OtherThing>(
                static () => new OtherThing(),
                Array.Empty<Type>(),
                null,
                null,
                null));

        Assert.Contains(nameof(IThing), exception.Message);
    }

    [Fact]
    public void Registry_RejectsResolveBeforeInitialization()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IThing, Thing>(
            static () => new Thing(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Resolve<IThing>());

        Assert.Contains("InitializeAsync", exception.Message);
    }

    [Fact]
    public async Task Registry_CanRetryAfterFailedInitialization()
    {
        var attempts = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IRetryService, RetryService>(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("first attempt");
                }

                return new RetryService();
            },
            Array.Empty<Type>(),
            null,
            null,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InitializeAsync());
        await registry.InitializeAsync();

        Assert.Equal(2, attempts);
        Assert.IsType<RetryService>(registry.Resolve<IRetryService>());
    }

    [Fact]
    public async Task Registry_RejectsRegistrationAfterInitialization()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IThing, Thing>(
            static () => new Thing(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        await registry.InitializeAsync();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterProvider<IOtherThing, OtherThing>(
                static () => new OtherThing(),
                Array.Empty<Type>(),
                null,
                null,
                null));

        Assert.Contains("initializ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registry_AllowsRegistrationAfterFailedInitialization()
    {
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IMissingDependencyConsumer, MissingDependencyConsumer>(
            static () => new MissingDependencyConsumer(),
            new[] { typeof(IMissingDependency) },
            null,
            null,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.InitializeAsync());

        registry.RegisterProvider<IMissingDependency, MissingDependency>(
            static () => new MissingDependency(),
            Array.Empty<Type>(),
            null,
            null,
            null);

        await registry.InitializeAsync();

        Assert.IsType<MissingDependencyConsumer>(registry.Resolve<IMissingDependencyConsumer>());
    }

    [Fact]
    public async Task Registry_FailedInitializationCleansEveryCreatedInstanceInReverseOrderAndCanRetry()
    {
        var events = new List<string>();
        var attempts = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ICleanupLevelOne, CleanupLevelOne>(
            () => new CleanupLevelOne(events),
            Array.Empty<Type>(),
            null,
            value => events.Add("dispose:LevelOne"),
            null);
        registry.RegisterProvider<ICleanupLevelTwo, CleanupLevelTwo>(
            () => new CleanupLevelTwo(events),
            new[] { typeof(ICleanupLevelOne) },
            null,
            value => events.Add("dispose:LevelTwo"),
            null);
        registry.RegisterProvider<ICleanupLevelThree, CleanupLevelThree>(
            () => new CleanupLevelThree(events),
            new[] { typeof(ICleanupLevelTwo) },
            async _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("initialization failed");
                }
            },
            value => events.Add("dispose:LevelThree"),
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.InitializeAsync());

        Assert.Contains("failed to initialize", exception.Message);
        Assert.Equal("initialization failed", exception.InnerException?.Message);
        Assert.Equal(
            new[]
            {
                "create:LevelOne",
                "create:LevelTwo",
                "create:LevelThree",
                "dispose:LevelThree",
                "dispose:LevelTwo",
                "dispose:LevelOne"
            },
            events);

        await registry.DisposeAsync();
        await registry.DisposeAsync();
        await registry.InitializeAsync();

        Assert.Equal(2, attempts);
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task Registry_FailedInitializationPreservesOriginalErrorWhenCleanupFails()
    {
        var events = new List<string>();
        var rootDisposeCount = 0;
        var middleDisposeCount = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<ICleanupFailureRoot, CleanupFailureRoot>(
            () => new CleanupFailureRoot(events),
            Array.Empty<Type>(),
            null,
            _ => rootDisposeCount++,
            null);
        registry.RegisterProvider<ICleanupFailureMiddle, CleanupFailureMiddle>(
            () => new CleanupFailureMiddle(events),
            new[] { typeof(ICleanupFailureRoot) },
            null,
            _ =>
            {
                middleDisposeCount++;
                throw new InvalidOperationException("cleanup failed");
            },
            null);
        registry.RegisterProvider<ICleanupFailureLeaf, CleanupFailureLeaf>(
            () => new CleanupFailureLeaf(events),
            new[] { typeof(ICleanupFailureMiddle) },
            _ => throw new InvalidOperationException("initialization failed"),
            _ => events.Add("dispose:Leaf"),
            null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.InitializeAsync());

        Assert.Contains("failed to initialize", exception.Message);
        Assert.Equal("initialization failed", exception.InnerException?.Message);
        Assert.Equal(1, rootDisposeCount);
        Assert.Equal(1, middleDisposeCount);
        await registry.DisposeAsync();
        await registry.DisposeAsync();
    }

    [Fact]
    public async Task Registry_ReinitializationWaitsForInProgressDisposal()
    {
        var disposeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCount = 0;
        var registry = new ServiceRegistry();
        registry.RegisterProvider<IQueuedService, QueuedService>(
            () =>
            {
                createCount++;
                return new QueuedService();
            },
            Array.Empty<Type>(),
            null,
            _ =>
            {
                disposeStarted.TrySetResult(true);
                releaseDispose.Task.GetAwaiter().GetResult();
            },
            null);

        await registry.InitializeAsync();
        var disposal = registry.DisposeAsync().AsTask();
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reinitialization = registry.InitializeAsync();

        Assert.False(reinitialization.IsCompleted);

        releaseDispose.TrySetResult(true);
        await disposal;
        await reinitialization;

        Assert.Equal(2, createCount);
        await registry.DisposeAsync();
    }

    private interface IStaleContextSpawner
    {
    }

    private sealed class StaleContextSpawner : IStaleContextSpawner
    {
    }

    private interface IStaleContextTarget
    {
    }

    private sealed class StaleContextTarget : IStaleContextTarget
    {
    }

    private interface ICurrentGenerationBlocker
    {
    }

    private sealed class CurrentGenerationBlocker : ICurrentGenerationBlocker
    {
    }

    private interface ICleanupLevelOne
    {
    }

    private sealed class CleanupLevelOne : ICleanupLevelOne
    {
        public CleanupLevelOne(List<string> events)
        {
            events.Add("create:LevelOne");
        }
    }

    private interface ICleanupLevelTwo
    {
    }

    private sealed class CleanupLevelTwo : ICleanupLevelTwo
    {
        public CleanupLevelTwo(List<string> events)
        {
            events.Add("create:LevelTwo");
        }
    }

    private interface ICleanupLevelThree
    {
    }

    private sealed class CleanupLevelThree : ICleanupLevelThree
    {
        public CleanupLevelThree(List<string> events)
        {
            events.Add("create:LevelThree");
        }
    }

    private interface ICleanupFailureRoot
    {
    }

    private sealed class CleanupFailureRoot : ICleanupFailureRoot
    {
        public CleanupFailureRoot(List<string> events)
        {
            events.Add("create:FailureRoot");
        }
    }

    private interface ICleanupFailureMiddle
    {
    }

    private sealed class CleanupFailureMiddle : ICleanupFailureMiddle
    {
        public CleanupFailureMiddle(List<string> events)
        {
            events.Add("create:FailureMiddle");
        }
    }

    private interface ICleanupFailureLeaf
    {
    }

    private sealed class CleanupFailureLeaf : ICleanupFailureLeaf
    {
        public CleanupFailureLeaf(List<string> events)
        {
            events.Add("create:FailureLeaf");
        }
    }

    private interface IQueuedService
    {
    }

    private sealed class QueuedService : IQueuedService
    {
    }

    private interface IThing
    {
    }

    private sealed class Thing : IThing
    {
    }

    private interface IOtherThing
    {
    }

    private sealed class OtherThing : IOtherThing, IThing
    {
    }

    private interface IBase
    {
    }

    private sealed class BaseService : IBase
    {
        private readonly List<string> _events;

        public BaseService(List<string> events)
        {
            _events = events;
            events.Add("create:BaseService");
        }

        public void Dispose()
        {
            _events.Add("dispose:BaseService");
        }
    }

    private interface IDependent
    {
    }

    private sealed class DependentService : IDependent
    {
        private readonly List<string> _events;

        public DependentService(List<string> events)
        {
            _events = events;
            events.Add("create:DependentService");
        }

        public void Dispose()
        {
            _events.Add("dispose:DependentService");
        }
    }

    private interface IParallelOne
    {
    }

    private sealed class ParallelOne : IParallelOne
    {
    }

    private interface IParallelTwo
    {
    }

    private sealed class ParallelTwo : IParallelTwo
    {
    }

    private interface IFailingDisposable
    {
    }

    private sealed class FailingDisposable : IFailingDisposable
    {
    }

    private interface IOtherDisposable
    {
    }

    private sealed class OtherDisposable : IOtherDisposable
    {
    }

    private sealed class ConcreteOnlyService
    {
    }

    private interface IUnknown
    {
    }

    private interface ICountedDisposable
    {
    }

    private sealed class CountedDisposable : ICountedDisposable
    {
    }

    private interface IAsyncOne
    {
    }

    private sealed class AsyncOne : IAsyncOne
    {
    }

    private interface IAsyncTwo
    {
    }

    private sealed class AsyncTwo : IAsyncTwo
    {
    }

    private interface IDisposableService
    {
    }

    private sealed class DisposableService : IDisposableService
    {
    }

    private interface IMissingDependency
    {
    }

    private interface IMissingDependencyConsumer
    {
    }

    private sealed class MissingDependencyConsumer : IMissingDependencyConsumer
    {
    }

    private sealed class MissingDependency : IMissingDependency
    {
    }

    private interface ICycleFirst
    {
    }

    private sealed class CycleFirst : ICycleFirst
    {
    }

    private interface ICycleSecond
    {
    }

    private sealed class CycleSecond : ICycleSecond
    {
    }

    private interface IRetryService
    {
    }

    private sealed class RetryService : IRetryService
    {
    }

    private interface IWorkingService
    {
    }

    private sealed class WorkingService : IWorkingService
    {
    }

    private interface IFailingCreationService
    {
    }

    private sealed class FailingCreationService : IFailingCreationService
    {
    }

    private interface ISecondFailingDisposable
    {
    }
    private sealed class SecondFailingDisposable : ISecondFailingDisposable
    {
    }

    private interface IThirdFailingDisposable
    {
    }

    private sealed class ThirdFailingDisposable : IThirdFailingDisposable
    {
    }
}
