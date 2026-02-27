namespace SingletonDI.Attributes;

/// <summary>
/// Optional interface for asynchronous initialization of singleton providers.
/// </summary>
public interface IInitializeAsync
{
    /// <summary>
    /// Called during container initialization to perform asynchronous setup.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization.</returns>
    Task InitializeAsync();
}
