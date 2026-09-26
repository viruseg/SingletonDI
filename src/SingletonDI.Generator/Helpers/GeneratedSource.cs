using System.Text;

namespace SingletonDI.Generator.Helpers;

/// <summary>
/// Appends lines to generated source with a newline that does not depend on the host.
/// </summary>
/// <remarks>
/// <see cref="StringBuilder.AppendLine(string)"/> follows <see cref="System.Environment.NewLine"/>, so
/// the same generator run produced different bytes for the same input on Windows and on Linux. That
/// shows up in an <c>EmitCompilerGeneratedFiles</c> dump and defeats any text snapshot of the output.
/// </remarks>
internal static class GeneratedSource
{
    /// <summary>
    /// Appends one generated line followed by a line feed.
    /// </summary>
    /// <param name="source">The buffer receiving the generated source.</param>
    /// <param name="value">The line to append. An empty value writes the newline on its own.</param>
    internal static void AppendLine(StringBuilder source, string value)
    {
        source.Append(value).Append('\n');
    }
}
