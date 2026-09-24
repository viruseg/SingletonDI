using System;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace SingletonDI.Generated;

internal sealed class ShutdownManager : IDisposable
{
    private readonly Func<ValueTask> _dispose;
    private readonly object _sync = new();
    private IDisposable[]? _posixRegistrations;
    private bool _registered;
    private bool _disposed;

    internal ShutdownManager(Func<ValueTask> dispose)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    internal void Register()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ShutdownManager));
            }

            if (_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
#if NET8_0_OR_GREATER
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                RegisterPosixSignals();
            }
#endif
            _registered = true;
        }
    }

    public void Dispose()
    {
        IDisposable[]? posixRegistrations;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Console.CancelKeyPress -= OnCancelKeyPress;
            posixRegistrations = _posixRegistrations;
            _posixRegistrations = null;
        }

        if (posixRegistrations is null)
        {
            return;
        }

        foreach (var registration in posixRegistrations)
        {
            try
            {
                registration.Dispose();
            }
            catch
            {
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        BeginDispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        BeginDispose();
    }

#if NET8_0_OR_GREATER
    private void RegisterPosixSignals()
    {
        try
        {
            _posixRegistrations = new IDisposable[]
            {
                PosixSignalRegistration.Create(PosixSignal.SIGINT, HandlePosixSignal),
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandlePosixSignal),
                PosixSignalRegistration.Create(PosixSignal.SIGQUIT, HandlePosixSignal)
            };
        }
        catch (PlatformNotSupportedException)
        {
            _posixRegistrations = null;
        }
    }

    private void HandlePosixSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        BeginDispose();
    }
#endif

    private void BeginDispose()
    {
        _ = DisposeAsyncSafely();
    }

    private async Task DisposeAsyncSafely()
    {
        try
        {
            await _dispose().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
