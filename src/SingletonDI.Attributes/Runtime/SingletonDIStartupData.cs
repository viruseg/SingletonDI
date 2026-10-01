using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

/// <summary>
/// Carries external data from the code that starts the application to the providers that are
/// initialized during <see cref="SingletonDIInitializer.InitializeAsync(bool, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// A provider that needs a value it cannot construct on its own reads it with
/// <see cref="Get{T}"/> from its constructor or its initializer. The data is a hand-off, not a
/// field: the container calls <see cref="IDisposable.Dispose"/> or
/// <see cref="IAsyncDisposable.DisposeAsync"/> on every value it holds and releases its references
/// when initialization completes. A provider that keeps a disposable value for longer than its own
/// initialization must copy what it needs instead.
/// </remarks>
public static class SingletonDIStartupData
{
    /// <summary>
    /// Key under which a failure to release startup data is recorded on the initialization failure
    /// that triggered the cleanup, so the original error is still what propagates.
    /// </summary>
    internal const string CleanupFailureKey = "SingletonDI.StartupDataCleanupFailure";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static bool _sealed;
    private static bool _released;

    /// <summary>
    /// Stores a value for the providers that are initialized next.
    /// </summary>
    /// <typeparam name="T">The type readers must ask for. One name carries exactly one type.</typeparam>
    /// <param name="name">
    /// Identifies the value. Compared with ordinal string equality, unique across every call.
    /// </param>
    /// <param name="value">
    /// The value to hand to the providers. A null value is stored like any other: a missing name
    /// is reported as a missing name, so it is never confused with a stored null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or consists only of white-space characters.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The name is already taken, or initialization has already started. Writes stop at the first
    /// <see cref="SingletonDIInitializer.InitializeAsync(bool, CancellationToken)"/> call, whether or
    /// not that call has completed yet.
    /// </exception>
    public static void Set<T>(string name, T value)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A startup data name cannot be empty.", nameof(name));
        }

        lock (Sync)
        {
            if (_sealed)
            {
                throw new InvalidOperationException(
                    "Startup data cannot be set because initialization has already started.");
            }

            if (Entries.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Startup data named '{name}' has already been set as '{existing.DeclaredType}'.");
            }

            Entries.Add(name, new Entry(typeof(T), value));
        }
    }

    /// <summary>
    /// Reads a value stored with <see cref="Set{T}"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The type the value was stored as. It has to match: a base or derived type argument is
    /// rejected, so one name means one type for every reader.
    /// </typeparam>
    /// <param name="name">The name the value was stored under.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Initialization has completed and the data has been released.
    /// </exception>
    /// <exception cref="KeyNotFoundException">No value is stored under <paramref name="name"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The value is stored as a different type.
    /// </exception>
    public static T Get<T>(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        lock (Sync)
        {
            if (_released)
            {
                throw new InvalidOperationException(
                    "Startup data is no longer available. It is cleared when initialization completes.");
            }

            if (!Entries.TryGetValue(name, out var entry))
            {
                throw new KeyNotFoundException($"No startup data named '{name}' has been set.");
            }

            if (entry.DeclaredType != typeof(T))
            {
                throw new InvalidCastException(
                    $"Startup data named '{name}' is '{entry.DeclaredType}', not '{typeof(T)}'.");
            }

            return (T)entry.Value!;
        }
    }

    /// <summary>
    /// Refuses every later write, so nothing can join an initialization that is already running.
    /// </summary>
    internal static void Seal()
    {
        lock (Sync)
        {
            _sealed = true;
        }
    }

    /// <summary>
    /// Releases every value and drops every reference the container holds.
    /// </summary>
    /// <returns>
    /// A task that completes once each value has been disposed. A value that throws while being
    /// released does not stop the others: one failure is rethrown as it is, several are reported as
    /// an <see cref="AggregateException"/>.
    /// </returns>
    internal static async Task ClearAndDisposeAsync()
    {
        List<Entry> released;
        lock (Sync)
        {
            released = new List<Entry>(Entries.Count);
            foreach (var entry in Entries)
            {
                released.Add(entry.Value);
            }

            Entries.Clear();
            _released = true;
        }

        var failures = new List<Exception>();
        foreach (var entry in released)
        {
            try
            {
                if (entry.Value is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        else if (failures.Count > 1)
        {
            throw new AggregateException(
                "One or more startup data values failed to release.", failures);
        }
    }

    /// <summary>
    /// Clears the data and allows writing again, so a later test can start from a clean state.
    /// </summary>
    internal static void ResetForTesting()
    {
        try
        {
            ClearAndDisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // A test reset has no caller to report to, and the next test starts empty either way.
        }

        lock (Sync)
        {
            _sealed = false;
            _released = false;
        }
    }

    private readonly struct Entry
    {
        internal Entry(Type declaredType, object? value)
        {
            DeclaredType = declaredType;
            Value = value;
        }

        internal Type DeclaredType { get; }

        internal object? Value { get; }
    }
}