# Межпроектные singleton-провайдеры и потребители

**Дата:** 2026-09-23
**Статус:** дизайн утверждён

## Контекст

Текущий генератор анализирует только объявления исходного кода текущей компиляции. Каждый проект с `[SingletonDIProvide]` получает собственный сгенерированный внутренний контейнер и `SingletonDIInitializer`. Поэтому провайдер из другого проекта нельзя использовать в общем графе, а consumer не может обратиться к контейнеру composition root.

Цель изменения — разрешить App-проекту агрегировать провайдеров из текущего проекта и всех транзитивных ссылок, включая `ProjectReference` и `PackageReference`, и разрешить consumer в любом проекте получать доступ к общему экземпляру.

Одного реестра в runtime недостаточно для сценария, в котором `Lib` использует провайдер, объявленный в `App`: `Lib` не может ссылаться на тип из `App` без циклической зависимости проектов. Поэтому межпроектный доступ строится на общем контракте из нижнеуровневой сборки.

## Цели

- Один процессный контейнер и один экземпляр каждого зарегистрированного провайдера.
- Автоматический рекурсивный обход всех сборок, доступных composition root.
- Поддержка `[SingletonDIConsume]` в любом проекте.
- Возможность объявить интерфейсный контракт в отдельной общей сборке, а реализацию — в App.
- Сохранение текущего single-project сценария без дополнительной настройки.
- Сохранение пользовательского API `SingletonDIInitializer.InitializeAsync` и `SingletonDIInitializer.DisposeAsync`.
- Сохранение типизированной генерации без runtime reflection.

## Не входит в задачу

- Независимость от типов без общего контракта, строковые ключи и `dynamic`-доступ.
- Несколько независимых composition root в одном процессе.
- Несколько contract types на один provider в первой версии.
- Изменение правил инициализации `InitializeAsync` и освобождения `DisposeAsync`, кроме переноса реализации в runtime.

## Архитектурные решения

### Роли проектов

`App.csproj` явно opt-in-ом становится composition root:

```xml
<PropertyGroup>
  <SingletonDICompositionRoot>true</SingletonDICompositionRoot>
</PropertyGroup>
```

Генератор читает значение через `build_property.SingletonDICompositionRoot`. Значение `true` не зависит от регистра.

Проекты с локальными провайдерами всегда генерируют локальный registration module initializer. Поэтому существующий single-project код продолжает работать без флага. В App с флагом generated module initializer дополнительно загружает registration module каждой найденной provider-сборки.

В библиотечном consumer-проекте отдельный composition root не требуется. Он генерирует только обращения к runtime-реестру.

### Runtime-контейнер

Фактическое состояние переносится из генерируемого `__SingletonDIContainer__` в runtime-часть сборки `SingletonDI.Attributes`, которая уже входит в NuGet-пакет `SingletonDI`.

Runtime содержит:

- единый статический реестр регистраций;
- типизированные registration adapters без reflection;
- построение графа и топологическую сортировку;
- потокобезопасную инициализацию с повторным вызовом;
- освобождение в обратном порядке;
- обработчики shutdown, сохраняющие текущую семантику `registerShutdownHandlers`.

Публичный класс `SingletonDI.Generated.SingletonDIInitializer` переносится из генерируемого исходного файла в runtime-сборку с теми же публичными сигнатурами:

- `InitializeAsync(bool registerShutdownHandlers = true)`;
- `DisposeAsync()`.

Для генерируемого кода в runtime добавляется скрытый публичный host-контракт `SingletonDI.Generated.__SingletonDIHost__` с регистрацией и разрешением сервисов. Его публичный протокол имеет следующий смысл:

```csharp
public static void RegisterProvider<TService, TImplementation>(
    Func<TImplementation> factory,
    Type[] dependencyTypes,
    Func<TImplementation, Task>? initializeAsync,
    Action<TImplementation>? dispose,
    Func<TImplementation, Task>? disposeAsync);

public static TService Resolve<TService>();
```

Он помечается `EditorBrowsable(EditorBrowsableState.Never)` и не является пользовательским API. Публичные элементы runtime-контракта документируются XML-документацией.

Runtime регистрирует провайдер под двумя ключами, если задан `ServiceType`:

1. по типу реализации;
2. по типу контракта.

Оба ключи ссылаются на один объект. Повторная инициализация возвращает тот же объект, а `DisposeAsync` освобождает его один раз.

### Регистрация провайдеров

Каждый provider-проект генерирует внутренний module initializer, который:

- регистрирует фабрику `new T()`;
- передаёт зависимости из `[SingletonDIConsume]`;
- передаёт делегаты `InitializeAsync`, `Dispose` и `DisposeAsync`;
- регистрирует concrete type и, при наличии, `ServiceType`.

В provider-проекте генератор может обращаться к `internal InitializeAsync`, потому что делегаты создаются внутри его compilation.

Provider-сборка помечается сгенерированным assembly marker `SingletonDI.Attributes.SingletonDIProviderModuleAttribute`. Атрибут имеет `AttributeUsage(AttributeTargets.Assembly)` и XML-документацию. Marker нужен, чтобы composition root мог отличить сборку, собранную новым протоколом, от старой сборки с атрибутами, но без registration module.

### Сканирование ссылок

Composition root анализирует `Compilation.References` рекурсивно. Для каждого `IAssemblySymbol` он:

- переходит по namespace и вложенным типам;
- находит `[SingletonDIProvide]`;
- оставляет только public-типы, доступные из App; internal/private provider-ы не импортируются;
- собирает `ServiceType`, зависимости и признаки lifecycle;
- дедуплицирует сборки по assembly identity и типы по symbol identity.

ProjectReference и PackageReference обрабатываются одинаково, поскольку оба представлены metadata references. В generated root module initializer для каждой внешней provider-сборки вызывается `RuntimeHelpers.RunClassConstructor` через `TypeHandle` public-провайдера. Сборка текущей compilation повторно не загружается: её локальный registration module initializer выполняется в том же module. Это гарантирует выполнение внешнего module initializer до вызова `InitializeAsync` и не использует сканирование сборок reflection-ом.

Если provider-сборка не содержит нового marker, root сообщает диагностику несовместимости.

## Контракты и имена

### Изменение `SingletonDIProvideAttribute`

Добавляется публичное свойство:

```csharp
public Type? ServiceType { get; set; }
```

Старый конструктор с `propertyName` сохраняется. `ServiceType` получает XML-документацию и задаёт ключ контракта, под которым провайдер будет доступен consumer-ам, не имеющим прямой ссылки на реализацию. Использование:

```csharp
[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService
{
}
```

Если `ServiceType` не задан, провайдер регистрируется только по concrete type, как сейчас.

В первой версии один provider объявляет не более одного `ServiceType`. Тип контракта должен быть reference type, быть доступным из provider-сборки и быть присваиваемым типу провайдера.

### Изменение обработки `SingletonDIConsume`

Аргумент `typeof(...)` может быть:

- concrete provider type, видимым в текущем или доступном reference;
- внешним contract type: интерфейсом или abstract class, для которого реализация появится в composition root.

Если аргумент является concrete class, который не является provider-ом, или не является допустимым внешним contract type, применяется `DM0006`. Для внешнего contract type проверка наличия provider-а откладывается до composition root.

Для контрактного consumer-а имя свойства вычисляется из типа контракта. `PropertyName` provider-а используется для concrete-доступа и не влияет на имя свойства в библиотечном consumer-е, поскольку provider-астройка недоступна без циклической ссылки.

Имена разрешаются только среди зависимостей конкретного consumer-а. Добавление несвязанного провайдера в App не изменяет уже скомпилированный API библиотеки.

При наличии нескольких зависимостей с одинаковым коротким именем используется существующий namespace-qualified алгоритм.

### Пример межпроектной композиции

```csharp
namespace Shared.Contracts
{
    public interface IDatabaseService
    {
    }
}

namespace Lib
{
    [SingletonDIConsume(typeof(IDatabaseService))]
    public partial class Repository
    {
        public void UseService()
        {
            var service = IDatabaseServiceInstance;
        }
    }
}

namespace App
{
    [SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
    public sealed class DatabaseService : IDatabaseService
    {
    }
}
```

`Shared.Contracts` не ссылается на `Lib` или `App`; `Lib` и `App` ссылаются на него. App является единственным владельцем composition graph.

## Генерация consumers

`ConsumerEmitter` перестаёт обращаться к локальному `__SingletonDIContainer__`. Сгенерированное свойство получает значение через runtime host:

```csharp
return global::SingletonDI.Generated.__SingletonDIHost__.Resolve<TService>();
```

Тип свойства и все проверки partial-типа остаются compile-time. Валидатор различает локальный concrete provider и внешний contract. Отсутствие реализации внешнего контракта проверяется root-ом, а не библиотекой, которая не имеет обратной ссылки на App.

## Диагностика

Существующие диагностики сохраняют смысл:

- `DM0001` — конфликт custom property name;
- `DM0003` — конфликт custom и generated property name;
- `DM0004` — отсутствует public parameterless constructor;
- `DM0006` — consumer ссылается на неподдерживаемый тип, который не является ни provider-ом, ни допустимым внешним contract type;
- `DM0007` — consumer не partial;
- `DM0009` — цикл в полном графе.

Добавляются отдельные диагностики для:

- несовместимой provider-сборки без registration marker;
- отсутствующего composition root: ошибка выдаётся executable-проекту с локальным consumer-ом внешнего provider/contract, если `SingletonDICompositionRoot` не задан; library-проекты не получают эту диагностику;
- отсутствующего provider для внешнего contract;
- нескольких providers для одного contract;
- неприсваиваемого `ServiceType`.

Диагностика внешних символов использует assembly-qualified names и общее местоположение, если source location недоступен. Локальные ошибки сохраняют точную source location.

Root дополнительно проверяет полный граф, property-name conflicts и cycles после объединения локальных и внешних providers. Дублирующиеся ссылки на одну сборку не считаются конфликтом.

## Ошибки во время выполнения

- `Resolve<T>()` до успешной `InitializeAsync` выбрасывает `InvalidOperationException` с указанием initializer.
- Неизвестный service key выбрасывает `InvalidOperationException` с именем типа.
- Повторная регистрация одного service key из разных provider-сборок обнаруживается как ошибка протокола/графа.
- Ошибка инициализации сбрасывает cached task и разрешает retry, как сейчас.
- `DisposeAsync` идемпотентен.
- Поздняя регистрация нового provider после начала инициализации отклоняется, чтобы не менять частично инициализированный граф.

## Совместимость

- Один provider в одном project продолжает работать без изменения атрибутов.
- Старый concrete `[SingletonDIProvide]` и `[SingletonDIConsume(typeof(ConcreteProvider))]` сохраняются.
- `SingletonDIInitializer` сохраняет namespace и сигнатуры методов.
- Generated consumer property names сохраняются для single-project сценария.
- Проекты без composition-root flag не получают cross-project root diagnostics, если они являются библиотеками.
- Два независимых composition root в одном процессе не поддерживаются: runtime-реестр является процессным и не разделяет состояние по roots.

## Пакеты и целевые сборки

Runtime остаётся частью существующего NuGet-пакета `SingletonDI`; новый пакет не вводится. Общая `Contracts` сборка является пользовательским проектом и не входит в пакет.

Реализация должна сохранить текущую матрицу тестовых target frameworks (`net8.0`, `net9.0`, `net10.0`) и текущую совместимость атрибутной сборки. Если для shutdown API требуется разделение реализаций, оно выполняется внутри runtime без изменения публичного API.

## План тестирования

### Unit-тесты генератора

Проверяются:

- чтение `ServiceType` из атрибута;
- рекурсивное сканирование metadata references;
- фильтрация non-public providers;
- дедупликация сборок и типов;
- генерация provider registration module;
- генерация root module с `RunClassConstructor`;
- генерация contract consumer без обратной ссылки на provider;
- сохранение concrete consumer behavior;
- диагностика отсутствующего/множественного contract provider и несовместимой сборки.

### Межпроектная интеграционная fixture

Добавляется цепочка проектов:

1. `Shared.Contracts` — интерфейс `IDatabaseService`;
2. `Lib` — consumer интерфейса, ссылающийся только на `Shared.Contracts`;
3. `App` — provider `DatabaseService`, ссылающийся на `Lib` и `Shared.Contracts`, с `SingletonDICompositionRoot=true`.

Fixture проверяет:

- успешную компиляцию всех проектов;
- один и тот же объект через concrete type и contract;
- инициализацию до обращения из `Lib`;
- правильный порядок async initialization;
- корректный reverse-order disposal;
- ошибку до initialization;
- single-project compatibility.

### Команды проверки

- `dotnet build`;
- `dotnet test`;
- `dotnet run --project src/SingletonDI.SampleApp`.

## Критерии готовности

- App с флагом компилируется при провайдерах из транзитивных сборок.
- Library consumer компилируется без ссылки на App implementation.
- После `InitializeAsync` consumer из library получает singleton, зарегистрированный в App.
- Повторный доступ возвращает тот же объект.
- Cross-assembly цикл, конфликт contract mapping и несовместимая provider-сборка дают диагностику до запуска.
- Старый single-project тестовый сценарий и текущие diagnostic tests проходят без регрессий.
