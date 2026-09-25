using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SingletonDI.Attributes;
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
    public void DM0008_SelfReference_ExactDiagnosticAndOutputCompiles()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(Consumer))]
            public partial class Consumer
            {
                public Consumer()
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics.Where(item => item.Id == "DM0008"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("SingletonDI", diagnostic.Descriptor.Category);
        Assert.Equal("Self-reference not allowed", diagnostic.Descriptor.Title.ToString());
        Assert.Equal(
            "[SingletonDIConsume] class 'Consumer' cannot consume itself. Remove 'Consumer' from the dependencies.",
            diagnostic.GetMessage());
        Assert.True(diagnostic.Location.IsInSource);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Severity == DiagnosticSeverity.Error);
    }

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
        var dm0002 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0002");
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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Equal("Consumer property name conflict", dm0003.Descriptor.Title);
        Assert.Equal(
            "Consumer property 'DatabaseServiceInstance' conflicts with the property resolved for service 'global::MyApp.DatabaseService' " +
            "in the same consumer dependency set. Use distinct names or the namespace-qualified fallback.",
            dm0003.GetMessage());

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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("global::MyApp.DatabaseService", dm0003.GetMessage());

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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Contains("DatabaseServiceInstance", dm0003.GetMessage());
        Assert.Contains("global::MyApp.DatabaseService", dm0003.GetMessage());

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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Equal(
            "Consumer property 'DatabaseServiceInstance' conflicts with the property resolved for service " +
            "'global::SingletonDI.SampleApp.DatabaseService' in the same consumer dependency set. " +
            "Use distinct names or the namespace-qualified fallback.",
            dm0003.GetMessage());

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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
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
        var dm0003 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Contains("SingletonDI_SampleApp_DatabaseServiceInstance", dm0003.GetMessage());

        // Verify Location is not None
        Assert.NotEqual(Location.None, dm0003.Location);
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void DM0003_NotReportedWhenCustomNamesCannotCollide()
    {
        // Neither provider can ever generate a name: both carry an explicit one, so
        // 'FooInstance' is not a name 'Foo' will ever emit.
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp
            {
                [SingletonDIProvide("FooInstance")]
                public class Alpha { }

                [SingletonDIProvide("BarInstance")]
                public class Foo { }
            }
            """;

        var result = RunGeneratorWithOutput(SOURCE);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0003");
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("global::MyApp.Alpha", result.GeneratedSource);
        Assert.Contains("global::MyApp.Foo", result.GeneratedSource);
    }

    [Fact]
    public void DM0003_ReportedWhenCustomNameMatchesAnotherProvidersGeneratedName()
    {
        const string SOURCE = """
            using SingletonDI.Attributes;

            namespace MyApp
            {
                [SingletonDIProvide("FooInstance")]
                public class Alpha { }

                [SingletonDIProvide]
                public class Foo { }
            }
            """;

        var diagnostics = RunGenerator(SOURCE);

        var dm0003 = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DM0003"));
        Assert.Equal(DiagnosticSeverity.Error, dm0003.Severity);
        Assert.Contains("FooInstance", dm0003.GetMessage());
        Assert.True(dm0003.Location.IsInSource);
    }

    [Fact]
    public void ProviderInheritsConsumeDependenciesFromBaseType()
    {
        // SingletonDIConsumeAttribute is Inherited = true. GetAttributes only reports directly
        // declared attributes, so a provider deriving from a consuming base type used to be
        // registered with no dependencies and could be constructed before what it needs.
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class DatabaseService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService))]
                public partial class NeedsDatabase
                {
                }

                [SingletonDIProvide]
                public sealed partial class ReportService : NeedsDatabase
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "RegisterProvider<global::App.ReportService, global::App.ReportService>",
            result.GeneratedSource);
        Assert.Contains("typeof(global::App.DatabaseService)", result.GeneratedSource);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ProviderOverridingInheritedConsumeDependenciesWins()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                public sealed class DatabaseService
                {
                }

                [SingletonDIProvide]
                public sealed class CacheService
                {
                }

                [SingletonDIConsume(typeof(DatabaseService))]
                public partial class NeedsDatabase
                {
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(CacheService))]
                public sealed partial class ReportService : NeedsDatabase
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("typeof(global::App.CacheService)", result.GeneratedSource);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
        var dm0004 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0004");
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
        var dm0004 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0004");
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
        var dm0005 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0005");
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
        var dm0005 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0005");
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
        var dm0006 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0006");
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
        var dm0007 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0007");
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
        var dm0009 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0009");
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
        var dm0009 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0009");
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
        var dm0010 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0010");
        Assert.Contains("MyService", dm0010.GetMessage());
        Assert.Contains("specified multiple times", dm0010.GetMessage());
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
        var dm0012 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0012");
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
        var dm0013 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0013");
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
        var dm0013 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0013");
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
        var dm0013 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0013");
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
        var dm0013 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0013");
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
        var dm0014 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0014");
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
        var dm0014 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0014");
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
        var dm0015 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0015");
        Assert.Equal("Generic types are not supported for singletons", dm0015.Descriptor.Title);
        Assert.Equal(
            "Generic type 'Repository' cannot be a singleton. Generic types are not supported.",
            dm0015.GetMessage());

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
        var dm0015 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0015");
        Assert.Equal("Generic types are not supported for singletons", dm0015.Descriptor.Title);
        Assert.Equal(
            "Generic type 'Repository' cannot be a singleton. Generic types are not supported.",
            dm0015.GetMessage());

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
        var dm0015 = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DM0015");
        Assert.Contains("Service", dm0015.GetMessage());
    }

    [Fact]
    public void DM0016_InvalidServiceType()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(string))]
                public sealed class InvalidService
                {
                }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0016");
    }

    [Fact]
    public void PredefinedTypeAsServiceTypeProducesCompilableModule()
    {
        // The predefined types render by their C# keyword, so a name destined for generated code
        // must not be produced by a format that aliases them. Emitting 'global::object' is a CS1001
        // in the generated module, and the validator accepts this input, so nothing else would
        // have reported it.
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide(ServiceType = typeof(object))]
                public sealed class ObjectService
                {
                }

                [SingletonDIProvide]
                public sealed class Service
                {
                }

                [SingletonDIConsume(typeof(ObjectService), typeof(Service))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains("global::System.Object, global::App.ObjectService", result.GeneratedSource);
        Assert.DoesNotContain("global::object", result.GeneratedSource);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DM0022_PrivateNestedProviderIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            public partial class Outer
            {
                [SingletonDIProvide]
                private sealed class Service
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0022");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("SingletonDI", diagnostic.Descriptor.Category);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("Service", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0122");
    }

    [Fact]
    public void DM0022_FileLocalProviderIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            file sealed class FileService
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0022");
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("FileService", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0122");
    }

    [Fact]
    public void DM0022_ProviderInsideInaccessibleContainingTypeIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            public partial class Outer
            {
                private partial class Container
                {
                    [SingletonDIProvide]
                    public sealed class Service
                    {
                    }
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0022");
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("Service", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0122");
    }

    [Fact]
    public void DM0022_ProtectedServiceTypeIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            public class ProviderBase
            {
                protected interface IService
                {
                }

                [SingletonDIProvide(ServiceType = typeof(IService))]
                public sealed class Service : IService
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0022");
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Contains("IService", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0122");
    }

    [Fact]
    public void DM0022_DoesNotRejectAccessibleInternalProvider()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace App
            {
                [SingletonDIProvide]
                internal sealed class InternalService
                {
                    public InternalService()
                    {
                    }
                }

                [SingletonDIConsume(typeof(InternalService))]
                public partial class Consumer
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0022");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            result.OutputCompilation.SyntaxTrees,
            tree => tree.GetRoot().ToString().Contains(
                "class __SingletonDIProviderModule__",
                StringComparison.Ordinal));
    }

    [Fact]
    public void DM0022_ProviderDependencyMustBeAccessibleFromGeneratedCode()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            file sealed class FileService
            {
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(FileService))]
            public sealed class Service
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0022");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Id == "CS0122");
    }

    [Fact]
    public void DM0023_GenericInitializeAsyncIsRejected()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
                public Task InitializeAsync<T>() => Task.CompletedTask;
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0023");
        Assert.Equal("Generic InitializeAsync is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("InitializeAsync", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0411");
    }

    [Fact]
    public void DM0024_OpenGenericDependencyIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IBox<T>
            {
            }

            [SingletonDIConsume(typeof(IBox<>))]
            public partial class Consumer
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0024");
        Assert.Equal("Open generic dependency is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("IBox", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS7003");
    }

    [Fact]
    public void DM0024_OpenGenericProviderContractIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IBox<T>
            {
            }

            [SingletonDIProvide(ServiceType = typeof(IBox<>))]
            public sealed class Service : IBox<int>
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0024");
        Assert.Contains("IBox", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS7003");
    }

    [Fact]
    public void DM0025_ExistingConsumerMemberCollisionIsReported()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
            }

            [SingletonDIConsume(typeof(Service))]
            public partial class Consumer
            {
                public int ServiceInstance => 0;
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0025");
        Assert.Equal("Consumer property name already exists", diagnostic.Descriptor.Title);
        Assert.Contains("ServiceInstance", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0102");

        var generated = string.Join(
            Environment.NewLine,
            result.OutputCompilation.SyntaxTrees.Select(tree => tree.ToString()));
        Assert.DoesNotContain(generated, "Resolve<global::Service>()", StringComparison.Ordinal);
    }

    [Fact]
    public void DM0025_CollisionSkipsOnlyTheConflictingProperty()
    {
        const string source = """
            using SingletonDI.Attributes;

            namespace A
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }

            namespace B
            {
                [SingletonDIProvide]
                public sealed class Service
                {
                }
            }

            [SingletonDIConsume(typeof(A.Service), typeof(B.Service))]
            public partial class Consumer
            {
                public int A_ServiceInstance => 0;

                public B.Service Get() => B_ServiceInstance;
            }
            """;

        var result = RunGeneratorWithOutput(source);
        Assert.Single(result.Diagnostics, item => item.Id == "DM0025");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0102");

        var generated = string.Join(
            Environment.NewLine,
            result.OutputCompilation.SyntaxTrees.Select(tree => tree.ToString()));
        Assert.DoesNotContain(generated, "Resolve<global::A.Service>()", StringComparison.Ordinal);
        Assert.Contains("Resolve<global::B.Service>()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void DM0025_InheritedConsumerMemberCollisionIsReported()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
            }

            public class ConsumerBase
            {
                protected int ServiceInstance => 0;
            }

            [SingletonDIConsume(typeof(Service))]
            public partial class Consumer : ConsumerBase
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0025");
        Assert.Contains("ServiceInstance", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id is "CS0102" or "CS0108");
    }

    [Fact]
    public void DM0017_LibraryConsumerDoesNotRequireCompositionRoot()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IExternalService
            {
            }

            [SingletonDIConsume(typeof(IExternalService))]
            public partial class Consumer
            {
            }
            """;

        var diagnostics = RunGenerator(
            source,
            compositionRoot: false,
            OutputKind.DynamicallyLinkedLibrary);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void DM0017_ExecutableConsumerRequiresCompositionRoot()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IExternalService
            {
            }

            [SingletonDIConsume(typeof(IExternalService))]
            public partial class Consumer
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = RunGenerator(
            source,
            compositionRoot: false,
            OutputKind.ConsoleApplication);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void DM0017_DoesNotReportWhenLocalProviderSatisfiesContract()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface ILocalService
            {
            }

            [SingletonDIProvide(ServiceType = typeof(ILocalService))]
            public sealed class LocalService : ILocalService
            {
            }

            [SingletonDIConsume(typeof(ILocalService))]
            public partial class Consumer
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = RunGenerator(
            source,
            compositionRoot: false,
            OutputKind.ConsoleApplication);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void DM0029_FileLocalConsumerIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
            }

            [SingletonDIConsume(typeof(Service))]
            file partial class FileConsumer
            {
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0029");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("File-local consumer is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("FileConsumer", diagnostic.GetMessage());
        Assert.True(diagnostic.Location.IsInSource);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DM0031_TypeParameterAttributesAreRejected()
    {
        const string source = """
            using System;
            using Marker = App.MarkerAttribute;
            using SingletonDI.Attributes;

            namespace App
            {
                [AttributeUsage(AttributeTargets.GenericParameter)]
                public sealed class MarkerAttribute : Attribute
                {
                }

                [SingletonDIProvide]
                public sealed class Service
                {
                }

                [SingletonDIConsume(typeof(Service))]
                public partial class GenericConsumer<[Marker] T>
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0031");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Consumer type parameter attributes are not supported", diagnostic.Descriptor.Title);
        Assert.Contains("GenericConsumer", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS0246");
    }

    [Fact]
    public void DM0032_ProviderWithRequiredMembersIsRejected()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class RequiredService
            {
                public required int Value { get; init; }
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0032");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Provider required members are not supported", diagnostic.Descriptor.Title);
        Assert.Contains("RequiredService", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS9035");
    }

    [Fact]
    public void ProviderWithSetsRequiredMembersConstructorIsAccepted()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class RequiredService
            {
                [SetsRequiredMembers]
                public RequiredService()
                {
                }

                public required int Value { get; init; } = 1;
            }
            """;

        var result = RunGeneratorWithOutput(source);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DM0032");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(
            "RegisterProvider<global::RequiredService, global::RequiredService>",
            result.GeneratedSource);
    }

    [Fact]
    public void DM0033_NullableInitializerReturnTypeIsRejected()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public sealed class Service
            {
                public Task? InitializeAsync() => null;
            }
            """;

        var result = RunGeneratorWithOutput(source);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0033");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Nullable initializer return type is not supported", diagnostic.Descriptor.Title);
        Assert.Contains("Service", diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            item => item.Id == "CS8604");
    }

    [Fact]
    public void DM0017_UsesCompilationOutputKindWhenOutputTypePropertyIsMissing()
    {
        const string source = """
            using SingletonDI.Attributes;

            public interface IExternalService
            {
            }

            [SingletonDIConsume(typeof(IExternalService))]
            public partial class Consumer
            {
                public static void Main()
                {
                }
            }
            """;

        var result = RunGeneratorWithOutput(
            source,
            compositionRoot: false,
            outputKind: OutputKind.ConsoleApplication,
            includeOutputTypeProperty: false);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DM0017");
    }

    [Fact]
    public void DM0027_CompositionRootWithoutLocalAttributesReportsLanguageVersion()
    {
        const string source = """
            namespace App
            {
                public static class Program
                {
                    public static void Main()
                    {
                    }
                }
            }
            """;

        var result = RunGeneratorWithOutput(
            source,
            compositionRoot: true,
            outputKind: OutputKind.ConsoleApplication,
            languageVersion: LanguageVersion.CSharp7_3);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "DM0027");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Location.None, diagnostic.Location);
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        return RunGeneratorWithOutput(source).Diagnostics;
    }

    private static ImmutableArray<Diagnostic> RunGenerator(
        string source,
        bool compositionRoot,
        OutputKind outputKind)
    {
        return RunGeneratorWithOutput(source, compositionRoot, outputKind).Diagnostics;
    }

    private static GeneratorTestResult RunGeneratorWithOutput(
        string source,
        bool compositionRoot = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeOutputTypeProperty = true,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var compilation = CreateCompilation(source, outputKind, languageVersion);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SingletonDIGenerator().AsSourceGenerator() },
            additionalTexts: Array.Empty<AdditionalText>(),
            parseOptions: new CSharpParseOptions(
                languageVersion,
                preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]),
            optionsProvider: new GeneratorTestAnalyzerConfigOptionsProvider(
                new GeneratorTestOptions(compositionRoot, outputKind, includeOutputTypeProperty)),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false,
                baseDirectory: null));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var inputTrees = compilation.SyntaxTrees.ToImmutableArray();
        var generatedSource = string.Join(
            Environment.NewLine,
            outputCompilation.SyntaxTrees
                .Where(tree => !inputTrees.Contains(tree))
                .Select(tree => tree.ToString()));
        return new GeneratorTestResult(
            diagnostics,
            outputCompilation,
            generatedSource,
            outputKind);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RuntimeHelpers).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RuntimeInformation).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDIProvideAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDIProviderModuleAttribute).Assembly.Location),
        };

        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[]
                 {
                     "System.Runtime",
                     "System.Runtime.CompilerServices",
                     "System.Threading.Tasks",
                     "System.Runtime.InteropServices",
                     "System.Collections",
                     "System.Linq",
                     "netstandard",
                 })
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(
                    languageVersion,
                    preprocessorSymbols: ["NET10_0_OR_GREATER", "NET5_0_OR_GREATER"]))],
            references,
            new CSharpCompilationOptions(outputKind));
    }
}
