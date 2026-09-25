using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

internal sealed class ShutdownManager : IDisposable
{
    private readonly Func<ValueTask> _dispose;
    private readonly Action<int> _terminateProcess;
    private readonly object _sync = new();
    private IDisposable[]? _posixRegistrations;
    private Task? _disposeTask;
    private Task? _shutdownTask;
    private bool _registered;
    private bool _disposed;

    internal ShutdownManager(Func<ValueTask> dispose, Action<int> terminateProcess)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _terminateProcess = terminateProcess ?? throw new ArgumentNullException(nameof(terminateProcess));
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
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                RegisterPosixSignals();
            }
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
        _ = GetDisposeTask();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        _ = HandleCancelKeyPress(e);
    }

    internal Task HandleCancelKeyPress(ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        return BeginSignalShutdown(130);
    }

    internal Task HandleCancelKeyPressForTesting(Action cancel)
    {
        if (cancel is null)
        {
            throw new ArgumentNullException(nameof(cancel));
        }

        cancel();
        return BeginSignalShutdown(130);
    }

    private void RegisterPosixSignals()
    {
        try
        {
            _posixRegistrations = new IDisposable[]
            {
                PosixSignalRegistration.Create(
                    PosixSignal.SIGINT,
                    context => HandlePosixSignal(context, 130)),
                PosixSignalRegistration.Create(
                    PosixSignal.SIGTERM,
                    context => HandlePosixSignal(context, 143)),
                PosixSignalRegistration.Create(
                    PosixSignal.SIGQUIT,
                    context => HandlePosixSignal(context, 131))
            };
        }
        catch (PlatformNotSupportedException)
        {
            _posixRegistrations = null;
        }
    }

    private void HandlePosixSignal(PosixSignalContext context, int exitCode)
    {
        context.Cancel = true;
        _ = BeginSignalShutdown(exitCode);
    }

    internal Task HandlePosixSignalForTesting(Action cancel, int exitCode)
    {
        if (cancel is null)
        {
            throw new ArgumentNullException(nameof(cancel));
        }

        cancel();
        return BeginSignalShutdown(exitCode);
    }

    private Task BeginSignalShutdown(int exitCode)
    {
        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownTask = completion.Task;
        }

        _ = CompleteSignalShutdownAsync(exitCode, completion);
        return completion.Task;
    }

    private async Task CompleteSignalShutdownAsync(
        int exitCode,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await GetDisposeTask().ConfigureAwait(false);
            _terminateProcess(exitCode);
            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private Task GetDisposeTask()
    {
        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            if (_disposeTask is not null)
            {
                return _disposeTask;
            }

            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = CompleteDisposeAsync(completion);
        return completion.Task;
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await _dispose().ConfigureAwait(false);
            completion.TrySetResult(true);
        }
        catch
        {
            completion.TrySetResult(false);
        }
    }
}
