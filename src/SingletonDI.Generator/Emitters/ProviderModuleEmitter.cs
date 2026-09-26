using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Emitters;

internal static class ProviderModuleEmitter
{
    private const string ProviderModuleTypePrefix = "__SingletonDIProviderModule__";

    /// <summary>
    /// Module type name emitted for an assembly that both hosts providers and is a composition
    /// root. Exposed so the referencing side can discover the same name.
    /// </summary>
    internal const string CompositionRootModuleTypeName = "__SingletonDICompositionRootModule__";

    internal static string Generate(
        ImmutableArray<ProviderModel> localProviders,
        ImmutableArray<ProviderAssemblyModel> externalProviderAssemblies,
        bool isCompositionRoot)
    {
        var orderedLocalProviders = localProviders.IsDefault
            ? ImmutableArray<ProviderModel>.Empty
            : localProviders
                .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
                .ToImmutableArray();
        var hasLocalProviders = !orderedLocalProviders.IsDefault && !orderedLocalProviders.IsEmpty;
        var externalAssemblies = GetExternalAssemblies(
            orderedLocalProviders,
            externalProviderAssemblies,
            isCompositionRoot);
        if (!hasLocalProviders && externalAssemblies.Count == 0)
        {
            return string.Empty;
        }

        var moduleTypeName = isCompositionRoot
            ? CompositionRootModuleTypeName
            : GetProviderModuleTypeName(orderedLocalProviders[0].AssemblyIdentity);

        var source = new StringBuilder();
        GeneratedSource.AppendLine(source, "#nullable enable");

        if (hasLocalProviders)
        {
            GeneratedSource.AppendLine(source, "[assembly: global::SingletonDI.Attributes.SingletonDIProviderModuleAttribute]");
        }

        GeneratedSource.AppendLine(source, "#if !NET5_0_OR_GREATER");
        GeneratedSource.AppendLine(source, "namespace System.Runtime.CompilerServices");
        GeneratedSource.AppendLine(source, "{");
        GeneratedSource.AppendLine(source, "    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]");
        GeneratedSource.AppendLine(source, "    internal sealed class ModuleInitializerAttribute : global::System.Attribute");
        GeneratedSource.AppendLine(source, "    {");
        GeneratedSource.AppendLine(source, "    }");
        GeneratedSource.AppendLine(source, "}");
        GeneratedSource.AppendLine(source, "#endif");
        GeneratedSource.AppendLine(source, string.Empty);
        GeneratedSource.AppendLine(source, "namespace SingletonDI.Generated");
        GeneratedSource.AppendLine(source, "{");
        GeneratedSource.AppendLine(source, "    /// <summary>");
        GeneratedSource.AppendLine(source, "    /// Provides generated provider registrations for this assembly.");
        GeneratedSource.AppendLine(source, "    /// </summary>");
        GeneratedSource.AppendLine(source, "    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        GeneratedSource.AppendLine(source, $"    public static class {moduleTypeName}");
        GeneratedSource.AppendLine(source, "    {");
        GeneratedSource.AppendLine(source, "        private static int _bootstrapState;");
        GeneratedSource.AppendLine(source, "        [global::System.Runtime.CompilerServices.ModuleInitializerAttribute]");
        GeneratedSource.AppendLine(source, "        internal static void Initialize()");
        GeneratedSource.AppendLine(source, "        {");
        GeneratedSource.AppendLine(source, "            Bootstrap();");
        GeneratedSource.AppendLine(source, "        }");
        GeneratedSource.AppendLine(source, string.Empty);
        GeneratedSource.AppendLine(source, "        /// <summary>");
        GeneratedSource.AppendLine(source, "        /// Registers generated providers exactly once.");
        GeneratedSource.AppendLine(source, "        /// </summary>");
        GeneratedSource.AppendLine(source, "        /// <remarks>");
        GeneratedSource.AppendLine(source, "        /// A failure part-way through undoes the registrations that already succeeded,");
        GeneratedSource.AppendLine(source, "        /// releases the guard, and rethrows, so a retry sees the original error at the");
        GeneratedSource.AppendLine(source, "        /// bootstrap site instead of a duplicate key or a later missing-dependency failure");
        GeneratedSource.AppendLine(source, "        /// over a half-registered graph.");
        GeneratedSource.AppendLine(source, "        /// </remarks>");
        GeneratedSource.AppendLine(source, "        public static void Bootstrap()");
        GeneratedSource.AppendLine(source, "        {");
        GeneratedSource.AppendLine(source, "            if (global::System.Threading.Interlocked.Exchange(ref _bootstrapState, 1) != 0)");
        GeneratedSource.AppendLine(source, "            {");
        GeneratedSource.AppendLine(source, "                return;");
        GeneratedSource.AppendLine(source, "            }");
        GeneratedSource.AppendLine(source, string.Empty);
        GeneratedSource.AppendLine(source, "            try");
        GeneratedSource.AppendLine(source, "            {");
        AppendBootstrapBody(source, externalAssemblies, orderedLocalProviders);
        GeneratedSource.AppendLine(source, "            }");
        GeneratedSource.AppendLine(source, "            catch");
        GeneratedSource.AppendLine(source, "            {");
        GeneratedSource.AppendLine(source, "                global::SingletonDI.Generated.__SingletonDIHost__.RollbackRegistrations();");
        GeneratedSource.AppendLine(source, "                global::System.Threading.Interlocked.Exchange(ref _bootstrapState, 0);");
        GeneratedSource.AppendLine(source, "                throw;");
        GeneratedSource.AppendLine(source, "            }");
        GeneratedSource.AppendLine(source, "        }");
        GeneratedSource.AppendLine(source, string.Empty);

        if (hasLocalProviders && orderedLocalProviders.Any(provider => provider.HasInitializeAsyncMethod))
        {
            GeneratedSource.AppendLine(source, "        private static global::System.Threading.Tasks.Task ToTask(global::System.Threading.Tasks.Task task)");
            GeneratedSource.AppendLine(source, "        {");
            GeneratedSource.AppendLine(source, "            return task;");
            GeneratedSource.AppendLine(source, "        }");
            GeneratedSource.AppendLine(source, string.Empty);
            GeneratedSource.AppendLine(source, "        private static global::System.Threading.Tasks.Task ToTask(global::System.Threading.Tasks.ValueTask valueTask)");
            GeneratedSource.AppendLine(source, "        {");
            GeneratedSource.AppendLine(source, "            return valueTask.AsTask();");
            GeneratedSource.AppendLine(source, "        }");
            GeneratedSource.AppendLine(source, string.Empty);
        }

        GeneratedSource.AppendLine(source, "    }");
        GeneratedSource.AppendLine(source, "}");
        return source.ToString();
    }

    internal static string GetProviderModuleTypeName(string assemblyIdentity)
    {
        using var sha256 = SHA256.Create();
        var hash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(assemblyIdentity)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        return ProviderModuleTypePrefix + "_" + hash;
    }

    private static List<ProviderAssemblyModel> GetExternalAssemblies(
        ImmutableArray<ProviderModel> localProviders,
        ImmutableArray<ProviderAssemblyModel> externalProviderAssemblies,
        bool isCompositionRoot)
    {
        if (!isCompositionRoot || externalProviderAssemblies.IsDefault)
        {
            return [];
        }

        var localAssemblyIdentities = new HashSet<string>(StringComparer.Ordinal);
        if (!localProviders.IsDefault)
        {
            foreach (var provider in localProviders)
            {
                localAssemblyIdentities.Add(provider.AssemblyIdentity);
            }
        }

        return externalProviderAssemblies
            .Where(assembly =>
                assembly.HasModuleMarker &&
                assembly.HasBootstrapMethod &&
                assembly.BootstrapTypeIdentity is not null &&
                !localAssemblyIdentities.Contains(assembly.AssemblyIdentity))
            .GroupBy(assembly => assembly.AssemblyIdentity, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(assembly => assembly.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();
    }

    private static void AppendBootstrapBody(
        StringBuilder source,
        IReadOnlyList<ProviderAssemblyModel> externalAssemblies,
        ImmutableArray<ProviderModel> orderedLocalProviders)
    {
        foreach (var externalAssembly in externalAssemblies)
        {
            var bootstrapType = externalAssembly.BootstrapTypeIdentity!.Value.ToGlobalTypeName();
            GeneratedSource.AppendLine(source, $"                {bootstrapType}.Bootstrap();");
        }

        if (externalAssemblies.Count > 0)
        {
            GeneratedSource.AppendLine(source, string.Empty);
        }

        foreach (var provider in orderedLocalProviders)
        {
            AppendProviderRegistration(source, provider);
        }
    }

    private static void AppendProviderRegistration(StringBuilder source, ProviderModel provider)
    {
        var implementationType = ToGlobalTypeName(provider.FullyQualifiedName);
        var serviceType = provider.ServiceTypeIdentity is { } serviceTypeIdentity
            ? serviceTypeIdentity.ToGlobalTypeName()
            : provider.ServiceTypeFullyQualifiedName is { Length: > 0 } serviceTypeName
                ? ToGlobalTypeName(serviceTypeName)
                : implementationType;

        GeneratedSource.AppendLine(source, 
            $"                global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<{serviceType}, {implementationType}>(");
        GeneratedSource.AppendLine(source, $"                    () => new {implementationType}(),");
        AppendDependencies(source, provider.DependencyIdentities);
        GeneratedSource.AppendLine(source, provider.HasInitializeAsyncMethod
            ? "                    static value => ToTask(value.InitializeAsync()),"
            : "                    null,");
        AppendDisposeDelegates(source, provider);
        GeneratedSource.AppendLine(source, string.Empty);
    }

    private static void AppendDependencies(
        StringBuilder source,
        ImmutableArray<ServiceTypeIdentity> dependencies)
    {
        if (dependencies.IsDefault || dependencies.IsEmpty)
        {
            GeneratedSource.AppendLine(source, "                    global::System.Array.Empty<global::System.Type>(),");
            return;
        }

        var distinctDependencies = dependencies
            .Distinct()
            .ToImmutableArray();
        if (distinctDependencies.IsEmpty)
        {
            GeneratedSource.AppendLine(source, "                    global::System.Array.Empty<global::System.Type>(),");
            return;
        }

        GeneratedSource.AppendLine(source, "                    new global::System.Type[]");
        GeneratedSource.AppendLine(source, "                    {");
        for (var index = 0; index < distinctDependencies.Length; index++)
        {
            var separator = index == distinctDependencies.Length - 1 ? string.Empty : ",";
            GeneratedSource.AppendLine(source, 
                $"                        typeof({distinctDependencies[index].ToGlobalTypeName()}){separator}");
        }

        GeneratedSource.AppendLine(source, "                    },");
    }

    private static void AppendDisposeDelegates(
        StringBuilder source,
        ProviderModel provider)
    {
        if (provider.IsAsyncDisposable)
        {
            GeneratedSource.AppendLine(source, "                    null,");
            GeneratedSource.AppendLine(source, 
                $"                    static value => ((global::System.IAsyncDisposable)value).DisposeAsync().AsTask());");
            return;
        }

        if (provider.IsDisposable)
        {
            GeneratedSource.AppendLine(source, 
                $"                    static value => ((global::System.IDisposable)value).Dispose(),");
            GeneratedSource.AppendLine(source, "                    null);");
            return;
        }

        GeneratedSource.AppendLine(source, "                    null,");
        GeneratedSource.AppendLine(source, "                    null);");
    }

    private static string ToGlobalTypeName(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName
            : $"global::{fullyQualifiedName}";
    }
}
