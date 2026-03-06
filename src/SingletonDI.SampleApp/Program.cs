using DependencyManager.Generated;
using SingletonDI.Attributes;

namespace SingletonDI.SampleApp;

// ============================================================================
// LEVEL 0: Base services (no dependencies)
// ============================================================================

/// <summary>
/// Level 0: Configuration service - base singleton with no dependencies.
/// </summary>
[SingletonDIProvide]
public class ConfigService : IDisposable
{
    private bool _disposed;

    public string ConnectionString { get; } = "Server=localhost;Database=SampleDb";

    public ConfigService()
    {
        Console.WriteLine("[INIT] Level 0: ConfigService initializing...");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 0: ConfigService disposing...");
        _disposed = true;
    }
}

/// <summary>
/// Level 0: Logger service - base singleton with no dependencies.
/// </summary>
[SingletonDIProvide]
public class LoggerService : IDisposable
{
    private bool _disposed;

    public LoggerService()
    {
        Console.WriteLine("[INIT] Level 0: LoggerService initializing...");
    }

    public void Log(string message)
    {
        Console.WriteLine($"[LOG] {message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 0: LoggerService disposing...");
        _disposed = true;
    }
}

// ============================================================================
// LEVEL 1: Services depending on Level 0
// ============================================================================

/// <summary>
/// Level 1: Database service - depends on ConfigService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(ConfigService))]
public partial class DatabaseService : IDisposable
{
    private bool _disposed;

    public DatabaseService()
    {
        Console.WriteLine("[INIT] Level 1: DatabaseService initializing...");
        Console.WriteLine($"  -> Using connection: {ConfigServiceInstance.ConnectionString}");
    }

    public void ExecuteQuery(string sql)
    {
        Console.WriteLine($"[DatabaseService] Executing: {sql}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 1: DatabaseService disposing...");
        _disposed = true;
    }
}

/// <summary>
/// Level 1: Cache service - depends on ConfigService and LoggerService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(ConfigService), typeof(LoggerService))]
public partial class CacheService : IDisposable
{
    private bool _disposed;

    public CacheService()
    {
        Console.WriteLine("[INIT] Level 1: CacheService initializing...");
        LoggerServiceInstance.Log("CacheService created");
    }

    public void Set(string key, string value)
    {
        LoggerServiceInstance.Log($"Cache SET: {key}={value}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 1: CacheService disposing...");
        _disposed = true;
    }
}

// ============================================================================
// LEVEL 2: Services depending on Level 1
// ============================================================================

/// <summary>
/// Level 2: User repository - depends on DatabaseService and CacheService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(DatabaseService), typeof(CacheService))]
public partial class UserRepository : IDisposable
{
    private bool _disposed;

    public UserRepository()
    {
        Console.WriteLine("[INIT] Level 2: UserRepository initializing...");
        DatabaseServiceInstance.ExecuteQuery("SELECT * FROM Users");
    }

    public string GetUser(int id)
    {
        DatabaseServiceInstance.ExecuteQuery($"SELECT * FROM Users WHERE Id = {id}");
        return $"User{id}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 2: UserRepository disposing...");
        _disposed = true;
    }
}

/// <summary>
/// Level 2: Product repository - depends on DatabaseService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(DatabaseService))]
public partial class ProductRepository : IDisposable
{
    private bool _disposed;

    public ProductRepository()
    {
        Console.WriteLine("[INIT] Level 2: ProductRepository initializing...");
        DatabaseServiceInstance.ExecuteQuery("SELECT * FROM Products");
    }

    public string GetProduct(int id)
    {
        DatabaseServiceInstance.ExecuteQuery($"SELECT * FROM Products WHERE Id = {id}");
        return $"Product{id}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 2: ProductRepository disposing...");
        _disposed = true;
    }
}

// ============================================================================
// LEVEL 3: Services depending on Level 2
// ============================================================================

/// <summary>
/// Level 3: User service - depends on UserRepository and LoggerService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(UserRepository), typeof(LoggerService))]
public partial class UserService : IDisposable
{
    private bool _disposed;

    public UserService()
    {
        Console.WriteLine("[INIT] Level 3: UserService initializing...");
        LoggerServiceInstance.Log("UserService created");
    }

    public string GetUserName(int id)
    {
        var user = UserRepositoryInstance.GetUser(id);
        LoggerServiceInstance.Log($"Retrieved user: {user}");
        return user;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 3: UserService disposing...");
        _disposed = true;
    }
}

/// <summary>
/// Level 3: Product service - depends on ProductRepository and CacheService.
/// </summary>
[SingletonDIProvide]
[SingletonDIConsume(typeof(ProductRepository), typeof(CacheService))]
public partial class ProductService : IDisposable
{
    private bool _disposed;

    public ProductService()
    {
        Console.WriteLine("[INIT] Level 3: ProductService initializing...");
        CacheServiceInstance.Set("products_loaded", "true");
    }

    public string GetProductName(int id)
    {
        var product = ProductRepositoryInstance.GetProduct(id);
        CacheServiceInstance.Set($"product_{id}", product);
        return product;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Console.WriteLine("[DISPOSE] Level 3: ProductService disposing...");
        _disposed = true;
    }
}

// ============================================================================
// CONSUMER: Application controller using Level 3 services
// ============================================================================

/// <summary>
/// Consumer class that uses UserService and ProductService.
/// </summary>
[SingletonDIConsume(typeof(UserService), typeof(ProductService))]
public partial class AppController
{
    public void Run()
    {
        Console.WriteLine("\n=== Running Application ===");

        var user = UserServiceInstance.GetUserName(1);
        Console.WriteLine($"Got user: {user}");

        var product = ProductServiceInstance.GetProductName(100);
        Console.WriteLine($"Got product: {product}");

        Console.WriteLine("=== Application Complete ===\n");
    }
}

// ============================================================================
// MAIN: Entry point
// ============================================================================

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("SingletonDI Demo - 4 Levels of Dependencies");
        Console.WriteLine("========================================\n");

        Console.WriteLine("--- INITIALIZATION PHASE ---");

        // Initialize all singletons in dependency order
        await SingletonDIInitializer.InitializeAsync();

        Console.WriteLine("\n--- EXECUTION PHASE ---");

        // Use the consumer
        var controller = new AppController();
        controller.Run();
    }
}
