using DependencyManager.Generated;
using SingletonDI.Attributes;

namespace SingletonDI.SampleApp;

// Example 1: Simple provider with synchronous initialization (constructor only)
[SingletonDIProvide]
public class DatabaseService
{
    public string ConnectionString { get; private set; } = "Server=localhost;Database=Sample;Connected=true";

    public DatabaseService()
    {
        Console.WriteLine("[DatabaseService] Constructor called - connecting to database...");
        Console.WriteLine("[DatabaseService] Database connected successfully!");
    }
}

// Example 2: Provider with async initialization via InitializeAsync method
[SingletonDIProvide]
public class UserService
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
[SingletonDIProvide]
[SingletonDIConsume(typeof(DatabaseService))]
public partial class OrderService
{
    // DatabaseService доступен через сгенерированное свойство DatabaseServiceInstance

    public OrderService()
    {
        Console.WriteLine("[OrderService] Constructor called - setting up order processing...");
        Console.WriteLine($"[OrderService] Using database: {DatabaseServiceInstance?.ConnectionString}");
    }
}

// Example 4: Consumer class
[SingletonDIConsume(typeof(DatabaseService), typeof(UserService), typeof(OrderService))]
public partial class OrderController
{
    public void ProcessOrder()
    {
        // All dependencies are already initialized via SingletonInitializer
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
