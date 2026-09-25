using System.Reflection;
using System.Composition;
using Microsoft.CodeAnalysis.CodeRefactorings;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Refactoring;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for CodeFixProviders in SingletonDI.Refactoring.
/// </summary>
/// <remarks>
/// Every fix is compared on normalized whitespace, so the structural check tolerates the
/// inconsistent indentation of the fixtures. Comments, documentation and preprocessor directives
/// are dropped by that comparison and are asserted separately.
/// </remarks>
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

    #region Formatting Scope Tests

    [Fact]
    public async Task DM0005_MakeMethodPublic_LeavesMethodBodyUnformatted()
    {
        // Annotating the whole method for formatting re-lays out every token inside it, so a fix
        // that changes one modifier rewrote the body and produced a diff on unrelated lines.
        const string test = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                private Task InitializeAsync()
                {
                    int total=0;
                    for(int i=0;i<10;i++)
                    {
                        total+=i*2;
                    }
                    return Task.CompletedTask;
                }
            }
            """;

        const string expected = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                public Task InitializeAsync()
                {
                    int total=0;
                    for(int i=0;i<10;i++)
                    {
                        total+=i*2;
                    }
                    return Task.CompletedTask;
                }
            }
            """;

        await VerifyCodeFixExactlyAsync(test, expected, "DM0005", new SingletonDIProviderCodeFixProvider());
    }

    [Fact]
    public async Task DM0012_RemoveStaticModifier_LeavesMethodBodyUnformatted()
    {
        const string test = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                public static Task InitializeAsync()
                {
                    int value=1;
                    return Task.CompletedTask;
                }
            }
            """;

        const string expected = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                public Task InitializeAsync()
                {
                    int value=1;
                    return Task.CompletedTask;
                }
            }
            """;

        await VerifyCodeFixExactlyAsync(test, expected, "DM0012", new SingletonDIProviderCodeFixProvider());
    }

    [Fact]
    public async Task DM0004_MakeConstructorPublic_LeavesRemainingMembersUnformatted()
    {
        const string test = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                private Service()
                {
                    int counter=0;
                }

                public int GetValue()
                {
                    int result=1;
                    return result;
                }
            }
            """;

        const string expected = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            class Service
            {
                public Service()
                {
                    int counter=0;
                }

                public int GetValue()
                {
                    int result=1;
                    return result;
                }
            }
            """;

        await VerifyCodeFixExactlyAsync(test, expected, "DM0004", new SingletonDIProviderCodeFixProvider());
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

    [Fact]
    public async Task DM0007_AddPartialModifier_WithoutModifiersKeepsDocumentation()
    {
        // No modifiers at all, and the documentation sits between the attribute list and the
        // keyword - the inserted 'partial' becomes the declaration's first token there, so the
        // documentation has to move with it instead of being stranded inside the modifier list.
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService
                   {
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   /// <summary>
                   /// Консьюмер без модификаторов.
                   /// </summary>
                   class MyConsumer
                   {
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService
                       {
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       /// <summary>
                       /// Консьюмер без модификаторов.
                       /// </summary>
                       partial class MyConsumer
                       {
                       }
                       """;

        await VerifyPartialCodeFixExactlyAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_WithoutModifiersKeepsRegionDirective()
    {
        // A #region between the attribute list and the keyword is leading trivia of the keyword,
        // so it used to end up stranded after the inserted modifier.
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService
                   {
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   #region MyConsumer
                   class MyConsumer
                   {
                   }
                   #endregion
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService
                       {
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       #region MyConsumer
                       partial class MyConsumer
                       {
                       }
                       #endregion
                       """;

        await VerifyPartialCodeFixExactlyAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_WithoutModifiersKeepsLineComment()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService
                   {
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   // Обычный комментарий.
                   class MyConsumer
                   {
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService
                       {
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       // Обычный комментарий.
                       partial class MyConsumer
                       {
                       }
                       """;

        await VerifyPartialCodeFixExactlyAsync(test, expected, "DM0007");
    }

    [Fact]
    public async Task DM0007_AddPartialModifier_ToRecordWithoutModifiers()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService
                   {
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   record MyConsumer;
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService
                       {
                       }

                       [SingletonDIConsume(typeof(MyService))]
                       partial record MyConsumer;
                       """;

        await VerifyPartialCodeFixExactlyAsync(test, expected, "DM0007");
    }

    #endregion

    #region Helper Methods

    private static async Task VerifyProviderCodeFixAsync(string testSource, string expectedSource, string diagnosticId)
    {
        var codeFixProvider = new SingletonDIProviderCodeFixProvider();
        await VerifyCodeFixAsync(testSource, expectedSource, diagnosticId, codeFixProvider);
    }

    [Fact]
    public void EveryCodeFixProviderSupportsFixAll()
    {
        // A provider whose GetFixAllProvider returns null silently disables fix-all in the IDE.
        // Nothing asserted that, and AGENTS.md claimed fix-all was covered.
        foreach (var provider in new CodeFixProvider[]
                 {
                     new SingletonDIProviderCodeFixProvider(),
                     new SingletonDIPartialCodeFixProvider(),
                     new SingletonDIConsumerCodeFixProvider()
                 })
        {
            Assert.NotNull(provider.GetFixAllProvider());
        }
    }

    [Fact]
    public void EveryCodeFixProviderFixesExactlyTheDiagnosticsTheGeneratorReports()
    {
        var fixable = new CodeFixProvider[]
            {
                new SingletonDIProviderCodeFixProvider(),
                new SingletonDIPartialCodeFixProvider(),
                new SingletonDIConsumerCodeFixProvider()
            }
            .SelectMany(provider => provider.FixableDiagnosticIds)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var reported = typeof(SingletonDI.Generator.DiagnosticDescriptors)
            .Assembly
            .GetType("SingletonDI.Generator.DiagnosticDescriptors")!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => (DiagnosticDescriptor)field.GetValue(null)!)
            .Select(descriptor => descriptor.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(fixable);
        Assert.All(fixable, id => Assert.Contains(id, reported));
    }

    [Fact]
    public void EveryCodeFixAndRefactoringProviderIsExportedForMef()
    {
        // The code fixes ship inside the package under analyzers/dotnet/cs, so a provider the host
        // cannot discover never loads in the IDE while the suite stays green - every other test
        // constructs the providers directly. Assert the export contract each one declares.
        var providers = new CodeFixProvider[]
            {
                new SingletonDIProviderCodeFixProvider(),
                new SingletonDIPartialCodeFixProvider(),
                new SingletonDIConsumerCodeFixProvider()
            }
            .Cast<object>()
            .Concat([new SingletonDIProvideRefactoringProvider()]);

        foreach (var provider in providers)
        {
            var type = provider.GetType();
            var codeFixExport = type.GetCustomAttribute<ExportCodeFixProviderAttribute>();
            var refactoringExport = type.GetCustomAttribute<ExportCodeRefactoringProviderAttribute>();

            Assert.True(
                codeFixExport is not null || refactoringExport is not null,
                $"{type.Name} has no code fix or refactoring export.");
            var export = (ExportCodeFixProviderAttribute?)codeFixExport;
            if (export is not null)
            {
                Assert.Contains(LanguageNames.CSharp, export.Languages);
                Assert.Equal(type.Name, export.Name);
            }
            else
            {
                Assert.Contains(
                    LanguageNames.CSharp,
                    ((ExportCodeRefactoringProviderAttribute)refactoringExport!).Languages);
            }

            Assert.NotNull(type.GetCustomAttribute<SharedAttribute>());
        }
    }

    [Fact]
    public async Task DM0010_FixAllChangesDoNotOverlap()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class ServiceA
            {
            }

            [SingletonDIProvide]
            public class ServiceB
            {
            }

            [SingletonDIConsume(typeof(ServiceA), typeof(ServiceA))]
            public partial class FirstConsumer
            {
            }

            [SingletonDIConsume(typeof(ServiceB), typeof(ServiceB))]
            public partial class SecondConsumer
            {
            }
            """;

        var changes = await CodeFixTestHarness.GetFixAllTextChangesAsync(
            source,
            "DM0010",
            new SingletonDIConsumerCodeFixProvider());

        Assert.Equal(2, changes.Length);
        AssertNoOverlap(changes);
    }

    [Fact]
    public async Task DM0007_FixAllChangesDoNotOverlap()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
            }

            [SingletonDIConsume(typeof(Service))]
            public class FirstConsumer
            {
            }

            [SingletonDIConsume(typeof(Service))]
            public class SecondConsumer
            {
            }
            """;

        var changes = await CodeFixTestHarness.GetFixAllTextChangesAsync(
            source,
            "DM0007",
            new SingletonDIPartialCodeFixProvider());

        Assert.Equal(2, changes.Length);
        AssertNoOverlap(changes);
    }

    [Fact]
    public async Task DM0004_FixAllChangesDoNotOverlap()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class FirstService
            {
                private FirstService()
                {
                }
            }

            [SingletonDIProvide]
            public class SecondService
            {
                private SecondService()
                {
                }
            }
            """;

        var changes = await CodeFixTestHarness.GetFixAllTextChangesAsync(
            source,
            "DM0004",
            new SingletonDIProviderCodeFixProvider());

        Assert.Equal(2, changes.Length);
        AssertNoOverlap(changes);
    }

    [Fact]
    public async Task DM0010_FixAllChangesDoNotOverlapForTwoDuplicatePairsInOneAttribute()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class ServiceA
            {
            }

            [SingletonDIProvide]
            public class ServiceB
            {
            }

            [SingletonDIConsume(typeof(ServiceA), typeof(ServiceA), typeof(ServiceB), typeof(ServiceB))]
            public partial class Consumer
            {
            }
            """;

        var changes = await CodeFixTestHarness.GetFixAllTextChangesAsync(
            source,
            "DM0010",
            new SingletonDIConsumerCodeFixProvider());

        Assert.Equal(2, changes.Length);
        AssertNoOverlap(changes);
    }

    private static void AssertNoOverlap(ImmutableArray<TextChange> changes)
    {
        var ordered = changes
            .OrderBy(change => change.Span.Start)
            .ThenBy(change => change.Span.End)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            Assert.True(
                ordered[index - 1].Span.End <= ordered[index].Span.Start,
                $"Fix-all changes overlap: [{ordered[index - 1].Span}] and [{ordered[index].Span}].");
        }
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

    /// <summary>
    /// Applies the fix, formats the result the way the IDE does, and compares the document
    /// text verbatim. Unlike <see cref="VerifyCodeFixAsync"/> this keeps comments, documentation,
    /// preprocessor directives and untouched formatting, so a fix that relocates or drops them,
    /// or that reformats code it had no business touching, fails here.
    /// </summary>
    private static async Task VerifyCodeFixExactlyAsync(
        string testSource,
        string expectedSource,
        string diagnosticId,
        CodeFixProvider codeFixProvider)
    {
        var document = await CodeFixTestHarness.ApplyFirstAndFormatAsync(
            testSource,
            diagnosticId,
            codeFixProvider,
            ExpectedTitle(codeFixProvider, diagnosticId));

        var actual = (await document.GetTextAsync()).ToString();
        Assert.Equal(NormalizeNewLines(expectedSource), NormalizeNewLines(actual));

        var compilation = await document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Id == diagnosticId);
        Assert.Empty(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static async Task VerifyPartialCodeFixExactlyAsync(
        string testSource,
        string expectedSource,
        string diagnosticId)
    {
        await VerifyCodeFixExactlyAsync(
            testSource,
            expectedSource,
            diagnosticId,
            new SingletonDIPartialCodeFixProvider());
    }

    private static string ExpectedTitle(CodeFixProvider codeFixProvider, string diagnosticId) =>
        (codeFixProvider, diagnosticId) switch
        {
            (SingletonDIProviderCodeFixProvider, "DM0004") => "Add public parameterless constructor",
            (SingletonDIProviderCodeFixProvider, "DM0005") => "Make method public",
            (SingletonDIProviderCodeFixProvider, "DM0012") => "Remove static modifier",
            (SingletonDIConsumerCodeFixProvider, "DM0010") => "Remove duplicate type",
            (SingletonDIPartialCodeFixProvider, "DM0007") => "Add 'partial' modifier",
            _ => throw new ArgumentException($"Unknown diagnostic/provider combination: {diagnosticId}")
        };

    private static async Task VerifyCodeFixAsync(
        string testSource,
        string expectedSource,
        string diagnosticId,
        CodeFixProvider codeFixProvider)
    {
        var expectedTitle = ExpectedTitle(codeFixProvider, diagnosticId);

        var result = await CodeFixTestHarness.ApplyFirstAsync(
            testSource,
            diagnosticId,
            codeFixProvider,
            expectedTitle);
        var fixedSource = (await result.Document.GetTextAsync()).ToString();
        var normalizedExpected = NormalizeWhitespace(expectedSource);

        Assert.Equal(normalizedExpected, NormalizeWhitespace(fixedSource));
        AssertCommentsPreserved(testSource, fixedSource);
        var compilation = await result.Document.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Id == diagnosticId);
        Assert.Empty(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// Asserts that the fix kept every comment, documentation comment and preprocessor directive,
    /// in order and verbatim.
    /// </summary>
    /// <remarks>
    /// The structural comparison above runs on normalized whitespace, which discards all of them.
    /// Without this guard a fix could delete a type's XML documentation or push a #region off the
    /// start of its line and still pass.
    /// </remarks>
    private static void AssertCommentsPreserved(string before, string after)
    {
        Assert.Equal(Comments(before), Comments(after));
    }

    private static string[] Comments(string source) =>
        CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantTrivia()
            .Where(IsCommentOrDirective)
            .Select(trivia => trivia.ToFullString())
            .ToArray();

    private static bool IsCommentOrDirective(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.RegionDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia)
        || trivia.IsKind(SyntaxKind.PreprocessingMessageTrivia);

    private static string NormalizeWhitespace(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        return root.NormalizeWhitespace().ToFullString();
    }

    /// <summary>
    /// Replaces line endings with a fixed marker so the comparison does not depend on the host
    /// newline convention - the workspace formatter rewrites trailing trivia in its own style.
    /// </summary>
    private static string NormalizeNewLines(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

#endregion
}
