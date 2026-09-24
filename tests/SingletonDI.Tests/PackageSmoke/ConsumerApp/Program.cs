using SingletonDI.Attributes;
using SingletonDI.Generated;

namespace PackageSmoke;

[SingletonDIProvide]
public sealed class SmokeService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

[SingletonDIConsume(typeof(SmokeService))]
public sealed partial class SmokeConsumer
{
    public SmokeService ResolveService()
    {
        return SmokeServiceInstance;
    }
}

public static class Program
{
    public static async Task Main()
    {
        SmokeService? service = null;
        try
        {
            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            service = new SmokeConsumer().ResolveService();
        }
        finally
        {
            await SingletonDIInitializer.DisposeAsync();
        }

        if (service is null || !service.IsDisposed)
        {
            throw new InvalidOperationException("The package smoke scenario did not complete.");
        }
    }
}
