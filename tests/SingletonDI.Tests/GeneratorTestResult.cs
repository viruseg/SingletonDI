using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SingletonDI.Tests;

internal sealed record GeneratorTestResult(
    ImmutableArray<Diagnostic> Diagnostics,
    Compilation OutputCompilation,
    string GeneratedSource,
    OutputKind OutputKind);
