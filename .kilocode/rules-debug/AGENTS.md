# AGENTS.md — Debug Mode

This file provides guidance to agents when working with code in this repository.

## Debug Mode — SingletonDI Generator

### Common issues and debugging

- Source generator doesn't run: Check .csproj has `<GeneratorPackage>Microsoft.CodeAnalysis.CSharp.SourceGenerators</GeneratorPackage>`
- Generated code not appearing: Enable "Show generated files" in IDE
- Incremental pipeline caching: Use `dotnet build -v:diag` for detailed pipeline execution info
- Test failures: Use `dotnet test --filter "FullyQualifiedName~TestName" -v d` for verbose output

### Debug diagnostics

- Set environment variable `ROSLYN_GENERATOR_LOGGING=1` to see generator execution
- Check "Analyzers" node in Solution Explorer for diagnostic messages
- View generated code by opening `<file>.g.cs` files (may be hidden)

### Test debugging

- Tests use Microsoft.CodeAnalysis.Testing — use `Verifier.VerifyAnalyzer()` for diagnostic tests
- Set breakpoints in generator code — attach to build process if needed
