using ConsumerLibrary;
using SingletonDI.InterProjectFixtures.ProviderLibrary;
using Shared.Contracts;
using SingletonDI.Attributes;
using SingletonDI.Generated;

namespace SingletonDI.InterProjectFixtures.RootApp;

/// <summary>
/// Exposes the concrete provider through a generated root consumer property.
/// </summary>
[SingletonDIConsume(typeof(DatabaseService), typeof(ExternalService))]
public sealed partial class RootApp
{
    /// <summary>
    /// Gets the initialized concrete database service.
    /// </summary>
    /// <returns>The resolved concrete provider.</returns>
    public DatabaseService GetConcreteService()
    {
        return DatabaseServiceInstance;
    }

    /// <summary>
    /// Gets the initialized external provider.
    /// </summary>
    /// <returns>The resolved external provider.</returns>
    public ExternalService GetExternalService()
    {
        return ExternalServiceInstance;
    }
}

/// <summary>
/// Executes the cross-project singleton lifecycle scenario.
/// </summary>
public static class Program
{
    private static readonly SemaphoreSlim ScenarioGate = new(1, 1);
    private static ScenarioResult? _scenarioResult;

    /// <summary>
    /// Initializes the process-wide container, resolves both service keys, and disposes it.
    /// </summary>
    /// <returns>The observed identity, disposal, and lifecycle values.</returns>
    public static async Task<ScenarioResult> RunScenarioAsync()
    {
        await ScenarioGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_scenarioResult is not null)
            {
                return _scenarioResult;
            }

            LifecycleLog.Clear();
            await SingletonDIInitializer.InitializeAsync(false).ConfigureAwait(false);

            try
            {
                var contractService = new Repository().GetService();
                var concreteService = new RootApp().GetConcreteService();
                var externalService = new RootApp().GetExternalService();
                var serviceDisposedBeforeDispose = DatabaseService.IsDisposed;
                var lifecycleEventsBeforeDispose = LifecycleLog.Snapshot();

                await SingletonDIInitializer.DisposeAsync().ConfigureAwait(false);

                var lifecycleEvents = LifecycleLog.Snapshot();
                var scenarioResult = new ScenarioResult(
                    contractService,
                    concreteService,
                    externalService,
                    serviceDisposedBeforeDispose,
                    DatabaseService.IsDisposed,
                    ExternalService.IsDisposed,
                    lifecycleEvents);

                if (lifecycleEventsBeforeDispose.Count == 0)
                {
                    throw new InvalidOperationException("The lifecycle log was not populated during initialization.");
                }

                if (!ExternalService.IsInitialized)
                {
                    throw new InvalidOperationException("The external provider was not bootstrapped and initialized.");
                }

                _scenarioResult = scenarioResult;
                return scenarioResult;
            }
            catch
            {
                await SingletonDIInitializer.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            ScenarioGate.Release();
        }
    }

    /// <summary>
    /// Runs the cross-project scenario as the fixture application's entry point.
    /// </summary>
    /// <returns>A task that completes after the process-wide container is disposed.</returns>
    public static async Task Main()
    {
        await RunScenarioAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Contains the observable values from the cross-project scenario.
/// </summary>
public sealed class ScenarioResult
{
    /// <summary>
    /// Initializes a new scenario result.
    /// </summary>
    /// <param name="contractService">The service obtained through the shared contract.</param>
    /// <param name="concreteService">The service obtained through the concrete provider type.</param>
    /// <param name="externalService">The service obtained from the external provider assembly.</param>
    /// <param name="serviceDisposedBeforeDispose">Whether disposal had occurred before container disposal.</param>
    /// <param name="serviceDisposedAfterDispose">Whether disposal occurred after container disposal.</param>
    /// <param name="externalServiceDisposedAfterDispose">Whether the external service was disposed after container disposal.</param>
    /// <param name="lifecycleEvents">The ordered lifecycle events observed by the scenario.</param>
    public ScenarioResult(
        IDatabaseService contractService,
        DatabaseService concreteService,
        ExternalService externalService,
        bool serviceDisposedBeforeDispose,
        bool serviceDisposedAfterDispose,
        bool externalServiceDisposedAfterDispose,
        IReadOnlyList<string> lifecycleEvents)
    {
        ContractService = contractService;
        ConcreteService = concreteService;
        ExternalService = externalService;
        ServiceDisposedBeforeDispose = serviceDisposedBeforeDispose;
        ServiceDisposedAfterDispose = serviceDisposedAfterDispose;
        ExternalServiceDisposedAfterDispose = externalServiceDisposedAfterDispose;
        LifecycleEvents = lifecycleEvents;
    }

    /// <summary>
    /// Gets the service resolved through the shared contract.
    /// </summary>
    public IDatabaseService ContractService { get; }

    /// <summary>
    /// Gets the service resolved through the concrete provider type.
    /// </summary>
    public DatabaseService ConcreteService { get; }

    /// <summary>
    /// Gets the service obtained from the external provider assembly.
    /// </summary>
    public ExternalService ExternalService { get; }

    /// <summary>
    /// Gets whether the database service was disposed before container disposal.
    /// </summary>
    public bool ServiceDisposedBeforeDispose { get; }

    /// <summary>
    /// Gets whether the database service was disposed after container disposal.
    /// </summary>
    public bool ServiceDisposedAfterDispose { get; }

    /// <summary>
    /// Gets whether the external service was disposed after container disposal.
    /// </summary>
    public bool ExternalServiceDisposedAfterDispose { get; }

    /// <summary>
    /// Gets the ordered lifecycle events observed by the scenario.
    /// </summary>
    public IReadOnlyList<string> LifecycleEvents { get; }
}
