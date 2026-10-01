using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Serializes tests that share the process-wide SingletonDI runtime.
/// </summary>
[CollectionDefinition("SingletonDI runtime", DisableParallelization = true)]
public sealed class SingletonDIRuntimeCollection
{
}

[Collection("SingletonDI runtime")]
public sealed class InterProjectScenarioTests
{
    [Fact]
    public async Task ScenarioCanRunTwiceWithFreshState()
    {
        var first = await ProcessScenarioRunner.RunAsync();
        var second = await ProcessScenarioRunner.RunAsync();

        Assert.NotSame(first, second);
        Assert.Equal(first.LifecycleEvents, second.LifecycleEvents);
        Assert.True(first.ContractAndConcreteSame);
        Assert.True(second.ContractAndConcreteSame);
    }

    [Fact]
    public async Task StartupDataPassedByTheRootReachesAProviderInAnotherAssembly()
    {
        var scenario = await ProcessScenarioRunner.RunAsync();

        Assert.Equal("hello-from-root", scenario.ExternalStartupDataValue);
    }

    [Fact]
    public async Task RootResolvesContractAndConcreteProviderAndDisposesInOrder()
    {
        string[] expectedLifecycleOrder =
        [
            "Register Level 0 CacheConfigB",
            "Register Level 0 DatabaseConfigA",
            "Register Level 0 QueueConfigC",
            "Register Level 1 CacheConnectionB",
            "Register Level 1 DatabaseConnectionA",
            "Register Level 1 QueueConnectionC",
            "Register Level 2 CacheRepositoryB",
            "Register Level 2 RepositoryA",
            "Register Level 2 QueueProcessorC",
            "Dispose Level 2 CacheRepositoryB",
            "Dispose Level 2 RepositoryA",
            "Dispose Level 2 QueueProcessorC",
            "Dispose Level 1 CacheConnectionB",
            "Dispose Level 1 DatabaseConnectionA",
            "Dispose Level 1 QueueConnectionC",
            "Dispose Level 0 CacheConfigB",
            "Dispose Level 0 DatabaseConfigA",
            "Dispose Level 0 QueueConfigC"
        ];

        var scenario = await ProcessScenarioRunner.RunAsync();

        Assert.True(scenario.ContractAndConcreteSame);
        Assert.False(scenario.ServiceDisposedBeforeDispose);
        Assert.True(scenario.ServiceDisposedAfterDispose);
        Assert.True(scenario.ExternalServiceDisposedAfterDispose);
        Assert.Equal(expectedLifecycleOrder, scenario.LifecycleEvents);
        Assert.DoesNotContain(
            scenario.LifecycleEvents,
            value => value.StartsWith("Sync Dispose", StringComparison.Ordinal));
    }
}
