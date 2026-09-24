using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Refactoring;
using Xunit;

namespace SingletonDI.Tests;

public sealed class RefactoringProviderTests
{
    [Fact]
    public async Task OffersInitializeActionOnProviderName()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declaration = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        var actions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.Identifier.Span,
            new SingletonDIProvideRefactoringProvider());

        var action = Assert.Single(actions);
        Assert.Equal("Add InitializeAsync method", action.Title);
        var changedDocument = await RefactoringTestHarness.ApplyAsync(context, action);
        var compilation = await changedDocument.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DoesNotOfferActionOutsideProviderName()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
                public int Value => 1;
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declaration = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        var actions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.OpenBraceToken.Span,
            new SingletonDIProvideRefactoringProvider());

        Assert.Empty(actions);
    }

    [Fact]
    public async Task DoesNotOfferActionWithoutProviderAttribute()
    {
        const string source = """
            public class Service
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declaration = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        var actions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.Identifier.Span,
            new SingletonDIProvideRefactoringProvider());

        Assert.Empty(actions);
    }

    [Fact]
    public async Task DoesNotOfferActionWhenTaskInitializerExists()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
                public Task InitializeAsync() => Task.CompletedTask;
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declaration = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        var actions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.Identifier.Span,
            new SingletonDIProvideRefactoringProvider());

        Assert.Empty(actions);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declaration = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RefactoringTestHarness.GetActionsAsync(
                context,
                declaration.Identifier.Span,
                new SingletonDIProvideRefactoringProvider(),
                cancellation.Token));
    }
}
