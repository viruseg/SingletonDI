using Shared.Contracts;
using SingletonDI.Attributes;

namespace SingletonDI.InterProjectFixtures.RootApp;

/// <summary>
/// Provides the database implementation for the shared contract.
/// </summary>
[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService, IDisposable
{
    /// <summary>
    /// Gets whether the most recently created service instance has been disposed.
    /// </summary>
    public static bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes a new database service instance.
    /// </summary>
    public DatabaseService()
    {
        IsDisposed = false;
    }

    /// <summary>
    /// Marks the database service instance as disposed.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
    }
}
