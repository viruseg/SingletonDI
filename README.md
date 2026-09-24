# SingletonDI

**SingletonDI** is a library for declarative management of singleton dependencies in .NET. The tool replaces manual service registration and DI container configuration with an attribute-based system. The library automatically builds the dependency graph, manages object creation order, their asynchronous initialization, and correct resource disposal. Strict compile-time control guarantees the absence of typical binding errors during application runtime.

## Features

- **Declarative dependency description** — service injection and provisioning is configured through `[SingletonDIProvide]` and `[SingletonDIConsume]` attributes directly in class code, without centralized registration modules.
- **No Reflection at runtime** — all code for instantiation and injection is generated at build time, ensuring execution speed at the level of direct constructor calls.
- **Asynchronous initialization** — support for `Task` or `ValueTask` `InitializeAsync()` methods for services requiring I/O operations at startup (database connections, reading configurations, network requests).
- **Parallel startup** — services at the same level of the dependency graph (not depending on each other) are initialized in parallel to minimize application startup time.
- **Automatic dependency resolution** — built-in topological sorting creates dependency instances first and starts each service level's `InitializeAsync` only after all lower levels complete.
- **Compile-time graph validation** — detection of circular dependencies, missing providers, and name conflicts occurs at compile time, preventing runtime errors at application startup.
- **Lifecycle management** — automatic tracking of objects implementing `IDisposable` and `IAsyncDisposable`, with dependency-graph levels disposed in reverse order on shutdown.
- **Cross-project composition** — a composition root aggregates public providers from transitive `ProjectReference` and `PackageReference` assemblies; shared contracts let libraries consume implementations supplied by the app.
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

## Cross-project composition

A library can consume a provider implemented by the executable without referencing that implementation. Put the service contract in a lower-level Contracts project referenced by both the library and the app.

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

Among the solution projects, the consumer library references only `Shared.Contracts`; it also references SingletonDI, but not the app or `DatabaseService`. The generated `IDatabaseServiceInstance` property resolves the shared contract after the app initializes the process-wide registry.

### RootApp.csproj

The executable that owns the complete dependency graph opts in as the composition root:

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

With this property set, the root recursively inspects its metadata references, imports public providers, validates the complete service graph, and loads each referenced provider registration module before `InitializeAsync()` runs. `ProjectReference` and `PackageReference` are supported identically. Non-public providers from referenced assemblies are not imported, and a provider package built without the generated `SingletonDIProviderModuleAttribute` is rejected with `DM0020`.

The app registers `DatabaseService` under both its concrete type and `IDatabaseService`; both keys resolve the same object. A contract consumer's property name is derived from the contract type (`IDatabaseServiceInstance`), not from the provider's `PropertyName`, because a library that sees only the contract cannot inspect the app's provider declaration. Concrete and contract access can coexist in the app.

A single-project application does not need `SingletonDICompositionRoot`.

## Attributes

### [SingletonDIProvide]

Marks a class as a singleton provider. Generated registration delegates create its instance, and the runtime manages its lifecycle.

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonDIProvideAttribute : Attribute
{
    /// <summary>
    /// Optional custom property name for accessing the singleton.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Service contract exposed by the provider.
    /// </summary>
    public Type? ServiceType { get; set; }

    public SingletonDIProvideAttribute(string? propertyName = null)
    {
        PropertyName = propertyName;
    }
}
```

**Properties and parameters:**
- `propertyName` (optional) — custom property name for concrete access in consumers. If not specified, the default name is `{TypeName}Instance`
- `ServiceType` (optional) — a reference type assignable to the provider and visible to its assembly; the provider is registered under both this contract and its concrete type, and both keys resolve the same instance

**Requirements:**
- Applicable only to `class`
- Class must have a public parameterless constructor
- Class must not be `abstract`
- Not inherited (each class must be explicitly marked)
- A provider can expose at most one `ServiceType`

**Initialization:**
- For synchronous initialization, use a parameterless constructor
- For asynchronous initialization, implement a parameterless `Task InitializeAsync()` or `ValueTask InitializeAsync()` method

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

// ValueTask asynchronous initialization
[SingletonDIProvide]
public class DataService
{
    public async ValueTask InitializeAsync()
    {
        // Asynchronous initialization
    }
}
```

A provider can expose one shared contract without coupling the consumer library to its implementation:

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

`ServiceType` must be a reference type to which the provider is assignable. A consumer may depend on that contract without referencing the implementation assembly; the executable composition root validates that exactly one provider maps to it. Contract property names are derived from the contract type and the consumer's dependency set, not from the provider's `PropertyName`. If it is not set, only the concrete provider type is registered.

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

For every contract dependency, the generated name is based on the contract type and the provider's custom `PropertyName` is not used. This matters especially for a library consumer that cannot inspect the provider declaration:

```csharp
[SingletonDIProvide("db", ServiceType = typeof(IDatabaseService))]
public sealed class DatabaseService : IDatabaseService { }

[SingletonDIConsume(typeof(IDatabaseService))]
public partial class Repository
{
    public IDatabaseService GetService() => IDatabaseServiceInstance;
}
```

Here `IDatabaseServiceInstance` is the contract property, while a consumer that can see the concrete provider may use `db`. Namespace qualification is still applied when dependencies in the same consumer have conflicting short names.

### [SingletonDIConsume]

Marks a class as a consumer of singleton dependencies. The generator will create properties for accessing the specified singletons.

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

**Requirements:**
- Class must be declared as `partial`
- Each dependency must be a visible `[SingletonDIProvide]` type or a supported interface/abstract contract; the composition root verifies that a requested contract has exactly one provider
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

The SingletonDI runtime supplies the process-wide `SingletonDIInitializer` class. Generated provider modules register into this runtime registry, and generated consumer properties resolve from it.

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

Provider instances are created level by level, with dependencies before dependents, before any `InitializeAsync` methods run. Initializers for providers in the same level then run in parallel. Both `Task` and `ValueTask` initializer return types are supported. Concurrent lifecycle calls are serialized with disposal: a reinitialization requested during disposal waits for disposal to finish and receives a new task, never the previous successful task. A failed initialization disposes every created instance in reverse dependency order, continues after individual disposer failures, clears state, and preserves the original initialization exception so a later call can retry. Registering a new provider after initialization has started is rejected.

Ordinary access to a generated property or hidden host resolution before successful initialization and outside the active initialization context throws `InvalidOperationException`. During the active initialization context, a provider factory or constructor may resolve already-created dependencies; the SampleApp uses this pattern in provider constructors.

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

Asynchronously disposes all initialized singletons implementing `IAsyncDisposable` or `IDisposable`. The method returns `ValueTask`; concurrent and repeated calls are idempotent. Cleanup continues after an individual disposer failure, and the disposal error is reported after the remaining instances have been released.

**Returns:** `ValueTask`

**Disposal order:**
- Dependency-graph levels are disposed in reverse initialization order (LIFO)
- Providers in the same level are disposed concurrently
- `DisposeAsync()` is preferred when a provider implements both `IAsyncDisposable` and `IDisposable`

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

Cross-project service and provider identities include the containing assembly. Repeated references to the same assembly are deduplicated, while equal type names from different assemblies remain distinct. `DM0019` is also emitted for conflicting local `ServiceType` mappings, not only in a composition root, and reports all conflicting provider identities. It also reports ambiguity when the same fully qualified name is associated with multiple identities, for example a local `App.Service` and a referenced `App.Service`; the diagnostic keeps their assembly identities separate.

### DM0001: Duplicate property name

Occurs when multiple `[SingletonDIProvide]` attributes that can participate in the same consumer dependency set specify the same `propertyName` value. A composition root scopes this validation to providers actually referenced by consumers; unrelated providers do not block a valid root.

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

### DM0003: Consumer property name conflict

Occurs when two dependencies of one consumer resolve to the same property name, including a provider custom name and a contract-derived name. The diagnostic includes the conflicting property and service identity; the generator uses a namespace-qualified fallback for the second property and reports the conflict instead of emitting an ambiguous API.

```csharp
[SingletonDIProvide("IServiceInstance")]
public class Service : IService { }

[SingletonDIConsume(typeof(Service), typeof(IService))]
public partial class Consumer { } // DM0003; IService dependency receives a fallback name
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

Occurs when `[SingletonDIConsume]` references an unsupported type. A concrete class without `[SingletonDIProvide]` is invalid; an interface or abstract class may instead be an external contract whose implementation is validated by the composition root.

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

### DM0016: Invalid ServiceType

Occurs when `ServiceType` is not a reference type assignable to the provider. A provider can declare only one `ServiceType`.

```csharp
public interface ICacheService { }

[SingletonDIProvide(ServiceType = typeof(ICacheService))]  // DM0016
public sealed class DatabaseService { }
```

### DM0017: Executable has an unmapped consumer dependency

Occurs when an executable project without `SingletonDICompositionRoot=true` has a consumer dependency that is not mapped by its local provider map. This includes an interface or abstract contract declared in the current assembly, a contract supplied by another assembly, and a concrete provider from another assembly. Library consumer projects do not receive `DM0017`; their dependencies are validated by the executable composition root.

### DM0018: No provider for requested service

Occurs when any dependency in the complete composition-root graph has no provider in the service map. This includes dependencies declared by local or external providers, dependencies declared by referenced consumers, and dependencies declared by consumers in the root project. The message identifies each missing service by its assembly-qualified identity.

### DM0019: Multiple providers for service key

Occurs when multiple providers map to the same concrete or contract service key. It also reports ambiguity when one fully qualified source name is associated with multiple service or provider identities, including the same `App.Service` name in the root and a referenced provider assembly. These identities are not merged by fully qualified name; their assembly identities are preserved in the diagnostic.

### DM0020: Provider module marker is missing

Occurs when a referenced public provider assembly does not contain the generated `SingletonDIProviderModuleAttribute` assembly marker. The provider package or project must be built with a compatible SingletonDI generator/runtime protocol before a composition root can bootstrap it.

## Limitations

- **Singletons only** — the library does not support Scoped or Transient lifestyles
- **One contract per provider** — a provider can declare at most one `ServiceType`; multiple contract types for one implementation are not supported
- **One process-wide root** — the runtime registry is shared by the whole process; independent composition roots in one process are not supported
- **Public external providers only** — composition roots import public `[SingletonDIProvide]` types from referenced assemblies
- **Shared contracts for reverse dependencies** — a library that consumes an app-owned implementation must depend on a lower-level contract assembly, not on the app
- **`struct` not allowed** — only `class` can be a provider

## License

MIT License
