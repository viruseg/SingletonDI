# План: Incremental Source Generator — Dependency Manager (Singleton)

---

## 1. Целевая архитектура

```text
SingletonDI/
├── src/
│   ├── SingletonDI.Attributes/
│   │   ├── SingletonDIProvideAttribute.cs
│   │   ├── SingletonDIConsumeAttribute.cs
│   │   ├── SingletonDIProviderModuleAttribute.cs
│   │   └── Runtime/
│   │       ├── __SingletonDIHost__.cs
│   │       ├── ProviderRegistration.cs
│   │       ├── ServiceRegistry.cs
│   │       ├── ServiceGraph.cs
│   │       ├── SingletonDIInitializer.cs
│   │       └── ShutdownManager.cs
│   ├── SingletonDI.Generator/
│   │   ├── Models/
│   │   ├── Validators/
│   │   ├── Helpers/
│   │   └── Emitters/
│   ├── SingletonDI.Refactoring/
│   └── SingletonDI.SampleApp/
└── tests/
    ├── SingletonDI.Tests/
    └── InterProjectFixtures/
        ├── Shared.Contracts/
        ├── ConsumerLibrary/
        └── RootApp/
```

Runtime-состояние находится в общем пакете `SingletonDI`, а не в контейнере, создаваемом генератором для каждой сборки. Generator регистрирует провайдеров через типизированные делегаты, а runtime хранит один реестр на процесс.

### Роли проектов

- **Provider-проект** генерирует assembly marker и `[ModuleInitializer]`, который регистрирует локальных провайдеров.
- **Consumer-проект** генерирует типизированные свойства, обращающиеся к runtime host. Библиотека может видеть только общий контракт из нижнеуровневой сборки.
- **Composition root** — единственный исполняемый проект, который агрегирует полный граф и запускает registration module внешних provider-сборок.
- **Single-project** продолжает работать без `SingletonDICompositionRoot`: локальный module initializer регистрирует провайдеров, а `SingletonDIInitializer` инициализирует процессный реестр.

---

## 2. Публичные контракты

### `SingletonDIProvideAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    public string? PropertyName { get; }

    public Type? ServiceType { get; set; }

    public SingletonDIProvideAttribute(string? propertyName = null)
    {
        PropertyName = propertyName;
    }
}
```

`ServiceType` — единственный дополнительный contract key провайдера. Он должен быть reference type, доступным provider-сборке, и провайдер должен быть назначаем этому типу. При его наличии runtime регистрирует одну реализацию по concrete type и по contract type.

Асинхронный initializer provider-а — метод без параметров `InitializeAsync()`, возвращающий `Task` или `ValueTask`. `ProviderModuleEmitter` преобразует `ValueTask` через `AsTask()` в общий typed delegate `Func<T, Task>`.

### `SingletonDIConsumeAttribute`

```csharp
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true)]
public sealed class SingletonDIConsumeAttribute : Attribute
{
    public Type[] Dependencies { get; }

    public SingletonDIConsumeAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
```

Аргумент `typeof(...)` может быть concrete provider type, видимым текущему проекту, либо interface/abstract contract, объявленным локально или во внешней сборке. Наличие реализации контракта проверяет composition root.

### Assembly marker

```csharp
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class SingletonDIProviderModuleAttribute : Attribute
{
}
```

Marker генерируется автоматически в provider-сборке. Он позволяет composition root отличить сборку, поддерживающую текущий registration protocol, от старой сборки только с атрибутами.

### Runtime API

Публичные методы lifecycle сохраняют исходные сигнатуры:

```csharp
namespace SingletonDI.Generated;

public static class SingletonDIInitializer
{
    public static Task InitializeAsync(bool registerShutdownHandlers = true);

    public static ValueTask DisposeAsync();
}
```

Скрытый generated-code host помечен `EditorBrowsable(EditorBrowsableState.Never)` и не является пользовательским API:

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public static class __SingletonDIHost__
{
    public static void RegisterProvider<TService, TImplementation>(
        Func<TImplementation> factory,
        Type[] dependencyTypes,
        Func<TImplementation, Task>? initializeAsync,
        Action<TImplementation>? dispose,
        Func<TImplementation, Task>? disposeAsync)
        where TImplementation : TService;

    public static TService Resolve<TService>();
}
```

`__SingletonDIHost__` адаптирует typed delegates в процессный `ServiceRegistry`; reflection для обнаружения, создания или вызова сервисов не используется.

---

## 3. Диагностика

Все descriptors хранятся в `DiagnosticDescriptors` и создаются через единую фабрику.

| ID | Уровень | Условие |
|---|---|---|
| `DM0001` | Error | Несколько `[SingletonDIProvide]` используют одинаковый custom `PropertyName` |
| `DM0002` | Error | `[SingletonDIProvide]` применён к abstract class |
| `DM0003` | Error | Custom `PropertyName` конфликтует с generated property name |
| `DM0004` | Error | Provider не имеет public parameterless constructor |
| `DM0005` | Error | `InitializeAsync` недоступен из сгенерированного provider module |
| `DM0006` | Error | Зависимость consumer-а не является provider-ом или допустимым interface/abstract contract |
| `DM0007` | Error | `[SingletonDIConsume]`-тип не объявлен `partial` |
| `DM0008` | Error | Consumer ссылается на сам себя |
| `DM0009` | Error | Полный граф содержит циклическую зависимость |
| `DM0010` | Error | Один тип указан несколько раз в `[SingletonDIConsume]` |
| `DM0012` | Error | `InitializeAsync` объявлен `static` |
| `DM0013` | Error | `PropertyName` не является допустимым C# identifier |
| `DM0014` | Error | `PropertyName` является зарезервированным C# keyword |
| `DM0015` | Error | Provider является generic type |
| `DM0016` | Error | `ServiceType` не является reference type, назначаемым provider-у |
| `DM0017` | Error | Executable без root имеет consumer-зависимость, не сопоставленную в локальном service map; это включает `IsContract=true` |
| `DM0018` | Error | Для зависимости в полном графе composition root нет provider-а |
| `DM0019` | Error | Несколько provider-ов отображаются на один service key или одно полное имя связано с несколькими identity |
| `DM0020` | Error | Во внешней provider-сборке отсутствует `SingletonDIProviderModuleAttribute` |

`DM0017` применяется только к executable-проекту без composition root, когда локальный consumer зависит от service key, отсутствующего в локальном service map. Это может быть `IsContract=true` interface/abstract dependency, объявленный в текущей сборке, а не только во внешней; concrete service из другой сборки также может потребовать root. Library consumer компилируется без root, а его зависимости проверяет executable composition root. Одно-проектный executable, все consumer-зависимости которого сопоставлены локальными providers, также не требует свойства.

`DM0018` проверяет полный root graph: отсутствующий ключ может прийти из `[SingletonDIConsume]` локального или внешнего provider-а, referenced consumer-а либо consumer-а самого root-проекта. Для каждой отсутствующей identity выдаётся одна диагностика с source location, когда она доступна.

Идентичность типа включает полное metadata name, assembly identity и рекурсивную canonical identity. Одинаковые полные имена из разных сборок не объединяются; повторные ссылки на одну provider-сборку дедуплицируются. `ServiceTypeResolver` также создаёт `DM0019`, если одно полное имя оказывается связано с несколькими полными identity, например локальный `App.Service` и внешний `App.Service`. В диагностике сохраняются assembly identities, а не одно текстовое имя. Для внешних типов используется `Location.None`, если source location недоступен.

---

## 4. Immutable-модели

Модели остаются `readonly record struct`/`readonly record class` и используются в Incremental API без мутабельного разделяемого состояния.

### `ServiceTypeIdentity`

```text
FullyQualifiedName: string
AssemblyIdentity: string
CanonicalIdentity: string
```

`CanonicalIdentity` рекурсивно учитывает constructed type arguments и containing types, чтобы service key совпадал с CLR identity, а не только с текстовым именем.

### `ServiceReferenceModel`

```text
FullyQualifiedName: string
ShortName: string
Namespace: string
PropertyName: string?
IsContract: bool
Identity: ServiceTypeIdentity?
```

`PropertyName` доступен только для visible concrete provider. Для любой `IsContract=true` зависимости свойство равно `null`, а generated name вычисляется из `ShortName`/`Namespace` контракта.

### `ProviderModel`

```text
FullyQualifiedName: string
ShortName: string
Namespace: string
AssemblyIdentity: string
TypeIdentity: ServiceTypeIdentity
HasInitializeAsyncMethod: bool
IsDisposable: bool
IsAsyncDisposable: bool
Dependencies: ImmutableArray<string>
DependencyIdentities: ImmutableArray<ServiceTypeIdentity>
ServiceTypeFullyQualifiedName: string?
ServiceTypeShortName: string?
ServiceTypeNamespace: string?
ServiceTypeIdentity: ServiceTypeIdentity?
PropertyName: string?
Location: Location
PropertyNameLocation: Location?
```

`Dependencies` и `DependencyIdentities` являются service keys, а не только именами локальных provider-типов. `Location.None` обозначает provider, прочитанный из metadata reference.

### `ProviderAssemblyModel`

```text
AssemblyIdentity: string
BootstrapTypeFullyQualifiedName: string
HasModuleMarker: bool
BootstrapTypeIdentity: ServiceTypeIdentity?
```

Одна модель представляет одну внешнюю provider-сборку и выбранный public provider type для `RuntimeHelpers.RunClassConstructor`.

### `ConsumerModel`

```text
FullyQualifiedName: string
ShortName: string
Namespace: string
IsPartial: bool
Dependencies: ImmutableArray<ServiceReferenceModel>
```

### `CombinedModel`

```text
Providers: ImmutableArray<ProviderModel>
Consumers: ImmutableArray<ConsumerModel>
TopologicalOrder: ImmutableArray<string>
HasCircularDependency: bool
```

### `ServiceTypeMapResult`

```text
Map: ImmutableDictionary<string, string>
IdentityMap: ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity>
Conflicts: ImmutableArray<ServiceTypeConflict>
```

`ServiceTypeResolver.BuildServiceTypeMap` добавляет concrete key каждого provider-а и его `ServiceType`, если он задан. Concrete key и contract key одного provider-а указывают на одну provider identity. Конфликты не перезаписываются молча: одинаковые полные имена не объединяются, а один полный source name, связанный с несколькими canonical identities, создаёт отдельную неоднозначность для `DM0019`.

---

## 5. Incremental pipeline

Генератор регистрирует три независимые ветви:

```text
AnalyzerConfigOptionsProvider
  → ReadGeneratorOptions()
  → build_property.SingletonDICompositionRoot

Provider syntax
  → ForAttributeWithMetadataName(SingletonDIProvideAttribute)
  → ProviderValidator
  → ProviderCandidate
  → Collect()

Consumer syntax
  → ForAttributeWithMetadataName(SingletonDIConsumeAttribute)
  → TypeDeclarationSyntax
  → Collect()

ProviderModuleOutput
  → локальные provider-ы + options
  → ProviderModuleEmitter
  → выполняется только для не-root проекта

CompositionRootOutput
  → provider candidates + consumer declarations + Compilation + options
  → выполняется только при SingletonDICompositionRoot=true
  → ProviderModuleEmitter для root

ConsumerOutput
  → consumer declarations + Compilation + provider candidates + options
  → ConsumerEmitter
```

`ProviderModuleOutput`, `CompositionRootOutput` и `ConsumerOutput` имеют отдельные incremental tracking stages, поэтому локальная генерация consumers не зависит от внешнего reference scan.

### Composition-root flow

1. Получить локальные provider-ы из source syntax.
2. Если `build_property.SingletonDICompositionRoot` не равен `true` без учёта регистра, завершить root branch.
3. Рекурсивно обойти `Compilation.References` и `IAssemblySymbol.Module.ReferencedAssemblySymbols`.
4. Дедуплицировать сборки по assembly identity.
5. Импортировать только public `[SingletonDIProvide]`-типы из внешних сборок.
6. Собрать `ServiceType`, dependency service keys и lifecycle metadata из symbols.
7. Собрать dependency keys из `[SingletonDIConsume]` во внешних consumer-сборках.
8. Проверить marker, `ServiceType`, полный service map, отсутствующие ключи, конфликты, property names и циклы.
9. Передать локальные providers и дедуплицированные внешние provider-сборки в `ProviderModuleEmitter`.

`ProjectReference` и `PackageReference` обрабатываются одинаково: оба представлены metadata references. Старый provider-пакет без marker обнаруживается как `DM0020` и не загружается.

---

## 6. Граф зависимостей

### Compile-time validation

`TopologicalSorter.SortByLevels` строит рёбра по полным `ServiceTypeIdentity`. Concrete provider dependencies и contract dependencies разрешаются через `ServiceTypeResolver.IdentityMap`. При цикле root сообщает `DM0009` с assembly-qualified участниками и не генерирует невалидный root module.

Локальная генерация использует тот же service-key resolver, поэтому single-project и cross-assembly проекты имеют одинаковые правила порядка.

### Runtime graph

`ServiceRegistry` хранит typed registrations, а `ServiceGraph` строит уровни по `Type` keys:

1. Проверяет наличие каждого dependency key.
2. Проверяет отсутствие циклов.
3. Создаёт все экземпляры по уровням: сначала зависимости, затем зависящие от них компоненты; создание всех экземпляров завершается до запуска любого initializer.
4. После создания экземпляров запускает async initializers одного уровня через `Task.WhenAll`.
5. Переходит к следующему уровню только после успешного завершения initializers текущего уровня.
6. При освобождении обрабатывает уровни в обратном порядке.

Concrete и contract registrations являются двумя ключами одной `ProviderRegistration`, поэтому `Resolve<DatabaseService>()` и `Resolve<IDatabaseService>()` возвращают один объект, который освобождается один раз.

---

## 7. Генерируемый код

### Provider module

`ProviderModuleEmitter` создаёт один файл в `SingletonDI.Generated` с internal static class `__SingletonDIProviderModule__`.

```csharp
[assembly: global::SingletonDI.Attributes.SingletonDIProviderModuleAttribute]

namespace SingletonDI.Generated
{
    internal static class __SingletonDIProviderModule__
    {
        [global::System.Runtime.CompilerServices.ModuleInitializerAttribute]
        internal static void Initialize()
        {
        }
    }
}
```

Фактический generated body:

- вызывает `RuntimeHelpers.RunClassConstructor` для каждой внешней provider-сборки, если текущий проект является root;
- вызывает `__SingletonDIHost__.RegisterProvider<TService, TImplementation>` для локальных provider-ов;
- передаёт factory `new T()`;
- передаёт `Type[]` dependency service keys;
- адаптирует `Task`/`ValueTask` initializer к `Func<T, Task>`;
- передаёт sync/async disposal delegates;
- при наличии обоих интерфейсов выбирает `IAsyncDisposable`.

Assembly marker генерируется только при наличии локальных provider-ов. Для target framework без `ModuleInitializerAttribute` emitter выдаёт guarded compatibility type под `#if !NET5_0_OR_GREATER`.

### Consumer partial

`ConsumerEmitter` добавляет одно private static property на dependency:

```csharp
partial class Repository
{
    private static global::Shared.Contracts.IDatabaseService IDatabaseServiceInstance
    {
        get
        {
            return global::SingletonDI.Generated.__SingletonDIHost__
                .Resolve<global::Shared.Contracts.IDatabaseService>();
        }
    }
}
```

Для visible concrete provider применяется provider `PropertyName`. Для любой `IsContract=true` зависимости имя вычисляется только из contract type и dependency set конкретного consumer-а. При конфликте коротких имён используется namespace-qualified name. Добавление несвязанного provider-а в root не меняет API уже собранной библиотеки.

Generator больше не создаёт per-assembly `__SingletonDIContainer__`, public `SingletonDIInitializer`, lifetime container или `ExceptionHelper`. Runtime host и initializer являются частью `SingletonDI.Attributes`/NuGet-пакета `SingletonDI`.

---

## 8. Runtime lifecycle и потокобезопасность

`SingletonDIInitializer` владеет одним process-wide `ServiceRegistry` и синхронизирует доступ через lock.

- Параллельные вызовы `InitializeAsync` используют одну базовую cached task. При `registerShutdownHandlers=true` каждый вызов во время initialization возвращает собственное продолжение, которое ждёт общую task и затем регистрирует handlers; при `false` возвращается сама общая task.
- Повторный вызов после успешной инициализации является no-op; при `true` handlers регистрируются synchronously.
- После ошибки cached initialization task сбрасывается, разрешая retry следующим вызовом.
- Поздняя регистрация provider-а после начала initialization отклоняется.
- Обычный доступ к `Resolve<T>` до успешной инициализации и вне активного initialization context выбрасывает `InvalidOperationException` с указанием initializer. В активном context provider factory/constructor может разрешать уже созданные зависимости; SampleApp использует этот путь в конструкторах provider-ов.
- `DisposeAsync` ожидает незавершённую инициализацию, освобождает уровни в обратном порядке и выполняется не более одного раза для одновременных вызовов.
- При реализации обоих disposal-интерфейсов выбирается `DisposeAsync`.
- `registerShutdownHandlers` сохраняет регистрацию `ProcessExit`, `Console.CancelKeyPress` и POSIX signal handlers на `net8.0`; fallback для `netstandard2.0` не меняет публичный API.
- После успешного `DisposeAsync` процессный registry можно инициализировать повторно теми же зарегистрированными provider-ами.

Независимые composition roots в одном процессе не поддерживаются, потому что runtime registry не разделён по root.

---

## 9. Ограничения и совместимость

- Поддерживаются только singleton lifecycle; Scoped и Transient отсутствуют.
- Провайдером может быть только concrete `class` с public parameterless constructor.
- Generic provider types не поддерживаются.
- Один provider объявляет не более одного `ServiceType`.
- Несколько contract types для одного provider-а не поддерживаются.
- Composition root импортирует только public provider types из внешних сборок.
- Для library-to-app зависимости нужен общий контракт из нижнеуровневой сборки, иначе возникнет циклическая project reference.
- ProjectReference и PackageReference поддерживаются одинаково при наличии совместимого generated provider module.
- Один consumer может ссылаться на concrete provider type или interface/abstract contract, если composition root подтверждает ровно одно сопоставление.
- Process-wide state не допускает независимые composition roots в одном процессе.
- Single-project сценарий не требует `SingletonDICompositionRoot`.

---

## 10. Порядок реализации

1. **Публичный protocol и runtime** — `ServiceType`, assembly marker, hidden host, registry, graph, initializer и shutdown handling.
2. **Immutable models и service identities** — provider/consumer models, `ServiceTypeIdentity`, `ServiceReferenceModel`, `ServiceTypeResolver`.
3. **Локальная генерация** — provider module initializer, typed registrations и runtime-backed consumer properties.
4. **Cross-project root** — рекурсивный metadata reference scan, referenced consumer collection, bootstrap внешних модулей и диагностика `DM0016`–`DM0020`.
5. **Интеграционные fixture-проекты** — `Shared.Contracts`, `ConsumerLibrary`, `RootApp`; identity, lifecycle и disposal assertions.
6. **Публичная документация** — английский и русский README, архитектурный `Plan.md`.
7. **Финальная проверка** — полный build/test matrix, fixture run, sample run и проверка удалённых per-assembly путей.

---

## 11. Структура решения и target frameworks

- `SingletonDI.Attributes` targets `netstandard2.0;net8.0` и сохраняет package ID `SingletonDI`.
- `SingletonDI.Generator` targets `netstandard2.0` и использует Roslyn Incremental API.
- `SingletonDI.Refactoring` остаётся analyzer/code-fix проектом.
- `SingletonDI.Tests` targets `net8.0;net9.0;net10.0`.
- Inter-project fixture targets `net8.0` и проверяет реальные `ProjectReference` boundaries.
- Public lifecycle API не зависит от версии host executable framework.

Runtime dependencies assembly:

- `System.Threading.Tasks.Extensions` 4.6.0;
- `Microsoft.Bcl.AsyncInterfaces` 9.0.0.

Производительность сохраняется за счёт `IIncrementalGenerator`, immutable/record models, отдельных provider/root/consumer stages, дедупликации по assembly identity и отсутствия runtime reflection.

---

## 12. Итоговые пользовательские потоки

### Single-project

```csharp
[SingletonDIProvide]
public sealed class DatabaseService
{
}

[SingletonDIConsume(typeof(DatabaseService))]
public partial class Repository
{
    public DatabaseService GetService() => DatabaseServiceInstance;
}

await SingletonDIInitializer.InitializeAsync();
var repository = new Repository();
repository.GetService();
```

### Cross-project contract

```xml
<PropertyGroup>
  <SingletonDICompositionRoot>true</SingletonDICompositionRoot>
</PropertyGroup>
```

```csharp
namespace Shared.Contracts;

public interface IDatabaseService
{
}

[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
    public IDatabaseService GetService() => IDatabaseServiceInstance;
}

[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService
{
}
```

Composition root регистрирует реализацию, bootstrap-ит внешний provider module, после чего `SingletonDIInitializer.InitializeAsync()` создаёт один процессный граф. Library consumer получает тот же объект по контракту, а root может получить его также по concrete type.

---

## Подтверждённые требования

- Один process-wide runtime registry и один экземпляр каждого зарегистрированного provider-а.
- Public providers из транзитивных `ProjectReference` и `PackageReference` сборок.
- Явный opt-in composition root через `SingletonDICompositionRoot=true` только для межпроектного executable root.
- Shared contract-based consumers без обратной ссылки на App implementation.
- Concrete и contract keys одного provider-а разрешают один объект.
- Identity-aware диагностика с assembly-qualified names.
- `DM0016`–`DM0020` для contract/root/provider protocol ошибок.
- Async initialization через `Task` или `ValueTask`: все экземпляры создаются по уровням, initializers выполняются параллельно внутри уровня.
- Reverse-level disposal, async disposal preference и idempotent `DisposeAsync`.
- Failed initialization оставляет возможность retry.
- Single-project compatibility без composition-root property.
- `netstandard2.0;net8.0` runtime assets и test matrix `net8.0;net9.0;net10.0`.
- Generic providers и несколько contract types на provider не поддерживаются.
