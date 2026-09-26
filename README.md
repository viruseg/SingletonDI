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

With this property set, the root recursively inspects its metadata references, imports public providers, validates the complete service graph, and invokes the generated `Bootstrap()` method on each referenced provider module before `InitializeAsync()` runs. `ProjectReference` and `PackageReference` are supported identically. The generated module method is idempotent and avoids running a user provider constructor as a side effect of module loading. A bootstrap that fails part-way through undoes the registrations that already succeeded and releases its guard, so calling it again reports the original error rather than a duplicate key over a half-registered graph. Non-public providers from referenced assemblies are not imported, and a provider package built without the generated `SingletonDIProviderModuleAttribute` is rejected with `DM0020`.

The app registers `DatabaseService` under both its concrete type and `IDatabaseService`; both keys resolve the same object. A contract consumer's property name is derived from the contract type (`IDatabaseServiceInstance`), not from the provider's `PropertyName`, because a library that sees only the contract cannot inspect the app's provider declaration. Concrete and contract access can coexist in the app.

The runtime API is in the `SingletonDI.Generated` namespace. A cross-project executable must opt in with `SingletonDICompositionRoot=true`, expose that property to the compiler, and await `SingletonDI.Generated.SingletonDIInitializer.InitializeAsync()` after its generated composition-root module is available. A single-project application does not need `SingletonDICompositionRoot`.

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
- `ServiceType` (optional) — a reference type the provider converts to implicitly, that is the provider implements or derives from it, and that is visible to its assembly; the provider is registered under both this contract and its concrete type, and both keys resolve the same instance

**Requirements:**
- Applicable only to `class`
- Class must have a public parameterless constructor
- Class must not be `abstract`, and a `static` class is rejected with the same diagnostic, because a static class is abstract in the language and cannot have the instance constructor a singleton needs
- Not inherited (each class must be explicitly marked)
- A provider can expose at most one `ServiceType`
- Generated source requires C# 9 or later

**Initialization:**
- For synchronous initialization, use a parameterless constructor
- For asynchronous initialization, implement a public, internal, or protected internal parameterless `Task InitializeAsync()` or `ValueTask InitializeAsync()` instance method
- Generic, static, open-generic, and inaccessible initializers are rejected during generation; an explicit interface implementation is inaccessible, so it is rejected as well

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
- The consumer may be a top-level or nested `class`, `struct`, `record`, or `record struct`; every containing type that receives generated members must be `partial`
- File-scoped consumer declarations require C# 10 or later; other generated source requires C# 9 or later
- Each dependency must be a visible `[SingletonDIProvide]` type or a supported interface/abstract contract; the composition root verifies that a requested contract has exactly one provider
- Cannot specify the consumer type itself in the dependency list (self-reference)
- Cannot duplicate types in the dependency list
- The attribute is inherited (`Inherited = true`), so a derived consumer reads the same generated dependencies through the base type without repeating the attribute; a derived declaration is not itself validated and receives no generated members, so DM0007, DM0026, and DM0029 do not apply to it. The provider side does inherit the attribute for validation
- Generated properties are `protected` for an unsealed class and `private` for a sealed class, a `static` class, a `struct`, or a `record struct`

```csharp
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService))]
public partial class OrderController { }
```

### Name conflict resolution

When type name conflicts occur (e.g., `Foo.Bar` and `Baz.Bar`), names with namespace prefix are generated.

## Package compatibility

Version `1.1.0` targets `net10.0`. The packed generator and refactoring assemblies use the Roslyn `4.8` API baseline and are verified with SDK `10.0.401`. Generated source requires C# 9 or later; file-scoped consumer declarations require C# 10 or later.

The package places the generator and refactoring assemblies under `analyzers/dotnet/cs`, so consumers receive them automatically through the `SingletonDI` package reference.

## SingletonDIInitializer API

The SingletonDI runtime supplies the process-wide `SingletonDIInitializer` class. Generated provider modules register into this runtime registry, and generated consumer properties resolve from it.

### InitializeAsync

```csharp
public static Task InitializeAsync(bool registerShutdownHandlers = true)
```

Initializes all singletons in the correct order (topological sorting by dependencies).

**Parameters:**
- `registerShutdownHandlers` (default `true`):
  - `true` — registers process-exit, console cancel, and supported POSIX signal handlers for graceful disposal
  - `false` — skips handler registration; the application owns the complete lifetime and must call `DisposeAsync`

**Returns:** `Task` that completes only after provider creation and all initializers finish.

Provider instances are created level by level, with dependencies before dependents, before any `InitializeAsync` methods run. Initializers for providers in the same level then run in parallel. Both `Task` and `ValueTask` initializer return types are supported. Concurrent lifecycle calls are serialized with disposal: a reinitialization requested during disposal waits for disposal to finish and receives a new task, never the previous successful task. A failed initialization disposes every created instance in reverse dependency order, continues after individual disposer failures, clears state, and preserves the original initialization exception so a later call can retry. Registering a new provider after initialization has started is rejected.

When shutdown handlers are enabled, `Ctrl+C`/`SIGINT`, `SIGTERM`, and `SIGQUIT` cancel default termination, await one disposal operation, and then exit with codes `130`, `143`, and `131` respectively. Repeated signals do not start another disposal. On a POSIX platform `Ctrl+C` is handled by the signal registration alone, so one interrupt starts one disposal. Subscribing the handlers can fail on a host that does not allow it; initialization still succeeds and the container stays usable, but shutdown then depends on an explicit `DisposeAsync`. `ProcessExit` is best-effort because the host may terminate the process before asynchronous cleanup finishes; await `SingletonDIInitializer.DisposeAsync()` explicitly when cleanup must be guaranteed.

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

Asynchronously disposes all initialized singletons implementing `IAsyncDisposable` or `IDisposable`. The method returns `ValueTask`; concurrent and repeated calls are idempotent. Cleanup continues after an individual disposer failure, and the disposal error is reported after the remaining instances have been released. Await the returned task in application shutdown code when disposal must complete before the process exits.

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

Occurs when the `[SingletonDIProvide]` attribute is applied to an abstract class. A singleton must be a concrete class that can be instantiated. A `static` class is rejected with the same diagnostic: it is abstract in the language and cannot have the instance constructor a singleton needs, so the "add a constructor" code fix is not offered for it.

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

Occurs when the `InitializeAsync` method is declared with an inaccessible access modifier. The method must be `public`, `internal`, or `protected internal`. An explicit interface implementation is private and is rejected as well, so declaring the initializer through an interface does not hide it from the generator.

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

Occurs when a property name in `[SingletonDIProvide]` cannot be declared as a C# member: it is empty, does not start with a letter or underscore, or contains characters other than letters, digits, and underscores. A name the validator rejects is not used in consumers either, so the generated consumer falls back to the default name and stays compilable.

```csharp
[SingletonDIProvide("invalid-name")]  // DM0013: hyphen is not allowed
public class DatabaseService { }
```

### DM0014: Property name is a reserved keyword

Occurs when a property name in `[SingletonDIProvide]` is a reserved C# keyword. Prefixing it with `@` is the supported way to name the property after a keyword, and the escaped name is emitted verbatim.

```csharp
[SingletonDIProvide("class")]  // DM0014: reserved word
public class DatabaseService { }

[SingletonDIProvide("@class")]  // accepted, emitted as @class
public class CachedService { }
```

### DM0015: Generic types are not supported for singletons

Occurs when the `[SingletonDIProvide]` attribute is applied to a generic type. SingletonDI does not support generic types as singletons, as a separate instance would be required for each generic parameter. A provider that has no type parameters of its own but is nested in a generic type is reported by DM0035 instead, because the reason above does not apply to it.

```csharp
[SingletonDIProvide]  // Error DM0015
public class Repository<T>
{
}
```

### DM0016: Invalid ServiceType

Occurs when `ServiceType` is not a reference type the provider converts to implicitly. A downward cast does not qualify: the instance would fail the cast when it is resolved, so a contract the provider does not implement or derive from is rejected. A provider can declare only one `ServiceType`.

```csharp
public interface ICacheService { }

[SingletonDIProvide(ServiceType = typeof(ICacheService))]  // DM0016
public sealed class DatabaseService { }
```

### DM0017: Composition root is required for an unmapped consumer dependency

Occurs when an executable project without `SingletonDICompositionRoot=true` has a consumer dependency that is not mapped by its local provider map. This includes an interface or abstract contract declared in the current assembly, a contract supplied by another assembly, and a concrete provider from another assembly. Library consumer projects do not receive `DM0017`; their dependencies are validated by the executable composition root.

### DM0018: No provider for requested service

Occurs when any dependency in the complete composition-root graph has no provider in the service map. This includes dependencies declared by local or external providers, dependencies declared by referenced consumers, and dependencies declared by consumers in the root project. The message identifies each missing service by its assembly-qualified identity.

### DM0019: Multiple providers for service key

Occurs when multiple providers map to the same concrete or contract service key. It also reports ambiguity when one fully qualified source name is associated with multiple service or provider identities, including the same `App.Service` name in the root and a referenced provider assembly. These identities are not merged by fully qualified name; their assembly identities are preserved in the diagnostic.

### DM0020: Provider module marker is missing

Occurs when a referenced public provider assembly does not contain the generated `SingletonDIProviderModuleAttribute` assembly marker. The provider package or project must be built with a compatible SingletonDI generator/runtime protocol before a composition root can bootstrap it.

### DM0021: Provider module bootstrap is missing

Occurs when a marked provider assembly does not expose a public static parameterless `Bootstrap()` method. Rebuild the provider with a compatible SingletonDI generator.

### DM0022: Provider type is not accessible

Occurs when generated code cannot name a provider, service contract, or provider dependency because of its declared accessibility or a file-local containing type. Use a public or assembly-accessible type.

### DM0023: Generic InitializeAsync is not supported

Occurs when a parameterless `InitializeAsync` method declares its own type parameters. Remove the method type parameters or expose initialization through a non-generic method.

### DM0024: Open generic dependency is not supported

Occurs when a provider or consumer uses an unbound generic type such as `typeof(IContract<>)`. Use a constructed dependency type instead.

### DM0025: Consumer property name already exists

Occurs when a generated dependency property would collide with a member already declared by the consumer or an inherited type. Rename the dependency property or remove the conflicting member; the generator omits the colliding property.

The check reaches further than the sentence above: a member with the same name on an implemented interface triggers DM0025 as well, even though a class does not inherit members from its interfaces, and the match is by name only, so a field, method, or nested type of that name triggers it too.

### DM0026: Consumer containing type is not partial

Occurs when a nested consumer has a containing type that cannot be reopened. Declare every containing type as `partial`.

### DM0027: Generated code requires C# 9 or newer

Occurs when the compilation language version is below C# 9. Upgrade the project language version or use a compatible target framework.

### DM0028: File-scoped consumers require C# 10

Occurs when a file-scoped consumer declaration is compiled below C# 10. Upgrade the language version or use a block-scoped namespace.

### DM0029: File-local consumer is not supported

Occurs when a consumer is declared with the `file` accessibility modifier. Generated code cannot reopen a file-local type, so no consumer partial is emitted. Declare the consumer with `internal` or `public` accessibility.

### DM0030: Aliased service type is not supported

Occurs when a provider `ServiceType` or a consumer dependency names a type through an `extern alias`. Generated code cannot reproduce the alias, because a referenced provider assembly does not carry the consumer's alias declarations. Reference the type by its global name instead.

### DM0031: Consumer type parameter attributes are not supported

Occurs when a consumer declares attributes on its type parameters. Generated code reopens the consumer declaration without those attribute targets, which would produce a compiler error. Move the attribute to the type parameter usage or remove it.

### DM0032: Provider required members are not supported

Occurs when a provider has required members and its public parameterless constructor does not declare `[SetsRequiredMembers]`. Generated construction cannot satisfy the members. Mark the constructor with `[SetsRequiredMembers]` or remove `required` from the provider members.

### DM0033: Nullable initializer return type is not supported

Occurs when a provider `InitializeAsync` method returns `Task?` or `ValueTask?`. The generated registration requires a non-nullable task, because a `null` return would break initialization ordering. Return a non-nullable `Task` or `ValueTask`.

### DM0034: InitializeAsync has an unsupported return type

Occurs when a provider declares a parameterless `InitializeAsync` whose return type is neither `Task` nor `ValueTask`, for example `async void` or `Task<int>`. The method is not registered as the initializer. Return a non-generic `Task` or `ValueTask`.

### DM0035: Provider nested in a generic type is not supported

Occurs when a provider that has no type parameters of its own is declared inside a generic type. Generated code cannot name such a provider, because its only fully qualified name is not a legal C# source. Declare the provider in a non-generic type.

## Limitations

- **Singletons only** — the library does not support Scoped or Transient lifestyles
- **One contract per provider** — a provider can declare at most one `ServiceType`; multiple contract types for one implementation are not supported
- **One process-wide root** — the runtime registry is shared by the whole process; independent composition roots in one process are not supported
- **Public external providers only** — composition roots import public `[SingletonDIProvide]` types from referenced assemblies
- **Shared contracts for reverse dependencies** — a library that consumes an app-owned implementation must depend on a lower-level contract assembly, not on the app
- **`struct` not allowed** — only `class` can be a provider

## License

MIT License
