# SingletonDI

**SingletonDI** — это библиотека для декларативного управления singleton-зависимостями в .NET. Инструмент заменяет ручную регистрацию сервисов и настройку DI-контейнеров на систему атрибутов. Библиотека автоматически формирует граф зависимостей, управляет порядком создания объектов, их асинхронной инициализацией и корректным освобождением ресурсов. Строгий контроль на этапе компиляции гарантирует отсутствие типичных ошибок связывания во время выполнения приложения.
## Возможности

- **Декларативное описание зависимостей** — внедрение и предоставление сервисов настраивается через атрибуты `[SingletonDIProvide]` и `[SingletonDIConsume]` непосредственно в коде классов, без централизованных модулей регистрации.
- **Отсутствие Reflection в runtime** — весь код для инстанцирования и внедрения генерируется на этапе сборки, что обеспечивает скорость выполнения на уровне прямого вызова конструкторов.
- **Асинхронная инициализация** — поддержка метода `InitializeAsync()` для сервисов, требующих I/O операций при старте (подключение к БД, чтение конфигураций, сетевые запросы).
- **Параллельный запуск** — сервисы, находящиеся на одном уровне графа зависимостей (не зависящие друг от друга), инициализируются параллельно для минимизации времени старта приложения.
- **Автоматическое разрешение зависимостей** — встроенная топологическая сортировка гарантирует, что каждый сервис будет создан строго после инициализации всех его зависимостей.
- **Compile-time валидация графа** — выявление циклических зависимостей, отсутствующих провайдеров и конфликтов имен происходит на этапе компиляции, предотвращая падения (runtime errors) при запуске приложения.
- **Управление жизненным циклом** — автоматическое отслеживание объектов, реализующих `IDisposable` и `IAsyncDisposable`, с последующим их освобождением в обратном порядке (LIFO) при завершении работы.
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

## Атрибуты

### [SingletonDIProvide]

Маркирует класс как singleton-провайдер. Генератор создаст экземпляр этого класса и будет управлять его жизненным циклом.

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    /// <summary>
    /// Опциональное пользовательское имя свойства для доступа к синглтону.
    /// </summary>
    public string? PropertyName { get; }

    public SingletonDIProvideAttribute(string? propertyName = null) { }
}
```

**Параметры:**
- `propertyName` (опционально) — пользовательское имя свойства для доступа к синглтону в потребителях. Если не указано, используется имя по умолчанию: `{TypeName}Instance`

**Требования:**
- Применим только к `class`
- Класс должен иметь публичный конструктор без параметров
- Класс не должен быть `abstract`
- Не наследуется (каждый класс должен быть явно помечен)

**Инициализация:**
- Для синхронной инициализации используйте конструктор без параметров
- Для асинхронной инициализации реализуйте метод `Task InitializeAsync()`

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

// Асинхронная инициализация через метод
[SingletonDIProvide]
public class DataService
{
    public async Task InitializeAsync()
    {
        // Асинхронная инициализация
    }
}
```

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

### [SingletonDIConsume]

Маркирует класс как потребителя singleton-зависимостей. Генератор создаст свойства для доступа к указанным синглтонам.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, 
                AllowMultiple = false, Inherited = true)]
public sealed class SingletonDIConsumeAttribute : Attribute
{
    public Type[] Dependencies { get; }
    
    public SingletonDIConsumeAttribute(params Type[] dependencies) { }
}
```

**Требования:**
- Класс должен быть объявлен как `partial`
- Все указанные зависимости должны быть помечены `[SingletonDIProvide]`
- Нельзя указывать сам класс в списке зависимостей (self-reference)
- Нельзя дублировать типы в списке зависимостей
- Наследуется (`Inherited = true`) — наследники автоматически получают те же зависимости

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }
```

### Разрешение конфликтов имён

При конфликте имён типов (например, `Foo.Bar` и `Baz.Bar`) генерируются имена с префиксом пространства имён.

## API SingletonDIInitializer

Генератор создаёт класс `SingletonDIInitializer` с методами для управления жизненным циклом синглтонов.

### InitializeAsync

```csharp
public static Task InitializeAsync(bool registerShutdownHandlers = true)
```

Инициализирует все синглтоны в правильном порядке (топологическая сортировка по зависимостям).

**Параметры:**
- `registerShutdownHandlers` (по умолчанию `true`):
  - `true` — автоматически регистрирует обработчики shutdown для корректного освобождения ресурсов при завершении приложения
  - `false` — не регистрирует обработчики (для сценариев с ручным управлением lifetime)

**Возвращает:** `Task`

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
public static ValueTask DisposeAsync()
```

Асинхронно освобождает все синглтоны, реализующие `IAsyncDisposable` или `IDisposable`.

**Возвращает:** `ValueTask`

**Порядок освобождения:**
- Синглтоны освобождаются в обратном порядке инициализации (LIFO)
- Сначала вызывается `DisposeAsync()` для `IAsyncDisposable`, затем `Dispose()` для `IDisposable`

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
| **DM0003** | Error | Property name conflicts with generated name |
| **DM0004** | Error | Missing parameterless constructor |
| **DM0005** | Error | InitializeAsync method has inaccessible access modifier |
| **DM0006** | Error | Referenced type is not a provider |
| **DM0007** | Error | Consumer must be partial |
| **DM0008** | Error | Self-reference not allowed |
| **DM0009** | Error | Circular dependency detected |
| **DM0010** | Error | Duplicate type in SingletonDIConsume attribute arguments |
| **DM0011** | Error | Type already declared in base class |
| **DM0012** | Error | InitializeAsync method cannot be static |
| **DM0013** | Error | Invalid property name |
| **DM0014** | Error | Property name is a reserved keyword |
| **DM0015** | Error | Generic types are not supported for singletons |

### DM0001: Duplicate property name

Возникает, когда несколько атрибутов `[SingletonDIProvide]` указывают одинаковое значение параметра `propertyName`. Каждое имя свойства должно быть уникальным.

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

### DM0003: Property name conflicts with generated name

Возникает, когда пользовательское имя свойства в `[SingletonDIProvide]` совпадает с автоматически сгенерированным именем другого синглтона.

```csharp
// Автоматически генерирует свойство "DatabaseServiceInstance"
[SingletonDIProvide]
public class DatabaseService { }

// Ошибка DM0003: "DatabaseServiceInstance" совпадает с сгенерированным именем
[SingletonDIProvide("DatabaseServiceInstance")]  // DM0003
public class UserService { }
```

Это также работает для полных имён с префиксом namespace при конфликтах:

```csharp
namespace MyApp.Services
{
    [SingletonDIProvide]  // Генерирует "MyApp_Services_DatabaseServiceInstance"
    public class DatabaseService { }
}

namespace MyApp.Other
{
    // Ошибка DM0003
    [SingletonDIProvide("MyApp_Services_DatabaseServiceInstance")]
    public class UserService { }
}
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

Возникает, когда `[SingletonDIConsume]` ссылается на тип, который не помечен атрибутом `[SingletonDIProvide]`.

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

### DM0011: Type already declared in base class

Возникает, когда наследник пытается указать тип в `[SingletonDIConsume]`, который уже был объявлен в базовом классе.

```csharp
[SingletonDIConsume(typeof(DatabaseService))]
public partial class BaseController { }

[SingletonDIConsume(typeof(DatabaseService))]  // DM0011: уже объявлено в BaseController
public partial class OrderController : BaseController { }
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

Возникает, когда атрибут `[SingletonDIProvide]` применяется к generic типу. SingletonDI не поддерживает generic типы как синглтоны, так как для каждого generic-параметра потребовался бы отдельный экземпляр.

```csharp
[SingletonDIProvide]  // Error DM0015
public class Repository<T>
{
}
```

## Ограничения

- **Только синглтоны** — библиотека не поддерживает Scoped или Transient lifestyle
- **Одна сборка** — генератор работает только в рамках одной сборки (per-assembly ограничение Roslyn)
- **Нет межсборочных ссылок** — `[SingletonDIProvide]`-типы из других сборок не поддерживаются
- **`struct` запрещён** — только `class` может быть провайдером

## Лицензия

MIT License
