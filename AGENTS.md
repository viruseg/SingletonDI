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
- **Test:** `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --no-restore --nologo --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 300s --blame-hang-dump-type mini`
- **Focused test:** `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~TestClass.TestMethod" --no-restore --nologo --logger "console;verbosity=normal" --blame-hang --blame-hang-timeout 300s --blame-hang-dump-type mini`
- **Inner loop:** add `--filter "Category!=Packaging"` to the test command.

`--blame-hang` is mandatory, not optional: without it a stuck `testhost` never exits and the agent session has to be
unblocked by hand. When it fires, the output names the test that hung and a dump is written under
`tests/SingletonDI.Tests/TestResults`. Raise `--blame-hang-timeout` only after checking how long a legitimate cold
run takes; one target framework is tested per invocation.

Use `verbosity=normal` for the documented runs. At `verbosity=minimal` the console logger prints nothing for the whole
execution window, so a run that spends 20-30 seconds in the packaging tests looks like a hang. The suite is not slow:
a warm full run reports about 25 seconds and exits, and the whole documented workflow from a cold tree takes under a
minute.

The two `Category=Packaging` classes dominate the runtime. `PackageSmokeTests` packs `SingletonDI.Attributes` in Release
and builds and runs a consumer app against the produced package, and `PackageContentTests` packs again to compare the
analyzer assembly in the package with the one MSBuild produced. Both are required before a release, so run them in the
full command and skip them with `Category!=Packaging` while iterating.

`PackageSmokeTests` serializes concurrent runs through `%TEMP%\SingletonDI.PackageSmokeTests.lock` and waits at most
60 seconds for it. A wait is reported through `ITestOutputHelper`, so it only appears at `verbosity=normal` or in the
run result. A testhost orphaned by a killed run keeps the lock held, and the next run then fails after that wait
instead of waiting silently for minutes; delete the lock file after confirming no test run is active.

## Important characteristics

- The source generator runs at compile time; generated source requires C# 9 or later, and file-scoped consumer declarations require C# 10 or later.
- The package targets the Roslyn 4.8 API baseline and is verified with SDK 10.
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
- `WellKnownFixAllProviders.BatchFixer` is internal Roslyn API and cannot be executed from a test; cover fix-all with `CodeFixTestHarness.GetFixAllTextChangesAsync` and assert that the text changes of one document do not overlap. Assert separately that every `CodeFixProvider` returns a non-null `GetFixAllProvider()`, since `null` silently disables fix-all in the IDE.
- Start child `dotnet` processes only through `DotnetProcessRunner` so a stuck build cannot hang the test run.
- Assert exact diagnostic IDs, severity, locations, messages, and generated compilation results.
- Keep package smoke tests isolated from the repository NuGet cache and verify the packed package on SDK 10.
