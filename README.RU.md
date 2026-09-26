# SingletonDI

**SingletonDI** — это библиотека для декларативного управления singleton-зависимостями в .NET. Инструмент заменяет ручную регистрацию сервисов и настройку DI-контейнеров на систему атрибутов. Библиотека автоматически формирует граф зависимостей, управляет порядком создания объектов, их асинхронной инициализацией и корректным освобождением ресурсов. Строгий контроль на этапе компиляции гарантирует отсутствие типичных ошибок связывания во время выполнения приложения.
## Возможности

- **Декларативное описание зависимостей** — внедрение и предоставление сервисов настраивается через атрибуты `[SingletonDIProvide]` и `[SingletonDIConsume]` непосредственно в коде классов, без централизованных модулей регистрации.
- **Отсутствие Reflection в runtime** — весь код для инстанцирования и внедрения генерируется на этапе сборки, что обеспечивает скорость выполнения на уровне прямого вызова конструкторов.
- **Асинхронная инициализация** — поддержка методов `Task` или `ValueTask` `InitializeAsync()` для сервисов, требующих I/O операций при старте (подключение к БД, чтение конфигураций, сетевые запросы).
- **Параллельный запуск** — сервисы, находящиеся на одном уровне графа зависимостей (не зависящие друг от друга), инициализируются параллельно для минимизации времени старта приложения.
- **Автоматическое разрешение зависимостей** — встроенная топологическая сортировка сначала создаёт экземпляры зависимостей, а `InitializeAsync` каждого уровня запускается только после завершения всех нижних уровней.
- **Compile-time валидация графа** — выявление циклических зависимостей, отсутствующих провайдеров и конфликтов имен происходит на этапе компиляции, предотвращая падения (runtime errors) при запуске приложения.
- **Управление жизненным циклом** — автоматическое отслеживание объектов, реализующих `IDisposable` и `IAsyncDisposable`, с освобождением уровней графа в обратном порядке при завершении работы.
- **Композиция между проектами** — composition root объединяет публичных провайдеров из транзитивных сборок, подключённых через `ProjectReference` или `PackageReference`; общие контракты позволяют библиотекам использовать реализации, зарегистрированные приложением.
- **Потокобезопасность** — сгенерированный код обеспечивает безопасный доступ к экземплярам синглтонов при работе в многопоточной среде.

## Установка

NuGet:

|Package|Download|
|-|-|
|SingletonDI|[![NuGet](https://img.shields.io/nuget/v/SingletonDI.svg)](https://www.nuget.org/packages/SingletonDI) [![NuGet](https://img.shields.io/nuget/dt/SingletonDI.svg)](https://www.nuget.org/packages/SingletonDI)

```bash
dotnet add package SingletonDI
```

## Быстрый старт

### 1. Объявление singleton-провайдеров

```csharp
using SingletonDI.Attributes;

// Простой singleton с синхронной инициализацией (через конструктор)
[SingletonDIProvide]
public class DatabaseService
{
    public string ConnectionString { get; private set; }

    public DatabaseService()
    {
        // Код инициализации выполняется при создании экземпляра
        ConnectionString = "Server=localhost;Database=MyApp;Connected=true";
    }
}

// Singleton с асинхронной инициализацией
[SingletonDIProvide]
public class UserService
{
    public string UserName { get; private set; }

    public async Task InitializeAsync()
    {
        // Асинхронная загрузка данных
        await LoadUserDataAsync();
        UserName = "LoadedUser";
    }
}
```

### 2. Объявление потребителей

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController
{
    public void ProcessOrder()
    {
        // Доступ к синглтонам через сгенерированные свойства
        Console.WriteLine($"Database: {DatabaseServiceInstance.ConnectionString}");
        Console.WriteLine($"User: {UserServiceInstance.UserName}");
    }
}
```

### 3. Инициализация при старте приложения

```csharp
using SingletonDI.Generated;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Инициализация всех синглтонов с автоматической регистрацией shutdown-обработчиков
        await SingletonDIInitializer.InitializeAsync();

        // Теперь можно использовать потребителей
        var controller = new OrderController();
        controller.ProcessOrder();
    }
}
```

## Композиция между проектами

Библиотека может использовать провайдер, реализованный исполняемым приложением, без прямой ссылки на его реализацию. Поместите контракт в нижнеуровневый проект Contracts, на который ссылаются и библиотека, и приложение.

### Shared.Contracts/IDatabaseService.cs

```csharp
namespace Shared.Contracts;

public interface IDatabaseService
{
}
```

### ConsumerLibrary/Repository.cs

```csharp
using Shared.Contracts;
using SingletonDI.Attributes;

namespace ConsumerLibrary;

[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
    public IDatabaseService GetService()
    {
        return IDatabaseServiceInstance;
    }
}
```

Среди проектов решения библиотека-потребитель ссылается только на `Shared.Contracts`; она также ссылается на SingletonDI, но не на приложение или `DatabaseService`. Сгенерированное свойство `IDatabaseServiceInstance` разрешает общий контракт после инициализации общего реестра приложением.

### RootApp.csproj

Исполняемый проект, владеющий полным графом зависимостей, явно opt-in-ом становится composition root:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <SingletonDICompositionRoot>true</SingletonDICompositionRoot>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="SingletonDICompositionRoot" />
</ItemGroup>
```

### RootApp/DatabaseService.cs

```csharp
using Shared.Contracts;
using SingletonDI.Attributes;

namespace SingletonDI.InterProjectFixtures.RootApp;

[SingletonDIProvide(ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService
{
}
```

При установленном свойстве root рекурсивно анализирует свои metadata references, импортирует публичных провайдеров, проверяет полный граф сервисов и запускает registration module каждой подключённой provider-сборки до вызова `InitializeAsync()`. `ProjectReference` и `PackageReference` поддерживаются одинаково. Непубличные провайдеры из подключённых сборок импортируются, если объявляющая сборка называет root другом через `InternalsVisibleTo`, и пропускаются иначе; вложенный непубличный провайдер пропускается в обоих случаях. Provider-пакет без сгенерированного assembly marker `SingletonDIProviderModuleAttribute` отклоняется с `DM0020`.

Приложение регистрирует `DatabaseService` по двум ключам: concrete type и `IDatabaseService`. Оба ключа разрешают один и тот же объект. Имя свойства contract-потребителя вычисляется из типа контракта (`IDatabaseServiceInstance`), а не из `PropertyName` провайдера: библиотека, видящая только контракт, не может анализировать объявление провайдера в приложении. В самом приложении могут одновременно использоваться concrete- и contract-доступ.

Одно-проектному приложению `SingletonDICompositionRoot` не требуется.

## Атрибуты

### [SingletonDIProvide]

Маркирует класс как singleton-провайдер. Сгенерированные registration-делегаты создают его экземпляр, а runtime управляет его жизненным циклом.

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    /// <summary>
    /// Опциональное пользовательское имя свойства для доступа к синглтону.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Контракт сервиса, предоставляемый провайдером.
    /// </summary>
    public Type? ServiceType { get; set; }

    public SingletonDIProvideAttribute(string? propertyName = null)
    {
        PropertyName = propertyName;
    }
}
```

**Свойства и параметры:**
- `propertyName` (опционально) — пользовательское имя свойства для concrete-доступа в потребителях. Если не указано, используется имя `{TypeName}Instance`
- `ServiceType` (опционально) — reference type, которому назначается провайдер и который доступен его сборке; провайдер регистрируется по этому контракту и по своему concrete type, причём оба ключа разрешают один и тот же экземпляр

**Требования:**
- Применим только к `class`
- Класс должен иметь публичный конструктор без параметров
- Класс не должен быть `abstract`
- Не наследуется (каждый класс должен быть явно помечен)
- Один провайдер может объявить не более одного `ServiceType`

**Инициализация:**
- Для синхронной инициализации используйте конструктор без параметров
- Для асинхронной инициализации реализуйте метод без параметров `Task InitializeAsync()` или `ValueTask InitializeAsync()`
- Инициализатор связывается по правилам поиска членов C#, поэтому унаследованный от базового типа тоже используется, а перегрузки могут сосуществовать: пригодный метод без параметров побеждает в любом порядке объявления
- Непригодное объявление в *базовом* типе не диагностируется: его не владеет автор провайдера и не может переименовать. Провайдер регистрируется без инициализатора и без диагностики, поэтому член базового типа, похожий на инициализатор, но непригодный, например `protected` или `static`, молча не вызывается. Переопределите его в провайдере, чтобы он учитывался.

**Примеры:**

```csharp
// Синхронная инициализация через конструктор
[SingletonDIProvide]
public class MyService 
{ 
    public MyService()
    {
        // Инициализация
    }
}

// Асинхронная инициализация через ValueTask
[SingletonDIProvide]
public class DataService
{
    public async ValueTask InitializeAsync()
    {
        // Асинхронная инициализация
    }
}
```

Провайдер может предоставить общий контракт, не связывая библиотеку-потребитель со своей реализацией:

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

`ServiceType` должен быть reference type, которому назначается провайдер. Consumer может зависеть от этого контракта без ссылки на сборку реализации; исполняемый composition root проверяет, что контракт сопоставлен ровно одному провайдеру. Имена contract-свойств вычисляются из типа контракта и dependency set конкретного consumer, а не из `PropertyName` провайдера. Если свойство не задано, провайдер регистрируется только по concrete type.

**Имена свойств:**

По умолчанию генератор создаёт свойства с суффиксом `Instance`:

```csharp
[SingletonDIProvide]
public class DatabaseService { }

[SingletonDIConsume(typeof(DatabaseService))]
public partial class OrderService
{
    public void Process()
    {
        // Доступ через DatabaseServiceInstance
        var db = DatabaseServiceInstance;
    }
}
```

С параметром `propertyName` можно указать пользовательское имя:

```csharp
[SingletonDIProvide("_db")]
public class DatabaseService { }

[SingletonDIConsume(typeof(DatabaseService))]
public partial class OrderService
{
    public void Process()
    {
        // Доступ через пользовательское имя _db
        var db = _db;
    }
}
```

Для любой contract-зависимости имя генерируется из типа контракта, а пользовательский `PropertyName` провайдера не используется. Это особенно важно для библиотеки-потребителя, которая не может анализировать объявление провайдера:

```csharp
[SingletonDIProvide("db", ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService { }

[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
    public IDatabaseService GetService() => IDatabaseServiceInstance;
}
```

Здесь `IDatabaseServiceInstance` — свойство контракта, а потребитель, видящий concrete provider, может использовать `db`. Если короткие имена зависимостей одного потребителя конфликтуют, применяется существующее разрешение имён через namespace.

### [SingletonDIConsume]

Маркирует класс как потребителя singleton-зависимостей. Генератор создаст свойства для доступа к указанным синглтонам.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, 
                AllowMultiple = false, Inherited = true)]
public sealed class SingletonDIConsumeAttribute : Attribute
{
    public Type[] Dependencies { get; }
    
    public SingletonDIConsumeAttribute(params Type[] dependencies)
    {
        Dependencies = dependencies;
    }
}
```

**Требования:**
- Класс должен быть объявлен как `partial`
- Каждая зависимость должна быть видимым `[SingletonDIProvide]`-типом или поддерживаемым interface/abstract contract; composition root проверяет, что для запрошенного контракта существует ровно один провайдер
- Нельзя указывать сам класс в списке зависимостей (self-reference)
- Нельзя дублировать типы в списке зависимостей
- Наследуется (`Inherited = true`) — производный потребитель читает те же сгенерированные зависимости через базовый тип, не повторяя атрибут; само производное объявление не проверяется и не получает собственных сгенерированных членов, поэтому DM0007, DM0026 и DM0029 к нему не применяются. Провайдерская сторона действительно наследует атрибут для проверки

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }
```

### Разрешение конфликтов имён

При конфликте имён типов (например, `Foo.Bar` и `Baz.Bar`) генерируются имена с префиксом пространства имён.

## Совместимость пакета

Версия `1.1.0` целится в `net10.0`. Упакованные сборки генератора и рефакторинга используют базовую линию Roslyn `4.8` и проверяются на SDK `10.0.401`. Сгенерированный исходный код требует C# 9 или новее; объявления consumer в file-scoped namespace требуют C# 10 или новее.

Пакет размещает сборки генератора и рефакторинга в `analyzers/dotnet/cs`, поэтому они подключаются автоматически через ссылку на пакет `SingletonDI`.

## API SingletonDIInitializer

Runtime-часть SingletonDI предоставляет общепроцессный класс `SingletonDIInitializer`. Сгенерированные provider-модули регистрируются в этом runtime-реестре, а сгенерированные свойства потребителей разрешают сервисы из него.

### InitializeAsync

```csharp
public static Task InitializeAsync(
    bool registerShutdownHandlers = true,
    CancellationToken cancellationToken = default)
```

Инициализирует все синглтоны в правильном порядке (топологическая сортировка по зависимостям).

**Параметры:**
- `registerShutdownHandlers` (по умолчанию `true`):
  - `true` — автоматически регистрирует обработчики shutdown для корректного освобождения ресурсов при завершении приложения
  - `false` — не регистрирует обработчики (для сценариев с ручным управлением lifetime)

  Подписка обработчиков может не удаться на хосте, который её не разрешает. Инициализация при этом
  завершается успешно, контейнер остаётся рабочим, но завершение работы теперь зависит от явного
  вызова `DisposeAsync`. На POSIX-платформе `Ctrl+C` обрабатывается только регистрацией сигнала,
  поэтому одно нажатие запускает одно освобождение.
- `cancellationToken` (по умолчанию `default`): ограничивает ожидание вызывающего и отклоняется сразу,
  если уже отменён, до создания любого провайдера. Он не прерывает работу, которую провайдер уже начал,
  поскольку initializer провайдера не принимает токен.

**Возвращает:** `Task`

Все экземпляры провайдеров создаются по уровням: сначала зависимости, затем зависящие от них компоненты; этот этап завершается до запуска любых методов `InitializeAsync`. Затем initializers провайдеров одного уровня выполняются параллельно. Поддерживаются `Task` и `ValueTask`. Параллельные lifecycle-вызовы сериализуются с disposal: повторная инициализация, запрошенная во время disposal, ждёт его завершения и получает новую задачу, а не предыдущую успешную. При ошибке инициализации все созданные экземпляры освобождаются в обратном порядке зависимостей, cleanup продолжается после отдельных ошибок disposer-ов, состояние очищается, а исходная ошибка инициализации сохраняется для следующей попытки. Если очистка тоже завершилась ошибкой, она прикрепляется к пробрасываемому исключению инициализации в `Data` под ключом `SingletonDI.InitializationCleanupFailure` — это единственное место, где её можно увидеть, поэтому приложение, логирующее только `ex.Message`, её не увидит. Регистрация нового провайдера после начала инициализации запрещается.

Обычный доступ к сгенерированному свойству или разрешение через скрытый host до успешной инициализации и вне активного контекста инициализации выбрасывает `InvalidOperationException`. В активном контексте инициализации provider factory или конструктор может разрешать уже созданные зависимости; SampleApp использует этот паттерн в конструкторах провайдеров.

**Примеры:**

```csharp
// Стандартное использование с автоматической регистрацией shutdown-обработчиков
await SingletonDIInitializer.InitializeAsync();

// Ручное управление lifetime (без автоматических shutdown-обработчиков)
await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

// ... работа с приложением ...

// Явный вызов освобождения ресурсов
await SingletonDIInitializer.DisposeAsync();
```

### DisposeAsync

```csharp
public static ValueTask DisposeAsync(CancellationToken cancellationToken = default)
```

Асинхронно освобождает все инициализированные синглтоны, реализующие `IAsyncDisposable` или `IDisposable`. Метод возвращает `ValueTask`; параллельные и повторные вызовы идемпотентны. Очистка продолжается после ошибки отдельного disposer-а, а ошибка освобождения сообщается после обработки остальных экземпляров. `cancellationToken` ограничивает ожидание незавершённой инициализации, поэтому зависший initializer провайдера не оставит вызывающего ждать вечно. Он не прерывает уже начавшийся disposer, а отменённое таким образом освобождение не выполняется, поэтому контейнер остаётся инициализированным.

**Возвращает:** `ValueTask`

**Порядок освобождения:**
- Уровни графа зависимостей освобождаются в обратном порядке инициализации (LIFO)
- Провайдеры одного уровня освобождаются параллельно
- `DisposeAsync()` используется предпочтительно, если провайдер реализует и `IAsyncDisposable`, и `IDisposable`

**Пример:**

```csharp
public static async Task Main(string[] args)
{
    await SingletonDIInitializer.InitializeAsync();
    
    try
    {
        // Работа приложения
        await RunApplicationAsync();
    }
    finally
    {
        // Явное освобождение ресурсов
        await SingletonDIInitializer.DisposeAsync();
    }
}
```

## Диагностика

Генератор сообщает об ошибках на этапе компиляции:

| ID | Уровень | Описание |
|---|---|---|
| **DM0001** | Error | Duplicate property name |
| **DM0002** | Error | Cannot use [SingletonDIProvide] on abstract class |
| **DM0003** | Error | Consumer property name conflicts with a generated name in the same dependency set |
| **DM0004** | Error | Missing parameterless constructor |
| **DM0005** | Error | InitializeAsync method has inaccessible access modifier |
| **DM0006** | Error | Referenced type is not a provider |
| **DM0007** | Error | Consumer must be partial |
| **DM0008** | Error | Self-reference not allowed |
| **DM0009** | Error | Circular dependency detected |
| **DM0010** | Error | Duplicate type in SingletonDIConsume attribute arguments |
| **DM0012** | Error | InitializeAsync method cannot be static |
| **DM0013** | Error | Invalid property name |
| **DM0014** | Error | Property name is a reserved keyword |
| **DM0015** | Error | Generic types are not supported for singletons |
| **DM0016** | Error | Invalid `ServiceType` |
| **DM0017** | Error | Executable has an unmapped consumer dependency and requires a composition root |
| **DM0018** | Error | No provider for requested service |
| **DM0019** | Error | Multiple providers for service key |
| **DM0020** | Error | Provider module marker is missing |
| **DM0021** | Error | Provider module bootstrap method is missing |
| **DM0022** | Error | Provider or service type is not accessible from generated code |
| **DM0023** | Error | Generic `InitializeAsync` methods are not supported |
| **DM0024** | Error | Open generic dependencies are not supported |
| **DM0025** | Error | Generated consumer property name already exists |
| **DM0026** | Error | A containing consumer type must be partial |
| **DM0027** | Error | Generated source requires C# 9 or later |
| **DM0028** | Error | File-scoped consumers require C# 10 or later |
| **DM0029** | Error | File-local consumer is not supported |
| **DM0030** | Error | Aliased service type is not supported |
| **DM0031** | Error | Consumer type parameter attributes are not supported |
| **DM0032** | Error | Provider required members are not supported |
| **DM0033** | Error | Nullable initializer return type is not supported |
| **DM0034** | Error | InitializeAsync has an unsupported return type |
| **DM0035** | Error | Provider nested in a generic type is not supported |

Идентификаторы сервисов и провайдеров в межпроектной диагностике включают содержащую их сборку. Повторные ссылки на одну сборку дедуплицируются, а одинаковые имена типов из разных сборок остаются разными CLR-типами. `DM0019` также выдаётся для конфликтов локальных `ServiceType`-сопоставлений, не только в composition root, и сообщает все конфликтующие identity провайдеров. Диагностика также сообщает о неоднозначности, когда одно полное имя связано с несколькими identity, например локальный `App.Service` и подключённый `App.Service`; assembly identity сохраняются.

### DM0001: Duplicate property name

Возникает, когда несколько атрибутов `[SingletonDIProvide]`, которые могут попасть в один dependency set consumer, указывают одинаковое значение `propertyName`. Composition root ограничивает проверку провайдерами, реально используемыми consumer-ами; несвязанные провайдеры не блокируют корректный root.

```csharp
[SingletonDIProvide(propertyName: "DbService")]
public class DatabaseService { }

[SingletonDIProvide(propertyName: "DbService")]  // DM0001
public class AnotherDatabaseService { }
```

### DM0002: Cannot use [SingletonDIProvide] on abstract class

Возникает, когда атрибут `[SingletonDIProvide]` применяется к абстрактному классу. Синглтон должен быть конкретным классом, который можно инстанцировать.

```csharp
[SingletonDIProvide]  // DM0002
public abstract class BaseService { }
```

### DM0003: Конфликт имени свойства consumer

Возникает, когда две зависимости одного consumer получают одинаковое имя свойства, включая custom `PropertyName` провайдера и имя contract-зависимости. Диагностика содержит конфликтующее свойство и identity сервиса; генератор применяет namespace-qualified fallback ко второму свойству и сообщает о конфликте, не создавая неоднозначный API.

```csharp
[SingletonDIProvide("IServiceInstance")]
public class Service : IService { }

[SingletonDIConsume(typeof(Service), typeof(IService))]
public partial class Consumer { } // DM0003; contract-зависимость получает fallback-имя
```

### DM0004: Missing parameterless constructor

Возникает, когда класс с `[SingletonDIProvide]` не имеет публичного конструктора без параметров. Генератор требует возможность создать экземпляр через `new()`.

```csharp
[SingletonDIProvide]  // DM0004
public class DatabaseService
{
    // Нет конструктора без параметров, только с параметрами
    public DatabaseService(string connectionString) { }
}
```

### DM0005: InitializeAsync method has inaccessible access modifier

Возникает, когда метод `InitializeAsync` объявлен с недоступным модификатором доступа. Метод должен быть `public`, `internal` или `protected internal`.

```csharp
[SingletonDIProvide]
public class DataService
{
    private Task InitializeAsync() { return Task.CompletedTask; }  // DM0005
}
```

### DM0006: Referenced type is not a provider

Возникает, когда `[SingletonDIConsume]` ссылается на неподдерживаемый тип. Concrete class без `[SingletonDIProvide]` недопустим; interface или abstract class может быть внешним контрактом, реализацию которого проверит composition root.

```csharp
// Класс без атрибута [SingletonDIProvide]
public class SomeService { }

[SingletonDIConsume(typeof(SomeService))]  // DM0006
public partial class Consumer { }
```

### DM0007: Consumer must be partial

Возникает, когда класс с `[SingletonDIConsume]` не объявлен как `partial`. Генератор требует `partial` для добавления свойств.

```csharp
[SingletonDIConsume(typeof(DatabaseService))]  // DM0007
public class OrderController  // Отсутствует ключевое слово partial
{
}
```

### DM0008: Self-reference not allowed

Возникает, когда класс указывает сам себя в `[SingletonDIConsume]`. Это приведёт к бесконечной рекурсии.

```csharp
[SingletonDIProvide]
[SingletonDIConsume(typeof(SelfReferencingService))]  // DM0008
public class SelfReferencingService { }
```

### DM0009: Circular dependency detected

Возникает, когда обнаружена циклическая зависимость между провайдерами. Циклы приводят к невозможности корректной инициализации.

```csharp
[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceB))]  // DM0009: A зависит от B
public partial class ServiceA { }

[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceA))]  // DM0009: B зависит от A
public partial class ServiceB { }
```

### DM0010: Duplicate type in SingletonDIConsume attribute arguments

Возникает, когда один и тот же тип указан несколько раз в `[SingletonDIConsume]`.

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(DatabaseService))]  // DM0010
public partial class OrderController { }
```

### DM0012: InitializeAsync method cannot be static

Возникает, когда метод `InitializeAsync` объявлен как `static`. Метод инициализации должен быть экземплярным.

```csharp
[SingletonDIProvide]
public class DataService
{
    public static Task InitializeAsync() { return Task.CompletedTask; }  // DM0012
}
```

### DM0013: Invalid property name

Возникает, когда имя свойства в `[SingletonDIProvide]` содержит недопустимые символы. Имя должно начинаться с буквы или подчёркивания и содержать только буквы, цифры или подчёркивания.

```csharp
[SingletonDIProvide("invalid-name")]  // DM0013: дефис недопустим
public class DatabaseService { }
```

### DM0014: Property name is a reserved keyword

Возникает, когда имя свойства в `[SingletonDIProvide]` является зарезервированным ключевым словом C#.

```csharp
[SingletonDIProvide("class")]  // DM0014: зарезервированное слово
public class DatabaseService { }
```

### DM0015: Generic types are not supported for singletons

Возникает, когда атрибут `[SingletonDIProvide]` применяется к generic типу. SingletonDI не поддерживает generic типы как синглтоны, так как для каждого generic-параметра потребовался бы отдельный экземпляр. Провайдер, у которого нет собственных параметров типа, но вложенный в generic тип, сообщается диагностикой DM0035, потому что причина выше к нему не относится.

```csharp
[SingletonDIProvide]  // Error DM0015
public class Repository<T>
{
}
```

### DM0016: Invalid ServiceType

Возникает, когда `ServiceType` не является reference type, которому назначается провайдер. Один провайдер может объявить только один `ServiceType`.

```csharp
public interface ICacheService { }

[SingletonDIProvide(ServiceType = typeof(ICacheService))]  // DM0016
public sealed class DatabaseService { }
```

### DM0017: Исполняемый проект имеет несопоставленную зависимость consumer

Возникает, когда исполняемый проект без `SingletonDICompositionRoot=true` содержит зависимость consumer, которая не сопоставлена локальной картой провайдеров. Это включает interface/abstract-контракт, объявленный в текущей сборке, контракт из другой сборки и concrete provider из другой сборки. Библиотеки-потребители не получают `DM0017`; их зависимости проверяет исполняемый composition root.

### DM0018: No provider for requested service

Возникает, когда для любой зависимости в полном графе composition root отсутствует провайдер в service map. Это включает зависимости локальных и внешних провайдеров, зависимости подключённых потребителей и зависимости потребителей самого root-проекта. В сообщении каждый отсутствующий сервис указывается с assembly-qualified identity. Провайдер, который был объявлен, но отклонён при генерации, здесь не сообщается: его собственная диагностика уже объясняет отклонение, а сообщение об отсутствующем провайдере назвало бы провайдера, который автор написал.

### DM0019: Multiple providers for service key

Возникает, когда несколько провайдеров отображаются на один concrete или contract service key. Диагностика также сообщает о неоднозначности, когда одно полное имя источника связано с несколькими service/provider identity, включая одинаковое имя `App.Service` в root и подключённой provider-сборке. Такие identity не объединяются по полному имени; их assembly identity сохраняются в диагностике. Конфликт между локальным провайдером и провайдером из подключённой сборки сообщается независимо от того, является ли проект composition root.

### DM0020: Provider module marker is missing

Возникает, когда подключённая публичная provider-сборка не содержит сгенерированный assembly marker `SingletonDIProviderModuleAttribute`. Provider-пакет или проект должен быть собран совместимыми generator/runtime-протоколом SingletonDI до того, как composition root сможет его загрузить.

### DM0021: Provider module bootstrap is missing

Возникает, когда помеченная provider-сборка не предоставляет публичный статический параметрический метод `Bootstrap()`. Пересоберите провайдера совместимым генератором SingletonDI.

### DM0022: Provider type is not accessible

Возникает, когда сгенерированный код не может именовать провайдера, контракт сервиса, зависимость провайдера или зависимость consumer-а из-за объявленной доступности или file-local содержащего типа. Используйте публичный тип либо тип, доступный сборке. Зависимость consumer-а отклоняется по той же причине даже тогда, когда файл с атрибутом её разрешает: сгенерированное свойство находится в отдельном документе. Доступность проверяется относительно сборки, в которую попадает сгенерированный код, а для провайдера это его собственная сборка, поэтому провайдер из подключённой сборки может использовать свой internal-контракт или internal-зависимость.

### DM0023: Generic InitializeAsync is not supported

Возникает, когда параметрический метод `InitializeAsync` объявляет собственные параметры типа. Уберите параметры типа у метода или выразите инициализацию через не-generic метод.

### DM0024: Open generic dependency is not supported

Возникает, когда провайдер или consumer использует несобранный generic-тип, например `typeof(IContract<>)`. Используйте конкретизированный тип зависимости.

### DM0025: Consumer property name already exists

Возникает, когда генерируемое свойство зависимости конфликтует с членом, уже объявленным в consumer или унаследованным от базового типа. Переименуйте свойство зависимости или удалите конфликтующий член; генератор пропустит конфликтующее свойство.

Проверка охватывает больше, чем сказано выше: DM0025 срабатывает и на член с тем же именем на реализуемом интерфейсе, хотя класс не наследует члены у интерфейсов, и совпадение идёт только по имени, поэтому срабатывание вызывают также поле, метод или вложенный тип с таким именем. DM0025 срабатывает и на параметр типа самого consumer-а или содержащего его типа: параметр типа видим во всём теле типа и при этом не является членом.

### DM0026: Consumer containing type is not partial

Возникает, когда вложенный consumer имеет содержащий тип, который нельзя переоткрыть. Объявите все содержащие типы как `partial`.

### DM0027: Generated code requires C# 9 or newer

Возникает, когда версия языка компиляции ниже C# 9. Повысьте версию языка проекта или используйте совместимый target framework.

### DM0028: File-scoped consumers require C# 10

Возникает, когда consumer в file-scoped namespace компилируется с версией языка ниже C# 10. Повысьте версию языка или используйте блочный namespace.

### DM0029: File-local consumer is not supported

Возникает, когда consumer объявлен с модификатором доступа `file`. Сгенерированный код не может переоткрыть file-local тип, поэтому consumer partial не создаётся. Объявите consumer с доступностью `internal` или `public`.

### DM0030: Aliased service type is not supported

Возникает, когда `ServiceType` провайдера или зависимость consumer указывает тип через `extern alias`. Сгенерированный код не может воспроизвести alias, потому что подключённая provider-сборка не содержит объявлений alias из проекта consumer. Укажите тип по его глобальному имени.

### DM0031: Consumer type parameter attributes are not supported

Возникает, когда consumer объявляет атрибуты на своих параметрах типа. Сгенерированный код переоткрывает объявление consumer без этих целей атрибутов, что привело бы к ошибке компилятора. Перенесите атрибут на использование параметра типа или удалите его.

### DM0032: Provider required members are not supported

Возникает, когда у провайдера есть required-члены, а его публичный параметрический конструктор не объявляет `[SetsRequiredMembers]`. Сгенерированное создание объекта не может удовлетворить эти члены. Пометьте конструктор `[SetsRequiredMembers]` или уберите `required` у членов провайдера.

### DM0033: Nullable initializer return type is not supported

Возникает, когда метод `InitializeAsync` провайдера возвращает `Task?` или `ValueTask?`. Генерируемая регистрация требует non-nullable task, поскольку `null` нарушил бы порядок инициализации. Возвращайте non-nullable `Task` или `ValueTask`.

### DM0034: InitializeAsync has an unsupported return type

Возникает, когда провайдер объявляет `InitializeAsync` без параметров, тип возврата которого — не `Task` и не `ValueTask`, например `async void` или `Task<int>`. Метод не регистрируется как инициализатор. Возвращайте non-generic `Task` или `ValueTask`.

### DM0035: Provider nested in a generic type is not supported

Возникает, когда провайдер, не имеющий собственных параметров типа, объявлен внутри generic типа. Генерируемый код не может назвать такого провайдера, потому что единственное его полностью квалифицированное имя не является допустимым исходником C#. Объявляйте провайдера в не-обобщённом типе.

## Ограничения

- **Только синглтоны** — библиотека не поддерживает Scoped или Transient lifestyles
- **Один контракт на провайдера** — провайдер может объявить не более одного `ServiceType`; несколько contract types для одной реализации не поддерживаются
- **Один процессный root** — runtime-реестр общий для всего процесса; независимые composition roots в одном процессе не поддерживаются
- **Только публичные внешние провайдеры** — composition root импортирует публичные `[SingletonDIProvide]`-типы из подключённых сборок
- **Общий контракт для обратных зависимостей** — библиотека, использующая реализацию из приложения, должна зависеть от нижнеуровневой Contracts-сборки, а не от приложения
- **`struct` запрещён** — только `class` может быть провайдером

## Лицензия

MIT License
