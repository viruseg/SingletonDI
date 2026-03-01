# SingletonDI

**Incremental Source Generator для упрощённого DI-менеджера (только синглтоны)**

SingletonDI — это Roslyn Source Generator, который автоматизирует управление зависимостями singleton-объектов в .NET приложениях. Библиотека генерирует весь необходимый код на этапе компиляции, избавляя от ручного написания DI-контейнеров и boilerplate-кода.

## Возможности

- ✅ **Автоматическая генерация кода** — весь DI-код создаётся на этапе компиляции
- ✅ **Incremental Source Generator** — высокая производительность благодаря инкрементальной генерации
- ✅ **Async инициализация** — поддержка асинхронной инициализации через метод `InitializeAsync()`
- ✅ **Параллельная инициализация** — синглтоны на одном уровне зависимостей инициализируются параллельно
- ✅ **Топологическая сортировка** — автоматическое определение порядка инициализации по зависимостям
- ✅ **Детекция циклических зависимостей** — ошибки обнаруживаются на этапе компиляции
- ✅ **Потокобезопасность** — корректная работа в многопоточной среде
- ✅ **Диагностика ошибок** — информативные сообщения об ошибках компиляции
- ✅ **Code Refactoring** — автоматическое добавление метода `InitializeAsync`

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
        await SingletonInitializer.InitializeAsync();

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
```

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
// ✅ Корректно — синхронная инициализация через конструктор
[SingletonDIProvide]
public class MyService 
{ 
    public MyService()
    {
        // Инициализация
    }
}

// ✅ Корректно — асинхронная инициализация через метод
[SingletonDIProvide]
public class DataService
{
    public async Task InitializeAsync()
    {
        // Асинхронная инициализация
    }
}

// ✅ Корректно — record class разрешён
[SingletonDIProvide]
public record class MyRecord(string Value);

// ❌ Ошибка — struct не поддерживается
[SingletonDIProvide]
public struct MyStruct { }  // DM0001

// ❌ Ошибка — abstract class не поддерживается
[SingletonDIProvide]
public abstract class MyAbstract { }  // DM0002
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

**Примеры:**

```csharp
// ✅ Корректно
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }

// ✅ Наследник автоматически получает те же зависимости
public partial class AdvancedOrderController : OrderController { }

// ❌ Ошибка — класс не partial
[SingletonDIConsume(typeof(DatabaseService))]
public class OrderController { }  // DM0007

// ❌ Ошибка — зависимость не является провайдером
[SingletonDIConsume(typeof(SomeNonProviderClass))]
public partial class MyClass { }  // DM0006

// ❌ Ошибка — дублирование типа
[SingletonDIConsume(typeof(DatabaseService), typeof(DatabaseService))]
public partial class MyClass { }  // DM0010
```

## Code Refactoring

Проект включает Code Refactoring провайдер для автоматического добавления метода `InitializeAsync` в классы с атрибутом `[SingletonDIProvide]`.

**Использование:**
1. Установите курсор на имя класса с `[SingletonDIProvide]`
2. Нажмите `Ctrl+.` (или `Alt+Enter` в Rider)
3. Выберите "Add InitializeAsync method"

Генератор добавит следующий метод:

```csharp
public Task InitializeAsync()
{
    return Task.CompletedTask;
}
```

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

Для каждого `[SingletonDIConsume]`-класса генерируется partial-класс со свойствами:

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
| **DM0001** | Error | `[SingletonDIProvide]` применён к `struct` или `record struct` |
| **DM0002** | Error | `[SingletonDIProvide]` применён к `abstract class` |
| **DM0003** | Error | `[SingletonDIProvide]` применён к `interface` |
| **DM0004** | Error | Класс с `[SingletonDIProvide]` не имеет публичного конструктора без параметров |
| **DM0005** | Error | Метод `InitializeAsync` имеет недоступный модификатор доступа (должен быть `public`, `internal` или `protected internal`) |
| **DM0006** | Error | `[SingletonDIConsume]` ссылается на тип без `[SingletonDIProvide]` |
| **DM0007** | Error | Класс с `[SingletonDIConsume]` не объявлен как `partial` |
| **DM0008** | Error | Класс с `[SingletonDIConsume]` ссылается сам на себя (self-reference) |
| **DM0009** | Error | Обнаружена циклическая зависимость между провайдерами |
| **DM0010** | Error | Дублирование типа в аргументах `[SingletonDIConsume]` |
| **DM0011** | Error | Тип уже объявлен в базовом классе |
| **DM0012** | Error | Метод `InitializeAsync` не может быть `static` |

### Пример ошибки циклической зависимости

```csharp
[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceB))]
public partial class ServiceA { }

[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceA))]
public partial class ServiceB { }

// Ошибка DM0009: Circular dependency detected: ServiceA -> ServiceB -> ServiceA
```

### Пример ошибки недоступного InitializeAsync

```csharp
[SingletonDIProvide]
public class MyService
{
    private async Task InitializeAsync()  // ❌ DM0005: private недоступен
    {
        await Task.Delay(100);
    }
}

// ✅ Корректно:
[SingletonDIProvide]
public class MyService
{
    public async Task InitializeAsync()  // public доступен
    {
        await Task.Delay(100);
    }
}
```

## Ограничения

- **Только синглтоны** — библиотека не поддерживает Scoped или Transient lifestyle
- **Одна сборка** — генератор работает только в рамках одной сборки (per-assembly ограничение Roslyn)
- **Нет межсборочных ссылок** — `[SingletonDIProvide]`-типы из других сборок не поддерживаются
- **`struct` запрещён** — только `class` может быть провайдером

## Структура решения

```
SingletonDI/
├── src/
│   ├── SingletonDI.Attributes/     # Атрибуты
│   │   ├── SingletonDIProvideAttribute.cs
│   │   └── SingletonDIConsumeAttribute.cs
│   │
│   ├── SingletonDI.Generator/      # Incremental Source Generator
│   │   ├── SingletonDIGenerator.cs # Главный файл генератора
│   │   ├── DiagnosticDescriptors.cs
│   │   ├── Emitters/               # Генераторы кода
│   │   │   ├── ContainerEmitter.cs
│   │   │   ├── ConsumerEmitter.cs
│   │   │   ├── ExceptionHelperEmitter.cs
│   │   │   └── SingletonInitializerEmitter.cs
│   │   ├── Models/                 # Модели данных
│   │   │   ├── ProviderModel.cs
│   │   │   ├── ConsumerModel.cs
│   │   │   └── CombinedModel.cs
│   │   ├── Validators/             # Валидаторы
│   │   │   ├── ProviderValidator.cs
│   │   │   └── ConsumerValidator.cs
│   │   └── Helpers/                # Вспомогательные классы
│   │       ├── TopologicalSorter.cs
│   │       └── PropertyNameResolver.cs
│   │
│   ├── SingletonDI.Refactoring/    # Code Refactoring провайдеры
│   │   └── SingletonDIProvideRefactoringProvider.cs
│   │
│   └── SingletonDI.SampleApp/      # Пример использования
│       ├── Program.cs
│       └── Generated/              # Примеры сгенерированного кода
│
└── tests/
    └── SingletonDI.Tests/          # Unit-тесты
        ├── SingletonDIGeneratorTests.cs
        └── DiagnosticErrorTests.cs
```

## Лицензия

MIT License
