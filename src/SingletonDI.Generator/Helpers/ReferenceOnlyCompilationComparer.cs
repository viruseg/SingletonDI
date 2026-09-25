using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SingletonDI.Generator.Helpers;

internal sealed class ReferenceOnlyCompilationComparer : IEqualityComparer<Compilation>
{
    public static ReferenceOnlyCompilationComparer Instance { get; } = new();

    public bool Equals(Compilation? x, Compilation? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return string.Equals(GetKey(x), GetKey(y), StringComparison.Ordinal);
    }

    public int GetHashCode(Compilation obj)
    {
        return StringComparer.Ordinal.GetHashCode(GetKey(obj));
    }

    private static string GetKey(Compilation compilation)
    {
        var options = compilation.Options;
        var csharpOptions = options as CSharpCompilationOptions;
        var specificDiagnostics = string.Join(
            ";",
            options.SpecificDiagnosticOptions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        var references = string.Join(
            "\u001f",
            compilation.References
                .OrderBy(reference => reference.Display, StringComparer.Ordinal)
                .Select(reference =>
                {
                    var portableReference = reference as PortableExecutableReference;
                    var aliases = reference.Properties.Aliases.IsDefault
                        ? string.Empty
                        : string.Join(",", reference.Properties.Aliases.OrderBy(alias => alias, StringComparer.Ordinal));
                    return string.Join(
                        "|",
                        RuntimeHelpers.GetHashCode(reference),
                        reference.Display,
                        portableReference?.FilePath,
                        reference.Properties.EmbedInteropTypes,
                        aliases);
                }));
        var key = new StringBuilder()
            .Append(compilation.Assembly.Identity)
            .Append('\u001f')
            .Append(options.OutputKind)
            .Append('\u001f')
            .Append(csharpOptions?.AllowUnsafe)
            .Append('\u001f')
            .Append(options.CheckOverflow)
            .Append('\u001f')
            .Append(options.OptimizationLevel)
            .Append('\u001f')
            .Append(options.Platform)
            .Append('\u001f')
            .Append(options.WarningLevel)
            .Append('\u001f')
            .Append(options.NullableContextOptions)
            .Append('\u001f')
            .Append(options.ConcurrentBuild)
            .Append('\u001f')
            .Append(options.GeneralDiagnosticOption)
            .Append('\u001f')
            .Append(options.MainTypeName)
            .Append('\u001f')
            .Append(options.ModuleName)
            .Append('\u001f')
            .Append(options.CryptoKeyContainer)
            .Append('\u001f')
            .Append(options.CryptoKeyFile)
            .Append('\u001f')
            .Append(options.MetadataImportOptions)
            .Append('\u001f')
            .Append(options.SourceReferenceResolver is null
                ? 0
                : RuntimeHelpers.GetHashCode(options.SourceReferenceResolver))
            .Append('\u001f')
            .Append(options.MetadataReferenceResolver is null
                ? 0
                : RuntimeHelpers.GetHashCode(options.MetadataReferenceResolver))
            .Append('\u001f')
            .Append(specificDiagnostics)
            .Append('\u001f')
            .Append(references)
            .ToString();
        return key;
    }
}
