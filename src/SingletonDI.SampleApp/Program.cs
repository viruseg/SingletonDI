using DependencyManager.Generated;
using SingletonDI.Attributes;

namespace SingletonDI.SampleApp;

// Example 1: Simple sync provider
[Provide]
public class DatabaseService : IInitializeSync
{
    public string ConnectionString { get; private set; } = "Server=localhost;Database=Sample";

    public void Initialize()
    {
        Console.WriteLine("[DatabaseService] Initialize() called - connecting to database...");
        // Simulate database connection
        ConnectionString = "Server=localhost;Database=Sample;Connected=true";
        Console.WriteLine("[DatabaseService] Database connected successfully!");
    }
}

// Example 2: Async provider
[Provide]
public class UserService : IInitializeAsync
{
    public string UserName { get; private set; } = "DefaultUser";

    public async Task InitializeAsync()
    {
        Console.WriteLine("[UserService] InitializeAsync() called - loading user data...");
        // Simulate async loading
        await Task.Delay(100);
        UserName = "LoadedUser";
        Console.WriteLine("[UserService] User data loaded!");
    }
}

// Example 3: Provider that depends on another provider
[Provide]
[Consume(typeof(DatabaseService))]
public partial class OrderService : IInitializeSync
{
    // DatabaseService доступен через сгенерированное свойство DatabaseServiceInstance

    public void Initialize()
    {
        Console.WriteLine("[OrderService] Initialize() called - setting up order processing...");
        Console.WriteLine($"[OrderService] Using database: {DatabaseServiceInstance?.ConnectionString}");
    }
}

// Example 4: Consumer class
[Consume(typeof(DatabaseService), typeof(UserService), typeof(OrderService))]
public partial class OrderController
{
    public void ProcessOrder()
    {
        // All dependencies are already initialized via ModuleInitializer
        Console.WriteLine("\n=== OrderController ===");
        Console.WriteLine($"Database: {DatabaseServiceInstance?.ConnectionString}");
        Console.WriteLine($"User: {UserServiceInstance?.UserName}");
        Console.WriteLine("Order processed successfully!");
    }
}

// Main program entry point
public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== SingletonDI Sample Application ===\n");
        Console.WriteLine("Starting application...");

        // Explicitly initialize the singleton container
        await SingletonInitializer.InitializeAsync();

        // Use the consumer
        var controller = new OrderController();
        controller.ProcessOrder();

        Console.WriteLine("\n=== Application completed ===");
    }
}
