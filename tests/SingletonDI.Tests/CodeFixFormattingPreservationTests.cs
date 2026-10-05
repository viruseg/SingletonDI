using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Refactoring;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Blast-radius coverage for every fix the analyzer ships.
/// </summary>
/// <remarks>
/// <para>
/// Marking a node with <c>Formatter.Annotation</c> makes the IDE re-lay out that node's whole span,
/// so a fix that annotated its enclosing type or method came back having rewritten code it had no
/// business touching: method signatures lost their spacing, casts lost the space after them, bodies
/// were re-indented. The re-layout shows up as changed trivia on tokens the fix never authored, which
/// is what these tests look for.
/// </para>
/// <para>
/// Every fixture is written in a deliberately wrong style throughout, and the assertion is that the
/// fixed document differs from the original only by the tokens the fix itself produced. Every
/// retained token has to keep its exact text, leading and trailing trivia included.
/// </para>
/// </remarks>
public class CodeFixFormattingPreservationTests
{
    /// <summary>
    /// Upper bound on the tokens a fix may author. Generous enough for the member a fix adds and
    /// tight enough that reformatting any declaration body trips it.
    /// </summary>

    [Fact]
    public async Task DM0004_AuthorsOnlyTheConstructorItAdds()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                private  Service ( )
                {
                    int  counter  =  0;
                }

                public  int  GetValue ( )
                {
                    return  ( counter  )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0004",
            new SingletonDIProviderCodeFixProvider(),
            "Add public parameterless constructor",
            authoredTokenBudget: 1);
    }

    [Fact]
    public async Task DM0005_AuthorsOnlyTheModifierItInserts()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                private  Task  InitializeAsync ( )
                {
                    int  total  =  0;
                    return  Task.CompletedTask;
                }

                public  int  GetValue ( )
                {
                    return  ( total  )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0005",
            new SingletonDIProviderCodeFixProvider(),
            "Make method public", authoredTokenBudget: 1);
    }

    [Fact]
    public async Task DM0010_AuthorsOnlyTheArgumentItRemoves()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  int  GetValue ( )
                {
                    return  ( 1 )  -  1;
                }
            }

            [SingletonDIConsume(typeof(Service),
                               typeof(Service),
                               typeof(Other))]
            public  partial  class  MyConsumer
            {
                public  int  GetValue ( )
                {
                    return  ( 2 )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0010",
            new SingletonDIConsumerCodeFixProvider(),
            "Remove duplicate type", authoredTokenBudget: 5);
    }

    [Fact]
    public async Task DM0010_RemovingTheLastArgumentAuthorsOnlyTheArgumentAndItsSeparator()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  int  GetValue ( )
                {
                    return  ( 1 )  -  1;
                }
            }

            [SingletonDIConsume(typeof(Service),
                               typeof(Service))]
            public  partial  class  MyConsumer
            {
                public  int  GetValue ( )
                {
                    return  ( 2 )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0010",
            new SingletonDIConsumerCodeFixProvider(),
            "Remove duplicate type", authoredTokenBudget: 5);
    }

    [Fact]
    public async Task DM0010_AuthorsOnlyTheArgumentItRemovesFromAPaddedAttribute()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  int  GetValue ( )
                {
                    return  ( 1 )  -  1;
                }
            }

            [SingletonDIConsume( typeof(Service) ,typeof(Service) )]
            public  partial  class  MyConsumer
            {
                public  int  GetValue ( )
                {
                    return  ( 2 )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0010",
            new SingletonDIConsumerCodeFixProvider(),
            "Remove duplicate type", authoredTokenBudget: 5);
    }

    [Fact]
    public async Task DM0012_AuthorsOnlyTheModifierItRemoves()
    {
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  static  Task  InitializeAsync ( )
                {
                    int  value  =  1;
                    return  Task.CompletedTask;
                }

                public  int  GetValue ( )
                {
                    return  ( value )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0012",
            new SingletonDIProviderCodeFixProvider(),
            "Remove static modifier", authoredTokenBudget: 1);
    }

    [Fact]
    public async Task DM0007_AuthorsOnlyTheModifierItInserts()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  int  GetValue ( )
                {
                    return  ( 1 )  -  1;
                }
            }

            [SingletonDIConsume(typeof(Service))]
            public  class  MyConsumer
            {
                public  uint  TestMethod (
                   int  i,
                      byte  b )
                {
                        i += 1;
                    return  ( uint )  ( i +
                                        ( int )  b );
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0007",
            new SingletonDIPartialCodeFixProvider(),
            "Add 'partial' modifier", authoredTokenBudget: 1);
    }

    [Fact]
    public async Task DM0036_AuthorsOnlyTheAttributeItRemoves()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  static  int  Value => 1;
            }

            /// <summary>
            /// Документация консьюмера.
            /// </summary>
            [SingletonDIConsume(typeof(Service))]
            public  partial  class  MyConsumer
            {
                public  int  GetValue ( )
                {
                    return  ( 2 )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0036",
            new SingletonDIConsumerCodeFixProvider(),
            "Remove unused dependency", authoredTokenBudget: 9);
    }

    [Fact]
    public async Task DM0036_AuthorsOnlyTheArgumentItRemoves()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  static  int  Value => 1;
            }

            [SingletonDIProvide]
            public  class  Other
            {
                public  static  int  Value => 2;
            }

            [SingletonDIConsume(typeof(Service),
                               typeof(Other))]
            public  partial  class  MyConsumer
            {
                public  int  GetValue ( )
                {
                    return  ( Service.Value )  -  1;
                }
            }
            """;

        await AssertFixAuthorsOnlyItsOwnTokensAsync(
            source,
            "DM0036",
            new SingletonDIConsumerCodeFixProvider(),
            "Remove unused dependency", authoredTokenBudget: 5);
    }

    [Fact]
    public async Task AddInitializeAsync_AuthorsOnlyTheMethodItAdds()
    {
        const string source = """
            using SingletonDI.Attributes;

            [SingletonDIProvide]
            public  class  Service
            {
                public  int  GetValue ( )
                {
                    return  ( 1 )  -  1;
                }
            }
            """;

        var context = await RefactoringTestHarness.CreateAsync(source);
        var identifier = (await context.Document.GetSyntaxRootAsync())!
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Service")
            .Identifier.Span;

        var refactored = await RefactoringTestHarness.ApplyOnlyAndFormatAsync(
            source,
            identifier,
            new SingletonDIProvideRefactoringProvider(),
            "Add InitializeAsync method");

        // The budget is the generated method's own token count: a fully qualified return type spells
        // out 28 of them. Nothing may be rewritten beyond that method.
        AssertAuthorsOnlyItsOwnTokens(
            source,
            (await refactored.GetTextAsync()).ToString(),
            authoredTokenBudget: 28);
    }

    private static async Task AssertFixAuthorsOnlyItsOwnTokensAsync(
        string source,
        string diagnosticId,
        CodeFixProvider provider,
        string expectedTitle,
        int authoredTokenBudget)
    {
        var document = await CodeFixTestHarness.ApplyFirstAndFormatAsync(
            source,
            diagnosticId,
            provider,
            expectedTitle);

        AssertAuthorsOnlyItsOwnTokens(source, (await document.GetTextAsync()).ToString(), authoredTokenBudget);
    }

    /// <summary>
    /// Asserts that <paramref name="fixedText"/> differs from <paramref name="source"/> only by a
    /// single contiguous run of at most <paramref name="authoredTokenBudget"/> tokens, and that every
    /// token outside that run kept its exact trivia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The run is located on token text alone, because a re-layout never changes a token's text - it
    /// changes the whitespace around it, which is exactly what the assertions below catch.
    /// </para>
    /// <para>
    /// The token bordering the run on either side is exempt: the whitespace between the last token
    /// the fix kept and the first token it added is the fix's to adjust, and every fix that adds a
    /// token needs it. Exempting only those two tokens keeps the rule narrow enough to still fail on
    /// a fix that reformats a whole declaration.
    /// </para>
    /// </remarks>
    private static void AssertAuthorsOnlyItsOwnTokens(
        string source,
        string fixedText,
        int authoredTokenBudget)
    {
        var before = Tokens(source);
        var after = Tokens(fixedText);

        var prefix = 0;
        while (prefix < before.Count && prefix < after.Count && before[prefix].Text == after[prefix].Text)
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < before.Count - prefix &&
               suffix < after.Count - prefix &&
               before[before.Count - 1 - suffix].Text == after[after.Count - 1 - suffix].Text)
        {
            suffix++;
        }

        var authoredBefore = before.Count - prefix - suffix;
        var authoredAfter = after.Count - prefix - suffix;

        Assert.True(
            authoredBefore <= authoredTokenBudget && authoredAfter <= authoredTokenBudget,
            $"The fix rewrote {Math.Max(authoredBefore, authoredAfter)} tokens but may only author "
            + $"{authoredTokenBudget}, so it reformatted code the user had formatted their own way. "
            + $"It starts at {Show(before[prefix].Text)}, which became {Show(after[prefix].Text)}.");

        for (var index = 0; index < prefix - 1; index++)
        {
            AssertSameToken(before[index], after[index]);
        }

        for (var offset = 1; offset < suffix; offset++)
        {
            AssertSameToken(before[before.Count - suffix + offset], after[after.Count - suffix + offset]);
        }
    }

    private static void AssertSameToken(SyntaxToken before, SyntaxToken after)
    {
        Assert.Equal(before.ToFullString(), after.ToFullString());
    }

    private static string Show(string token) =>
        token.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    /// <summary>
    /// Folds line endings to a single marker, so the comparison reports formatting changes rather
    /// than the host newline convention the workspace formatter writes.
    /// </summary>
    private static string NormalizeNewLines(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IReadOnlyList<SyntaxToken> Tokens(string source) =>
        CSharpSyntaxTree.ParseText(NormalizeNewLines(source))
            .GetRoot()
            .DescendantTokens()
            .ToList();
}
