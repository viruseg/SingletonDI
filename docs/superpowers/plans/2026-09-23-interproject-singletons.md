# Межпроектные singleton-провайдеры и contract-based consumers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Разрешить App-проекту собирать провайдеров из транзитивных сборок в один runtime-контейнер и разрешить library consumer-ам получать реализацию из App через общий contract без циклических ссылок.

**Architecture:** Runtime-реестр переносится в `SingletonDI.Attributes`; каждый provider-проект генерирует один module initializer с типизированной регистрацией, а composition root с `SingletonDICompositionRoot=true` генерирует bootstrap внешних provider-сборок. Consumers генерируют `Resolve<T>()`, где `T` может быть concrete provider type или interface/abstract contract. Root-генератор обходит metadata references, проверяет полный service graph и эмитит диагностику до запуска.

**Tech Stack:** C# latest, .NET Standard 2.0 + .NET 8 runtime assets, Roslyn `IIncrementalGenerator`, `Microsoft.CodeAnalysis.CSharp` 5.0.0, xUnit 2.9.3, `dotnet` CLI, existing NuGet package `SingletonDI`.

**Spec:** `docs/superpowers/specs/2026-09-23-interproject-singletons-design.md`

## Global Constraints

- Работать напрямую в текущей ветке; не создавать ветки, worktree или коммиты без отдельного запроса пользователя.
- Не добавлять комментарии в изменяемый код; сохранять существующие комментарии без необходимости.
- Не использовать reflection для обнаружения или создания сервисов; обход metadata symbols выполняется генератором, runtime работает с типизированными делегатами и `Type` keys.
- Сохранять `SingletonDI.Generated.SingletonDIInitializer.InitializeAsync(bool registerShutdownHandlers = true)` и `DisposeAsync()` без изменения пользовательских сигнатур.
- Сохранять single-project сценарий без `SingletonDICompositionRoot`; свойство требуется только для межпроектного composition root.
- Импортировать только public provider types; internal/private providers не включаются в root graph.
- Один provider в первой версии объявляет не более одного `ServiceType`; `ServiceType` — reference type, доступный provider-у и assignable типу реализации.
- Contract-based consumer names вычисляются только среди зависимостей самого consumer-а; `PropertyName` provider-а не влияет на имя свойства, когда provider assembly недоступна consumer-у.
- Поддерживать target frameworks `net8.0`, `net9.0`, `net10.0`; runtime также публикуется для `netstandard2.0` без изменения существующего package identity.
- Existing diagnostic IDs сохраняются; новые diagnostic IDs не переиспользуют `DM0001`–`DM0015`.
- Runtime state process-wide; независимые composition roots в одном процессе не поддерживаются.

## Review Focus

- Библиотечный consumer, видящий только общий interface contract, должен получить тот же объект, что и concrete provider из App.
- Внешний provider с `internal InitializeAsync()` должен регистрироваться из собственной сборки и не требовать public lifecycle API.
- Повторная транзитивная ссылка на одну provider-сборку и два providers с одинаковым contract должны давать дедупликацию и диагностику соответственно.
- Cross-assembly dependency graph должен иметь тот же topological order и reverse disposal order, что и single-project graph.
- Runtime registry не должен позволять новому provider зарегистрироваться после начала initialization; тесты, исполняющие runtime в одном process, не должны создавать несколько независимых root graphs.

---

## File Map

### Runtime и public attribute contract

- Modify: `src/SingletonDI.Attributes/SingletonDIProvideAttribute.cs:18-40` — добавить `ServiceType` без изменения существующего constructor overload.
- Create: `src/SingletonDI.Attributes/SingletonDIProviderModuleAttribute.cs` — assembly marker, который root использует для проверки provider protocol.
- Create: `src/SingletonDI.Attributes/Runtime/__SingletonDIHost__.cs` — скрытый generated-code registration/resolution contract.
- Create: `src/SingletonDI.Attributes/Runtime/ProviderRegistration.cs` — non-generic base и generic typed adapters без reflection.
- Create: `src/SingletonDI.Attributes/Runtime/ServiceRegistry.cs` — service-key map, duplicate detection, instances и testable registry instance.
- Create: `src/SingletonDI.Attributes/Runtime/ServiceGraph.cs` — runtime dependency graph, levels, cycle/missing-key handling.
- Create: `src/SingletonDI.Attributes/Runtime/SingletonDIInitializer.cs` — process-wide initialization, retry lock, disposal и public API.
- Create: `src/SingletonDI.Attributes/Runtime/ShutdownManager.cs` — перенесённая регистрация shutdown handlers с conditional modern-POSIX implementation.
- Modify: `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj` — runtime assets/dependencies и `InternalsVisibleTo` для unit tests.

### Generator models and validation

- Modify: `src/SingletonDI.Generator/Models/ProviderModel.cs` — assembly identity, `ServiceType` metadata и service-key dependencies.
- Modify: `src/SingletonDI.Generator/Models/ConsumerModel.cs` — typed consumer dependency descriptors.
- Create: `src/SingletonDI.Generator/Models/ServiceReferenceModel.cs` — FQN, short name, namespace, custom name и contract flag для generated consumer property.
- Modify: `src/SingletonDI.Generator/Models/CombinedModel.cs` — graph data uses service keys consistently.
- Create: `src/SingletonDI.Generator/Helpers/ServiceTypeResolver.cs` — concrete/contract key-to-provider map и duplicate mapping detection.
- Modify: `src/SingletonDI.Generator/Validators/ProviderValidator.cs` — read `ServiceType`, retain valid external/contract dependency keys и provide symbol-only overload for metadata providers.
- Modify: `src/SingletonDI.Generator/Validators/ConsumerValidator.cs` — allow external interface/abstract contracts, reject unsupported concrete types, preserve precise locations.
- Modify: `src/SingletonDI.Generator/Helpers/TopologicalSorter.cs` — sort by service-key edges using `ServiceTypeResolver` mapping.
- Modify: `src/SingletonDI.Generator/Helpers/PropertyNameResolver.cs` — resolve names per consumer dependency set.

### Generator collection and emission

- Create: `src/SingletonDI.Generator/Models/ProviderAssemblyModel.cs` — assembly identity, marker state и representative public provider type for bootstrap.
- Create: `src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs` — reference symbol traversal and provider model extraction.
- Create: `src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs` — dependency keys declared by consumers in referenced assemblies for root validation.
- Create: `src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs` — one module initializer containing local registration and external assembly bootstrap.
- Modify: `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs` — emit `__SingletonDIHost__.Resolve<T>()` properties and consumer-local names.
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs` — composition-root options, independent incremental branches, external aggregation and new emitters.
- Delete: `src/SingletonDI.Generator/Emitters/ContainerEmitter.cs` — stateful per-assembly container is replaced by runtime registry.
- Delete: `src/SingletonDI.Generator/Emitters/SingletonInitializerEmitter.cs` — public initializer moves to runtime assembly.
- Delete: `src/SingletonDI.Generator/Emitters/LifetimeEmitter.cs` — shutdown implementation moves to runtime assembly.
- Delete: `src/SingletonDI.Generator/Emitters/ExceptionHelperEmitter.cs` — runtime host owns initialization errors.
- Modify: `src/SingletonDI.Generator/DiagnosticDescriptors.cs` — add root/contract diagnostics `DM0016`–`DM0020`.

### Tests and integration fixtures

- Create: `tests/SingletonDI.Tests/RuntimeRegistryTests.cs` — runtime registry graph, aliasing, ordering, retry and failure tests using a fresh internal registry instance.
- Create: `tests/SingletonDI.Tests/AttributeContractTests.cs` — `ServiceType` and marker API contract.
- Create: `tests/SingletonDI.Tests/GeneratorOutputTests.cs` — generated provider module and runtime-backed consumer source assertions.
- Create: `tests/SingletonDI.Tests/GeneratorCompositionTests.cs` — in-memory multi-compilation reference scanning, generated source and root diagnostics.
- Modify: `tests/SingletonDI.Tests/SingletonDIGeneratorTests.cs` — updated model constructors and service metadata assertions.
- Modify: `tests/SingletonDI.Tests/DiagnosticErrorTests.cs` — runtime metadata references, analyzer options and new diagnostic cases.
- Modify: `tests/SingletonDI.Tests/PropertyNameResolverTests.cs` — runtime host reference and contract-name cases.
- Modify: `tests/SingletonDI.Tests/SingletonDisposeOrderTests.cs` — use the process-wide public initializer and avoid executing multiple independent graphs in one test process.
- Create: `tests/InterProjectFixtures/Shared.Contracts/Shared.Contracts.csproj` and `IDatabaseService.cs` — shared contract assembly.
- Create: `tests/InterProjectFixtures/ConsumerLibrary/ConsumerLibrary.csproj` and `Repository.cs` — consumer library referencing only Contracts.
- Create: `tests/InterProjectFixtures/RootApp/RootApp.csproj`, `DatabaseService.cs`, `LifecycleServices.cs`, and `Program.cs` — provider implementation and composition root with runtime lifecycle checks.
- Create: `tests/SingletonDI.Tests/InterProjectScenarioTests.cs` — real project-reference scenario and reference-equality assertion.
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj` — reference the RootApp fixture and runtime test support.
- Modify: `SingletonDI.slnx` — include the three fixture projects.

### Documentation and packaging

- Modify: `README.md` — English cross-project/contract setup, root property, diagnostics and removed per-assembly limitation.
- Modify: `README.RU.md` — Russian equivalent of the same public contract and examples.
- Modify: `Plan.md` — update architecture, models, pipeline and limitations to match the approved design.

---

### Task 1: Add the runtime service registry and attribute protocol

**Files:**
- Modify: `src/SingletonDI.Attributes/SingletonDIProvideAttribute.cs`
- Create: `src/SingletonDI.Attributes/SingletonDIProviderModuleAttribute.cs`
- Create: `src/SingletonDI.Attributes/Runtime/__SingletonDIHost__.cs`
- Create: `src/SingletonDI.Attributes/Runtime/ProviderRegistration.cs`
- Create: `src/SingletonDI.Attributes/Runtime/ServiceRegistry.cs`
- Create: `src/SingletonDI.Attributes/Runtime/ServiceGraph.cs`
- Create: `src/SingletonDI.Attributes/Runtime/SingletonDIInitializer.cs`
- Create: `src/SingletonDI.Attributes/Runtime/ShutdownManager.cs`
- Modify: `src/SingletonDI.Attributes/SingletonDI.Attributes.csproj`
- Create: `tests/SingletonDI.Tests/AttributeContractTests.cs`
- Create: `tests/SingletonDI.Tests/RuntimeRegistryTests.cs`

**Interfaces:**
- `SingletonDIProvideAttribute.ServiceType` is `Type?` with a public setter and XML documentation.
- `SingletonDIProviderModuleAttribute` is public, sealed and has `AttributeUsage(AttributeTargets.Assembly)`.
- `SingletonDI.Generated.__SingletonDIHost__` exposes:

```csharp
public static void RegisterProvider<TService, TImplementation>(
    Func<TImplementation> factory,
    Type[] dependencyTypes,
    Func<TImplementation, Task>? initializeAsync,
    Action<TImplementation>? dispose,
    Func<TImplementation, Task>? disposeAsync)
    where TImplementation : TService;

public static TService Resolve<TService>();
```

- `SingletonDI.Generated.SingletonDIInitializer` exposes only the existing public lifecycle methods:

```csharp
public static Task InitializeAsync(bool registerShutdownHandlers = true);
public static ValueTask DisposeAsync();
```

- `ServiceRegistry` is internal, constructible without static state, and exposes instance methods `RegisterProvider`, `InitializeAsync`, `Resolve<T>` and `DisposeAsync` for tests. The public initializer owns one process-wide instance.

- [ ] **Step 1: Write failing attribute contract tests**

Add tests that verify the old constructor remains usable and the named `ServiceType` value is retained:

```csharp
[Fact]
public void ServiceType_CanBeSetWithoutChangingPropertyName()
{
    var attribute = new SingletonDIProvideAttribute("_db")
    {
        ServiceType = typeof(IDatabaseService)
    };

    Assert.Equal("_db", attribute.PropertyName);
    Assert.Equal(typeof(IDatabaseService), attribute.ServiceType);
}

[Fact]
public void ProviderModuleMarker_TargetsAssembly()
{
    var usage = typeof(SingletonDIProviderModuleAttribute)
        .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
        .Cast<AttributeUsageAttribute>()
        .Single();

    Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
}
```

- [ ] **Step 2: Run the contract tests and verify the expected failure**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~AttributeContractTests"`

Expected: FAIL because `ServiceType` and `SingletonDIProviderModuleAttribute` do not exist yet.

- [ ] **Step 3: Implement the attribute and assembly marker**

Keep the existing `SingletonDIProvideAttribute(string? propertyName = null)` constructor and add only the documented settable property:

```csharp
public Type? ServiceType { get; set; }
```

Add the marker as a documented public assembly attribute. Do not add a second provider attribute or change `AllowMultiple` semantics.

- [ ] **Step 4: Implement the non-reflection runtime registry**

Use these runtime boundaries:

- `ProviderRegistration` stores implementation `Type`, dependency `Type[]`, factory, initialize delegate, synchronous dispose delegate and asynchronous dispose delegate.
- `ProviderRegistration<TService, TImplementation>` adapts the typed delegates to the non-generic base without scanning assemblies or invoking methods by reflection.
- `ServiceRegistry` stores one `ProviderRegistration` under `typeof(TService)` and `typeof(TImplementation)`, rejects a second provider for an existing key, rejects registration after initialization, and keeps concrete and contract aliases pointing to one instance.
- `ServiceGraph` builds dependency levels from registration dependency keys, reports missing keys/cycles as `InvalidOperationException`, creates each instance once, runs same-level async initializers with `Task.WhenAll`, and disposes levels in reverse order.
- `SingletonDIInitializer` owns the process-wide registry, lock, cached initialization `Task`, initialized flag and shutdown registration. A failed initialization clears the cached task and leaves the registry available for retry.
- `ShutdownManager` preserves `registerShutdownHandlers`, `ProcessExit`, `CancelKeyPress`, and modern POSIX signal behavior. Use conditional compilation for the `net8.0` asset and a netstandard-compatible fallback; do not use reflection to discover signals.

- [ ] **Step 5: Implement the public host and initializer adapters**

`__SingletonDIHost__.RegisterProvider` delegates to the initializer’s process-wide registry without exposing a mutable registry. `Resolve<T>` throws the existing initialization exception before successful initialization and throws a service-key-specific `InvalidOperationException` for an unknown key. `SingletonDIInitializer.InitializeAsync` and `DisposeAsync` forward to the same registry and preserve the existing public signatures.

- [ ] **Step 6: Add runtime package assets and test visibility**

Change the attributes project to target `netstandard2.0;net8.0`, add `System.Threading.Tasks.Extensions` 4.6.0 and `Microsoft.Bcl.AsyncInterfaces` 9.0.0 as runtime dependencies, and add `InternalsVisibleTo("SingletonDI.Tests")` for the internal registry tests. Move the Release documentation output to a target-framework-specific path such as `bin\Release\$(TargetFramework)\SingletonDI.Attributes.xml`. Keep the existing package ID, assembly name and analyzer packing paths.

- [ ] **Step 7: Write the runtime graph tests**

Use a new `ServiceRegistry` per test so tests do not create multiple process-wide roots. The test must cover:

```csharp
[Fact]
public async Task RegisterProvider_ConcreteAndContractResolveSameInstance()
{
    var registry = new ServiceRegistry();
    registry.RegisterProvider<IThing, Thing>(
        static () => new Thing(),
        Array.Empty<Type>(),
        null,
        null,
        null);

    await registry.InitializeAsync();

    Assert.Same(registry.Resolve<IThing>(), registry.Resolve<Thing>());
}

[Fact]
public async Task Registry_UsesDependencyLevelsAndReverseDisposal()
{
    var events = new List<string>();
    var registry = new ServiceRegistry();
    registry.RegisterProvider<IBase, BaseService>(
        () => new BaseService(events),
        Array.Empty<Type>(),
        null,
        value => value.Dispose(),
        null);
    registry.RegisterProvider<IDependent, DependentService>(
        () => new DependentService(events),
        new[] { typeof(IBase) },
        null,
        value => value.Dispose(),
        null);

    await registry.InitializeAsync();
    await registry.DisposeAsync();

    Assert.Equal(new[] { "create:BaseService", "create:DependentService", "dispose:DependentService", "dispose:BaseService" }, events);
}
```

Define the test service types in the same test fixture:

```csharp
private interface IThing
{
}

private sealed class Thing : IThing
{
}

private interface IBase
{
}

private sealed class BaseService : IBase, IDisposable
{
    private readonly List<string> _events;

    public BaseService(List<string> events)
    {
        _events = events;
        events.Add("create:BaseService");
    }

    public void Dispose()
    {
        _events.Add("dispose:BaseService");
    }
}

private interface IDependent
{
}

private sealed class DependentService : IDependent, IDisposable
{
    private readonly List<string> _events;

    public DependentService(List<string> events)
    {
        _events = events;
        events.Add("create:DependentService");
    }

    public void Dispose()
    {
        _events.Add("dispose:DependentService");
    }
}
```

Add separate assertions for missing dependencies, cycles, duplicate keys, resolve-before-initialize, retry after a failed initialize, and registration after initialization. Keep each test’s registry instance isolated; do not call the static public initializer from these unit tests.

- [ ] **Step 8: Run the runtime and contract tests**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~AttributeContractTests|FullyQualifiedName~RuntimeRegistryTests"`

Expected: PASS for the new contract and isolated runtime registry tests.

---

### Task 2: Extend immutable models, validators and service-key graph resolution

**Files:**
- Modify: `src/SingletonDI.Generator/Models/ProviderModel.cs`
- Modify: `src/SingletonDI.Generator/Models/ConsumerModel.cs`
- Modify: `src/SingletonDI.Generator/Models/CombinedModel.cs`
- Create: `src/SingletonDI.Generator/Models/ServiceReferenceModel.cs`
- Create: `src/SingletonDI.Generator/Helpers/ServiceTypeResolver.cs`
- Modify: `src/SingletonDI.Generator/Validators/ProviderValidator.cs`
- Modify: `src/SingletonDI.Generator/Validators/ConsumerValidator.cs`
- Modify: `src/SingletonDI.Generator/Helpers/TopologicalSorter.cs`
- Modify: `src/SingletonDI.Generator/Helpers/PropertyNameResolver.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDIGeneratorTests.cs`

**Interfaces:**

`ServiceReferenceModel` is the exact contract emitted for each consumer dependency:

```csharp
public readonly record struct ServiceReferenceModel(
    string FullyQualifiedName,
    string ShortName,
    string Namespace,
    string? PropertyName,
    bool IsContract);
```

`ProviderModel` uses this complete constructor shape after the model update:

```csharp
public readonly record struct ProviderModel(
    string fullyQualifiedName,
    string shortName,
    string @namespace,
    string assemblyIdentity,
    bool hasInitializeAsyncMethod,
    bool isDisposable,
    bool isAsyncDisposable,
    ImmutableArray<string> dependencies,
    string? serviceTypeFullyQualifiedName,
    string? serviceTypeShortName,
    string? serviceTypeNamespace,
    string? propertyName,
    Location location,
    Location? propertyNameLocation);
```

`ProviderModel` treats `Dependencies` as service keys rather than only locally declared provider FQNs. `ConsumerModel.Dependencies` becomes `ImmutableArray<ServiceReferenceModel>`.

Define `ServiceTypeMapResult` and `ServiceTypeConflict` in `ServiceTypeResolver.cs`; both are internal implementation records.

```csharp
internal readonly record struct ServiceTypeMapResult(
    ImmutableDictionary<string, string> Map,
    ImmutableArray<ServiceTypeConflict> Conflicts);

internal readonly record struct ServiceTypeConflict(
    string ServiceTypeFullyQualifiedName,
    ImmutableArray<string> ProviderFullyQualifiedNames);

internal static ServiceTypeMapResult BuildServiceTypeMap(
    IReadOnlyCollection<ProviderModel> providers);
```

The map contains both concrete implementation keys and each non-null `ServiceType` key. `Conflicts` contains every provider identity for a duplicated key; the resolver never silently overwrites a mapping.

- [ ] **Step 1: Write failing model and resolver tests**

Add tests that construct a provider with an assembly identity and service type, then verify the concrete and contract keys are distinct and point to the same provider:

```csharp
[Fact]
public void ServiceTypeMap_MapsContractAndImplementationToOneProvider()
{
    var provider = new ProviderModel(
        "global::App.DatabaseService",
        "DatabaseService",
        "App",
        "App, Version=1.0.0.0",
        true,
        false,
        false,
        ImmutableArray<string>.Empty,
        "global::Contracts.IDatabaseService",
        "IDatabaseService",
        "Contracts",
        null,
        Location.None,
        null);

    var result = ServiceTypeResolver.BuildServiceTypeMap([provider]);
    var map = result.Map;

    Assert.Equal(provider.FullyQualifiedName, map["global::App.DatabaseService"]);
    Assert.Equal(provider.FullyQualifiedName, map["global::Contracts.IDatabaseService"]);
}
```

Add a resolver test with two providers mapping the same contract and assert the caller receives both provider identities for the conflict diagnostic.

- [ ] **Step 2: Run the model tests and verify the expected failure**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~SingletonDIGeneratorTests"`

Expected: FAIL because the new model fields and `ServiceTypeResolver` do not exist.

- [ ] **Step 3: Update immutable models without changing public attribute contracts**

Add the new fields and update all existing model construction in `SingletonDIGeneratorTests`. Use explicit `ImmutableArray<string>` values for empty dependency sets. Keep `ProviderModel.Location` and `PropertyNameLocation` so local diagnostics retain precise source locations; external providers use `Location.None`.

- [ ] **Step 4: Implement service reference and service map models**

Implement `ServiceReferenceModel` as a value type with full XML documentation for its public properties. Implement `ServiceTypeResolver` with a deterministic map builder. It must:

1. Add every provider’s concrete FQN.
2. Add `ServiceTypeFullyQualifiedName` when present.
3. Preserve all conflicting provider FQNs for the diagnostic layer.
4. Deduplicate identical provider/assembly pairs.
5. Never use namespace string equality as a substitute for a full metadata type key.

- [ ] **Step 5: Extend `ProviderValidator`**

Add a symbol-only overload with this exact shape:

```csharp
internal static ProviderModel? Validate(
    INamedTypeSymbol typeSymbol,
    Location location,
    ImmutableHashSet<string> knownProviderFullyQualifiedNames,
    Action<Diagnostic> reportDiagnostic);
```

The existing syntax overload delegates to it and then attaches the source declaration location, so metadata references can use the same validation rules as source providers. The overload must:

- read `ServiceType` from named attribute data;
- report a new invalid-service diagnostic when the value is not a reference type or is not assignable to the provider;
- retain every dependency `Type` from `[SingletonDIConsume]` that is a provider symbol or an interface/abstract contract as a service key;
- report `DM0006` for a concrete dependency type that has no provider and is not a valid contract;
- preserve `AssemblyIdentity` and `Location.None` for external symbols;
- keep the existing generic, abstract, constructor, async accessibility and property-name checks.

The local syntax overload should call the symbol-only builder and then attach the source declaration location.

- [ ] **Step 6: Extend `ConsumerValidator` contract handling**

Change the `ConsumerValidator.Validate` input to `ImmutableHashSet<string> knownProviderFullyQualifiedNames` plus the existing diagnostic callback. For each dependency symbol, first check its own attributes; a metadata provider is valid even when it is not in the local set. A dependency is valid when it is a known provider, a symbol carrying `[SingletonDIProvide]`, or an interface/abstract class that can be supplied by a root. A concrete class with no provider remains `DM0006`. For every valid dependency, create `ServiceReferenceModel` from the symbol and set `IsContract=true` for an interface/abstract contract; a visible provider keeps `IsContract=false` and may carry its `PropertyName`.

Preserve DM0007, DM0008 and DM0010 behavior and their argument locations.

- [ ] **Step 7: Update topological sorting to use service keys**

Add a `SortByLevels` overload accepting the service-key-to-provider map. For each provider dependency key, resolve the target provider before adding an edge. Ignore an unresolved edge only after the caller has emitted the missing-contract diagnostic; never interpret an unresolved key as an unrelated provider. Keep the existing provider-FQN overload for current unit tests and update its implementation to delegate to the shared edge builder.

- [ ] **Step 8: Implement per-consumer property-name resolution**

Add `ResolveConsumerPropertyNames(IEnumerable<ServiceReferenceModel>)`. It must group only the current dependency set by `ShortName`, use a custom name only when that dependency has a visible provider custom name, use `Namespace_ShortNameInstance` for short-name conflicts, and use `ShortNameInstance` otherwise. The existing global provider-name resolver remains for root property conflict validation.

- [ ] **Step 9: Run model, validator and property resolver tests**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~SingletonDIGeneratorTests|FullyQualifiedName~PropertyNameResolverTests|FullyQualifiedName~DiagnosticErrorTests"`

Expected: existing tests pass after their model/helper updates, while the new contract-specific tests remain red until Task 3/4 adds generation support.

---

### Task 3: Generate provider registration modules and runtime-backed consumers

**Files:**
- Create: `src/SingletonDI.Generator/Models/ProviderAssemblyModel.cs`
- Create: `src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs`
- Modify: `src/SingletonDI.Generator/Emitters/ConsumerEmitter.cs`
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs`
- Delete: `src/SingletonDI.Generator/Emitters/ContainerEmitter.cs`
- Delete: `src/SingletonDI.Generator/Emitters/SingletonInitializerEmitter.cs`
- Delete: `src/SingletonDI.Generator/Emitters/LifetimeEmitter.cs`
- Delete: `src/SingletonDI.Generator/Emitters/ExceptionHelperEmitter.cs`
- Modify: `tests/SingletonDI.Tests/PropertyNameResolverTests.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDIGeneratorTests.cs`
- Create: `tests/SingletonDI.Tests/GeneratorOutputTests.cs`

**Interfaces:**

`ProviderAssemblyModel` is an internal readonly record struct containing `AssemblyIdentity`, `BootstrapTypeFullyQualifiedName`, and `HasModuleMarker`.

`ProviderModuleEmitter.Generate` has one source output for a compilation:

```csharp
internal static string Generate(
    ImmutableArray<ProviderModel> localProviders,
    ImmutableArray<ProviderAssemblyModel> externalProviderAssemblies,
    bool isCompositionRoot);
```

The emitter writes one module initializer. It emits the assembly marker when local providers exist, local `RegisterProvider` calls for all local providers, and `RuntimeHelpers.RunClassConstructor` calls for external provider assemblies only when `isCompositionRoot` is true. It never emits a local container or public initializer.

- [ ] **Step 1: Write failing source-generation tests**

Add tests that run the generator and inspect generated trees for a source containing two providers and a consumer. Assert that the generated tree contains `SingletonDIProviderModuleAttribute`, `ModuleInitializer`, `RegisterProvider`, and `Resolve<T>`, and does not contain `__SingletonDIContainer__` or a generated `SingletonDIInitializer` type.

The concrete source assertions should be equivalent to:

```csharp
Assert.Contains("RegisterProvider<global::App.FirstService, global::App.FirstService>", generated);
Assert.Contains("global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::App.FirstService>()", generated);
Assert.DoesNotContain("__SingletonDIContainer__", generated);
```

- [ ] **Step 2: Run the source-generation tests and verify the expected failure**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~GeneratorOutputTests"`

Expected: FAIL because the new module emitter does not exist and the old generator emits `__SingletonDIContainer__`.

- [ ] **Step 3: Implement `ProviderModuleEmitter`**

Generate code with these rules:

- use a single static void parameterless method marked `[ModuleInitializer]`;
- when the target framework does not provide `System.Runtime.CompilerServices.ModuleInitializerAttribute`, emit the guarded compatibility type under `#if !NET5_0_OR_GREATER`; do not emit a duplicate on modern targets;
- emit `[assembly: global::SingletonDI.Attributes.SingletonDIProviderModuleAttribute]` only when the current assembly has local providers;
- call `RegisterProvider<TService, TImplementation>` with `TService=TImplementation` when no `ServiceType` exists;
- call it with the provider’s contract and implementation when `ServiceType` exists;
- pass `Array.Empty<Type>()` or a generated `Type[]` containing every declared dependency service key;
- pass a `Task`-returning delegate for `InitializeAsync`, including `.AsTask()` for `ValueTask`;
- pass `Action<T>` for `IDisposable` and `Func<T, Task>` for `IAsyncDisposable`, preferring async disposal when both are implemented;
- use fully qualified names everywhere;
- call `RuntimeHelpers.RunClassConstructor(typeof(global::ProviderType).TypeHandle)` once per external provider assembly, excluding the current assembly.

Do not emit calls to local container fields. Do not emit a module initializer for a consumer-only library without a composition root or local provider.

- [ ] **Step 4: Replace `ConsumerEmitter` output**

Change the emitter to accept `List<ConsumerModel>` whose dependencies are `ServiceReferenceModel`. For each dependency, emit a property with the resolved name and return:

```csharp
return global::SingletonDI.Generated.__SingletonDIHost__.Resolve<global::DependencyType>();
```

Use the contract type as the property type for a contract dependency and the concrete provider type for a visible provider dependency. Preserve the current file-name collision strategy based on the consumer FQN.

- [ ] **Step 5: Rewire the local incremental generator branch**

Keep local provider and consumer syntax providers independent. Collect and validate local models, resolve local service keys, and emit:

1. one `ProviderModuleEmitter` output when local providers exist;
2. consumer outputs when local consumers exist;
3. no output when the project has neither.

Keep the provider/consumer `Collect()` and `CompilationProvider` combination so a local consumer change does not cause unrelated provider source to be regenerated. Remove all calls to the deleted emitters and remove the old global exception-helper output.

- [ ] **Step 6: Update the existing compilation helpers**

Ensure `PropertyNameResolverTests` and `SingletonDIGeneratorTests` include the runtime `SingletonDI.Attributes` metadata reference and compile the generated consumer call. Keep existing single-project source cases unchanged except for the generated return target.

- [ ] **Step 7: Run local generation and backward-compatibility tests**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~PropertyNameResolverTests|FullyQualifiedName~SingletonDIGeneratorTests"`

Expected: PASS for all existing single-project property and model tests plus the new module/consumer source assertions.

---

### Task 4: Add recursive reference scanning, composition-root bootstrap and diagnostics

**Files:**
- Create: `src/SingletonDI.Generator/Helpers/ProviderSymbolCollector.cs`
- Create: `src/SingletonDI.Generator/Helpers/ReferencedConsumerCollector.cs`
- Modify: `src/SingletonDI.Generator/Emitters/ProviderModuleEmitter.cs`
- Modify: `src/SingletonDI.Generator/SingletonDIGenerator.cs`
- Modify: `src/SingletonDI.Generator/DiagnosticDescriptors.cs`
- Modify: `src/SingletonDI.Generator/Helpers/ServiceTypeResolver.cs`
- Modify: `src/SingletonDI.Generator/Helpers/TopologicalSorter.cs`
- Create: `tests/SingletonDI.Tests/GeneratorCompositionTests.cs`
- Modify: `tests/SingletonDI.Tests/DiagnosticErrorTests.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj`

**Interfaces:**

`ProviderSymbolCollector` exposes referenced providers only; local source providers remain owned by the syntax pipeline:

```csharp
internal static ImmutableArray<ProviderModel> CollectReferencedProviders(
    Compilation compilation,
    CancellationToken cancellationToken);
```

`ReferencedConsumerCollector` exposes the service keys requested by consumers in referenced assemblies:

```csharp
internal static ImmutableArray<ImmutableArray<string>> CollectReferencedConsumerDependencies(
    Compilation compilation,
    CancellationToken cancellationToken);
```

Both collectors use `IAssemblySymbol.GlobalNamespace`, assembly identity and a visited assembly set; they never enumerate loaded runtime assemblies and never return providers from `compilation.Assembly` as external models.

The generator reads `build_property.SingletonDICompositionRoot` with `AnalyzerConfigOptionsProvider.GlobalOptions`, parses it case-insensitively as `true`, and only scans external references when the value is true.

- [ ] **Step 1: Write failing multi-compilation tests**

Create a helper that emits a Contracts compilation, a provider compilation with the new generator, and an App compilation referencing both. The helper must pass an `AnalyzerConfigOptionsProvider` containing `build_property.SingletonDICompositionRoot=true` to the App driver.

Add these tests:

```csharp
[Fact]
public void Root_ImportsPublicProviderFromReferencedAssembly()
{
    var result = RunComposition(
        providerSource,
        appSource,
        compositionRoot: true);

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    Assert.Contains("RunClassConstructor", result.GeneratedSources);
}

[Fact]
public void Root_IgnoresNonPublicReferencedProvider()
{
    var result = RunComposition(
        nonPublicProviderSource,
        appSource,
        compositionRoot: true);

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0020");
    Assert.DoesNotContain("RunClassConstructor", result.GeneratedSources);
}

[Fact]
public void Root_ReportsContractWithMultipleProviders()
{
    var result = RunComposition(
        twoProvidersForOneContract,
        appSource,
        compositionRoot: true);

    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0019");
}
```

Also add a test that the same referenced assembly reached through two metadata references produces one bootstrap call and no duplicate provider model. Add a test where a referenced ConsumerLibrary requests `IDatabaseService` but the App compilation has no matching provider and assert `DM0018`. Add a metadata-reference cycle fixture with providers in two assemblies consuming each other’s contract and assert `DM0009` with both assembly-qualified names.

- [ ] **Step 2: Run the composition tests and verify the expected failure**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~GeneratorCompositionTests"`

Expected: FAIL because the collector, root option handling and new diagnostics are not implemented.

- [ ] **Step 3: Implement symbol collection and provider metadata extraction**

Traverse referenced namespaces recursively. `ProviderSymbolCollector` must skip `compilation.Assembly`; the local syntax branch supplies source providers. For each referenced `[SingletonDIProvide]` type:

- retain public types for root aggregation;
- call the symbol-only `ProviderValidator` overload;
- store the provider’s containing assembly identity;
- read the assembly marker by metadata attribute name;
- retain one `ProviderAssemblyModel` per assembly identity;
- use a deterministic type/assembly sort before emission.

`ReferencedConsumerCollector` traverses the same referenced assembly set, reads constructor arguments from each `[SingletonDIConsume]` attribute, and returns the declared service keys without generating source. The root validates these keys against the complete service map so a missing implementation in a library is diagnosed in the App build.

When a provider assembly lacks the marker, retain the provider for diagnostics but do not emit a bootstrap call for that assembly. Internal/private providers are ignored by the root collector, while their own project can still emit a local module.

- [ ] **Step 4: Add root diagnostics with stable IDs**

Add these descriptors through the existing `DiagnosticDescriptors.Create` factory:

- `DM0016` — `ServiceType` is invalid or not assignable to the provider;
- `DM0017` — an executable with an external consumer lacks `SingletonDICompositionRoot=true`;
- `DM0018` — no provider exists for a requested external contract;
- `DM0019` — multiple providers map to one contract;
- `DM0020` — a referenced provider assembly lacks the new provider-module marker.

Use `Location` from source providers when available and `Location.None` with assembly-qualified type names for metadata providers. Keep the existing DM0009 cycle and DM0001/DM0003 property conflict diagnostics.

- [ ] **Step 5: Extend the root pipeline and module emitter**

When the root option is true, combine local provider models with `CollectReferencedProviders`, collect referenced consumer dependency keys, call `ServiceTypeResolver.BuildServiceTypeMap`, emit one diagnostic for every `ServiceTypeMapResult.Conflicts` entry, report every referenced consumer key absent from `ServiceTypeMapResult.Map`, validate all contract mappings and dependencies, sort the complete graph with `TopologicalSorter`, and pass distinct external `ProviderAssemblyModel` values to `ProviderModuleEmitter`. When the option is false, skip external scanning and emit only the local module from Task 3.

Do not return early after a local cycle before consumer source generation when the only error is an external root-graph issue; report all independent diagnostics and suppress only the invalid root bootstrap.

- [ ] **Step 6: Add analyzer-option and metadata-reference test support**

Update the test compilation helper to include `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `System.Runtime.InteropServices` and the runtime attributes assembly. Add a test `AnalyzerConfigOptionsProvider` implementation that supplies `build_property.SingletonDICompositionRoot` and `build_property.OutputType` without adding a production MSBuild dependency.

Add diagnostic tests for DM0016, DM0017, DM0018, DM0019 and DM0020, including the case where a library consumer has no root error and an executable consumer does.

- [ ] **Step 7: Run composition and diagnostic tests**

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~GeneratorCompositionTests|FullyQualifiedName~DiagnosticErrorTests"`

Expected: PASS for source generation, root scanning, contract validation and all existing diagnostics.

---

### Task 5: Add real cross-project fixtures and update runtime execution tests

**Files:**
- Create: `tests/InterProjectFixtures/Shared.Contracts/Shared.Contracts.csproj`
- Create: `tests/InterProjectFixtures/Shared.Contracts/IDatabaseService.cs`
- Create: `tests/InterProjectFixtures/ConsumerLibrary/ConsumerLibrary.csproj`
- Create: `tests/InterProjectFixtures/ConsumerLibrary/Repository.cs`
- Create: `tests/InterProjectFixtures/RootApp/RootApp.csproj`
- Create: `tests/InterProjectFixtures/RootApp/DatabaseService.cs`
- Create: `tests/InterProjectFixtures/RootApp/LifecycleServices.cs`
- Create: `tests/InterProjectFixtures/RootApp/Program.cs`
- Create: `tests/SingletonDI.Tests/InterProjectScenarioTests.cs`
- Modify: `tests/SingletonDI.Tests/SingletonDI.Tests.csproj`
- Modify: `tests/SingletonDI.Tests/SingletonDisposeOrderTests.cs`
- Modify: `SingletonDI.slnx`

**Interfaces:**

- `Shared.Contracts` contains only `IDatabaseService`.
- `ConsumerLibrary.Repository` exposes a public method that returns the generated `IDatabaseServiceInstance` property; it does not reference RootApp or DatabaseService.
- `RootApp.DatabaseService` is public and marked `[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]`.
- `RootApp` sets `SingletonDICompositionRoot=true` and exposes a public `RunScenarioAsync` method for tests.
- `InterProjectScenarioTests` calls the root scenario and asserts concrete/contract reference equality and disposal state.

- [ ] **Step 1: Create the shared Contracts project**

Create a net8.0 class library with no SingletonDI dependency beyond the project’s normal SDK settings. Add a public `IDatabaseService` with no implementation.

- [ ] **Step 2: Create the consumer library project**

Reference `Shared.Contracts`, `SingletonDI.Attributes`, and the generator/refactoring analyzers. Add a partial `Repository` with `[SingletonDIConsume(typeof(IDatabaseService))]` and a public `GetService()` method returning the generated property. Do not add a reference to RootApp.

- [ ] **Step 3: Create the root App fixture**

Reference Contracts and ConsumerLibrary, add the analyzer project references used by the existing SampleApp, and set:

```xml
<SingletonDICompositionRoot>true</SingletonDICompositionRoot>
```

Add a public `DatabaseService` with a public parameterless constructor, an `IDisposable` implementation, and a static disposal flag. Add `LifecycleServices.cs` with a three-level provider chain matching the existing `SingletonDisposeOrderTests` source. Add `Program.RunScenarioAsync()` that calls `SingletonDIInitializer.InitializeAsync(false)`, obtains the contract service through `Repository.GetService()`, resolves the concrete provider through a public RootApp method, records the lifecycle log before disposal, calls `DisposeAsync()`, and returns the observed values.

- [ ] **Step 4: Add the real integration test**

The test must assert all of the following:

```csharp
Assert.NotNull(scenario.ContractService);
Assert.NotNull(scenario.ConcreteService);
Assert.Same(scenario.ContractService, scenario.ConcreteService);
Assert.False(scenario.ServiceDisposedBeforeDispose);
Assert.True(scenario.ServiceDisposedAfterDispose);
Assert.Equal(expectedLifecycleOrder, scenario.LifecycleEvents);
```

Define `expectedLifecycleOrder` in the test as the existing three-level registration sequence followed by its exact reverse disposal sequence. The test must also prove that the consumer library compiles without a direct `DatabaseService` reference by building the three projects as part of the solution.

- [ ] **Step 5: Update the existing dispose-order runtime test**

Replace reflection against `__SingletonDIContainer__` with a compile-time generated-source assertion and remove all `Assembly.Load`/`InitializeAsync` calls from this test class. Move the runtime execution assertions to `RootApp/LifecycleServices.cs` and `Program.RunScenarioAsync()`, where the process-wide container is initialized exactly once. Preserve the exact assertions for level order, reverse disposal order, `IAsyncDisposable` preference and registration-before-disposal in the RootApp scenario.

- [ ] **Step 6: Add fixture projects to the solution and test project**

Add the three fixture projects to `SingletonDI.slnx` and reference `RootApp` from `SingletonDI.Tests`. Keep the existing generator and analyzer references in the test project unchanged.

- [ ] **Step 7: Build and run the integration fixture**

Run: `dotnet build tests/InterProjectFixtures/RootApp/RootApp.csproj`

Expected: PASS, including generation in ConsumerLibrary and RootApp and the root bootstrap of the provider assembly.

Run: `dotnet test tests/SingletonDI.Tests --filter "FullyQualifiedName~InterProjectScenarioTests|FullyQualifiedName~SingletonDisposeOrderTests"`

Expected: PASS with concrete/contract identity and reverse-order disposal assertions.

---

### Task 6: Update public documentation and project plan

**Files:**
- Modify: `README.md`
- Modify: `README.RU.md`
- Modify: `Plan.md`
- Modify: `src/SingletonDI.Attributes/SingletonDIProvideAttribute.cs` — align public XML documentation with the implemented `ServiceType` contract.

**Interfaces:** No new public API beyond the approved `ServiceType`, assembly marker and hidden generated-code host. Documentation must describe these exact names and the process-wide limitation.

- [ ] **Step 1: Replace the single-assembly limitation in both READMEs**

Document that providers can come from public types in referenced assemblies, that the composition root requires:

```xml
<SingletonDICompositionRoot>true</SingletonDICompositionRoot>
```

and that ProjectReference/PackageReference are both supported. Keep the existing single-project example and add the shared-contract example with `IDatabaseService`.

- [ ] **Step 2: Document `ServiceType`, contract access and naming rules**

Show:

```csharp
[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService
{
}

[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
}
```

State that the library consumer references only Contracts, that the provider is registered by App, that concrete and contract keys resolve the same object, and that contract consumer names are derived from the contract type rather than the provider’s `PropertyName`.

- [ ] **Step 3: Document lifecycle, root diagnostics and limitations**

Add DM0016–DM0020 to the diagnostics table, explain the missing-root diagnostic only for executable external consumers, preserve the retry/disposal semantics, and state that independent composition roots in one process and multiple contract types per provider are not supported.

- [ ] **Step 4: Update `Plan.md` architecture and pipeline**

Replace the one-assembly container description with the runtime registry, provider module initializer, contract service map and root bootstrap flow. Update the model fields, diagnostic table, generated-code section, target-framework note and the implementation order to match the approved design.

- [ ] **Step 5: Check documentation examples against the fixture**

Run: `dotnet run --project tests/InterProjectFixtures/RootApp/RootApp.csproj`

Expected: PASS. The documented `ServiceType` syntax and root property must compile and run exactly as written.

---

### Task 7: Run the full verification suite and clean obsolete paths

**Files:**
- Modify only files required by failed tests; remove any unused old emitter references found by compiler/search.
- Verify: `src/SingletonDI.Generator/Emitters/` contains no unreferenced old container/initializer emitter source.
- Verify: `tests/SingletonDI.Tests/` contains no test that reflects the removed per-assembly container.

**Interfaces:** The final public contract and generated source must match Tasks 1–6 exactly.

- [ ] **Step 1: Build the complete solution**

Run: `dotnet build`

Expected: PASS for all existing and newly added projects, including both runtime target assets and all analyzer projects.

- [ ] **Step 2: Run the complete test suite**

Run: `dotnet test`

Expected: PASS across `net8.0`, `net9.0` and `net10.0`, including existing diagnostics, property-name, code-fix, dispose-order and new cross-project tests.

- [ ] **Step 3: Run the existing sample application**

Run: `dotnet run --project src/SingletonDI.SampleApp`

Expected: PASS without requiring `SingletonDICompositionRoot`, proving the single-project compatibility requirement.

- [ ] **Step 4: Run the new cross-project fixture**

Run: `dotnet run --project tests/InterProjectFixtures/RootApp/RootApp.csproj`

Expected: PASS, with the library consumer receiving the App provider and the process exiting after disposal.

- [ ] **Step 5: Inspect generated source and repository diff**

Run: `git diff --check` and inspect `git status --short`.

Expected: no whitespace errors; only intended runtime, generator, tests, fixture, solution and documentation files are changed. Do not commit unless the user explicitly requests it.

- [ ] **Step 6: Verify no obsolete generated API remains**

Search for `__SingletonDIContainer__`, `SingletonDIInitializer.g.cs`, `ExceptionHelperEmitter.Generate` and `LifetimeEmitter.Generate` in generator source and tests. Any remaining match must be a deliberate compatibility test or documentation reference; otherwise remove the obsolete path before completion.
