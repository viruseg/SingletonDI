# SingletonDI

**Incremental Source Generator для упрощённого DI-менеджера (только синглтоны)**

SingletonDI — это Roslyn Source Generator, который автоматизирует управление зависимостями singleton-объектов в .NET приложениях. Библиотека генерирует весь необходимый код на этапе компиляции, избавляя от ручного написания DI-контейнеров и boilerplate-кода.

## Возможности

- **Автоматическая генерация кода** — весь DI-код создаётся на этапе компиляции
- **Incremental Source Generator** — высокая производительность благодаря инкрементальной генерации
- **Async инициализация** — поддержка асинхронной инициализации через метод `InitializeAsync()`
- **Параллельная инициализация** — синглтоны на одном уровне зависимостей инициализируются параллельно
- **Топологическая сортировка** — автоматическое определение порядка инициализации по зависимостям
- **Детекция циклических зависимостей** — ошибки обнаруживаются на этапе компиляции
- **Потокобезопасность** — корректная работа в многопоточной среде
- **Диагностика ошибок** — информативные сообщения об ошибках компиляции

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
// Класс, который использует зависимости
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController  // Обязательно partial!
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
using DependencyManager.Generated;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Инициализация всех синглтонов
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

## Диагностика

Генератор сообщает об ошибках на этапе компиляции:

| ID | Уровень | Описание |
|---|---|---|
| **DM0001** | Error | Дублирование имени свойства в `[SingletonDIProvide]` |
| **DM0002** | Error | `[SingletonDIProvide]` применён к `abstract class` |
| **DM0003** | Error | Имя свойства в `[SingletonDIProvide]` совпадает с автоматически сгенерированным именем другого синглтона |
| **DM0004** | Error | Класс с `[SingletonDIProvide]` не имеет публичного конструктора без параметров |
| **DM0005** | Error | Метод `InitializeAsync` имеет недоступный модификатор доступа (должен быть `public`, `internal` или `protected internal`) |
| **DM0006** | Error | `[SingletonDIConsume]` ссылается на тип без `[SingletonDIProvide]` |
| **DM0007** | Error | Класс с `[SingletonDIConsume]` не объявлен как `partial` |
| **DM0008** | Error | Класс с `[SingletonDIConsume]` ссылается сам на себя (self-reference) |
| **DM0009** | Error | Обнаружена циклическая зависимость между провайдерами |
| **DM0010** | Error | Дублирование типа в аргументах `[SingletonDIConsume]` |
| **DM0011** | Error | Тип уже объявлен в базовом классе |
| **DM0012** | Error | Метод `InitializeAsync` не может быть `static` |
| **DM0013** | Error | Недопустимое имя свойства. Имя должно начинаться с буквы или подчёркивания и содержать только буквы, цифры или подчёркивания |
| **DM0014** | Error | Имя свойства является зарезервированным ключевым словом C#. Используйте другое имя или добавьте префикс '@' в коде |

### DM0001: Duplicate property name

Возникает, когда несколько атрибутов `[SingletonDIProvide]` указывают одинаковое значение параметра `propertyName`. Каждое имя свойства должно быть уникальным.

```csharp
[SingletonDIProvide(propertyName: "DbService")]
public class DatabaseService { }

[SingletonDIProvide(propertyName: "DbService")]  // DM0001
public class AnotherDatabaseService { }
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

## Ограничения

- **Только синглтоны** — библиотека не поддерживает Scoped или Transient lifestyle
- **Одна сборка** — генератор работает только в рамках одной сборки (per-assembly ограничение Roslyn)
- **Нет межсборочных ссылок** — `[SingletonDIProvide]`-типы из других сборок не поддерживаются
- **`struct` запрещён** — только `class` может быть провайдером

## Лицензия

MIT License
