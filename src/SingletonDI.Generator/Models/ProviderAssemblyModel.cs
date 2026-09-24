namespace SingletonDI.Generator.Models;

internal readonly record struct ProviderAssemblyModel(
    string AssemblyIdentity,
    bool HasModuleMarker,
    ServiceTypeIdentity? BootstrapTypeIdentity = null,
    bool HasBootstrapMethod = false)
{
    internal string BootstrapTypeFullyQualifiedName =>
        BootstrapTypeIdentity?.FullyQualifiedName ?? string.Empty;
}
