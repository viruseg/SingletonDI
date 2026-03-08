# SingletonDI

**SingletonDI** is a library for declarative management of singleton dependencies in .NET. The tool replaces manual service registration and DI container configuration with an attribute-based system. The library automatically builds the dependency graph, manages object creation order, their asynchronous initialization, and correct resource disposal. Strict compile-time control guarantees the absence of typical binding errors during application runtime.

## Features

- **Declarative dependency description** — service injection and provisioning is configured through `[SingletonDIProvide]` and `[SingletonDIConsume]` attributes directly in class code, without centralized registration modules.
- **No Reflection at runtime** — all code for instantiation and injection is generated at build time, ensuring execution speed at the level of direct constructor calls.
- **Asynchronous initialization** — support for the `InitializeAsync()` method for services requiring I/O operations at startup (database connections, reading configurations, network requests).
- **Parallel startup** — services at the same level of the dependency graph (not depending on each other) are initialized in parallel to minimize application startup time.
- **Automatic dependency resolution** — built-in topological sorting guarantees that each service will be created strictly after all its dependencies are initialized.
- **Compile-time graph validation** — detection of circular dependencies, missing providers, and name conflicts occurs at compile time, preventing runtime errors at application startup.
- **Lifecycle management** — automatic tracking of objects implementing `IDisposable` and `IAsyncDisposable`, with their subsequent disposal in reverse order (LIFO) on shutdown.
- **Thread safety** — generated code ensures safe access to singleton instances in multi-threaded environments.

## Installation

NuGet:

|Package|Download|
|-|-|
|SingletonDI|[![NuGet](https://img.shields.io/nuget/v/SingletonDI.svg)](https://www.nuget.org/packages/SingletonDI) [![NuGet](https://img.shields.io/nuget/dt/SingletonDI.svg)](https://www.nuget.org/packages/SingletonDI)

```bash
dotnet add package SingletonDI
```

## Quick Start

### 1. Declaring singleton providers

```csharp
using SingletonDI.Attributes;

// Simple singleton with synchronous initialization (via constructor)
[SingletonDIProvide]
public class DatabaseService
{
    public string ConnectionString { get; private set; }

    public DatabaseService()
    {
        // Initialization code executes when instance is created
        ConnectionString = "Server=localhost;Database=MyApp;Connected=true";
    }
}

// Singleton with asynchronous initialization
[SingletonDIProvide]
public class UserService
{
    public string UserName { get; private set; }

    public async Task InitializeAsync()
    {
        // Asynchronous data loading
        await LoadUserDataAsync();
        UserName = "LoadedUser";
    }
}
```

### 2. Declaring consumers

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController
{
    public void ProcessOrder()
    {
        // Access singletons through generated properties
        Console.WriteLine($"Database: {DatabaseServiceInstance.ConnectionString}");
        Console.WriteLine($"User: {UserServiceInstance.UserName}");
    }
}
```

### 3. Initialization at application startup

```csharp
using SingletonDI.Generated;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Initialize all singletons with automatic shutdown handler registration
        await SingletonDIInitializer.InitializeAsync();

        // Now consumers can be used
        var controller = new OrderController();
        controller.ProcessOrder();
    }
}
```

## Attributes

### [SingletonDIProvide]

Marks a class as a singleton provider. The generator will create an instance of this class and manage its lifecycle.

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    /// <summary>
    /// Optional custom property name for accessing the singleton.
    /// </summary>
    public string? PropertyName { get; }

    public SingletonDIProvideAttribute(string? propertyName = null) { }
}
```

**Parameters:**
- `propertyName` (optional) — custom property name for accessing the singleton in consumers. If not specified, the default name is used: `{TypeName}Instance`

**Requirements:**
- Applicable only to `class`
- Class must have a public parameterless constructor
- Class must not be `abstract`
- Not inherited (each class must be explicitly marked)

**Initialization:**
- For synchronous initialization, use a parameterless constructor
- For asynchronous initialization, implement the `Task InitializeAsync()` method

**Examples:**

```csharp
// Synchronous initialization via constructor
[SingletonDIProvide]
public class MyService 
{ 
    public MyService()
    {
        // Initialization
    }
}

// Asynchronous initialization via method
[SingletonDIProvide]
public class DataService
{
    public async Task InitializeAsync()
    {
        // Asynchronous initialization
    }
}
```

**Property names:**

By default, the generator creates properties with the `Instance` suffix:

```csharp
[SingletonDIProvide]
public class DatabaseService { }

[SingletonDIConsume(typeof(DatabaseService))]
public partial class OrderService
{
    public void Process()
    {
        // Access via DatabaseServiceInstance
        var db = DatabaseServiceInstance;
    }
}
```

With the `propertyName` parameter, you can specify a custom name:

```csharp
[SingletonDIProvide("_db")]
public class DatabaseService { }

[SingletonDIConsume(typeof(DatabaseService))]
public partial class OrderService
{
    public void Process()
    {
        // Access via custom name _db
        var db = _db;
    }
}
```

### [SingletonDIConsume]

Marks a class as a consumer of singleton dependencies. The generator will create properties for accessing the specified singletons.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, 
                AllowMultiple = false, Inherited = true)]
public sealed class SingletonDIConsumeAttribute : Attribute
{
    public Type[] Dependencies { get; }
    
    public SingletonDIConsumeAttribute(params Type[] dependencies) { }
}
```

**Requirements:**
- Class must be declared as `partial`
- All specified dependencies must be marked with `[SingletonDIProvide]`
- Cannot specify the class itself in the dependency list (self-reference)
- Cannot duplicate types in the dependency list
- Inherited (`Inherited = true`) — inheritors automatically get the same dependencies

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }
```

### Name conflict resolution

When type name conflicts occur (e.g., `Foo.Bar` and `Baz.Bar`), names with namespace prefix are generated.

## SingletonDIInitializer API

The generator creates the `SingletonDIInitializer` class with methods for managing the singleton lifecycle.

### InitializeAsync

```csharp
public static Task InitializeAsync(bool registerShutdownHandlers = true)
```

Initializes all singletons in the correct order (topological sorting by dependencies).

**Parameters:**
- `registerShutdownHandlers` (default `true`):
  - `true` — automatically registers shutdown handlers for correct resource disposal on application termination
  - `false` — does not register handlers (for scenarios with manual lifetime management)

**Returns:** `Task`

**Examples:**

```csharp
// Standard usage with automatic shutdown handler registration
await SingletonDIInitializer.InitializeAsync();

// Manual lifetime management (without automatic shutdown handlers)
await SingletonDIInitializer.InitializeAsync(registerShutdownHandlers: false);

// ... application work ...

// Explicit resource disposal call
await SingletonDIInitializer.DisposeAsync();
```

### DisposeAsync

```csharp
public static ValueTask DisposeAsync()
```

Asynchronously disposes all singletons implementing `IAsyncDisposable` or `IDisposable`.

**Returns:** `ValueTask`

**Disposal order:**
- Singletons are disposed in reverse initialization order (LIFO)
- `DisposeAsync()` is called first for `IAsyncDisposable`, then `Dispose()` for `IDisposable`

**Example:**

```csharp
public static async Task Main(string[] args)
{
    await SingletonDIInitializer.InitializeAsync();
    
    try
    {
        // Application work
        await RunApplicationAsync();
    }
    finally
    {
        // Explicit resource disposal
        await SingletonDIInitializer.DisposeAsync();
    }
}
```

## Diagnostics

The generator reports errors at compile time:

| ID | Level | Description |
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
| **DM0012** | Error | InitializeAsync method cannot be static |
| **DM0013** | Error | Invalid property name |
| **DM0014** | Error | Property name is a reserved keyword |
| **DM0015** | Error | Generic types are not supported for singletons |

### DM0001: Duplicate property name

Occurs when multiple `[SingletonDIProvide]` attributes specify the same `propertyName` value. Each property name must be unique.

```csharp
[SingletonDIProvide(propertyName: "DbService")]
public class DatabaseService { }

[SingletonDIProvide(propertyName: "DbService")]  // DM0001
public class AnotherDatabaseService { }
```

### DM0002: Cannot use [SingletonDIProvide] on abstract class

Occurs when the `[SingletonDIProvide]` attribute is applied to an abstract class. A singleton must be a concrete class that can be instantiated.

```csharp
[SingletonDIProvide]  // DM0002
public abstract class BaseService { }
```

### DM0003: Property name conflicts with generated name

Occurs when a custom property name in `[SingletonDIProvide]` matches the automatically generated name of another singleton.

```csharp
// Automatically generates property "DatabaseServiceInstance"
[SingletonDIProvide]
public class DatabaseService { }

// Error DM0003: "DatabaseServiceInstance" matches a generated name
[SingletonDIProvide("DatabaseServiceInstance")]  // DM0003
public class UserService { }
```

This also works for full names with namespace prefix in case of conflicts:

```csharp
namespace MyApp.Services
{
    [SingletonDIProvide]  // Generates "MyApp_Services_DatabaseServiceInstance"
    public class DatabaseService { }
}

namespace MyApp.Other
{
    // Error DM0003
    [SingletonDIProvide("MyApp_Services_DatabaseServiceInstance")]
    public class UserService { }
}
```

### DM0004: Missing parameterless constructor

Occurs when a class with `[SingletonDIProvide]` does not have a public parameterless constructor. The generator requires the ability to create an instance via `new()`.

```csharp
[SingletonDIProvide]  // DM0004
public class DatabaseService
{
    // No parameterless constructor, only with parameters
    public DatabaseService(string connectionString) { }
}
```

### DM0005: InitializeAsync method has inaccessible access modifier

Occurs when the `InitializeAsync` method is declared with an inaccessible access modifier. The method must be `public`, `internal`, or `protected internal`.

```csharp
[SingletonDIProvide]
public class DataService
{
    private Task InitializeAsync() { return Task.CompletedTask; }  // DM0005
}
```

### DM0006: Referenced type is not a provider

Occurs when `[SingletonDIConsume]` references a type that is not marked with the `[SingletonDIProvide]` attribute.

```csharp
// Class without [SingletonDIProvide] attribute
public class SomeService { }

[SingletonDIConsume(typeof(SomeService))]  // DM0006
public partial class Consumer { }
```

### DM0007: Consumer must be partial

Occurs when a class with `[SingletonDIConsume]` is not declared as `partial`. The generator requires `partial` to add properties.

```csharp
[SingletonDIConsume(typeof(DatabaseService))]  // DM0007
public class OrderController  // Missing partial keyword
{
}
```

### DM0008: Self-reference not allowed

Occurs when a class specifies itself in `[SingletonDIConsume]`. This would lead to infinite recursion.

```csharp
[SingletonDIProvide]
[SingletonDIConsume(typeof(SelfReferencingService))]  // DM0008
public class SelfReferencingService { }
```

### DM0009: Circular dependency detected

Occurs when a circular dependency between providers is detected. Cycles make correct initialization impossible.

```csharp
[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceB))]  // DM0009: A depends on B
public partial class ServiceA { }

[SingletonDIProvide]
[SingletonDIConsume(typeof(ServiceA))]  // DM0009: B depends on A
public partial class ServiceB { }
```

### DM0010: Duplicate type in SingletonDIConsume attribute arguments

Occurs when the same type is specified multiple times in `[SingletonDIConsume]`.

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(DatabaseService))]  // DM0010
public partial class OrderController { }
```

### DM0012: InitializeAsync method cannot be static

Occurs when the `InitializeAsync` method is declared as `static`. The initialization method must be an instance method.

```csharp
[SingletonDIProvide]
public class DataService
{
    public static Task InitializeAsync() { return Task.CompletedTask; }  // DM0012
}
```

### DM0013: Invalid property name

Occurs when a property name in `[SingletonDIProvide]` contains invalid characters. The name must start with a letter or underscore and contain only letters, digits, or underscores.

```csharp
[SingletonDIProvide("invalid-name")]  // DM0013: hyphen is not allowed
public class DatabaseService { }
```

### DM0014: Property name is a reserved keyword

Occurs when a property name in `[SingletonDIProvide]` is a reserved C# keyword.

```csharp
[SingletonDIProvide("class")]  // DM0014: reserved word
public class DatabaseService { }
```

### DM0015: Generic types are not supported for singletons

Occurs when the `[SingletonDIProvide]` attribute is applied to a generic type. SingletonDI does not support generic types as singletons, as a separate instance would be required for each generic parameter.

```csharp
[SingletonDIProvide]  // Error DM0015
public class Repository<T>
{
}
```

## Limitations

- **Singletons only** — the library does not support Scoped or Transient lifestyle
- **Single assembly** — the generator only works within a single assembly (Roslyn per-assembly limitation)
- **No cross-assembly references** — `[SingletonDIProvide]` types from other assemblies are not supported
- **`struct` not allowed** — only `class` can be a provider

## License

MIT License
