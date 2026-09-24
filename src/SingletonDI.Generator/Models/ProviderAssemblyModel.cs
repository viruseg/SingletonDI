namespace SingletonDI.Generator.Models;

internal readonly record struct ProviderAssemblyModel(
    string AssemblyIdentity,
    string BootstrapTypeFullyQualifiedName,
    bool HasModuleMarker,
    ServiceTypeIdentity? BootstrapTypeIdentity = null);
