namespace SingletonDI.Attributes;

/// <summary>
/// Optional interface for synchronous initialization of singleton providers.
/// </summary>
public interface IInitializeSync
{
    /// <summary>
    /// Called during container initialization to perform synchronous setup.
    /// </summary>
    void Initialize();
}
