# SingletonDI Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Исправить все подтверждённые дефекты из design spec, сохранить совместимость API и оформить каждую атомарную проблему отдельным зелёным коммитом.

**Architecture:** Работа выполняется в текущей ветке `main` последовательно. Сначала усиливаются test/package harnesses, затем runtime lifecycle, затем generator models/validation/incremental pipeline и performance; docs и release metadata замыкают серию. Consumer generation переходит на immutable declaration snapshots, provider assemblies используют явный idempotent bootstrap, а metadata scanning выполняется один раз через reference-only projection.

**Tech Stack:** .NET 8/9/10 SDK, .NET Standard 2.0 runtime, C# 9 generated output, Roslyn 4.8 baseline, xUnit, `Microsoft.CodeAnalysis`/`CSharp.Workspaces`, MSBuild/NuGet packing.

**Spec:** `docs/superpowers/specs/2026-09-24-singletondi-hardening-design.md`

## Global Constraints

- Работать только в текущей ветке `main`; не создавать ветки, worktrees, PR или force-push.
- Каждый пункт атомарных коммитов из spec выполняется отдельным commit; regression test входит в commit своей проблемы.
- Не добавлять комментарии в production/test code без необходимости; следовать существующим C# conventions и strict typing.
- Сохранять namespace и сигнатуры `SingletonDIProvideAttribute`, `SingletonDIConsumeAttribute`, `SingletonDIProviderModuleAttribute`, `SingletonDIInitializer`.
- Generated source требует C# 9 или новее; file-scoped consumer declarations требуют C# 10; для более старой версии выдаётся diagnostic до `AddSource`.
- Runtime targets остаются `netstandard2.0;net8.0`; packed analyzer baseline — Roslyn 4.8, consumer smoke matrix — SDK 8/9/10.
- Версия package после release task — `1.1.0`; публикация NuGet не выполняется.
- После каждого commit: целевой тест, затем `git diff --check`; перед финальным завершением — полный build/test/pack/sample gate.
- Не смешивать несвязанный cleanup, не amend существующие commits и не оставлять tracked изменения вне текущей задачи.

## Review Focus

1. Первый external provider имеет static constructor с side effect/exception: root должен вызвать generated `Bootstrap()`, а не user `.cctor`.
2. Consumer объявлен как struct, record, nested, generic или file type: generated partial declaration и hint name должны компилироваться и не перезаписывать другой source.
3. Один same-level initializer бросает synchronously, второй ещё работает: disposal не должен начинаться до завершения обоих; Ctrl+C/SIGTERM должны завершить процесс после cleanup.
4. Packed package устанавливается из isolated feed на SDK 8, 9 и 10: analyzer загружается без `CS9057`, provider/consumer fixture компилируется и запускается.
5. Production code fix/refactoring, diagnostic, generated output и incremental mutation никогда не проходят только из-за test-local копии или substring assertion.

---

## File Map

### Существующие файлы, которые будут изменяться

- `src/SingletonDI.Generator/SingletonDIGenerator.cs` — incremental stages, composition-root validation, source output wiring.
- `src/SingletonDI.Generator/Models/ConsumerModel.cs` — consumer declaration snapshot.
- `src/SingletonDI.Generator/Validators/ConsumerValidator.cs` — consumer candidate creation, dependency validation, diagnostics.
- `src/SingletonDI.Generator/Validators/ProviderValidator.cs` — provider accessibility, initializer arity/accessibility, service validation.
- `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs` — partial declaration reconstruction, safe hint names, property emission.
- `src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs` — generated `Bootstrap()`, module initializer, registration source.
- `src/SingletonDI.Generator/Models/ProviderAssemblyModel.cs` — external bootstrap metadata.
- `src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs` — one-pass referenced provider/consumer collection.
- `src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs` — consumer dependency extraction без второго полного assembly scan.
- `src/SingletonDI.Generator/Helpers/TopologicalSorter.cs` — production cycle detector.
- `src/SingletonDI.Generator/DiagnosticDescriptors.cs` — new source-generation diagnostics.
- `src/SingletonDI.Generator/GeneratorOptions.cs` — effective language/composition options.- `src/SingletonDI.Attributes/Runtime/ServiceGraph.cs` — initializer task aggregation and cleanup ordering.
- `src/SingletonDI.Attributes/Runtime/ShutdownManager.cs` — termination delegate and signal lifecycle.
- `src/SingletonDI.Attributes/Runtime/SingletonDIInitializer.cs` — shutdown manager construction and public lifecycle docs.
- `src/SingletonDI.Refactoring/SingletonDIProvideRefactoringProvider.cs` — task-type detection and fully-qualified generated `Task`.
- `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj` — packable project, analyzer assets, dependencies, package metadata.
- `src/SingletonDI.Generator/SingletonDI.Generator.csproj` — Roslyn baseline.
- `src/SingletonDI.Refactoring/SingletonDI.Refactoring.csproj` — Workspaces baseline.
- `SingletonDI.slnx` — packability/build matrix, if project-level defaults require solution changes.
- `tests/SingletonDI.Tests/SingletonDI.Tests.csproj` — test-only Workspaces/SDK dependencies and package-smoke settings.
- `tests/SingletonDI.Tests/CodeFixProviderTests.cs` — production CodeAction integration tests.
- `tests/SingletonDI.Tests/PropertyNameResolverTests.cs` — true inheritance and declaration-shape tests.
- `tests/SingletonDI.Tests/DiagnosticErrorTests.cs` — exact diagnostics and output compilation assertions.
- `tests/SingletonDI.Tests/GeneratorOutputTests.cs` — semantic generated-source tests and incremental matrix.
- `tests/SingletonDI.Tests/GeneratorCompositionTests.cs` — referenced-consumer and bootstrap behavior.
- `tests/SingletonDI.Tests/RuntimeRegistryTests.cs` — runtime lifecycle races and error paths.
- `tests/SingletonDI.Tests/SingletonDIInitializerTests.cs` — shutdown handler tests.
- `tests/SingletonDI.Tests/InterProjectScenarioTests.cs` — process-isolated runtime scenario.
- `README.md`, `AGENTS.md` — public behavior, requirements and repository guidance.

### Новые файлы

- `src/SingletonDI.Generator/Models/ConsumerDeclarationShape.cs` — typed consumer declaration shape.
- `src/SingletonDI.Generator/Models/ReferencedCompositionSnapshot.cs` — immutable external provider/consumer scan result.
- `src/SingletonDI.Generator/Helpers/ReferenceOnlyCompilationComparer.cs` — metadata-only compilation equivalence.
- `src/SingletonDI.Generator/Helpers/IReferencedCompositionCollector.cs` — testable one-pass metadata collector contract.
- `tests/SingletonDI.Tests/CodeFixTestHarness.cs` — real CodeFixContext/action application helper.
- `tests/SingletonDI.Tests/RefactoringTestHarness.cs` — real CodeRefactoringContext/action application helper.
- `tests/SingletonDI.Tests/ProcessScenarioRunner.cs` — process-isolated runtime scenario helper.
- `tests/SingletonDI.Tests/GeneratorTestResult.cs` — generator diagnostics plus output compilation contract.
- `tests/SingletonDI.Tests/DocumentationConsistencyTests.cs` — stale documentation guard.
- `tests/SingletonDI.Tests/GeneratorIncrementalTests.cs` — native incremental tracking matrix.
- `tests/SingletonDI.Tests/PackageProjectSettingsTests.cs` — MSBuild packability assertions.
- `tests/SingletonDI.Tests/PackageContentTests.cs` — analyzer asset/hash assertions.
- `tests/SingletonDI.Tests/PackageMetadataTests.cs` — package version/dependency assertions.
- `tests/SingletonDI.Tests/TopologicalSorterTests.cs` — linear cycle detection tests.
- `tests/SingletonDI.Tests/PackageSmoke/PackageSmokeTests.cs` — isolated package restore/build/run test.
- `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/ConsumerApp.csproj` — isolated package consumer project.
- `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/Program.cs` — package consumer scenario.

---

### Task 1: Заменить fake code-fix harness на production CodeAction pipeline

**Files:**
- Modify: `tests/SingletonDI.Tests/CodeFixProviderTests.cs:805-1091`
- Create: `tests/SingletonDI.Tests/CodeFixTestHarness.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj:14-23`

**Interfaces:**
- Consumes: existing `SingletonDIProviderCodeFixProvider`, `SingletonDIConsumerCodeFixProvider`, `SingletonDIPartialCodeFixProvider`.
- Produces: `CodeFixTestHarness.ApplyAsync(Document, Diagnostic, CodeFixProvider)` returning a changed `Document` and registered action metadata.

- [ ] **Step 1: Add a failing integration test**

Add a test that creates an `AdhocWorkspace`, runs the generator to obtain the real diagnostic, creates a `CodeFixContext`, calls `RegisterCodeFixesAsync`, asserts one registered action, applies its operations, and compiles the resulting document. The test must assert the action title and that the target diagnostic is absent after the fix.

- [ ] **Step 2: Run the focused test and verify the old harness cannot satisfy it**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~CodeFixProviderTests" --no-restore`

Expected before harness replacement: failure because no real `CodeAction` is registered through the test path or the result is not compiled.

- [ ] **Step 3: Implement the harness**

Use `AdhocWorkspace`, `ProjectInfo`, `DocumentId`, `CodeFixContext`, `CodeAction.GetOperationsAsync`, and `SolutionChanges`. Do not duplicate production syntax transformation logic in tests. Preserve the exact source tree and compilation references returned by the workspace.

- [ ] **Step 4: Convert all existing code-fix cases to the helper**

Replace `ApplyDM0004Fix`, `ApplyDM0005Fix`, `ApplyDM0012Fix`, `ApplyConsumerFix`, and `ApplyDM0007Fix` calls with production action application. Keep assertions for formatted document text, but compile the changed document and check diagnostics.

- [ ] **Step 5: Run focused and full tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~CodeFixProviderTests" --no-restore`

Expected: all code-fix tests pass through the real provider.

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --no-restore`

Expected: no regressions.

- [ ] **Step 6: Commit**

```bash
git add tests/SingletonDI.Tests/CodeFixProviderTests.cs tests/SingletonDI.Tests/CodeFixTestHarness.cs tests/SingletonDI.Tests/SingletonDI.Tests.csproj
git commit -m "test: execute production code fixes"
```

### Task 2: Add real refactoring test harness

**Files:**
- Create: `tests/SingletonDI.Tests/RefactoringTestHarness.cs`
- Create: `tests/SingletonDI.Tests/RefactoringProviderTests.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj`

**Interfaces:**
- Consumes: `SingletonDIProvideRefactoringProvider.ComputeRefactoringsAsync`.
- Produces: `RefactoringTestHarness.GetActionsAsync(Document, TextSpan)` returning registered `CodeAction` objects.

- [ ] **Step 1: Write failing provider-refactoring tests**

Cover cursor on the provider class name, cursor outside the name, missing `[SingletonDIProvide]`, existing `Task InitializeAsync()`, existing `ValueTask InitializeAsync()`, overload with parameters, and cancellation.

- [ ] **Step 2: Run focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RefactoringProviderTests" --no-restore`

Expected: failures until the workspace-based harness invokes the production provider.

- [ ] **Step 3: Implement the workspace helper**

Build a compilation with the attributes and Roslyn references, create a `CodeRefactoringContext` with the cursor span, call `ComputeRefactoringsAsync`, and apply the returned action. Assert the resulting document compiles.

- [ ] **Step 4: Add the positive action test**

Assert that a provider without an initializer receives a `Task InitializeAsync()` method through the actual action and that the resulting document has no compiler errors.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RefactoringProviderTests" --no-restore`

Expected: all harness tests pass.

```bash
git add tests/SingletonDI.Tests/RefactoringTestHarness.cs tests/SingletonDI.Tests/RefactoringProviderTests.cs tests/SingletonDI.Tests/SingletonDI.Tests.csproj
git commit -m "test: cover production refactorings"
```

### Task 3: Fix refactoring task-type and using generation

**Files:**
- Modify: `src/SingletonDI.Refactoring/SingletonDIProvideRefactoringProvider.cs:58-111`
- Test: `tests/SingletonDI.Tests/RefactoringProviderTests.cs`

**Interfaces:**
- Consumes: `INamedTypeSymbol` for no-argument `InitializeAsync` methods.
- Produces: no action for any supported existing `Task`/`ValueTask` initializer and generated `global::System.Threading.Tasks.Task` syntax.

- [ ] **Step 1: Add failing regression tests**

Add a provider containing `public ValueTask InitializeAsync() => ValueTask.CompletedTask;` and assert no duplicate refactoring is offered. Add a file without `using System.Threading.Tasks` and assert the applied method compiles.

- [ ] **Step 2: Run the focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RefactoringProviderTests" --no-restore`

Expected: `ValueTask` and missing-using cases fail.

- [ ] **Step 3: Update `IsTaskType`**

Recognize both `System.Threading.Tasks.Task` and `System.Threading.Tasks.ValueTask` using `SpecialType`/`OriginalDefinition` metadata rather than display-string-only comparison. Keep parameterless matching.

- [ ] **Step 4: Generate fully-qualified task syntax**

Use `global::System.Threading.Tasks.Task` and `global::System.Threading.Tasks.Task.CompletedTask` in the generated method and return statement. Preserve `Formatter.Annotation` and cancellation.

- [ ] **Step 5: Verify and commit**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RefactoringProviderTests" --no-restore`

Expected: all tests pass.

```bash
git add src/SingletonDI.Refactoring/SingletonDIProvideRefactoringProvider.cs tests/SingletonDI.Tests/RefactoringProviderTests.cs
git commit -m "fix: handle refactoring task types and usings"
```

### Task 4: Make diagnostics and generated-output tests semantic and exact

**Files:**
- Modify: `tests/SingletonDI.Tests/DiagnosticErrorTests.cs:1187-1261`
- Modify: `tests/SingletonDI.Tests/GeneratorOutputTests.cs:38-405`
- Create: `tests/SingletonDI.Tests/GeneratorTestResult.cs`
- Test fixtures: existing generator source strings and output scenarios.

**Interfaces:**
- Produces: `GeneratorTestResult` with `Diagnostics`, `OutputCompilation`, `GeneratedSources`, and `OutputKind`.

- [ ] **Step 1: Add failing exact-diagnostic assertions**

For DM0004, DM0008, DM0013, DM0014 and DM0016 assert exact severity, category, ID, message, location span, and diagnostic count. Add a DM0008 self-reference end-to-end test that also compiles the generated output.

- [ ] **Step 2: Run the focused diagnostics tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests" --no-restore`

Expected: current substring-only harness fails the new assertions.

- [ ] **Step 3: Return output compilation from the harness**

Change `RunGenerator` to return `GeneratorTestResult`, call `RunGeneratorsAndUpdateCompilation`, retain `outputCompilation`, and expose exact `Diagnostic` objects. Do not discard compiler diagnostics after generator diagnostics.

- [ ] **Step 4: Replace substring-only output assertions**

Parse generated syntax, locate registration/resolution invocations, assert generic arguments and positional arguments through a semantic model, then call `OutputCompilation.GetDiagnostics()` and assert no unexpected compiler errors.

- [ ] **Step 5: Run focused and full tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests|FullyQualifiedName~GeneratorOutputTests" --no-restore`

Expected: all tests pass with exact output assertions.

- [ ] **Step 6: Commit**

```bash
git add tests/SingletonDI.Tests/DiagnosticErrorTests.cs tests/SingletonDI.Tests/GeneratorOutputTests.cs tests/SingletonDI.Tests/GeneratorTestResult.cs
git commit -m "test: assert diagnostics and generated output"
```

### Task 5: Add native incremental invalidation tests

**Files:**
- Modify: `tests/SingletonDI.Tests/GeneratorOutputTests.cs:218-291`
- Create: `tests/SingletonDI.Tests/GeneratorIncrementalTests.cs`

**Interfaces:**
- Consumes: `IIncrementalGenerator.SingletonDIGenerator`.
- Produces: tracked-step assertions for `ProviderModuleOutput`, `CompositionRootOutput`, and `ConsumerOutput`.

- [ ] **Step 1: Add failing matrix tests**

Create a native `CSharpGeneratorDriver` with `trackIncrementalGeneratorSteps: true` and separate compilations for no-op, unrelated method-body edit, provider add/remove/change, dependency change, service-type change, composition-root option change, and metadata-reference replacement.

- [ ] **Step 2: Run the new matrix**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests" --no-restore`

Expected: current generator fails at least the `ConsumerOutput` cache assertion and the option/reference invalidation assertions.

- [ ] **Step 3: Assert the exact tracked stage**

For each case assert the expected `IncrementalStepRunReason` and source-output equivalence. Do not accept any arbitrary cached step in the result.

- [ ] **Step 4: Verify and commit**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests" --no-restore`

Expected: all matrix cases pass.

```bash
git add tests/SingletonDI.Tests/GeneratorOutputTests.cs tests/SingletonDI.Tests/GeneratorIncrementalTests.cs
git commit -m "test: cover native incremental invalidation"
```

### Task 6: Isolate process-wide inter-project runtime scenarios

**Files:**
- Modify: `tests/SingletonDI.Tests/InterProjectScenarioTests.cs`
- Modify: `tests/InterProjectFixtures/RootApp/Program.cs:39-54`
- Create: `tests/SingletonDI.Tests/ProcessScenarioRunner.cs`

**Interfaces:**
- Produces: a fresh process/load-context per scenario invocation, with no static result memoization.

- [ ] **Step 1: Add a failing repeatability test**

Invoke the inter-project scenario twice and assert both runs perform initialization and disposal, create fresh instances, and return independent results.

- [ ] **Step 2: Run the focused test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~InterProjectScenarioTests" --no-restore`

Expected: current memoized/static scenario fails the second-run assertions.

- [ ] **Step 3: Implement isolated execution**

Launch the fixture in a separate process with a scenario argument, remove the process-global memoization, and serialize only the outer test collection. Assert process exit code and serialized output.

- [ ] **Step 4: Verify and commit**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~InterProjectScenarioTests" --no-restore`

Expected: repeated scenario test passes without registry leakage.

```bash
git add tests/SingletonDI.Tests/InterProjectScenarioTests.cs tests/InterProjectFixtures/RootApp/Program.cs tests/SingletonDI.Tests/ProcessScenarioRunner.cs
git commit -m "test: isolate process-wide runtime scenarios"
```

### Task 7: Isolate the packable runtime package

**Files:**
- Modify: `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj:10-12`
- Modify: `src/SingletonDI.Generator/SingletonDI.Generator.csproj`
- Modify: `src/SingletonDI.Refactoring/SingletonDI.Refactoring.csproj`
- Modify: `src/SingletonDI.SampleApp/SingletonDI.SampleApp.csproj`
- Modify: all `tests/InterProjectFixtures/*/*.csproj`
- Test: `tests/SingletonDI.Tests/PackageProjectSettingsTests.cs`

**Interfaces:**
- Produces: only `SingletonDI.Attributes` with `IsPackable=true`; all other projects explicitly `IsPackable=false`.

- [ ] **Step 1: Add failing MSBuild property tests**

Evaluate `IsPackable`, `PackageId`, and `GeneratePackageOnBuild` for every solution project and assert the exact allowed set.

- [ ] **Step 2: Run the property tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PackageProjectSettingsTests" --no-restore`

Expected: generator/refactoring/sample/fixtures are currently packable and runtime generates a package on build.

- [ ] **Step 3: Set explicit project properties**

Remove `GeneratePackageOnBuild` from the runtime project, set `IsPackable=false` on every non-runtime project, and retain `IsPackable=false` on tests.

- [ ] **Step 4: Verify pack output**

Run: `dotnet pack SingletonDI.slnx -c Release --no-restore -o C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-pack-clean`

Expected: exactly one `SingletonDI.*.nupkg` is produced.

- [ ] **Step 5: Commit**

```bash
git add src/SingletonDI.Attributes/SingletonDI.Attributes.csproj src/SingletonDI.Generator/SingletonDI.Generator.csproj src/SingletonDI.Refactoring/SingletonDI.Refactoring.csproj src/SingletonDI.SampleApp/SingletonDI.SampleApp.csproj tests/InterProjectFixtures tests/SingletonDI.Tests/PackageProjectSettingsTests.cs
git commit -m "build: isolate the packable runtime package"
```

### Task 8: Pack analyzer assemblies from actual target paths

**Files:**
- Modify: `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj:49-65`
- Test: `tests/SingletonDI.Tests/PackageContentTests.cs`

**Interfaces:**
- Produces: package items sourced from `TargetPath` of generator/refactoring ProjectReferences, independent of `bin/$(Configuration)`.

- [ ] **Step 1: Add a failing clean artifacts-path test**

Run a clean package build with `--artifacts-path` after removing default analyzer outputs, then inspect the nupkg for both analyzer assemblies and compare SHA-256 with the actual target DLLs.

- [ ] **Step 2: Run the package-content test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PackageContentTests" --no-restore`

Expected: current hardcoded `None Include` paths fail or select stale files.

- [ ] **Step 3: Implement target-path pack targets**

Use `TargetsForTfmSpecificBuildOutput` or an equivalent `BuildOutputInPackage` target. Add the generator/refactoring outputs to `analyzers/dotnet/cs` from `TargetPath`, with `Visible=false` and `Pack=true`.

- [ ] **Step 4: Verify both normal and custom output paths**

Run: `dotnet pack src/SingletonDI.Attributes/SingletonDI.Attributes.csproj -c Release --no-restore -o C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-pack-targets`

Run: `dotnet pack src/SingletonDI.Attributes/SingletonDI.Attributes.csproj -c Release --artifacts-path C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-artifacts -o C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-pack-artifacts`

Expected: both packages contain matching current analyzer/refactoring DLL hashes.

- [ ] **Step 5: Commit**

```bash
git add src/SingletonDI.Attributes/SingletonDI.Attributes.csproj tests/SingletonDI.Tests/PackageContentTests.cs
git commit -m "build: pack analyzers from target paths"
```

### Task 9: Lower the Roslyn baseline and add SDK compatibility smoke

**Files:**
- Modify: `src/SingletonDI.Generator/SingletonDI.Generator.csproj:13-16`
- Modify: `src/SingletonDI.Refactoring/SingletonDI.Refactoring.csproj:18-20`
- Modify: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/ConsumerApp.csproj`
- Create: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/Program.cs`
- Create: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/NuGet.Config`
- Create: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/Sdk8/global.json`
- Create: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/Sdk9/global.json`
- Create: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/Sdk10/global.json`

**Interfaces:**
- Consumes: local `SingletonDI.1.0.3.nupkg` produced by the current package metadata; Task 10 changes the version to `1.1.0` after this compatibility baseline is green.
- Produces: package consumer that restores only from an isolated feed and uses generated provider/consumer properties.

- [ ] **Step 1: Add the failing SDK 8/9 package smoke**

Create a package consumer with `WarningsAsErrors=CS9057`, `PackageReference Include="SingletonDI" Version="1.0.3"`, a provider, a consumer, and `Main` that calls `InitializeAsync`, resolves the service, and disposes it. Add SDK-specific `global.json` files for 8.0.131, 9.0.121 and 10.0.401. Add `<Compile Remove="PackageSmoke/**" />` to the main test project so the nested fixture is not compiled into the test assembly.

- [ ] **Step 2: Run the smoke matrix before downgrading**

Run from `Sdk8`, `Sdk9` and `Sdk10` respectively: `dotnet build ../ConsumerApp.csproj --force --nologo`

Expected: SDK 8/9 reproduce `CS9057` with the current Roslyn 5.0 analyzer.

- [ ] **Step 3: Change Roslyn package versions to 4.8.0**

Use the lowest API baseline that compiles all generator/refactoring source. Keep `Microsoft.CodeAnalysis.Analyzers` private and keep Workspaces private to the refactoring project where required.

- [ ] **Step 4: Run the isolated SDK matrix**

Run SDK 8, 9 and 10 builds with an isolated `NUGET_PACKAGES` directory. Expected: all three compile the packed consumer with no `CS9057`.

- [ ] **Step 5: Commit**

```bash
git add src/SingletonDI.Generator/SingletonDI.Generator.csproj src/SingletonDI.Refactoring/SingletonDI.Refactoring.csproj tests/SingletonDI.Tests/SingletonDI.Tests.csproj tests/SingletonDI.Tests/PackageSmoke
git commit -m "build: support Roslyn 4.8 consumers"
```

### Task 10: Prepare release metadata for 1.1.0

**Files:**
- Modify: `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj:13-29,31-38`
- Modify: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/ConsumerApp.csproj` — update the package reference from `1.0.3` to `1.1.0`.
- Modify: `README.md:333-390`
- Test: `tests/SingletonDI.Tests/PackageMetadataTests.cs`

**Interfaces:**
- Produces: package version `1.1.0`, assembly version `1.1.0`, release notes for the hardening series, symbols package configuration, and no unnecessary net8 compatibility dependencies.

- [ ] **Step 1: Add failing metadata assertions**

Assert `Version`, `AssemblyVersion`, release notes entry `1.1.0`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, and no `System.Threading.Tasks.Extensions`/`Microsoft.Bcl.AsyncInterfaces` dependency for `net8.0`.

- [ ] **Step 2: Update package metadata**

Set version properties, add release notes, configure symbols package, and condition compatibility package references to `netstandard2.0` only.

- [ ] **Step 3: Verify pack contents and metadata**

Run: `dotnet pack src/SingletonDI.Attributes/SingletonDI.Attributes.csproj -c Release --no-restore -o C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-pack-1.1.0`

Expected: `SingletonDI.1.1.0.nupkg` and `.snupkg` contain the expected runtime/analyzer assets and no net8-only compatibility dependency.

- [ ] **Step 4: Commit**

```bash
git add src/SingletonDI.Attributes/SingletonDI.Attributes.csproj tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/ConsumerApp.csproj README.md tests/SingletonDI.Tests/PackageMetadataTests.cs
git commit -m "release: prepare SingletonDI 1.1.0"
```

### Task 11: Add the package smoke test to the regular test suite

**Files:**
- Modify: `tests/SingletonDI.Tests/PackageSmoke/PackageSmokeTests.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj`
- Test assets: `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp/*`

**Interfaces:**
- Consumes: package produced by the release task.
- Produces: repeatable smoke test that packs, restores from an isolated feed and builds/runs the consumer.

- [ ] **Step 1: Add the failing smoke test**

Use a unique temp output directory, pack the runtime project, set `NUGET_PACKAGES` to a unique directory, and run SDK 8/9/10 consumers against `SingletonDI.1.1.0.nupkg`. Assert exit code zero and generated property access at runtime.

- [ ] **Step 2: Run the smoke test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PackageSmokeTests" --no-restore`

Expected: the test fails until cleanup, path and SDK selection are deterministic.

- [ ] **Step 3: Make cleanup and path handling deterministic**

Use `ProcessStartInfo.Environment["NUGET_PACKAGES"]` for each child process rather than mutating the test process environment. Use `try/finally` to remove only test-owned temp directories and do not depend on a globally cached package.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PackageSmokeTests" --no-restore
git add tests/SingletonDI.Tests/PackageSmoke tests/SingletonDI.Tests/SingletonDI.Tests.csproj
git commit -m "test: verify packed package consumption"
```

### Task 12: Wait for started initializers before cleanup

**Files:**
- Modify: `src/SingletonDI.Attributes/Runtime/ServiceGraph.cs:33-73`
- Test: `tests/SingletonDI.Tests/RuntimeRegistryTests.cs`

**Interfaces:**
- Consumes: `ProviderRegistration.InitializeAsync` delegates.
- Produces: unchanged `ServiceGraph.InitializeAsync()` contract with all started tasks awaited before `DisposeAsync`.

- [ ] **Step 1: Add the failing gate test**

Register two same-level providers. The first initializer returns a gate-backed `Task`; the second throws synchronously. Assert disposal is not called before the gate is released and the original exception is preserved.

- [ ] **Step 2: Run the focused test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RuntimeRegistryTests" --no-restore`

Expected: current code disposes the first provider before its initializer completes.

- [ ] **Step 3: Aggregate synchronous exceptions as tasks**

Wrap each delegate invocation in `try/catch`, add either the returned task or a faulted task to the level list, and await `Task.WhenAll` before leaving the level. Preserve the first initialization exception if cleanup also fails.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~RuntimeRegistryTests" --no-restore
git add src/SingletonDI.Attributes/Runtime/ServiceGraph.cs tests/SingletonDI.Tests/RuntimeRegistryTests.cs
git commit -m "fix: await started initializers before cleanup"
```

### Task 13: Complete graceful signal shutdown

**Files:**
- Modify: `src/SingletonDI.Attributes/Runtime/ShutdownManager.cs:9-138`
- Modify: `src/SingletonDI.Attributes/Runtime/SingletonDIInitializer.cs:216-220`
- Test: `tests/SingletonDI.Tests/SingletonDIInitializerTests.cs`

**Interfaces:**
- Produces: internal constructor `ShutdownManager(Func<ValueTask> dispose, Action<int> terminateProcess)`, internal signal callback methods for deterministic tests, and one idempotent signal-disposal operation.

- [ ] **Step 1: Add failing signal tests**

Use the internal constructor and a fake termination delegate. Invoke cancel/Posix callbacks, assert `Cancel=true`, assert disposal completes once, and assert exit code 130/143/131. Assert a second signal does not start a second disposal.

- [ ] **Step 2: Run the focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~SingletonDIInitializerTests" --no-restore`

Expected: current callbacks cancel termination but never call the terminator.

- [ ] **Step 3: Implement idempotent disposal-and-termination**

Store one `Task` under the manager lock. Signal handlers cancel default handling, start the task, await disposal with exception suppression, then call the termination delegate exactly once. Keep `ProcessExit` as best-effort disposal without recursive termination.

- [ ] **Step 4: Update initializer construction**

Pass `Environment.Exit` as the production terminator when creating `ShutdownManager`.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~SingletonDIInitializerTests" --no-restore
git add src/SingletonDI.Attributes/Runtime/ShutdownManager.cs src/SingletonDI.Attributes/Runtime/SingletonDIInitializer.cs tests/SingletonDI.Tests/SingletonDIInitializerTests.cs
git commit -m "fix: complete graceful signal shutdown"
```

### Task 14: Bootstrap external provider modules explicitly

**Files:**
- Modify: `src/SingletonDI.Generator/Models/ProviderAssemblyModel.cs`
- Modify: `src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs:106-135`
- Modify: `src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs:49-90`
- Test: `tests/SingletonDI.Tests/GeneratorCompositionTests.cs`, `tests/SingletonDI.Tests/GeneratorOutputTests.cs`

**Interfaces:**
- Produces: `ProviderAssemblyModel` with generated bootstrap type identity and `HasBootstrapMethod`; `ProviderModuleEmitter` calls `global::SingletonDI.Generated.__SingletonDIProviderModule__.Bootstrap()`. Use diagnostic `DM0021` for a marked assembly without the generated bootstrap method.

- [ ] **Step 1: Add a failing cctor regression test**

Create an external provider assembly whose alphabetically first provider has a static constructor that throws. Build a composition root referencing it and assert the current generated root fails before `Main`.

- [ ] **Step 2: Run the focused composition test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorCompositionTests" --no-restore`

Expected: current bootstrap uses the user provider and reproduces the failure.

- [ ] **Step 3: Generate an idempotent public hidden module type**

Emit XML documentation for the generated public type and method, then emit `[EditorBrowsable(EditorBrowsableState.Never)] public static class __SingletonDIProviderModule__` with a static `Bootstrap()` method guarded by an interlocked flag. The module initializer calls `Bootstrap()`.

- [ ] **Step 4: Change external bootstrap metadata and emission**

Store the generated module identity in `ProviderAssemblyModel`, verify the method exists in referenced metadata, and emit a direct `Bootstrap()` call for each marked external assembly. Emit a diagnostic for marker assemblies without the method.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorCompositionTests|FullyQualifiedName~GeneratorOutputTests" --no-restore
git add src/SingletonDI.Generator/Models/ProviderAssemblyModel.cs src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs tests/SingletonDI.Tests/GeneratorCompositionTests.cs tests/SingletonDI.Tests/GeneratorOutputTests.cs
git commit -m "fix: bootstrap provider modules explicitly"
```

### Task 15: Model and emit complete consumer declarations

**Files:**
- Create: `src/SingletonDI.Generator/Models/ConsumerDeclarationShape.cs`
- Modify: `src/SingletonDI.Generator/Models/ConsumerModel.cs`
- Modify: `src/SingletonDI.Generator/Validators/ConsumerValidator.cs:43-153`
- Modify: `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs:18-202`
- Test: `tests/SingletonDI.Tests/PropertyNameResolverTests.cs`, `tests/SingletonDI.Tests/GeneratorOutputTests.cs`

**Interfaces:**
- Produces: `ConsumerDeclarationShape` with `DeclarationKind` (`Class`, `Struct`, `RecordClass`, `RecordStruct`), containing names/arity, escaped names, type parameters, namespace and `IsFileScoped`; `ConsumerModel.Shape` uses this contract. Use diagnostic `DM0026` when a nested containing type is not partial.

- [ ] **Step 1: Add failing compile-and-run cases**

Add struct, record, record struct, nested, generic, file-scoped and escaped-name consumers. Each source must compile, expose the expected generated property, initialize, and resolve the service.

- [ ] **Step 2: Run the focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PropertyNameResolverTests|FullyQualifiedName~GeneratorOutputTests" --no-restore`

Expected: current output produces `CS0261`, invalid hint names, missing nested properties or unresolved names.

- [ ] **Step 3: Add the immutable shape model**

Store containing type names, type parameter lists, keyword kind, file scope and escaped identifiers as explicit strings/records. Do not store `TypeDeclarationSyntax` in the final model.

- [ ] **Step 4: Reconstruct declarations in the emitter**

Generate the correct partial keyword, containing nesting, generic parameter list, file qualifier and escaped names. Reject only nested consumers whose containing declarations are not partial, with a new diagnostic.

- [ ] **Step 5: Generate safe hint names**

Sanitize the identity and append a deterministic hash when the sanitized form is not unique. Assert all hint names contain only valid Roslyn characters.

- [ ] **Step 6: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PropertyNameResolverTests|FullyQualifiedName~GeneratorOutputTests" --no-restore
git add src/SingletonDI.Generator/Models/ConsumerDeclarationShape.cs src/SingletonDI.Generator/Models/ConsumerModel.cs src/SingletonDI.Generator/Validators/ConsumerValidator.cs src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs tests/SingletonDI.Tests/PropertyNameResolverTests.cs tests/SingletonDI.Tests/GeneratorOutputTests.cs
git commit -m "fix: model complete consumer declarations"
```

### Task 16: Make inherited consumer properties usable

**Files:**
- Modify: `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs:110-122`
- Modify: `src/SingletonDI.Attributes/SingletonDIConsumeAttribute.cs:16-20` only if XML contract wording needs clarification.
- Test: `tests/SingletonDI.Tests/PropertyNameResolverTests.cs:808-853`

**Interfaces:**
- Produces: private properties for sealed/struct consumers and protected properties for unsealed class consumers.

- [ ] **Step 1: Fix and extend the inheritance test**

Remove the repeated `[SingletonDIConsume]` attribute from the derived class, keep a protected/base property access, and assert the generated compilation is error-free. Add a sealed consumer test to ensure it remains private and warning-free.

- [ ] **Step 2: Run the focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PropertyNameResolverTests" --no-restore`

Expected: derived access fails with current private generated property.

- [ ] **Step 3: Select property accessibility from the declaration shape**

Use `protected` only for unsealed classes and `private` for sealed classes/structs. Do not generate protected members for record structs or sealed records.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~PropertyNameResolverTests" --no-restore
git add src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs src/SingletonDI.Attributes/SingletonDIConsumeAttribute.cs tests/SingletonDI.Tests/PropertyNameResolverTests.cs
git commit -m "fix: inherit consumer dependencies"
```

### Task 17: Validate provider and service accessibility

**Files:**
- Modify: `src/SingletonDI.Generator/Validators/ProviderValidator.cs:149-171`
- Modify: `src/SingletonDI.Generator/DiagnosticDescriptors.cs`
- Test: `tests/SingletonDI.Tests/DiagnosticErrorTests.cs`, `tests/SingletonDI.Tests/GeneratorCompositionTests.cs`

**Interfaces:**
- Produces: diagnostic `DM0022` for implementation/service types that cannot be named from the generated module namespace.

- [ ] **Step 1: Add failing accessibility cases**

Compile a provider with private nested `ServiceType`/implementation, a file-local provider, and a public provider with inaccessible containing types. Assert the generator diagnostic location and absence of `CS0122` in generated source.

- [ ] **Step 2: Run focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests|FullyQualifiedName~GeneratorCompositionTests" --no-restore`

Expected: current validator accepts the symbols and generated compilation reports `CS0122`.

- [ ] **Step 3: Implement generated-context accessibility checks**

Walk implementation/service containing types, reject file/private/protected-only accessibility, and report the source location before `ProviderModel` creation. Preserve public/internal cross-assembly rules.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests|FullyQualifiedName~GeneratorCompositionTests" --no-restore
git add src/SingletonDI.Generator/Validators/ProviderValidator.cs src/SingletonDI.Generator/DiagnosticDescriptors.cs tests/SingletonDI.Tests/DiagnosticErrorTests.cs tests/SingletonDI.Tests/GeneratorCompositionTests.cs
git commit -m "fix: validate provider accessibility"
```

### Task 18: Reject unsupported initializer methods, contracts and member collisions

**Files:**
- Modify: `src/SingletonDI.Generator/Validators/ProviderValidator.cs:212-233`
- Modify: `src/SingletonDI.Generator/Validators/ConsumerValidator.cs:100-145`
- Modify: `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs:99-123`
- Modify: `src/SingletonDI.Generator/DiagnosticDescriptors.cs`
- Test: `tests/SingletonDI.Tests/DiagnosticErrorTests.cs`, `tests/SingletonDI.Tests/GeneratorOutputTests.cs`

**Interfaces:**
- Produces: diagnostics `DM0023` (generic `InitializeAsync`), `DM0024` (open generic dependency), and `DM0025` (existing member collision); no invalid generated source is added.

- [ ] **Step 1: Add failing cases**

Add `InitializeAsync<T>()`, `typeof(IBox<>)`, and a consumer that already declares the resolved property name. Assert exact diagnostic and no generated member with the invalid type/name.

- [ ] **Step 2: Run focused tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests|FullyQualifiedName~GeneratorOutputTests" --no-restore`

Expected: current code produces `CS0411`, `CS7003` or `CS0102` instead of generator diagnostics.

- [ ] **Step 3: Filter invalid symbols before model creation**

Require `method.Arity == 0`; reject `INamedTypeSymbol.IsUnboundGenericType`; and resolve final property names before adding source.

- [ ] **Step 4: Check existing members**

Call `typeSymbol.GetMembers(finalName)` including inherited members. Emit a diagnostic at the existing member location when a generated property would collide, and skip only that property.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DiagnosticErrorTests|FullyQualifiedName~GeneratorOutputTests" --no-restore
git add src/SingletonDI.Generator/Validators/ProviderValidator.cs src/SingletonDI.Generator/Validators/ConsumerValidator.cs src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs src/SingletonDI.Generator/DiagnosticDescriptors.cs tests/SingletonDI.Tests/DiagnosticErrorTests.cs tests/SingletonDI.Tests/GeneratorOutputTests.cs
git commit -m "fix: reject unsupported initializer and contracts"
```

### Task 19: Validate referenced consumers in non-root executables

**Files:**
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs:85-125,371-454`
- Modify: `src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs`
- Test: `tests/SingletonDI.Tests/GeneratorCompositionTests.cs`, `tests/SingletonDI.Tests/DiagnosticErrorTests.cs`

**Interfaces:**
- Produces: referenced consumer dependency diagnostics for executable compilations even when the local consumer collection is empty.

- [ ] **Step 1: Add the failing three-assembly scenario**

Build a contracts assembly, a consumer library with one satisfied and one missing dependency, and an executable without local consumers or composition root. Assert no error when satisfied and a precise missing-provider diagnostic when missing.

- [ ] **Step 2: Run composition tests**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorCompositionTests" --no-restore`

Expected: current non-root executable compiles the missing referenced consumer without the root diagnostic.

- [ ] **Step 3: Run referenced validation for executable output kinds**

When `OutputKind` is an application and root is false, collect referenced consumer dependencies, compare them with reachable provider identities, and report missing dependencies before runtime. Keep library output kinds exempt.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorCompositionTests|FullyQualifiedName~DiagnosticErrorTests" --no-restore
git add src/SingletonDI.Generator/SingletonDIGenerator.cs src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs tests/SingletonDI.Tests/GeneratorCompositionTests.cs tests/SingletonDI.Tests/DiagnosticErrorTests.cs
git commit -m "fix: validate referenced consumers without root"
```

### Task 20: Diagnose unsupported generated language versions

**Files:**
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs:18-30,371-454`
- Modify: `src/SingletonDI.Generator/DiagnosticDescriptors.cs`
- Test: `tests/SingletonDI.Tests/GeneratorOutputTests.cs`, `tests/SingletonDI.Tests/DiagnosticErrorTests.cs`

**Interfaces:**
- Produces: diagnostics `DM0027` when effective language version is below C# 9 and `DM0028` when a file-scoped consumer is compiled below C# 10, before `AddSource`.

- [ ] **Step 1: Add a failing C# 7.3 test**

Generate a provider/consumer with `LanguageVersion.CSharp7_3`; assert the new diagnostic and absence of generated source rather than raw `CS8370`/`CS8630` errors. Add a file-scoped consumer with `LanguageVersion.CSharp9` and assert the C# 10 feature diagnostic.

- [ ] **Step 2: Run the focused test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorOutputTests|FullyQualifiedName~DiagnosticErrorTests" --no-restore`

Expected: current generator emits source and compiler errors.

- [ ] **Step 3: Add the language check**

Read parse options from the target syntax tree, map `Default` to the effective compiler version, compare with `LanguageVersion.CSharp9`, report once per compilation, and return before module/consumer source emission.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorOutputTests|FullyQualifiedName~DiagnosticErrorTests" --no-restore
git add src/SingletonDI.Generator/SingletonDIGenerator.cs src/SingletonDI.Generator/DiagnosticDescriptors.cs tests/SingletonDI.Tests/GeneratorOutputTests.cs tests/SingletonDI.Tests/DiagnosticErrorTests.cs
git commit -m "fix: diagnose unsupported generated language"
```

### Task 21: Make consumer generation independent of source compilation

**Files:**
- Create: `src/SingletonDI.Generator/Models/ConsumerCandidate.cs`
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs:55-125,371-454`
- Modify: `src/SingletonDI.Generator/Validators/ConsumerValidator.cs`
- Test: `tests/SingletonDI.Tests/GeneratorIncrementalTests.cs`

**Interfaces:**
- Produces: `ConsumerCandidate` with only immutable declaration/dependency data and diagnostics; final `ConsumerOutput` has no `CompilationProvider` dependency.

- [ ] **Step 1: Add the failing cached-stage test**

Change only an unrelated method body in a consumer compilation and assert `ConsumerOutput` is `Cached` while provider output remains semantically correct.

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests" --no-restore`

Expected: current `CompilationProvider` combination invalidates consumer output.

- [ ] **Step 3: Move validation into attribute transform**

Create the candidate and diagnostics while the semantic model is available, then remove raw syntax, `Compilation`, and semantic model from the final consumer pipeline. Use model data for property generation and diagnostics.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests" --no-restore
git add src/SingletonDI.Generator/Models/ConsumerCandidate.cs src/SingletonDI.Generator/SingletonDIGenerator.cs src/SingletonDI.Generator/Validators/ConsumerValidator.cs tests/SingletonDI.Tests/GeneratorIncrementalTests.cs
git commit -m "perf: make consumer generation incremental"
```

### Task 22: Scan referenced metadata once through a reference-only snapshot

**Files:**
- Create: `src/SingletonDI.Generator/Models/ReferencedCompositionSnapshot.cs`
- Create: `src/SingletonDI.Generator/Helpers/ReferenceOnlyCompilationComparer.cs`
- Create: `src/SingletonDI.Generator/Helpers/IReferencedCompositionCollector.cs`
- Modify: `src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs`
- Modify: `src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs`
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs:85-103,175-369`
- Test: `tests/SingletonDI.Tests/GeneratorIncrementalTests.cs`, `tests/SingletonDI.Tests/GeneratorCompositionTests.cs`

**Interfaces:**
- Produces: `IReferencedCompositionCollector.Collect(Compilation, CancellationToken)` returning providers, consumer dependency sets, assembly markers and bootstrap identities in one pass.

- [ ] **Step 1: Add a failing one-pass/cache test**

Assert the snapshot object is reused when only a source method body changes and changes when metadata references/options change. Use a fake `IReferencedCompositionCollector` in the unit test to count calls; do not add a production-only instrumentation hook.

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests|FullyQualifiedName~GeneratorCompositionTests" --no-restore`

Expected: current two collectors are not represented by one cached snapshot.

- [ ] **Step 3: Implement reference-only comparison and combined collection**

Implement `IReferencedCompositionCollector` with the production collector, project a compilation to reference/options data, compare assembly identities/reference handles/options, enumerate each referenced assembly once, and extract both provider and consumer data into an immutable snapshot.

- [ ] **Step 4: Rewire composition outputs**

Use the snapshot in root and executable validation. Preserve cancellation checks and deterministic ordering by assembly identity/FQN.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~GeneratorIncrementalTests|FullyQualifiedName~GeneratorCompositionTests" --no-restore
git add src/SingletonDI.Generator/Models/ReferencedCompositionSnapshot.cs src/SingletonDI.Generator/Helpers/ReferenceOnlyCompilationComparer.cs src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs src/SingletonDI.Generator/SingletonDIGenerator.cs tests/SingletonDI.Tests/GeneratorIncrementalTests.cs tests/SingletonDI.Tests/GeneratorCompositionTests.cs
git commit -m "perf: scan referenced metadata once"
```

### Task 23: Replace production topological level scans with linear cycle detection

**Files:**
- Modify: `src/SingletonDI.Generator/Helpers/TopologicalSorter.cs:146-239`
- Test: `tests/SingletonDI.Tests/SingletonDIGeneratorTests.cs`, new `tests/SingletonDI.Tests/TopologicalSorterTests.cs`

**Interfaces:**
- Produces: `TryFindCycle(ImmutableArray<ProviderModel>, resolver)` returning deterministic cycle identities without constructing unused levels.

- [ ] **Step 1: Add a large-chain regression test**

Generate an acyclic chain of 1,000 providers and a cyclic tail. Assert cycle identities/order are deterministic and that the operation does not build one level list per provider.

- [ ] **Step 2: Run the focused test**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~TopologicalSorterTests" --no-restore`

Expected: current level scan does not satisfy the linear-cycle contract.

- [ ] **Step 3: Implement Kahn residual-node detection**

Build adjacency/in-degree maps once, process a queue, and run deterministic DFS only on residual nodes when a cycle exists. Return assembly-qualified identities and cycle names for diagnostics.

- [ ] **Step 4: Update generator call sites**

Use the new cycle-only method in `SingletonDIGenerator`; retain level-producing overloads only for tested internal callers that genuinely need levels.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~TopologicalSorterTests|FullyQualifiedName~SingletonDIGeneratorTests" --no-restore
git add src/SingletonDI.Generator/Helpers/TopologicalSorter.cs tests/SingletonDI.Tests/TopologicalSorterTests.cs tests/SingletonDI.Tests/SingletonDIGeneratorTests.cs
git commit -m "perf: use linear cycle detection"
```

### Task 24: Synchronize documentation and repository guidance

**Files:**
- Create: `tests/SingletonDI.Tests/DocumentationConsistencyTests.cs`
- Modify: `README.md:193-207,317-323,333-390`
- Modify: `AGENTS.md:16,23-46`
- Modify: public XML documentation in `src/SingletonDI.Attributes/*.cs` only where the changed contract requires it.

**Interfaces:**
- Produces: documentation matching C# 9, SDK 8+, supported consumer forms, inherited properties, shutdown behavior, package version and current test commands.

- [ ] **Step 1: Add a documentation consistency check**

Create `DocumentationConsistencyTests` that reads `README.md` and `AGENTS.md` from the repository root and rejects stale names `GeneratorPackage`, `IInitializeSync`, `IInitializeAsync`, `Container`, `ExceptionHelper` and the `Microsoft.CodeAnalysis.Testing` dependency when absent from project files.

- [ ] **Step 2: Run the consistency check**

Run: `dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DocumentationConsistencyTests" --no-restore`

Expected: current `AGENTS.md` fails the stale-name check.

- [ ] **Step 3: Update docs and XML contracts**

Document the actual runtime namespace, explicit composition-root setup, generated C# 9 requirement, `Bootstrap()` behavior, `InitializeAsync` options, signal exit semantics, supported consumer shapes and `await DisposeAsync()` guarantee.

- [ ] **Step 4: Verify and commit**

```bash
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --filter "FullyQualifiedName~DocumentationConsistencyTests" --no-restore
git add README.md AGENTS.md src/SingletonDI.Attributes tests/SingletonDI.Tests/DocumentationConsistencyTests.cs
git commit -m "docs: synchronize project guidance"
```

---

## Final Verification Sequence

Run after Task 24 and before declaring completion:

```bash
git status --short
git log --oneline -30
dotnet build SingletonDI.slnx --configuration Release --no-restore
dotnet test tests/SingletonDI.Tests/SingletonDI.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"
dotnet pack SingletonDI.slnx --configuration Release --no-restore --output C:/Users/virus/AppData/Local/Temp/opencode/singleton-review-final-pack
dotnet run --project src/SingletonDI.SampleApp/SingletonDI.SampleApp.csproj --configuration Release --no-build --no-restore
```

Run the isolated package matrix from `tests/SingletonDI.Tests/PackageSmoke/ConsumerApp` with SDK 8.0.131, 9.0.121 and 10.0.401. Confirm each build produces generated consumer properties and the runtime scenario exits successfully.

Inspect `git status --short`, `git diff --check`, and the complete commit list. Do not amend, squash, push or create a PR.
