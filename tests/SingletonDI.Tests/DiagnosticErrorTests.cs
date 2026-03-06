using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for diagnostic errors in SingletonDI.Generator.
/// These tests verify that diagnostics are correctly reported for various error conditions.
/// </summary>
public class DiagnosticErrorTests
{
    [Fact]
    public void DM0001_DuplicatePropertyName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              namespace MyApp.Services
                              {
                                  /// <summary>
                                  /// First service with custom property name.
                                  /// </summary>
                                  [SingletonDIProvide("CustomService")]
                                  public class FirstService
                                  {
                                      public void DoWork() { }
                                  }

                                  /// <summary>
                                  /// Second service with the same custom property name.
                                  /// </summary>
                                  [SingletonDIProvide("CustomService")]
                                  public class SecondService
                                  {
                                      public void DoOtherWork() { }
                                  }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - should have two DM0001 diagnostics (one for each conflicting class)
        var dm0001Diagnostics = diagnostics.Where(d => d.Id == "DM0001").ToList();
        Assert.Equal(2, dm0001Diagnostics.Count);

        // Verify both diagnostics have valid locations (not Location.None)
        foreach (var dm0001 in dm0001Diagnostics)
        {
            Assert.Equal("Duplicate property name", dm0001.Descriptor.Title);
            Assert.Contains("CustomService", dm0001.GetMessage());
            Assert.Contains("FirstService", dm0001.GetMessage());
            Assert.Contains("SecondService", dm0001.GetMessage());

            // Verify Location is not None
            Assert.NotEqual(Location.None, dm0001.Location);
            Assert.True(dm0001.Location.IsInSource);
        }

        // Verify locations point to different classes
        var locations = dm0001Diagnostics.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).ToList();
        Assert.Equal(2, locations.Distinct().Count()); // Two different lines
    }

    [Fact]
    public void DM0002_ProvideOnAbstractClass()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Абстрактный базовый класс.
                              /// Не может использоваться с [SingletonDIProvide].
                              /// </summary>
                              [SingletonDIProvide]
                              public abstract class MyAbstractClass
                              {
                                  // Абстрактный метод
                                  public abstract void DoSomething();
                                  
                                  /* Многострочный комментарий
                                     в абстрактном классе */
                                  protected int _baseValue;
                                  
                                  /// <summary>
                                  /// Конкретный метод базового класса.
                                  /// </summary>
                                  public void ConcreteMethod() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0002 = diagnostics.FirstOrDefault(d => d.Id == "DM0002");
        Assert.NotNull(dm0002);
        Assert.Equal("Cannot use [SingletonDIProvide] on abstract class", dm0002.Descriptor.Title);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithShortName()
    {
        // Arrange - custom name conflicts with auto-generated short name
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp
            {
                [SingletonDIProvide]
                public class DatabaseService { }

                [SingletonDIProvide("DatabaseServiceInstance")]
                public class UserService { }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Equal("Property name conflicts with generated name", dm0003.Descriptor.Title);
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("DatabaseService", dm0003.GetMessage());

        // Verify Location is not None and points to the argument
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithFullName()
    {
        // Arrange - custom name conflicts with auto-generated full name (namespace conflict)
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp.Services
            {
                [SingletonDIProvide]
                public class DatabaseService { }
            }

            namespace MyApp.Other
            {
                [SingletonDIProvide]
                public class DatabaseService { }
            }

            namespace MyApp.Third
            {
                // Conflicts with MyApp.Services.DatabaseService generated name
                [SingletonDIProvide("MyApp_Services_DatabaseServiceInstance")]
                public class UserService { }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("MyApp_Services_DatabaseServiceInstance", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithOtherProvider()
    {
        // Arrange - custom name conflicts with another provider's generated name
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp
            {
                // Would generate "DatabaseServiceInstance" without custom name
                [SingletonDIProvide]
                public class DatabaseService { }

                // Conflicts with DatabaseService's generated name
                [SingletonDIProvide("DatabaseServiceInstance")]
                public class UserService { }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - should report DM0003
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("DatabaseService", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithSelf_ShortName()
    {
        // Arrange - custom name conflicts with its own generated short name
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp
            {
                // Would generate "DatabaseServiceInstance" without custom name
                [SingletonDIProvide("DatabaseServiceInstance")]
                public class DatabaseService { }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - self-conflict IS an error
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("DatabaseService", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithSelf_ShortName2()
    {
        // Arrange - custom name conflicts with its own generated short name
        const string SOURCE = """
            using SingletonDI.Attributes;
            
            namespace SingletonDI.SampleApp;
            
            [SingletonDIProvide("DatabaseServiceInstance")]
            public class DatabaseService
            {
                public DatabaseService()
                {
                    
                }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - self-conflict IS an error
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("DatabaseService", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithSelf_FullName()
    {
        // Arrange - custom name conflicts with its own generated full name (namespace conflict scenario)
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp.Services
            {
                // Would generate "MyApp_Services_DatabaseServiceInstance" due to conflict
                [SingletonDIProvide]
                public class DatabaseService { }
            }

            namespace MyApp.Other
            {
                // Would generate "MyApp_Other_DatabaseServiceInstance" due to conflict
                [SingletonDIProvide("MyApp_Other_DatabaseServiceInstance")]
                public class DatabaseService { }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - self-conflict IS an error
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("MyApp_Other_DatabaseServiceInstance", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_PropertyNameConflictsWithSelf_FullName2()
    {
        // Arrange - custom name conflicts with its own generated full name (namespace conflict scenario)
        const string SOURCE = """
            using SingletonDI.Attributes;
            
            namespace SingletonDI.SampleApp;

            [SingletonDIProvide("SingletonDI_SampleApp_DatabaseServiceInstance")]
            public class DatabaseService
            {
                public DatabaseService()
                {
                    
                }
            }
            """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - self-conflict IS an error
        var dm0003 = diagnostics.FirstOrDefault(d => d.Id == "DM0003");
        Assert.NotNull(dm0003);
        Assert.Contains("SingletonDI_SampleApp_DatabaseServiceInstance", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0004_ProvideMissingParameterlessConstructor()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис без параметризованного конструктора.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService
                              {
                                  // Поле для хранения значения
                                  private readonly int _value;
                                  
                                  /// <summary>
                                  /// Единственный конструктор с параметром.
                                  /// </summary>
                                  public MyService(int value) 
                                  { 
                                      _value = value; 
                                  }
                                  
                                  /* Метод для получения значения */
                                  public int GetValue() => _value;
                                  
                                  /// <summary>
                                  /// Имя сервиса.
                                  /// </summary>
                                  public string Name { get; set; } = "Default";
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0004 = diagnostics.FirstOrDefault(d => d.Id == "DM0004");
        Assert.NotNull(dm0004);
        Assert.Contains("MyService", dm0004.GetMessage());
        Assert.Contains("public parameterless constructor", dm0004.GetMessage());
    }

    [Fact]
    public void DM0004_ProvideWithPrivateConstructor()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис с приватным конструктором.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService
                              {
                                  // Приватный конструктор - ошибка!
                                  private MyService() { }
                                  
                                  // Статический счётчик экземпляров
                                  private static int _instanceCount;
                                  
                                  /// <summary>
                                  /// Идентификатор экземпляра.
                                  /// </summary>
                                  public int Id { get; } = ++_instanceCount;
                                  
                                  /* Многострочный комментарий
                                     перед методом */
                                  public void DoWork() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0004 = diagnostics.FirstOrDefault(d => d.Id == "DM0004");
        Assert.NotNull(dm0004);
        Assert.Contains("MyService", dm0004.GetMessage());
    }

    [Fact]
    public void DM0005_InitializeAsyncNotAccessible_Private()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис с приватным методом InitializeAsync.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService
                              {
                                  // Приватное поле
                                  private bool _isInitialized;
                                  
                                  // Приватный метод инициализации - ошибка!
                                  private Task InitializeAsync() => Task.CompletedTask;
                                  
                                  /// <summary>
                                  /// Проверка инициализации.
                                  /// </summary>
                                  public bool IsReady => _isInitialized;
                                  
                                  /* Вспомогательный метод */
                                  public void Reset() => _isInitialized = false;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0005 = diagnostics.FirstOrDefault(d => d.Id == "DM0005");
        Assert.NotNull(dm0005);
        Assert.Contains("private", dm0005.GetMessage());
    }

    [Fact]
    public void DM0005_InitializeAsyncNotAccessible_Protected()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис с защищённым методом InitializeAsync.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService
                              {
                                  // Защищённый метод инициализации - ошибка!
                                  protected Task InitializeAsync() => Task.CompletedTask;
                                  
                                  // Константа по умолчанию
                                  private const string DefaultName = "Service";
                                  
                                  /// <summary>
                                  /// Имя сервиса.
                                  /// </summary>
                                  public string Name { get; set; } = DefaultName;
                                  
                                  /* Многострочный комментарий
                                     в конце класса */
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0005 = diagnostics.FirstOrDefault(d => d.Id == "DM0005");
        Assert.NotNull(dm0005);
        Assert.Contains("protected", dm0005.GetMessage());
    }

    [Fact]
    public void DM0006_ConsumeReferencesNonProvider()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Обычный класс без атрибута [SingletonDIProvide].
                              /// </summary>
                              public class NonProviderService
                              {
                                  // Поле в обычном классе
                                  private int _value;
                                  
                                  /// <summary>
                                  /// Метод обычного класса.
                                  /// </summary>
                                  public void Execute() { }
                              }

                              /// <summary>
                              /// Консьюмер, ссылающийся на NonProviderService.
                              /// </summary>
                              [SingletonDIConsume(typeof(NonProviderService))]
                              public partial class MyConsumer
                              {
                                  // Комментарий в консьюмере
                                  private string _name = "Consumer";
                                  
                                  /* Многострочный комментарий */
                                  public void DoWork() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0006 = diagnostics.FirstOrDefault(d => d.Id == "DM0006");
        Assert.NotNull(dm0006);
        Assert.Contains("NonProviderService", dm0006.GetMessage());
        Assert.Contains("does not have the [SingletonDIProvide] attribute", dm0006.GetMessage());
    }

    [Fact]
    public void DM0007_ConsumeNotPartial()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис-провайдер.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService 
                              { 
                                  // Реализация сервиса
                                  public void Execute() { }
                              }

                              /// <summary>
                              /// Консьюмер без модификатора partial - ошибка!
                              /// </summary>
                              [SingletonDIConsume(typeof(MyService))]
                              public class MyConsumer  // Забыли partial!
                              {
                                  // Поле консьюмера
                                  private int _counter;
                                  
                                  /// <summary>
                                  /// Метод консьюмера.
                                  /// </summary>
                                  public void DoWork() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0007 = diagnostics.FirstOrDefault(d => d.Id == "DM0007");
        Assert.NotNull(dm0007);
        Assert.Contains("MyConsumer", dm0007.GetMessage());
        Assert.Contains("must be declared as partial", dm0007.GetMessage());
    }

    [Fact]
    public void DM0009_CircularDependency()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис A с зависимостью от B.
                              /// </summary>
                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceB))]
                              public partial class ServiceA
                              {
                                  // Идентификатор сервиса A
                                  public int IdA { get; set; }
                                  
                                  /// <summary>
                                  /// Метод сервиса A.
                                  /// </summary>
                                  public void MethodA() { }
                              }

                              /// <summary>
                              /// Сервис B с зависимостью от A.
                              /// </summary>
                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceA))]
                              public partial class ServiceB
                              {
                                  // Идентификатор сервиса B
                                  public int IdB { get; set; }
                                  
                                  /* Метод сервиса B */
                                  public void MethodB() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0009 = diagnostics.FirstOrDefault(d => d.Id == "DM0009");
        Assert.NotNull(dm0009);
        Assert.Contains("Circular dependency detected", dm0009.GetMessage());
    }

    [Fact]
    public void DM0009_CircularDependency_ThreeWay()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис A -> B.
                              /// </summary>
                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceB))]
                              public partial class ServiceA
                              {
                                  // Поле сервиса A
                                  private string _name = "A";
                                  
                                  /// <summary>
                                  /// Имя сервиса.
                                  /// </summary>
                                  public string Name => _name;
                              }

                              /// <summary>
                              /// Сервис B -> C.
                              /// </summary>
                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceC))]
                              public partial class ServiceB
                              {
                                  // Поле сервиса B
                                  private string _name = "B";
                                  
                                  /* Метод */
                                  public string GetName() => _name;
                              }

                              /// <summary>
                              /// Сервис C -> A (цикл!).
                              /// </summary>
                              [SingletonDIProvide]
                              [SingletonDIConsume(typeof(ServiceA))]
                              public partial class ServiceC
                              {
                                  // Поле сервиса C
                                  private string _name = "C";
                                  
                                  /// <summary>
                                  /// Имя сервиса C.
                                  /// </summary>
                                  public string Name => _name;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0009 = diagnostics.FirstOrDefault(d => d.Id == "DM0009");
        Assert.NotNull(dm0009);
        Assert.Contains("Circular dependency detected", dm0009.GetMessage());
    }

    [Fact]
    public void DM0010_ConsumeDuplicateTypes()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис-провайдер.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService 
                              { 
                                  // Реализация
                                  public void Execute() { }
                              }

                              /// <summary>
                              /// Консьюмер с дублирующимся типом в атрибуте.
                              /// </summary>
                              [SingletonDIConsume(typeof(MyService), typeof(MyService))]  // Дубликат!
                              public partial class MyConsumer
                              {
                                  // Поле консьюмера
                                  private int _value;
                                  
                                  /// <summary>
                                  /// Свойство консьюмера.
                                  /// </summary>
                                  public int Value => _value;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0010 = diagnostics.FirstOrDefault(d => d.Id == "DM0010");
        Assert.NotNull(dm0010);
        Assert.Contains("MyService", dm0010.GetMessage());
        Assert.Contains("specified multiple times", dm0010.GetMessage());
    }

    [Fact]
    public void DM0011_ConsumeDuplicateInBaseClass()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис для потребления.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService 
                              { 
                                  // Метод сервиса
                                  public void Execute() { }
                              }

                              /// <summary>
                              /// Базовый консьюмер.
                              /// </summary>
                              [SingletonDIConsume(typeof(MyService))]
                              public partial class BaseConsumer
                              {
                                  // Поле базового класса
                                  protected int _baseValue;
                                  
                                  /// <summary>
                                  /// Метод базового класса.
                                  /// </summary>
                                  public void BaseMethod() { }
                              }

                              /// <summary>
                              /// Производный консьюмер с дублирующейся зависимостью.
                              /// </summary>
                              [SingletonDIConsume(typeof(MyService))]  // Уже есть в BaseConsumer!
                              public partial class DerivedConsumer : BaseConsumer
                              {
                                  // Поле производного класса
                                  private string _derivedName;
                                  
                                  /* Метод производного класса */
                                  public void DerivedMethod() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0011 = diagnostics.FirstOrDefault(d => d.Id == "DM0011");
        Assert.NotNull(dm0011);
        Assert.Contains("MyService", dm0011.GetMessage());
        Assert.Contains("BaseConsumer", dm0011.GetMessage());
        Assert.Contains("already declared in base class", dm0011.GetMessage());
    }

    [Fact]
    public void DM0012_InitializeAsyncCannotBeStatic()
    {
        // Arrange
        const string SOURCE = """
                              using System.Threading.Tasks;
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Сервис со статическим методом InitializeAsync.
                              /// </summary>
                              [SingletonDIProvide]
                              public class MyService
                              {
                                  // Статическое поле
                                  private static int _counter;
                                  
                                  // Статический метод - ошибка!
                                  public static Task InitializeAsync() => Task.CompletedTask;
                                  
                                  /// <summary>
                                  /// Экземплярное свойство.
                                  /// </summary>
                                  public int Id { get; } = ++_counter;
                                  
                                  /* Многострочный комментарий
                                     перед методом */
                                  public void DoWork() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0012 = diagnostics.FirstOrDefault(d => d.Id == "DM0012");
        Assert.NotNull(dm0012);
        Assert.Equal("Method InitializeAsync in class 'MyService' is static. InitializeAsync must be an instance method.", dm0012.GetMessage());
    }

    [Fact]
    public void DM0013_InvalidPropertyName_StartsWithDigit()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с невалидным именем свойства (начинается с цифры).
                              /// </summary>
                              [SingletonDIProvide("123InvalidName")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0013 = diagnostics.FirstOrDefault(d => d.Id == "DM0013");
        Assert.NotNull(dm0013);
        Assert.Contains("123InvalidName", dm0013.GetMessage());
        Assert.Contains("valid C# identifier", dm0013.GetMessage());
    }

    [Fact]
    public void DM0013_InvalidPropertyName_ContainsSpecialCharacters()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с невалидным именем свойства (специальные символы).
                              /// </summary>
                              [SingletonDIProvide("my-property")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0013 = diagnostics.FirstOrDefault(d => d.Id == "DM0013");
        Assert.NotNull(dm0013);
        Assert.Contains("my-property", dm0013.GetMessage());
    }

    [Fact]
    public void DM0013_InvalidPropertyName_EmptyName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с невалидным именем свойства (пустая строка).
                              /// </summary>
                              [SingletonDIProvide("")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0013 = diagnostics.FirstOrDefault(d => d.Id == "DM0013");
        Assert.NotNull(dm0013);
        Assert.Contains("''", dm0013.GetMessage());
    }

    [Fact]
    public void DM0013_InvalidPropertyName_EmptyName2()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с невалидным именем свойства (пробелы).
                              /// </summary>
                              [SingletonDIProvide("   ")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0013 = diagnostics.FirstOrDefault(d => d.Id == "DM0013");
        Assert.NotNull(dm0013);
        Assert.Contains("'   '", dm0013.GetMessage());
    }

    [Fact]
    public void DM0014_PropertyNameIsReservedKeyword()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с именем свойства, совпадающим с ключевым словом.
                              /// </summary>
                              [SingletonDIProvide("class")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0014 = diagnostics.FirstOrDefault(d => d.Id == "DM0014");
        Assert.NotNull(dm0014);
        Assert.Contains("class", dm0014.GetMessage());
        Assert.Contains("reserved keyword", dm0014.GetMessage());
    }

    [Fact]
    public void DM0014_PropertyNameIsReservedKeyword_Void()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с именем свойства, совпадающим с ключевым словом void.
                              /// </summary>
                              [SingletonDIProvide("void")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0014 = diagnostics.FirstOrDefault(d => d.Id == "DM0014");
        Assert.NotNull(dm0014);
        Assert.Contains("void", dm0014.GetMessage());
    }

    [Fact]
    public void ValidPropertyName_WithUnderscorePrefix()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с валидным именем свойства (префикс подчёркивания).
                              /// </summary>
                              [SingletonDIProvide("_dbService")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - no DM0013 or DM0014 errors
        Assert.DoesNotContain(diagnostics, d => d.Id is "DM0013" or "DM0014");
    }

    [Fact]
    public void ValidPropertyName_WithPascalCase()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Провайдер с валидным именем свойства (PascalCase).
                              /// </summary>
                              [SingletonDIProvide("DbService")]
                              public class MyService
                              {
                                  public void DoSomething() { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert - no DM0013 or DM0014 errors
        Assert.DoesNotContain(diagnostics, d => d.Id is "DM0013" or "DM0014");
    }

    [Fact]
    public void DM0015_GenericTypeNotSupported()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Generic repository - not supported as singleton.
                              /// </summary>
                              [SingletonDIProvide]
                              public class Repository<T>
                              {
                                  public T GetById(int id) => default;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0015 = diagnostics.FirstOrDefault(d => d.Id == "DM0015");
        Assert.NotNull(dm0015);
        Assert.Equal("Generic types are not supported for singletons", dm0015.Descriptor.Title);
        Assert.Contains("Repository", dm0015.GetMessage());
        Assert.Contains("Generic type", dm0015.GetMessage());

        // Verify Location is on the attribute, not the class identifier
        Assert.NotEqual(Location.None, dm0015.Location);
        Assert.True(dm0015.Location.IsInSource);
    }

    [Fact]
    public void DM0015_GenericTypeNotSupported_CustomName()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Generic repository - not supported as singleton.
                              /// </summary>
                              [SingletonDIProvide("Repository")]
                              public class Repository<T>
                              {
                                  public T GetById(int id) => default;
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0015 = diagnostics.FirstOrDefault(d => d.Id == "DM0015");
        Assert.NotNull(dm0015);
        Assert.Equal("Generic types are not supported for singletons", dm0015.Descriptor.Title);
        Assert.Contains("Repository", dm0015.GetMessage());
        Assert.Contains("Generic type", dm0015.GetMessage());

        // Verify Location is on the attribute, not the class identifier
        Assert.NotEqual(Location.None, dm0015.Location);
        Assert.True(dm0015.Location.IsInSource);
    }

    [Fact]
    public void DM0015_GenericTypeNotSupported_MultipleTypeParameters()
    {
        // Arrange
        const string SOURCE = """
                              using SingletonDI.Attributes;

                              /// <summary>
                              /// Generic service with multiple type parameters.
                              /// </summary>
                              [SingletonDIProvide]
                              public class Service<TKey, TValue>
                              {
                                  public void Process(TKey key, TValue value) { }
                              }
                              """;

        // Act
        var diagnostics = RunGenerator(SOURCE);

        // Assert
        var dm0015 = diagnostics.FirstOrDefault(d => d.Id == "DM0015");
        Assert.NotNull(dm0015);
        Assert.Contains("Service", dm0015.GetMessage());
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new SingletonDIGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attributes.SingletonDIProvideAttribute).Assembly.Location),
        };

        // Add all referenced assemblies
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[] { "System.Runtime", "System.Collections", "System.Linq", "netstandard" })
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
