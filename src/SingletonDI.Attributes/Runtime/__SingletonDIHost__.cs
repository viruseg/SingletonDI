using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SingletonDI.Generated;

/// <summary>
/// Provides the registration and resolution contract used by generated provider modules.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class __SingletonDIHost__
{
    /// <summary>
    /// Registers a provider using its service and implementation types.
    /// </summary>
    /// <typeparam name="TService">The service key exposed to consumers.</typeparam>
    /// <typeparam name="TImplementation">The concrete provider type.</typeparam>
    /// <param name="factory">Creates the provider instance.</param>
    /// <param name="dependencyTypes">Service keys required by the provider.</param>
    /// <param name="initializeAsync">
    /// Initializes the provider asynchronously, when provided. Generated modules adapt both
    /// <see cref="Task"/>-returning and <see cref="ValueTask"/>-returning provider methods to this delegate.
    /// </param>
    /// <param name="dispose">Disposes the provider synchronously, when provided.</param>
    /// <param name="disposeAsync">Disposes the provider asynchronously, when provided.</param>
    public static void RegisterProvider<TService, TImplementation>(
        Func<TImplementation> factory,
        Type[] dependencyTypes,
        Func<TImplementation, Task>? initializeAsync,
        Action<TImplementation>? dispose,
        Func<TImplementation, Task>? disposeAsync)
        where TImplementation : TService
    {
        SingletonDIInitializer.RegisterProvider<TService, TImplementation>(
            factory,
            dependencyTypes,
            initializeAsync,
            dispose,
            disposeAsync);
    }

    /// <summary>
    /// Removes the registrations made since initialization last committed them, so a module whose
    /// bootstrap failed part-way through can be attempted again.
    /// </summary>
    public static void RollbackRegistrations()
    {
        SingletonDIInitializer.RollbackRegistrations();
    }

    /// <summary>
    /// Resolves an initialized provider by its service key.
    /// </summary>
    /// <typeparam name="TService">The service key to resolve.</typeparam>
    /// <returns>The singleton provider instance.</returns>
    public static TService Resolve<TService>()
    {
        return SingletonDIInitializer.Resolve<TService>();
    }

    /// <summary>
    /// Returns the container to its pre-initialization state so a later test can initialize it
    /// again. Test-only; a released container never accepts a second initialization.
    /// </summary>
    internal static void ResetForTesting()
    {
        SingletonDIInitializer.ResetForTesting();
    }
}
