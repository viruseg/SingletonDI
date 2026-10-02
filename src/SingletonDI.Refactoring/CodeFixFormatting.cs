using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SingletonDI.Refactoring;

/// <summary>
/// Keeps the diff of a code fix confined to the region the fix owns.
/// </summary>
/// <remarks>
/// <para>
/// Marking a node with <c>Formatter.Annotation</c> makes the IDE re-lay out that node's whole span, so
/// annotating a declaration or an attribute rewrote the user's own formatting across method bodies,
/// casts and parameter lists the fix had no business touching. Annotating only the tokens the fix
/// produced keeps the re-layout to the inserted text.
/// </para>
/// <para>
/// Every fix that changes text must route the change through this class so the rule cannot be
/// bypassed by accident.
/// </para>
/// </remarks>
internal static class CodeFixFormatting
{
    /// <summary>
    /// Appends a modifier to <paramref name="modifiers"/> and marks it for formatting.
    /// </summary>
    /// <param name="modifiers">The modifier list to extend. It is left untouched.</param>
    /// <param name="kind">The modifier keyword to append.</param>
    /// <returns>
    /// The extended list. The new token carries its own trailing space, because a token created
    /// through <see cref="SyntaxFactory"/> has no trivia at all.
    /// </returns>
    internal static SyntaxTokenList AppendModifier(SyntaxTokenList modifiers, SyntaxKind kind)
    {
        var token = SyntaxFactory.Token(kind)
            .WithTrailingTrivia(SyntaxFactory.Space)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return modifiers.Insert(modifiers.Count, token);
    }

    /// <summary>
    /// Marks a rewritten modifier list for formatting.
    /// </summary>
    /// <remarks>
    /// Only the first modifier is annotated, because that is the freshly inserted token and the only
    /// one whose spacing is now wrong. Annotating the whole list, or the declaration that owns it,
    /// would reformat every token in the range instead.
    /// </remarks>
    internal static SyntaxTokenList AnnotateFirstModifier(SyntaxTokenList modifiers)
    {
        if (modifiers.Count == 0)
        {
            return modifiers;
        }

        return modifiers.Replace(
            modifiers[0],
            modifiers[0].WithAdditionalAnnotations(Formatter.Annotation));
    }

    /// <summary>
    /// Removes one argument from an attribute argument list.
    /// </summary>
    /// <remarks>
    /// <see cref="SyntaxFactory.SyntaxList{T}"/> removal already carries the removed argument's
    /// trivia onto its neighbour and drops the separator that joined them, so the resulting list is
    /// well formed without being formatted. Nothing here is marked for formatting: annotating the
    /// list or the attribute would re-lay out the whole attribute and rewrite the spacing the user
    /// wrote inside the parentheses.
    /// </remarks>
    /// <param name="argumentList">The list to remove from. It is left untouched.</param>
    /// <param name="argumentToRemove">The argument to remove.</param>
    /// <returns>
    /// A list without <paramref name="argumentToRemove"/>, or the same list when the argument is not
    /// in it or is the only one.
    /// </returns>
    internal static AttributeArgumentListSyntax RemoveArgument(
        AttributeArgumentListSyntax argumentList,
        AttributeArgumentSyntax argumentToRemove)
    {
        var arguments = argumentList.Arguments;
        if (arguments.IndexOf(argumentToRemove) < 0 || arguments.Count < 2)
        {
            return argumentList;
        }

        return argumentList.WithArguments(arguments.Remove(argumentToRemove));
    }
}
