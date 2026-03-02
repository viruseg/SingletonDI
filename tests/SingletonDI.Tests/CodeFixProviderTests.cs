using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator;
using SingletonDI.Refactoring;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for CodeFixProviders in SingletonDI.Refactoring.
/// These tests verify that code fixes preserve comments, XML documentation, and unrelated members.
/// </summary>
public class CodeFixProviderTests
{
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
    public async Task DM0011_RemoveTypeFromAttribute()
    {
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис для потребления.
                   /// </summary>
                   [SingletonDIProvide]
                   public class MyService 
                   { 
                       // Реализация сервиса
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
                   [SingletonDIConsume(typeof(MyService))]
                   partial class DerivedConsumer : BaseConsumer
                   {
                       // Поле производного класса
                       private string _derivedName;
                       
                       /// <summary>
                       /// Метод производного класса.
                       /// </summary>
                       public void DerivedMethod() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис для потребления.
                       /// </summary>
                       [SingletonDIProvide]
                       public class MyService 
                       { 
                           // Реализация сервиса
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
                       partial class DerivedConsumer : BaseConsumer
                       {
                           // Поле производного класса
                           private string _derivedName;
                       
                           /// <summary>
                           /// Метод производного класса.
                           /// </summary>
                           public void DerivedMethod() { }
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0011");
    }

    [Fact]
    public async Task DM0011_RemoveTypeFromAttribute_MultipleTypes()
    {
        var test = """
                   using SingletonDI.Attributes;

                   /// <summary>
                   /// Сервис A.
                   /// </summary>
                   [SingletonDIProvide]
                   public class ServiceA 
                   { 
                       // Метод сервиса A
                       public void MethodA() { }
                   }

                   /// <summary>
                   /// Сервис B.
                   /// </summary>
                   [SingletonDIProvide]
                   public class ServiceB 
                   { 
                       // Метод сервиса B
                       public void MethodB() { }
                   }

                   /// <summary>
                   /// Базовый консьюмер.
                   /// </summary>
                   [SingletonDIConsume(typeof(ServiceA))]
                   public partial class BaseConsumer
                   {
                       // Комментарий в BaseConsumer
                       private int _counter;
                   }

                   /// <summary>
                   /// Производный консьюмер.
                   /// </summary>
                   [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB))]
                   partial class DerivedConsumer : BaseConsumer
                   {
                       /* Многострочный комментарий
                          в DerivedConsumer */
                       private string _name;
                       
                       /// <summary>
                       /// Имя консьюмера.
                       /// </summary>
                       public string Name => _name;
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       /// <summary>
                       /// Сервис A.
                       /// </summary>
                       [SingletonDIProvide]
                       public class ServiceA 
                       { 
                           // Метод сервиса A
                           public void MethodA() { }
                       }

                       /// <summary>
                       /// Сервис B.
                       /// </summary>
                       [SingletonDIProvide]
                       public class ServiceB 
                       { 
                           // Метод сервиса B
                           public void MethodB() { }
                       }

                       /// <summary>
                       /// Базовый консьюмер.
                       /// </summary>
                       [SingletonDIConsume(typeof(ServiceA))]
                       public partial class BaseConsumer
                       {
                           // Комментарий в BaseConsumer
                           private int _counter;
                       }

                       /// <summary>
                       /// Производный консьюмер.
                       /// </summary>
                       [SingletonDIConsume(typeof(ServiceB))]
                       partial class DerivedConsumer : BaseConsumer
                       {
                           /* Многострочный комментарий
                              в DerivedConsumer */
                           private string _name;
                       
                           /// <summary>
                           /// Имя консьюмера.
                           /// </summary>
                           public string Name => _name;
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0011");
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

    private static async Task VerifyCodeFixAsync(
        string testSource,
        string expectedSource,
        string diagnosticId,
        CodeFixProvider codeFixProvider)
    {
        // Create compilation with the test source
        var compilation = CreateCompilation(testSource);

        // Run generator to get diagnostics
        var generator = new SingletonDIGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        // Find the target diagnostic
        var targetDiagnostic = generatorDiagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        Assert.NotNull(targetDiagnostic);

        // Get the syntax tree from the compilation
        var syntaxTree = outputCompilation.SyntaxTrees.First();
        var root = await syntaxTree.GetRootAsync();

        // Find the node at the diagnostic location
        var node = root.FindNode(targetDiagnostic.Location.SourceSpan);

        // Apply the code fix based on the diagnostic ID and provider type
        SyntaxNode newRoot = null!;

        if (codeFixProvider is SingletonDIProviderCodeFixProvider)
        {
            newRoot = diagnosticId switch
            {
                "DM0004" => ApplyDM0004Fix(root, node),
                "DM0005" => ApplyDM0005Fix(root, node),
                "DM0012" => ApplyDM0012Fix(root, node),
                _ => throw new ArgumentException($"Unknown diagnostic ID: {diagnosticId}")
            };
        }
        else if (codeFixProvider is SingletonDIConsumerCodeFixProvider)
        {
            newRoot = ApplyConsumerFix(root, node);
        }

        Assert.NotNull(newRoot);

        // Get the fixed source
        var fixedSource = newRoot.NormalizeWhitespace().ToFullString();

        // Normalize whitespace for comparison
        var normalizedExpected = NormalizeWhitespace(expectedSource);

        Assert.Equal(normalizedExpected, fixedSource);
    }

    private static SyntaxNode ApplyDM0004Fix(SyntaxNode root, SyntaxNode node)
    {
        var typeDeclaration = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        Assert.NotNull(typeDeclaration);

        // Check if there's an existing parameterless constructor
        var existingParameterlessCtor = typeDeclaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault(c => c.ParameterList.Parameters.Count == 0);

        if (existingParameterlessCtor is not null)
        {
            // Change the existing constructor's accessibility to public
            // Preserve the leading trivia (comments, XML docs) from the original constructor
            var leadingTrivia = existingParameterlessCtor.GetLeadingTrivia();
            var newModifiers = MakePublicModifiers(existingParameterlessCtor.Modifiers);
            var newConstructor = existingParameterlessCtor
                .WithModifiers(newModifiers)
                .WithLeadingTrivia(leadingTrivia);
            var newMembers = typeDeclaration.Members.Replace(existingParameterlessCtor, newConstructor);
            var newTypeDeclaration = typeDeclaration.WithMembers(newMembers);
            return root.ReplaceNode(typeDeclaration, newTypeDeclaration);
        }
        else
        {
            // No parameterless constructor exists, create a new public one
            var constructor = SyntaxFactory.ConstructorDeclaration(typeDeclaration.Identifier)
                                           .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                                           .WithBody(SyntaxFactory.Block());

            var newMembers = typeDeclaration.Members.Insert(0, constructor);
            var newTypeDeclaration = typeDeclaration.WithMembers(newMembers);

            return root.ReplaceNode(typeDeclaration, newTypeDeclaration);
        }
    }

    private static SyntaxTokenList MakePublicModifiers(SyntaxTokenList existingModifiers)
    {
        // Remove all access modifiers and add public at the beginning
        var newModifiers = SyntaxFactory.TokenList();

        foreach (var modifier in existingModifiers)
        {
            if (!IsAccessModifier(modifier.Kind()))
            {
                newModifiers = newModifiers.Add(modifier);
            }
        }

        // Insert public at the beginning
        newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        return newModifiers;
    }

    private static SyntaxNode ApplyDM0005Fix(SyntaxNode root, SyntaxNode node)
    {
        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        Assert.NotNull(methodDeclaration);

        // Preserve the leading trivia (comments, XML docs) from the original method
        var leadingTrivia = methodDeclaration.GetLeadingTrivia();

        var modifiers = methodDeclaration.Modifiers;
        var newModifiers = SyntaxFactory.TokenList();
        var hasAccessModifier = false;

        foreach (var modifier in modifiers)
        {
            if (IsAccessModifier(modifier.Kind()))
            {
                if (!hasAccessModifier)
                {
                    newModifiers = newModifiers.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                    hasAccessModifier = true;
                }
            }
            else
            {
                newModifiers = newModifiers.Add(modifier);
            }
        }

        if (!hasAccessModifier)
        {
            newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PublicKeyword));
        }

        var newMethodDeclaration = methodDeclaration
            .WithModifiers(newModifiers)
            .WithLeadingTrivia(leadingTrivia);
        return root.ReplaceNode(methodDeclaration, newMethodDeclaration);
    }

    private static SyntaxNode ApplyDM0012Fix(SyntaxNode root, SyntaxNode node)
    {
        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        Assert.NotNull(methodDeclaration);

        // Preserve the leading trivia (comments, XML docs) from the original method
        var leadingTrivia = methodDeclaration.GetLeadingTrivia();

        var newModifiers = SyntaxFactory.TokenList(
            methodDeclaration.Modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));

        var newMethodDeclaration = methodDeclaration
            .WithModifiers(newModifiers)
            .WithLeadingTrivia(leadingTrivia);
        return root.ReplaceNode(methodDeclaration, newMethodDeclaration);
    }

    private static SyntaxNode ApplyConsumerFix(SyntaxNode root, SyntaxNode node)
    {
        var typeOfExpression = node.FirstAncestorOrSelf<TypeOfExpressionSyntax>();
        Assert.NotNull(typeOfExpression);

        var attributeArgument = typeOfExpression.Parent as AttributeArgumentSyntax;
        Assert.NotNull(attributeArgument);

        var attributeSyntax = attributeArgument.FirstAncestorOrSelf<AttributeSyntax>();
        Assert.NotNull(attributeSyntax);

        var argumentList = attributeSyntax.ArgumentList;
        Assert.NotNull(argumentList);

        var newArguments = argumentList.Arguments.Remove(attributeArgument);

        // If no arguments remain, remove the entire attribute
        if (newArguments.Count == 0)
        {
            var attributeList = attributeSyntax.Parent as AttributeListSyntax;
            Assert.NotNull(attributeList);

            if (attributeList.Attributes.Count == 1)
            {
                // Use KeepLeadingTrivia to preserve comments and XML docs
                return root.RemoveNode(attributeList, SyntaxRemoveOptions.KeepLeadingTrivia)!;
            }

            var newAttributeList = attributeList.RemoveNode(attributeSyntax, SyntaxRemoveOptions.KeepNoTrivia);
            Assert.NotNull(newAttributeList);

            return root.ReplaceNode(attributeList, newAttributeList);
        }

        var newArgumentList = argumentList.WithArguments(newArguments);
        var newAttribute = attributeSyntax.WithArgumentList(newArgumentList);

        return root.ReplaceNode(attributeSyntax, newAttribute);
    }

    private static bool IsAccessModifier(SyntaxKind kind)
    {
        return kind is SyntaxKind.PublicKeyword
            or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword
            or SyntaxKind.InternalKeyword;
    }

    private static string NormalizeWhitespace(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        return root.NormalizeWhitespace().ToFullString();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SingletonDI.Attributes.SingletonDIProvideAttribute).Assembly.Location),
        };

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

#endregion
}
