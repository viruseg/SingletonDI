using SingletonDI.Attributes;
using SingletonDI.Generated;

namespace SingletonDI.InterProjectFixtures.ProviderLibrary;

/// <summary>
/// Provides an external singleton used to verify cross-assembly bootstrapping.
/// </summary>
[SingletonDIProvide]
public sealed class ExternalService : IDisposable
{
    /// <summary>
    /// Name of the startup data this provider reads during initialization.
    /// </summary>
    public const string StartupDataName = "external-service-greeting";

    /// <summary>
    /// Gets whether the most recently created external service has been initialized.
    /// </summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Gets whether the most recently created external service has been disposed.
    /// </summary>
    public static bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the startup data this provider read during initialization, or null when the provider was
    /// created without any.
    /// </summary>
    public static string? StartupDataValue { get; private set; }

    /// <summary>
    /// Initializes a new external service instance.
    /// </summary>
    public ExternalService()
    {
        IsInitialized = false;
        IsDisposed = false;
        StartupDataValue = null;
    }

    /// <summary>
    /// Initializes the external service.
    /// </summary>
    /// <returns>A completed initialization operation.</returns>
    public ValueTask InitializeAsync()
    {
        // A provider in another assembly reads the startup data with the same public API, without
        // its own generator support.
        StartupDataValue = SingletonDIStartupData.Get<string>(StartupDataName);
        IsInitialized = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Marks the external service as disposed.
    /// </summary>
    public void Dispose()
    {
        IsDisposed = true;
    }
}
