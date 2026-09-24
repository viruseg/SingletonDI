using SingletonDI.Attributes;

namespace SingletonDI.InterProjectFixtures.RootApp;

/// <summary>
/// Provides the level-zero database configuration lifecycle service.
/// </summary>
[SingletonDIProvide]
public sealed class DatabaseConfigA : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service and records its registration.
    /// </summary>
    public DatabaseConfigA()
    {
        LifecycleLog.Add("Register Level 0 DatabaseConfigA");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 0 DatabaseConfigA");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 0 DatabaseConfigA");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-one database connection lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(DatabaseConfigA))]
public sealed partial class DatabaseConnectionA : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public DatabaseConnectionA()
    {
        _ = DatabaseConfigAInstance;
        LifecycleLog.Add("Register Level 1 DatabaseConnectionA");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 1 DatabaseConnectionA");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 1 DatabaseConnectionA");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-two database repository lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(DatabaseConnectionA))]
public sealed partial class RepositoryA : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public RepositoryA()
    {
        _ = DatabaseConnectionAInstance;
        LifecycleLog.Add("Register Level 2 RepositoryA");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 2 RepositoryA");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 2 RepositoryA");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-zero cache configuration lifecycle service.
/// </summary>
[SingletonDIProvide]
public sealed class CacheConfigB : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service and records its registration.
    /// </summary>
    public CacheConfigB()
    {
        LifecycleLog.Add("Register Level 0 CacheConfigB");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 0 CacheConfigB");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 0 CacheConfigB");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-one cache connection lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(CacheConfigB))]
public sealed partial class CacheConnectionB : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public CacheConnectionB()
    {
        _ = CacheConfigBInstance;
        LifecycleLog.Add("Register Level 1 CacheConnectionB");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 1 CacheConnectionB");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 1 CacheConnectionB");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-two cache repository lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(CacheConnectionB))]
public sealed partial class CacheRepositoryB : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public CacheRepositoryB()
    {
        _ = CacheConnectionBInstance;
        LifecycleLog.Add("Register Level 2 CacheRepositoryB");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 2 CacheRepositoryB");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 2 CacheRepositoryB");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-zero queue configuration lifecycle service.
/// </summary>
[SingletonDIProvide]
public sealed class QueueConfigC : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service and records its registration.
    /// </summary>
    public QueueConfigC()
    {
        LifecycleLog.Add("Register Level 0 QueueConfigC");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 0 QueueConfigC");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 0 QueueConfigC");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-one queue connection lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(QueueConfigC))]
public sealed partial class QueueConnectionC : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public QueueConnectionC()
    {
        _ = QueueConfigCInstance;
        LifecycleLog.Add("Register Level 1 QueueConnectionC");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 1 QueueConnectionC");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 1 QueueConnectionC");
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Provides the level-two queue processor lifecycle service.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(QueueConnectionC))]
public sealed partial class QueueProcessorC : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initializes the service, resolves its dependency, and records its registration.
    /// </summary>
    public QueueProcessorC()
    {
        _ = QueueConnectionCInstance;
        LifecycleLog.Add("Register Level 2 QueueProcessorC");
    }

    /// <summary>
    /// Records the synchronous disposal path.
    /// </summary>
    public void Dispose()
    {
        LifecycleLog.Add("Sync Dispose Level 2 QueueProcessorC");
    }

    /// <summary>
    /// Records the asynchronous disposal path.
    /// </summary>
    /// <returns>A completed disposal operation.</returns>
    public ValueTask DisposeAsync()
    {
        LifecycleLog.Add("Dispose Level 2 QueueProcessorC");
        return ValueTask.CompletedTask;
    }
}

internal static class LifecycleLog
{
    private static readonly List<string> Events = [];

    internal static void Add(string action)
    {
        lock (Events)
        {
            Events.Add(action);
        }
    }

    internal static void Clear()
    {
        lock (Events)
        {
            Events.Clear();
        }
    }

    internal static IReadOnlyList<string> Snapshot()
    {
        lock (Events)
        {
            return new List<string>(Events).AsReadOnly();
        }
    }
}
