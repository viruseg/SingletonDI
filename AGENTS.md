# AGENTS.md

This file provides guidance for agents working in the SingletonDI repository.

## Project

SingletonDI is an incremental C# source generator and runtime library for compile-time singleton dependency graphs. The generator emits typed registrations and consumer properties; the runtime is `SingletonDI.Generated`.

## Repository structure

```text
SingletonDI/
├── src/
│   ├── SingletonDI.Attributes/    # Attributes and generated-runtime API
│   ├── SingletonDI.Generator/     # Incremental source generator
│   ├── SingletonDI.Refactoring/   # Code fixes and refactorings
│   └── SingletonDI.SampleApp/     # Executable sample
└── tests/
    ├── SingletonDI.Tests/         # xUnit and Roslyn test harnesses
    ├── InterProjectFixtures/      # Provider, consumer, and root scenarios
    └── SingletonDI.Tests/PackageSmoke/
```

## Commands

- **Build solution:** `dotnet build SingletonDI.slnx --configuration Release`
- **Build generator:** `dotnet build src/SingletonDI.Generator/SingletonDI.Generator.csproj`
- **Run sample:** `dotnet run --project src/SingletonDI.SampleApp/SingletonDI.SampleApp.csproj`
- **Test:** `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --no-restore --nologo --logger "console;verbosity=minimal" --blame-hang --blame-hang-timeout 300s --blame-hang-dump-type mini`
- **Focused test:** `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~TestClass.TestMethod" --no-restore --nologo --logger "console;verbosity=minimal" --blame-hang --blame-hang-timeout 300s --blame-hang-dump-type mini`

`--blame-hang` is mandatory, not optional: without it a stuck `testhost` never exits and the agent session has to be
unblocked by hand. When it fires, the output names the test that hung and a dump is written under
`tests/SingletonDI.Tests/TestResults`. Raise `--blame-hang-timeout` only after checking how long a legitimate cold
run takes; three target frameworks are tested per invocation.

The test suite takes several minutes because it packs the runtime and builds it against SDK 8, 9, and 10. Give the
command a tool timeout well above that instead of raising the blame timeout.

## Important characteristics

- The source generator runs at compile time; generated source requires C# 9 or later, and file-scoped consumer declarations require C# 10 or later.
- The package targets the Roslyn 4.8 API baseline and is verified with SDK 8, 9, and 10.
- Runtime APIs and generated provider modules use the `SingletonDI.Generated` namespace.
- A cross-project executable opts in with `SingletonDICompositionRoot=true` and exposes the property through `CompilerVisibleProperty`.
- Consumers are partial top-level or nested classes, structs, records, or record structs. The consume attribute is inherited, so derived types receive the same dependencies.
- Use the existing xUnit and Roslyn harnesses. Do not introduce a second generator-test contract.

## Code generation

- Provider assemblies generate a public static module with an idempotent `Bootstrap()` method.
- A composition root validates the complete graph and calls each referenced provider module's `Bootstrap()` method before initialization.
- `SingletonDIInitializer.InitializeAsync(bool registerShutdownHandlers = true)` creates providers in dependency order and runs initializers level by level.
- `SingletonDIInitializer.DisposeAsync()` must be awaited when application code requires guaranteed asynchronous cleanup.
- Signal handlers cancel default termination, await one disposal operation, and then use exit codes 130 for SIGINT, 143 for SIGTERM, and 131 for SIGQUIT.

## Diagnostics and tests

- Use `GeneratorTestResult` for compilation and diagnostic assertions.
- Use `CodeFixTestHarness` and `RefactoringTestHarness` for real code-action and refactoring pipelines.
- Start child `dotnet` processes only through `DotnetProcessRunner` so a stuck build cannot hang the test run.
- Assert exact diagnostic IDs, severity, locations, messages, and generated compilation results.
- Keep package smoke tests isolated from the repository NuGet cache and verify the packed package on SDK 8, 9, and 10.
