using Shared.Contracts;
using SingletonDI.Attributes;

namespace ConsumerLibrary;

/// <summary>
/// Provides a consumer-side entry point for the shared database contract.
/// </summary>
[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
    /// <summary>
    /// Gets the initialized database service through the generated consumer property.
    /// </summary>
    /// <returns>The resolved database service.</returns>
    public IDatabaseService GetService()
    {
        return IDatabaseServiceInstance;
    }
}
