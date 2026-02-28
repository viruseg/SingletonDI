# SingletonDI

**Incremental Source Generator для упрощённого DI-менеджера (только синглтоны)**

SingletonDI — это Roslyn Source Generator, который автоматизирует управление зависимостями singleton-объектов в .NET приложениях. Библиотека генерирует весь необходимый код на этапе компиляции, избавляя от ручного написания DI-контейнеров и boilerplate-кода.

## Возможности

- ✅ **Автоматическая генерация кода** — весь DI-код создаётся на этапе компиляции
- ✅ **Incremental Source Generator** — высокая производительность благодаря инкрементальной генерации
- ✅ **Async инициализация** — поддержка асинхронной инициализации через `IInitializeAsync`
- ✅ **Sync инициализация** — поддержка синхронной инициализации через `IInitializeSync`
- ✅ **Параллельная инициализация** — синглтоны на одном уровне зависимостей инициализируются параллельно
- ✅ **Топологическая сортировка** — автоматическое определение порядка инициализации по зависимостям
- ✅ **Детекция циклических зависимостей** — ошибки обнаруживаются на этапе компиляции
- ✅ **Потокобезопасность** — корректная работа в многопоточной среде
- ✅ **Диагностика ошибок** — информативные сообщения об ошибках компиляции

## Установка

### Локальная сборка

```bash
# Клонирование репозитория
git clone <repository-url>
cd SingletonDI

# Сборка решения
dotnet build

# Запуск тестов
dotnet test

# Запуск примера
dotnet run --project src/SingletonDI.SampleApp
```

### Подключение к проекту

В `.csproj` файл вашего проекта добавьте ссылку на атрибуты и генератор:

```xml
<ItemGroup>
  <ProjectReference Include="path\to\SingletonDI.Attributes\SingletonDI.Attributes.csproj" />
  <ProjectReference Include="path\to\SingletonDI.Generator\SingletonDI.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Быстрый старт

### 1. Объявление singleton-провайдеров

```csharp
using SingletonDI.Attributes;

// Простой singleton с синхронной инициализацией
[Provide]
public class DatabaseService : IInitializeSync
{
    public string ConnectionString { get; private set; }

    public void Initialize()
    {
        // Код инициализации выполняется при старте приложения
        ConnectionString = "Server=localhost;Database=MyApp;Connected=true";
    }
}

// Singleton с асинхронной инициализацией
[Provide]
public class UserService : IInitializeAsync
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
[Consume(typeof(DatabaseService), typeof(UserService))]
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
        await SingletonInitializer.InitializeAsync();

        // Теперь можно использовать потребителей
        var controller = new OrderController();
        controller.ProcessOrder();
    }
}
```

## Атрибуты

### [Provide]

Маркирует класс как singleton-провайдер. Генератор создаст экземпляр этого класса и будет управлять его жизненным циклом.

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProvideAttribute : Attribute
```

**Требования:**
- Применим только к `class`
- Класс должен иметь публичный конструктор без параметров
- Класс не должен быть `abstract`
- Не наследуется (каждый класс должен быть явно помечен)

**Примеры:**

```csharp
// ✅ Корректно
[Provide]
public class MyService { }

// ✅ Корректно — record class разрешён
[Provide]
public record class MyRecord(string Value);

// ❌ Ошибка — struct не поддерживается
[Provide]
public struct MyStruct { }  // DM0001

// ❌ Ошибка — abstract class не поддерживается
[Provide]
public abstract class MyAbstract { }  // DM0002
```

### [Consume]

Маркирует класс как потребителя singleton-зависимостей. Генератор создаст свойства для доступа к указанным синглтонам.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, 
                AllowMultiple = false, Inherited = true)]
public sealed class ConsumeAttribute : Attribute
{
    public Type[] Dependencies { get; }
    
    public ConsumeAttribute(params Type[] dependencies) { }
}
```

**Требования:**
- Класс должен быть объявлен как `partial`
- Все указанные зависимости должны быть помечены `[Provide]`
- Нельзя указывать сам класс в списке зависимостей (self-reference)
- Наследуется (`Inherited = true`) — наследники автоматически получают те же зависимости

**Примеры:**

```csharp
// ✅ Корректно
[Consume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }

// ✅ Наследник автоматически получает те же зависимости
public partial class AdvancedOrderController : OrderController { }

// ❌ Ошибка — класс не partial
[Consume(typeof(DatabaseService))]
public class OrderController { }  // DM0007

// ❌ Ошибка — зависимость не является провайдером
[Consume(typeof(SomeNonProviderClass))]
public partial class MyClass { }  // DM0006
```

## Интерфейсы инициализации

### IInitializeSync

Интерфейс для синхронной инициализации singleton-объектов.

```csharp
public interface IInitializeSync
{
    void Initialize();
}
```

Используйте, когда инициализация не требует асинхронных операций:

```csharp
[Provide]
public class ConfigService : IInitializeSync
{
    public void Initialize()
    {
        // Загрузка конфигурации, подключение к ресурсам и т.д.
    }
}
```

### IInitializeAsync

Интерфейс для асинхронной инициализации singleton-объектов.

```csharp
public interface IInitializeAsync
{
    Task InitializeAsync();
}
```

Используйте для асинхронных операций (загрузка данных, сетевые запросы):

```csharp
[Provide]
public class DataService : IInitializeAsync
{
    public async Task InitializeAsync()
    {
        await LoadDataFromServerAsync();
        await InitializeCacheAsync();
    }
}
```

> **Важно:** Класс не может реализовывать оба интерфейса одновременно — это приведёт к ошибке компиляции (DM0010).

## Сгенерированный код

### SingletonContainer

Генерируется в пространстве имён `DependencyManager.Generated.Internal`:

```csharp
// <auto-generated/>
namespace DependencyManager.Generated.Internal
{
    internal static partial class SingletonContainer
    {
        private static volatile bool _isInitialized;
        private static readonly object _lock = new object();

        private static MyApp.DatabaseService? _DatabaseService;
        private static MyApp.UserService? _UserService;

        public static MyApp.DatabaseService DatabaseService 
            => _DatabaseService ?? ExceptionHelper.ThrowNotInitialized<MyApp.DatabaseService>();
        
        public static MyApp.UserService UserService 
            => _UserService ?? ExceptionHelper.ThrowNotInitialized<MyApp.UserService>();

        public static async Task InitializeAsync()
        {
            // Потокобезопасная инициализация с параллельной обработкой уровней
            // ...
        }
    }
}
```

### SingletonInitializer

Публичный API для инициализации:

```csharp
// <auto-generated/>
namespace DependencyManager.Generated
{
    public static class SingletonInitializer
    {
        public static Task InitializeAsync() 
            => Internal.SingletonContainer.InitializeAsync();
    }
}
```

### Partial-классы потребителей

Для каждого `[Consume]`-класса генерируется partial-класс со свойствами:

```csharp
// <auto-generated/>
namespace MyApp
{
    partial class OrderController
    {
        public global::MyApp.DatabaseService DatabaseServiceInstance
        {
            get
            {
                global::DependencyManager.Generated.Internal.SingletonContainer.ThrowIfNotInitialized();
                return global::DependencyManager.Generated.Internal.SingletonContainer.DatabaseService;
            }
        }

        public global::MyApp.UserService UserServiceInstance
        {
            get
            {
                global::DependencyManager.Generated.Internal.SingletonContainer.ThrowIfNotInitialized();
                return global::DependencyManager.Generated.Internal.SingletonContainer.UserService;
            }
        }
    }
}
```

### Разрешение конфликтов имён

При конфликте имён типов (например, `Foo.Bar` и `Baz.Bar`) генерируются имена с префиксом пространства имён:

```csharp
public global::Foo.Bar Foo_Bar { get; }
public global::Baz.Bar Baz_Bar { get; }
```

## Диагностика

Генератор сообщает об ошибках на этапе компиляции:

| ID | Уровень | Описание |
|---|---|---|
| **DM0001** | Error | `[Provide]` применён к `struct` или `record struct` |
| **DM0002** | Error | `[Provide]` применён к `abstract class` |
| **DM0003** | Error | `[Provide]` применён к `interface` |
| **DM0004** | Error | Класс с `[Provide]` не имеет публичного конструктора без параметров |
| **DM0005** | Warning | Класс с `[Provide]` не реализует `IInitializeSync` или `IInitializeAsync` |
| **DM0006** | Error | `[Consume]` ссылается на тип без `[Provide]` |
| **DM0007** | Error | Класс с `[Consume]` не объявлен как `partial` |
| **DM0008** | Error | Класс с `[Consume]` ссылается сам на себя (self-reference) |
| **DM0009** | Error | Обнаружена циклическая зависимость между провайдерами |
| **DM0010** | Error | Класс реализует оба интерфейса инициализации одновременно |

### Пример ошибки циклической зависимости

```csharp
[Provide]
[Consume(typeof(ServiceB))]
public partial class ServiceA { }

[Provide]
[Consume(typeof(ServiceA))]
public partial class ServiceB { }

// Ошибка DM0009: Circular dependency detected: ServiceA -> ServiceB -> ServiceA
```

## Ограничения

- **Только синглтоны** — библиотека не поддерживает Scoped или Transient lifestyle
- **Одна сборка** — генератор работает только в рамках одной сборки (per-assembly ограничение Roslyn)
- **Нет межсборочных ссылок** — `[Provide]`-типы из других сборок не поддерживаются
- **`struct` запрещён** — только `class` может быть провайдером

## Структура решения

```
SingletonDI/
├── src/
│   ├── SingletonDI.Attributes/     # Атрибуты и интерфейсы
│   │   ├── ProvideAttribute.cs
│   │   ├── ConsumeAttribute.cs
│   │   ├── IInitializeSync.cs
│   │   └── IInitializeAsync.cs
│   │
│   ├── SingletonDI.Generator/      # Incremental Source Generator
│   │   ├── SingletonDIGenerator.cs # Главный файл генератора
│   │   ├── DiagnosticDescriptors.cs
│   │   ├── Emitters/               # Генераторы кода
│   │   ├── Models/                 # Модели данных
│   │   ├── Validators/             # Валидаторы
│   │   └── Helpers/                # Вспомогательные классы
│   │
│   └── SingletonDI.SampleApp/      # Пример использования
│       ├── Program.cs
│       └── Generated/              # Примеры сгенерированного кода
│
└── tests/
    └── SingletonDI.Tests/          # Unit-тесты
```

## Лицензия

MIT License (или укажите вашу лицензию)
