using SingletonDI.Attributes;
using SingletonDI.Generated;

namespace PackageSmoke;

[SingletonDIProvide]
public sealed class SmokeService : IDisposable
{
    private bool _disposed;

    public bool IsDisposed { get; private set; }

    public string Greeting { get; } = SingletonDIStartupData.Get<string>("greeting");

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        IsDisposed = true;
        _disposed = true;
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
        var lateWriteRejected = false;
        try
        {
            // The startup data channel is public API of the package, so the smoke consumer uses it
            // exactly the way an application would.
            SingletonDIStartupData.Set("greeting", "hello-from-package");
            await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);
            service = new SmokeConsumer().ResolveService();

            if (service.Greeting != "hello-from-package")
            {
                throw new InvalidOperationException(
                    "The packaged startup data channel did not reach the provider.");
            }

            try
            {
                SingletonDIStartupData.Set("too-late", 1);
            }
            catch (InvalidOperationException)
            {
                lateWriteRejected = true;
            }
        }
        finally
        {
            await SingletonDIInitializer.DisposeAsync();
        }

        if (!lateWriteRejected)
        {
            throw new InvalidOperationException(
                "The packaged startup data channel accepted a write after initialization.");
        }

        if (service is null || !service.IsDisposed)
        {
            throw new InvalidOperationException("The package smoke scenario did not complete.");
        }
    }
}