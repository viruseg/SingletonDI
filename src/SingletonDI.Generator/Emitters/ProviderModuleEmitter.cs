using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
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
        source.AppendLine("#nullable enable");

        if (hasLocalProviders)
        {
            source.AppendLine("[assembly: global::SingletonDI.Attributes.SingletonDIProviderModuleAttribute]");
        }

        source.AppendLine("#if !NET5_0_OR_GREATER");
        source.AppendLine("namespace System.Runtime.CompilerServices");
        source.AppendLine("{");
        source.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]");
        source.AppendLine("    internal sealed class ModuleInitializerAttribute : global::System.Attribute");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine("#endif");
        source.AppendLine();
        source.AppendLine("namespace SingletonDI.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Provides generated provider registrations for this assembly.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.AppendLine($"    public static class {moduleTypeName}");
        source.AppendLine("    {");
        source.AppendLine("        private static int _bootstrapState;");
        source.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializerAttribute]");
        source.AppendLine("        internal static void Initialize()");
        source.AppendLine("        {");
        source.AppendLine("            Bootstrap();");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        /// <summary>");
        source.AppendLine("        /// Registers generated providers exactly once.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        public static void Bootstrap()");
        source.AppendLine("        {");
        source.AppendLine("            if (global::System.Threading.Interlocked.Exchange(ref _bootstrapState, 1) != 0)");
        source.AppendLine("            {");
        source.AppendLine("                return;");
        source.AppendLine("            }");
        source.AppendLine();

        foreach (var externalAssembly in externalAssemblies)
        {
            var bootstrapType = externalAssembly.BootstrapTypeIdentity!.Value.ToGlobalTypeName();
            source.AppendLine($"            {bootstrapType}.Bootstrap();");
        }

        foreach (var provider in orderedLocalProviders)
        {
            AppendProviderRegistration(source, provider);
        }

        source.AppendLine("        }");
        source.AppendLine();

        if (hasLocalProviders && orderedLocalProviders.Any(provider => provider.HasInitializeAsyncMethod))
        {
            source.AppendLine("        private static global::System.Threading.Tasks.Task ToTask(global::System.Threading.Tasks.Task task)");
            source.AppendLine("        {");
            source.AppendLine("            return task;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        private static global::System.Threading.Tasks.Task ToTask(global::System.Threading.Tasks.ValueTask valueTask)");
            source.AppendLine("        {");
            source.AppendLine("            return valueTask.AsTask();");
            source.AppendLine("        }");
            source.AppendLine();
        }

        source.AppendLine("    }");
        source.AppendLine("}");
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

    private static void AppendProviderRegistration(StringBuilder source, ProviderModel provider)
    {
        var implementationType = ToGlobalTypeName(provider.FullyQualifiedName);
        var serviceType = provider.ServiceTypeIdentity is { } serviceTypeIdentity
            ? serviceTypeIdentity.ToGlobalTypeName()
            : provider.ServiceTypeFullyQualifiedName is { Length: > 0 } serviceTypeName
                ? ToGlobalTypeName(serviceTypeName)
                : implementationType;

        source.AppendLine(
            $"            global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<{serviceType}, {implementationType}>(");
        source.AppendLine($"                () => new {implementationType}(),");
        AppendDependencies(source, provider.DependencyIdentities);
        source.AppendLine(provider.HasInitializeAsyncMethod
            ? "                static value => ToTask(value.InitializeAsync()),"
            : "                null,");
        AppendDisposeDelegates(source, provider);
        source.AppendLine();
    }

    private static void AppendDependencies(
        StringBuilder source,
        ImmutableArray<ServiceTypeIdentity> dependencies)
    {
        if (dependencies.IsDefault || dependencies.IsEmpty)
        {
            source.AppendLine("                global::System.Array.Empty<global::System.Type>(),");
            return;
        }

        var distinctDependencies = dependencies
            .Distinct()
            .ToImmutableArray();
        if (distinctDependencies.IsEmpty)
        {
            source.AppendLine("                global::System.Array.Empty<global::System.Type>(),");
            return;
        }

        source.AppendLine("                new global::System.Type[]");
        source.AppendLine("                {");
        for (var index = 0; index < distinctDependencies.Length; index++)
        {
            var separator = index == distinctDependencies.Length - 1 ? string.Empty : ",";
            source.AppendLine(
                $"                    typeof({distinctDependencies[index].ToGlobalTypeName()}){separator}");
        }

        source.AppendLine("                },");
    }

    private static void AppendDisposeDelegates(
        StringBuilder source,
        ProviderModel provider)
    {
        if (provider.IsAsyncDisposable)
        {
            source.AppendLine("                null,");
            source.AppendLine(
                $"                static value => ((global::System.IAsyncDisposable)value).DisposeAsync().AsTask());");
            return;
        }

        if (provider.IsDisposable)
        {
            source.AppendLine(
                $"                static value => ((global::System.IDisposable)value).Dispose(),");
            source.AppendLine("                null);");
            return;
        }

        source.AppendLine("                null,");
        source.AppendLine("                null);");
    }

    private static string ToGlobalTypeName(string fullyQualifiedName)
    {
        return fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName
            : $"global::{fullyQualifiedName}";
    }
}
