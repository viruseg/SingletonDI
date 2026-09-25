using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

internal sealed class ShutdownManager : IDisposable
{
    private static readonly (PosixSignal Signal, int ExitCode)[] PosixSignals =
    [
        (PosixSignal.SIGINT, 130),
        (PosixSignal.SIGTERM, 143),
        (PosixSignal.SIGQUIT, 131),
    ];

    private readonly Func<ValueTask> _dispose;
    private readonly Action<int> _terminateProcess;
    private readonly IShutdownSignalSource _signalSource;
    private readonly object _sync = new();
    private IDisposable[]? _registrations;
    private Task? _disposeTask;
    private Task? _shutdownTask;
    private bool _registered;
    private bool _disposed;

    internal ShutdownManager(Func<ValueTask> dispose, Action<int> terminateProcess)
        : this(dispose, terminateProcess, PlatformShutdownSignalSource.Instance)
    {
    }

    internal ShutdownManager(
        Func<ValueTask> dispose,
        Action<int> terminateProcess,
        IShutdownSignalSource signalSource)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _terminateProcess = terminateProcess ?? throw new ArgumentNullException(nameof(terminateProcess));
        _signalSource = signalSource ?? throw new ArgumentNullException(nameof(signalSource));
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

            var registrations = new List<IDisposable>(5);
            try
            {
                registrations.Add(_signalSource.RegisterProcessExit(
                    () => OnProcessExit(null, EventArgs.Empty)));
                registrations.Add(_signalSource.RegisterCancelKeyPress(
                    args => OnCancelKeyPress(null, args)));
                RegisterPosixSignals(registrations);
                _registrations = registrations.ToArray();
                _registered = true;
            }
            catch
            {
                DisposeRegistrations(registrations);
                throw;
            }
        }
    }

    public void Dispose()
    {
        IDisposable[]? registrations;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registrations = _registrations;
            _registrations = null;
        }

        if (registrations is not null)
        {
            DisposeRegistrations(registrations);
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

    private void RegisterPosixSignals(List<IDisposable> registrations)
    {
        if (!_signalSource.SupportsPosixSignals)
        {
            return;
        }

        var posixRegistrationStart = registrations.Count;
        try
        {
            foreach (var (signal, exitCode) in PosixSignals)
            {
                registrations.Add(_signalSource.RegisterPosixSignal(
                    signal,
                    context => HandlePosixSignal(context, exitCode)));
            }
        }
        catch (PlatformNotSupportedException)
        {
            var partialPosixRegistrations = registrations.GetRange(
                posixRegistrationStart,
                registrations.Count - posixRegistrationStart);
            registrations.RemoveRange(posixRegistrationStart, partialPosixRegistrations.Count);
            DisposeRegistrations(partialPosixRegistrations);
        }
    }

    private static void DisposeRegistrations(IEnumerable<IDisposable> registrations)
    {
        foreach (var registration in registrations.Reverse())
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

internal interface IShutdownSignalSource
{
    bool SupportsPosixSignals { get; }

    IDisposable RegisterProcessExit(Action handler);

    IDisposable RegisterCancelKeyPress(Action<ConsoleCancelEventArgs> handler);

    IDisposable RegisterPosixSignal(PosixSignal signal, Action<PosixSignalContext> handler);
}

internal sealed class PlatformShutdownSignalSource : IShutdownSignalSource
{
    internal static PlatformShutdownSignalSource Instance { get; } = new();

    public bool SupportsPosixSignals => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public IDisposable RegisterProcessExit(Action handler)
    {
        EventHandler eventHandler = (_, _) => handler();
        AppDomain.CurrentDomain.ProcessExit += eventHandler;
        return new ShutdownEventRegistration(() => AppDomain.CurrentDomain.ProcessExit -= eventHandler);
    }

    public IDisposable RegisterCancelKeyPress(Action<ConsoleCancelEventArgs> handler)
    {
        ConsoleCancelEventHandler eventHandler = (_, args) => handler(args);
        Console.CancelKeyPress += eventHandler;
        return new ShutdownEventRegistration(() => Console.CancelKeyPress -= eventHandler);
    }

    public IDisposable RegisterPosixSignal(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        return PosixSignalRegistration.Create(signal, handler);
    }

    private sealed class ShutdownEventRegistration : IDisposable
    {
        private Action? _unsubscribe;

        internal ShutdownEventRegistration(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
