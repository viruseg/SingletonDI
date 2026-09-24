using SingletonDI.Attributes;

namespace SingletonDI.InterProjectFixtures.ProviderLibrary;

/// <summary>
/// Provides an external singleton used to verify cross-assembly bootstrapping.
/// </summary>
[SingletonDIProvide]
public sealed class ExternalService : IDisposable
{
    /// <summary>
    /// Gets whether the most recently created external service has been initialized.
    /// </summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Gets whether the most recently created external service has been disposed.
    /// </summary>
    public static bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes a new external service instance.
    /// </summary>
    public ExternalService()
    {
        IsInitialized = false;
        IsDisposed = false;
    }

    /// <summary>
    /// Initializes the external service.
    /// </summary>
    /// <returns>A completed initialization operation.</returns>
    public ValueTask InitializeAsync()
    {
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
