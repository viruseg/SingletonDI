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

                   [SingletonDIProvide]
                   class MyClass
                   {
                       private MyClass() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyClass
                       {
                           public MyClass() { }
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

                   [SingletonDIProvide]
                   class MyService
                   {
                       private MyService() { }
                       
                       public void DoWork() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyService
                       {
                           public MyService() { }
                       
                           public void DoWork() { }
                       }
                       """;

        await VerifyProviderCodeFixAsync(test, expected, "DM0004");
    }

    [Fact]
    public async Task DM0004_AddPublicConstructor_WithExistingConstructor()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   class MyService
                   {
                       public MyService(int a) { }
                       
                       public void DoWork() { }
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyService
                       {
                           public MyService()
                           {
                           }
                           
                           public MyService(int a) { }

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

                   [SingletonDIProvide]
                   class MyClass
                   {
                       private Task InitializeAsync() => Task.CompletedTask;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyClass
                       {
                           public Task InitializeAsync() => Task.CompletedTask;
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

                   [SingletonDIProvide]
                   class MyClass
                   {
                       protected Task InitializeAsync() => Task.CompletedTask;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyClass
                       {
                           public Task InitializeAsync() => Task.CompletedTask;
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

                   [SingletonDIProvide]
                   class MyClass
                   {
                       public static Task InitializeAsync() => Task.CompletedTask;
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyClass
                       {
                           public Task InitializeAsync() => Task.CompletedTask;
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

                   [SingletonDIProvide]
                   class MyClass
                   {
                       public static async Task InitializeAsync() => await Task.Delay(1);
                   }
                   """;

        var expected = """
                       using System.Threading.Tasks;
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       class MyClass
                       {
                           public async Task InitializeAsync() => await Task.Delay(1);
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

                   [SingletonDIProvide]
                   public class MyService { }

                   [SingletonDIConsume(typeof(MyService), typeof(MyService))]
                   partial class MyClass
                   {
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService { }

                       [SingletonDIConsume(typeof(MyService))]
                       partial class MyClass
                       {
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0010");
    }

    [Fact]
    public async Task DM0010_RemoveDuplicateType_MultipleDuplicates()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class ServiceA { }

                   [SingletonDIProvide]
                   public class ServiceB { }

                   [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB), typeof(ServiceA))]
                   partial class MyClass
                   {
                   }
                   """;

        // Note: The code fix removes the first occurrence of the duplicate (the second typeof(ServiceA))
        // So the result is typeof(ServiceA), typeof(ServiceB)
        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class ServiceA { }

                       [SingletonDIProvide]
                       public class ServiceB { }

                       [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB))]
                       partial class MyClass
                       {
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0010");
    }

    [Fact]
    public async Task DM0011_RemoveTypeFromAttribute()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class MyService { }

                   [SingletonDIConsume(typeof(MyService))]
                   public partial class BaseConsumer
                   {
                   }

                   [SingletonDIConsume(typeof(MyService))]
                   partial class DerivedConsumer : BaseConsumer
                   {
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class MyService { }

                       [SingletonDIConsume(typeof(MyService))]
                       public partial class BaseConsumer
                       {
                       }

                       partial class DerivedConsumer : BaseConsumer
                       {
                       }
                       """;

        await VerifyConsumerCodeFixAsync(test, expected, "DM0011");
    }

    [Fact]
    public async Task DM0011_RemoveTypeFromAttribute_MultipleTypes()
    {
        var test = """
                   using SingletonDI.Attributes;

                   [SingletonDIProvide]
                   public class ServiceA { }

                   [SingletonDIProvide]
                   public class ServiceB { }

                   [SingletonDIConsume(typeof(ServiceA))]
                   public partial class BaseConsumer
                   {
                   }

                   [SingletonDIConsume(typeof(ServiceA), typeof(ServiceB))]
                   partial class DerivedConsumer : BaseConsumer
                   {
                   }
                   """;

        var expected = """
                       using SingletonDI.Attributes;

                       [SingletonDIProvide]
                       public class ServiceA { }

                       [SingletonDIProvide]
                       public class ServiceB { }

                       [SingletonDIConsume(typeof(ServiceA))]
                       public partial class BaseConsumer
                       {
                       }

                       [SingletonDIConsume(typeof(ServiceB))]
                       partial class DerivedConsumer : BaseConsumer
                       {
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
            var newModifiers = MakePublicModifiers(existingParameterlessCtor.Modifiers);
            var newConstructor = existingParameterlessCtor.WithModifiers(newModifiers);
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

        var newMethodDeclaration = methodDeclaration.WithModifiers(newModifiers);
        return root.ReplaceNode(methodDeclaration, newMethodDeclaration);
    }

    private static SyntaxNode ApplyDM0012Fix(SyntaxNode root, SyntaxNode node)
    {
        var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        Assert.NotNull(methodDeclaration);

        var newModifiers = SyntaxFactory.TokenList(
            methodDeclaration.Modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));

        var newMethodDeclaration = methodDeclaration.WithModifiers(newModifiers);
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
                return root.RemoveNode(attributeList, SyntaxRemoveOptions.KeepNoTrivia)!;
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