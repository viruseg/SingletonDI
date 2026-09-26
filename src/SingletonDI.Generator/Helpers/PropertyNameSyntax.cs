using Microsoft.CodeAnalysis.CSharp;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// The outcome of classifying a requested singleton property name.
/// </summary>
internal enum PropertyNameKind
{
    /// <summary>
    /// The name cannot be declared as a C# member, escaped or not.
    /// </summary>
    Invalid,

    /// <summary>
    /// The name is a reserved keyword written without the '@' escape.
    /// </summary>
    ReservedKeyword,

    /// <summary>
    /// The name can be declared and is emitted exactly as it was written.
    /// </summary>
    Valid,
}

/// <summary>
/// Classifies the property name requested through a provide attribute, so that the provider and the
/// consumer path agree on which names may reach the generated declaration.
/// </summary>
internal static class PropertyNameSyntax
{
    /// <summary>
    /// Classifies a requested property name. A leading '@' escapes a keyword, which is why such a name
    /// is valid: the emitter writes the name verbatim and the result is a legal declaration.
    /// </summary>
    /// <param name="name">The requested name, or <c>null</c> when no name was specified.</param>
    /// <returns>The classification of <paramref name="name"/>.</returns>
    public static PropertyNameKind Classify(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return PropertyNameKind.Invalid;
        }

        if (name![0] == '@')
        {
            return SyntaxFacts.IsValidIdentifier(name.Substring(1))
                ? PropertyNameKind.Valid
                : PropertyNameKind.Invalid;
        }

        if (!SyntaxFacts.IsValidIdentifier(name))
        {
            return PropertyNameKind.Invalid;
        }

        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? PropertyNameKind.Valid
            : PropertyNameKind.ReservedKeyword;
    }
}
