using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Refactoring;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for CodeFixProviders in SingletonDI.Refactoring.
/// These tests verify that code fixes preserve comments, XML documentation, and unrelated members.
/// </summary>
public class CodeFixProviderTests
{
    [Fact]
    public async Task DM0004_ProductionProviderRegistersAndAppliesCodeFix()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
                private Service()
                {
                }
            }
            """;

        var result = await CodeFixTestHarness.ApplyFirstAsync(
            source,
            "DM0004",
            new SingletonDIProviderCodeFixProvider(),
            "Add public parameterless constructor");

        Assert.Equal("Add public parameterless constructor", result.Action.Title);
        var compilation = await result.Document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Id == "DM0004");
    }

#region SingletonDIProviderCodeFixProvider Tests

    [Fact]
    public async Task DM0004_MakeExistingConstructorPublic()
    {
        // Class has a private parameterless constructor - fix should change it to public
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Тестовый класс для проверки исправления DM0004.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyClass
                   {
                       // Это приватный конструктор - должен стать публичным
                       private MyClass() { }
                       
                       /* Многострочный комментарий
                          который должен сохраниться */
                       private int _counter;  // Лишнее поле
                       
                       /// <summary>
                       /// Лишнее свойство для проверки сохранения.
                       /// </summary>
                       public string Name { get; set; } = "Test";
                       
                       /// <summary>
                       /// Лишний метод.
                       /// </summary>
                       public void DoSomething() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Тестовый класс для проверки исправления DM0004.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyClass
                       {
                           // Это приватный конструктор - должен стать публичным
                           public MyClass() { }
                       
                           /* Многострочный комментарий
                              который должен сохраниться */
                           private int _counter;  // Лишнее поле
                       
                           /// <summary>
                           /// Лишнее свойство для проверки сохранения.
                           /// </summary>
                           public string Name { get; set; } = "Test";
                       
                           /// <summary>
                           /// Лишний метод.
                           /// </summary>
                           public void DoSomething() { }
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0004");
    }

    [Fact]
    public async Task DM0004_MakeExistingConstructorPublic_WithMembers()
    {
        // Class has a private parameterless constructor and members - fix should change it to public
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис с приватным конструктором и членами.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyService
                   {
                       // Приватный конструктор
                       private MyService() { }
                       
                       // Счётчик операций
                       private int _operationCount;
                       
                       /// <summary>
                       /// Выполняет полезную работу.
                       /// </summary>
                       public void DoWork() { }
                       
                       /* Дополнительный метод
                          с многострочным комментарием */
                       public int GetCount() => _operationCount;
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис с приватным конструктором и членами.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyService
                       {
                           // Приватный конструктор
                           public MyService() { }
                       
                           // Счётчик операций
                           private int _operationCount;
                       
                           /// <summary>
                           /// Выполняет полезную работу.
                           /// </summary>
                           public void DoWork() { }
                       
                           /* Дополнительный метод
                              с многострочным комментарием */
                           public int GetCount() => _operationCount;
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0004");
    }

    [Fact]
    public async Task DM0004_AddPublicConstructor_WithExistingConstructor()
    {
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис с параметризованным конструктором.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyService
                   {
                       // Параметризованный конструктор
                       public MyService(int a) { }
                       
                       // Поле для хранения значения
                       private readonly int _value;
                       
                       /// <summary>
                       /// Выполняет работу.
                       /// </summary>
                       public void DoWork() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис с параметризованным конструктором.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyService
                       {
                           public MyService()
                           {
                           }
                       
                           // Параметризованный конструктор
                           public MyService(int a) { }
                       
                           // Поле для хранения значения
                           private readonly int _value;
                       
                           /// <summary>
                           /// Выполняет работу.
                           /// </summary>
                           public void DoWork() { }
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0004");
    }

    [Fact]
    public async Task DM0005_MakeMethodPublic_Private()
    {
        var test = """
                   using System.Threading.Tasks;
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Класс с приватным методом InitializeAsync.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyClass
                   {
                       // Приватное поле
                       private bool _isInitialized;
                       
                       // Приватный метод инициализации - должен стать публичным
                       private Task InitializeAsync() => Task.CompletedTask;
                       
                       /// <summary>
                       /// Проверяет инициализацию.
                       /// </summary>
                       public bool IsInitialized => _isInitialized;
                       
                       /* Дополнительный метод
                          который не должен изменяться */
                       public void Reset() => _isInitialized = false;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Класс с приватным методом InitializeAsync.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyClass
                       {
                           // Приватное поле
                           private bool _isInitialized;
                       
                           // Приватный метод инициализации - должен стать публичным
                           public Task InitializeAsync() => Task.CompletedTask;
                       
                           /// <summary>
                           /// Проверяет инициализацию.
                           /// </summary>
                           public bool IsInitialized => _isInitialized;
                       
                           /* Дополнительный метод
                              который не должен изменяться */
                           public void Reset() => _isInitialized = false;
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0005");
    }

    [Fact]
    public async Task DM0005_MakeMethodPublic_Protected()
    {
        var test = """
                   using System.Threading.Tasks;
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Класс с защищённым методом InitializeAsync.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyClass
                   {
                       // Защищённый метод - должен стать публичным
                       protected Task InitializeAsync() => Task.CompletedTask;
                       
                       // Константа
                       private const string DefaultName = "Default";
                       
                       /// <summary>
                       /// Имя экземпляра.
                       /// </summary>
                       public string Name { get; set; } = DefaultName;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Класс с защищённым методом InitializeAsync.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyClass
                       {
                           // Защищённый метод - должен стать публичным
                           public Task InitializeAsync() => Task.CompletedTask;
                       
                           // Константа
                           private const string DefaultName = "Default";
                       
                           /// <summary>
                           /// Имя экземпляра.
                           /// </summary>
                           public string Name { get; set; } = DefaultName;
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0005");
    }

    // Note: DM0005 is not reported for internal methods - internal is accessible within the assembly
    // DM0005 is only reported for private and protected methods

    [Fact]
    public async Task DM0012_RemoveStaticModifier()
    {
        var test = """
                   using System.Threading.Tasks;
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Класс со статическим методом InitializeAsync.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyClass
                   {
                       // Статический метод - должен стать экземплярным
                       public static Task InitializeAsync() => Task.CompletedTask;
                       
                       // Статическое поле
                       private static int _instanceCount;
                       
                       /// <summary>
                       /// Экземплярное свойство.
                       /// </summary>
                       public int Id { get; } = ++_instanceCount;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Класс со статическим методом InitializeAsync.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyClass
                       {
                           // Статический метод - должен стать экземплярным
                           public Task InitializeAsync() => Task.CompletedTask;
                       
                           // Статическое поле
                           private static int _instanceCount;
                       
                           /// <summary>
                           /// Экземплярное свойство.
                           /// </summary>
                           public int Id { get; } = ++_instanceCount;
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0012");
    }

    [Fact]
    public async Task DM0012_RemoveStaticModifier_WithOtherModifiers()
    {
        var test = """
                   using System.Threading.Tasks;
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Класс с async static методом InitializeAsync.
                   /// </summary>
                   [SingletonDIProvide]
                   class MyClass
                   {
                       // Асинхронный статический метод
                       public static async Task InitializeAsync() => await Task.Delay(1);
                       
                       /* Многострочный комментарий
                          перед полем */
                       private string _status = "Pending";
                       
                       /// <summary>
                       /// Статус объекта.
                       /// </summary>
                       public string Status => _status;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Класс с async static методом InitializeAsync.
                       /// </summary>
                       [SingletonDIProvide]
                       class MyClass
                       {
                           // Асинхронный статический метод
                           public async Task InitializeAsync() => await Task.Delay(1);
                       
                           /* Многострочный комментарий
                              перед полем */
                           private string _status = "Pending";
                       
                           /// <summary>
                           /// Статус объекта.
                           /// </summary>
                           public string Status => _status;
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0012");
    }

#endregion

#region SingletonDIConsumerCodeFixProvider Tests

    [Fact]
    public async Task DM0010_RemoveDuplicateType()
    {
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис-провайдер.
                   /// </summary>
                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       // Поле в сервисе
                       private int _value;
                       
                       /// <summary>
                       /// Получить значение.
                       /// </summary>
                       public int GetValue() => _value;
                   }

                   /// <summary>
                   /// Консьюмер с дублирующимся типом.
                   /// </summary>
                   [SingletonDIConsume(typeof(MyService), typeof(MyService))]
                   partial class MyClass
                   {
                       // Комментарий в консьюмере
                       private string _name = "Consumer";
                       
                       /// <summary>
                       /// Имя консьюмера.
                       /// </summary>
                       public string Name => _name;
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис-провайдер.
                       /// </summary>
                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           // Поле в сервисе
                           private int _value;
                       
                           /// <summary>
                           /// Получить значение.
                           /// </summary>
                           public int GetValue() => _value;
                       }

                       /// <summary>
                       /// Консьюмер с дублирующимся типом.
                       /// </summary>
                       [SingletonDIConsume(typeof(MyService))]
                       partial class MyClass
                       {
                           // Комментарий в консьюмере
                           private string _name = "Consumer";
                       
                           /// <summary>
                           /// Имя консьюмера.
                           /// </summary>
                           public string Name => _name;
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0010");
    }

    [Fact]
    public async Task DM0010_RemoveDuplicateType_MultipleDuplicates()
    {
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис A.
                   /// </summary>
                   [SingletonDIProvide]
                   public class ServiceA 
                   { 
                       // Идентификатор сервиса A
                       public int IdA { get; set; }
                   }

                   /// <summary>
                   /// Сервис B.
                   /// </summary>
                   [SingletonDIProvide]
                   public class ServiceB 
                   { 
                       // Идентификатор сервиса B
                       public int IdB { get; set; }
                   }

                   /// <summary>
                   /// Консьюмер с множественными дубликатами.
                   /// </summary>
                   [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB), typeof(ServiceA))]
                   partial class MyClass
                   {
                       /* Многострочный комментарий
                          в классе MyClass */
                       private bool _isActive = true;
                       
                       /// <summary>
                       /// Активность консьюмера.
                       /// </summary>
                       public bool IsActive => _isActive;
                   }
                   """;

        // Note: The code fix removes the first occurrence of the duplicate (the second typeof(ServiceA))
        // So the result is typeof(ServiceA), typeof(ServiceB)
        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис A.
                       /// </summary>
                       [SingletonDIProvide]
                       public class ServiceA 
                       { 
                           // Идентификатор сервиса A
                           public int IdA { get; set; }
                       }

                       /// <summary>
                       /// Сервис B.
                       /// </summary>
                       [SingletonDIProvide]
                       public class ServiceB 
                       { 
                           // Идентификатор сервиса B
                           public int IdB { get; set; }
                       }

                       /// <summary>
                       /// Консьюмер с множественными дубликатами.
                       /// </summary>
                       [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB))]
                       partial class MyClass
                       {
                           /* Многострочный комментарий
                              в классе MyClass */
                           private bool _isActive = true;
                       
                           /// <summary>
                           /// Активность консьюмера.
                           /// </summary>
                           public bool IsActive => _isActive;
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0010");
    }

    [Fact]
    public async Task DM0010_KeepsAttributeAndItsDocumentation()
    {
        var test = """
                   using System;
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис-контракт.
                   /// </summary>
                   public interface IService
                   {
                   }

                   /// <summary>
                   /// Провайдер сервиса.
                   /// </summary>
                   [SingletonDIProvide(ServiceType = typeof(IService))]
                   public class Service : IService
                   {
                   }

                   /// <summary>
                   /// Консьюмер с дубликатом в отдельном списке атрибутов.
                   /// </summary>
                   [Obsolete("legacy")]
                   [SingletonDIConsume(typeof(IService), typeof(IService))]
                   public partial class MyClass
                   {
                   }
                   """;

        var expected = """
                       using System;
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис-контракт.
                       /// </summary>
                       public interface IService
                       {
                       }

                       /// <summary>
                       /// Провайдер сервиса.
                       /// </summary>
                       [SingletonDIProvide(ServiceType = typeof(IService))]
                       public class Service : IService
                       {
                       }

                       /// <summary>
                       /// Консьюмер с дубликатом в отдельном списке атрибутов.
                       /// </summary>
                       [Obsolete("legacy")]
                       [SingletonDIConsume(typeof(IService))]
                       public partial class MyClass
                       {
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0010");
    }

    #endregion

    #region SingletonDIPartialCodeFixProvider Tests

    [Fact]
    public async Task DM0007_AddPartialModifier_ToSealedClass()
    {
        // sealed class without partial - fix should add partial before 'class' keyword
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис-провайдер.
                   /// </summary>
                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       public int GetValue() => 42;
                   }

                   /// <summary>
                   /// Консьюмер без модификатора partial.
                   /// </summary>
                   [SingletonDIConsume(typeof(MyService))]
                   public sealed class MyConsumer
                   {
                       private string _name = "Consumer";
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис-провайдер.
                       /// </summary>
                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           public int GetValue() => 42;
                       }

                       /// <summary>
                       /// Консьюмер без модификатора partial.
                       /// </summary>
                       [SingletonDIConsume(typeof(MyService))]
                       public sealed partial class MyConsumer
                       {
                           private string _name = "Consumer";
                       }
                       """;

        await VerifyPartialCodeFixAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_ToAbstractClass()
    {
        // abstract class without partial - fix should add partial before 'class' keyword
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       public int GetValue() => 42;
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   public abstract class MyConsumer
                   {
                       private string _name = "Consumer";
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           public int GetValue() => 42;
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       public abstract partial class MyConsumer
                       {
                           private string _name = "Consumer";
                       }
                       """;

        await VerifyPartialCodeFixAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_ToStaticClass()
    {
        // static class without partial - fix should add partial before 'class' keyword
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       public int GetValue() => 42;
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   public static class MyConsumer
                   {
                       private static string _name = "Consumer";
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           public int GetValue() => 42;
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       public static partial class MyConsumer
                       {
                           private static string _name = "Consumer";
                       }
                       """;

        await VerifyPartialCodeFixAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_ToInternalSealedClass()
    {
        // internal sealed class without partial - fix should add partial before 'class' keyword
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       public int GetValue() => 42;
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   internal sealed class MyConsumer
                   {
                       private string _name = "Consumer";
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           public int GetValue() => 42;
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       internal sealed partial class MyConsumer
                       {
                           private string _name = "Consumer";
                       }
                       """;

        await VerifyPartialCodeFixAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_ToPrivateNestedSealedClass()
    {
        // private nested sealed class without partial - fix should add partial before 'class' keyword
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       public int GetValue() => 42;
                   }

                   public class Container
                   {
                       [SingletonDIConsume(typeof(MyService))]
                       private sealed class MyConsumer
                       {
                           private string _name = "Consumer";
                       }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           public int GetValue() => 42;
                       }

                       public class Container
                       {
                           [SingletonDIConsume(typeof(MyService))]
                           private sealed partial class MyConsumer
                           {
                               private string _name = "Consumer";
                           }
                       }
                       """;

        await VerifyPartialCodeFixAsync(test, expected, "DM0007");
    }

    #endregion

    #region Helper Methods

    private static async Task VerifyProviderCodeFixAsync(string testSource, string expectedSource, string diagnosticId)
    {
        var codeFixProvider = new SingletonDIProviderCodeFixProvider();
        await VerifyCodeFixAsync(testSource, expectedSource, diagnosticId, codeFixProvider);
    }

    private static async Task VerifyConsumerCodeFixAsync(string testSource, string expectedSource, string diagnosticId)
    {
        var codeFixProvider = new SingletonDIConsumerCodeFixProvider();
        await VerifyCodeFixAsync(testSource, expectedSource, diagnosticId, codeFixProvider);
    }

    private static async Task VerifyPartialCodeFixAsync(string testSource, string expectedSource, string diagnosticId)
    {
        var codeFixProvider = new SingletonDIPartialCodeFixProvider();
        await VerifyCodeFixAsync(testSource, expectedSource, diagnosticId, codeFixProvider);
    }

    private static async Task VerifyCodeFixAsync(
        string testSource,
        string expectedSource,
        string diagnosticId,
        CodeFixProvider codeFixProvider)
    {
        var expectedTitle = (codeFixProvider, diagnosticId) switch
        {
            (SingletonDIProviderCodeFixProvider, "DM0004") => "Add public parameterless constructor",
            (SingletonDIProviderCodeFixProvider, "DM0005") => "Make method public",
            (SingletonDIProviderCodeFixProvider, "DM0012") => "Remove static modifier",
            (SingletonDIConsumerCodeFixProvider, "DM0010") => "Remove duplicate type",
            (SingletonDIPartialCodeFixProvider, "DM0007") => "Add 'partial' modifier",
            _ => throw new ArgumentException($"Unknown diagnostic/provider combination: {diagnosticId}")
        };

        var result = await CodeFixTestHarness.ApplyFirstAsync(
            testSource,
            diagnosticId,
            codeFixProvider,
            expectedTitle);
        var fixedSource = (await result.Document.GetTextAsync()).ToString();
        var normalizedExpected = NormalizeWhitespace(expectedSource);

        Assert.Equal(normalizedExpected, NormalizeWhitespace(fixedSource));
        var compilation = await result.Document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Id == diagnosticId);
    }

    private static string NormalizeWhitespace(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        return root.NormalizeWhitespace().ToFullString();
    }

#endregion
}
