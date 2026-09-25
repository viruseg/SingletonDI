using Microsoft.CodeAnalysis;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Symbol display formats shared by the generator.
/// </summary>
internal static class SymbolDisplayFormats
{
    /// <summary>
    /// Renders a type as a name that is valid C# source, ready to be prefixed with
    /// <c>global::</c> and written into generated code.
    /// </summary>
    /// <remarks>
    /// <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> carries
    /// <see cref="SymbolDisplayMiscellaneousOptions.UseSpecialTypes"/>, so it renders the
    /// predefined types by their C# keywords: <c>typeof(object)</c> came out as <c>object</c> and
    /// <c>typeof(IDisposable)</c> as <c>IDisposable</c>. Prefixing those with <c>global::</c>
    /// produced <c>global::object</c> and <c>global::IDisposable</c>, which do not compile. This
    /// format keeps the global namespace and the keyword escaping but drops the special types, so
    /// every rendered name is a real identifier. Diagnostics are unaffected apart from becoming
    /// fully qualified, which is unambiguous rather than lossy.
    /// </remarks>
    internal static readonly SymbolDisplayFormat CodeGeneration =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
}
