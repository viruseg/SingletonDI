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
        var text = (await changedDocument.GetTextAsync()).ToString();
        Assert.Contains("public global::System.Threading.Tasks.Task InitializeAsync()", text);
        Assert.Contains("return global::System.Threading.Tasks.Task.CompletedTask;", text);
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
        var provider = new SingletonDIProvideRefactoringProvider();

        var identifierActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.Identifier.Span,
            provider);
        var braceActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.OpenBraceToken.Span,
            provider);

        Assert.Single(identifierActions);
        Assert.Empty(braceActions);
    }

    [Fact]
    public async Task DoesNotOfferActionWithoutProviderAttribute()
    {
        const string source = """
            public class Service
            {
            }

            [SingletonDI.Attributes.SingletonDIProvide]
            public class Marked
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declarations = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToArray();
        var provider = new SingletonDIProvideRefactoringProvider();

        var markedActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[1].Identifier.Span,
            provider);
        var unmarkedActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[0].Identifier.Span,
            provider);

        Assert.Single(markedActions);
        Assert.Empty(unmarkedActions);
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

            [SingletonDIProvide]
            public class Other
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declarations = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToArray();
        var provider = new SingletonDIProvideRefactoringProvider();

        var otherActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[1].Identifier.Span,
            provider);
        var serviceActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[0].Identifier.Span,
            provider);

        Assert.Single(otherActions);
        Assert.Empty(serviceActions);
    }

    [Fact]
    public async Task DoesNotOfferActionWhenValueTaskInitializerExists()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
                public ValueTask InitializeAsync() => ValueTask.CompletedTask;
            }

            [SingletonDIProvide]
            public class Other
            {
            }
            """;
        var context = await RefactoringTestHarness.CreateAsync(source);
        var declarations = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .ToArray();
        var provider = new SingletonDIProvideRefactoringProvider();

        var otherActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[1].Identifier.Span,
            provider);
        var serviceActions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declarations[0].Identifier.Span,
            provider);

        Assert.Single(otherActions);
        Assert.Empty(serviceActions);
    }

    [Fact]
    public async Task OffersActionForInitializerWithParameters()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public class Service
            {
                public Task InitializeAsync(int value) => Task.CompletedTask;
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
        var changedDocument = await RefactoringTestHarness.ApplyAsync(context, action);
        var compilation = await changedDocument.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var text = (await changedDocument.GetTextAsync()).ToString();
        Assert.Contains("public Task InitializeAsync(int value)", text);
        Assert.Contains("public global::System.Threading.Tasks.Task InitializeAsync()", text);
    }

    [Fact]
    public async Task AddsCompilableTaskWithoutTaskUsing()
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
        var actions = await RefactoringTestHarness.GetActionsAsync(
            context,
            declaration.Identifier.Span,
            new SingletonDIProvideRefactoringProvider());
        var action = Assert.Single(actions);
        var changedDocument = await RefactoringTestHarness.ApplyAsync(context, action);
        var compilation = await changedDocument.Project.GetCompilationAsync();
        Assert.NotNull(compilation);
        Assert.DoesNotContain(compilation!.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var text = (await changedDocument.GetTextAsync()).ToString();
        Assert.Contains("public global::System.Threading.Tasks.Task InitializeAsync()", text);
        Assert.Contains("return global::System.Threading.Tasks.Task.CompletedTask;", text);
        Assert.DoesNotContain("using System.Threading.Tasks;", text);
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
