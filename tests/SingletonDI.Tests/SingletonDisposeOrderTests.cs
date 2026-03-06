using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SingletonDI.Generator;
using Xunit;

namespace SingletonDI.Tests;

/// <summary>
/// Tests for verifying the order of singleton creation and disposal.
/// Tests that singletons are created in dependency order (Level 0 → Level 1 → Level 2)
/// and disposed in reverse order (Level 2 → Level 1 → Level 0).
/// </summary>
public class SingletonDisposeOrderTests
{
    /// <summary>
    /// Source code with 3 independent threads of dependencies:
    /// - Thread A: DatabaseConfigA (level 0) → DatabaseConnectionA (level 1) → RepositoryA (level 2)
    /// - Thread B: CacheConfigB (level 0) → CacheConnectionB (level 1) → CacheRepositoryB (level 2)
    /// - Thread C: QueueConfigC (level 0) → QueueConnectionC (level 1) → QueueProcessorC (level 2)
    /// </summary>
    private const string ThreeThreadsSource =
        """
        using System;
        using System.Collections.Generic;
        using SingletonDI.Attributes;

        namespace TestApp
        {
            // ===== Thread A - Database =====

            [SingletonDIProvide]
            public class DatabaseConfigA : IDisposable
            {
                public DatabaseConfigA() => ActionLog.Add("Register Level 0 DatabaseConfigA");
                public void Dispose() => ActionLog.Add("Dispose Level 0 DatabaseConfigA");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(DatabaseConfigA))]
            public partial class DatabaseConnectionA : IDisposable
            {
                public DatabaseConnectionA() => ActionLog.Add("Register Level 1 DatabaseConnectionA");
                public void Dispose() => ActionLog.Add("Dispose Level 1 DatabaseConnectionA");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(DatabaseConnectionA))]
            public partial class RepositoryA : IDisposable
            {
                public RepositoryA() => ActionLog.Add("Register Level 2 RepositoryA");
                public void Dispose() => ActionLog.Add("Dispose Level 2 RepositoryA");
            }

            // ===== Thread B - Cache =====

            [SingletonDIProvide]
            public class CacheConfigB : IDisposable
            {
                public CacheConfigB() => ActionLog.Add("Register Level 0 CacheConfigB");
                public void Dispose() => ActionLog.Add("Dispose Level 0 CacheConfigB");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(CacheConfigB))]
            public partial class CacheConnectionB : IDisposable
            {
                public CacheConnectionB() => ActionLog.Add("Register Level 1 CacheConnectionB");
                public void Dispose() => ActionLog.Add("Dispose Level 1 CacheConnectionB");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(CacheConnectionB))]
            public partial class CacheRepositoryB : IDisposable
            {
                public CacheRepositoryB() => ActionLog.Add("Register Level 2 CacheRepositoryB");
                public void Dispose() => ActionLog.Add("Dispose Level 2 CacheRepositoryB");
            }

            // ===== Thread C - Queue =====

            [SingletonDIProvide]
            public class QueueConfigC : IDisposable
            {
                public QueueConfigC() => ActionLog.Add("Register Level 0 QueueConfigC");
                public void Dispose() => ActionLog.Add("Dispose Level 0 QueueConfigC");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(QueueConfigC))]
            public partial class QueueConnectionC : IDisposable
            {
                public QueueConnectionC() => ActionLog.Add("Register Level 1 QueueConnectionC");
                public void Dispose() => ActionLog.Add("Dispose Level 1 QueueConnectionC");
            }

            [SingletonDIProvide]
            [SingletonDIConsume(typeof(QueueConnectionC))]
            public partial class QueueProcessorC : IDisposable
            {
                public QueueProcessorC() => ActionLog.Add("Register Level 2 QueueProcessorC");
                public void Dispose() => ActionLog.Add("Dispose Level 2 QueueProcessorC");
            }

            // ===== Static Logger =====

            public static class ActionLog
            {
                private static readonly List<string> _actions = new();
                
                public static void Add(string action)
                {
                    lock (_actions)
                    {
                        _actions.Add(action);
                    }
                }
                
                public static void Clear()
                {
                    lock (_actions)
                    {
                        _actions.Clear();
                    }
                }
                
                public static IReadOnlyList<string> GetActions()
                {
                    lock (_actions)
                    {
                        return new List<string>(_actions).AsReadOnly();
                    }
                }
            }
        }
        """;

    [Fact]
    public async Task DisposeOrder_ThreeIndependentThreads_CorrectOrder()
    {
        // Arrange - compile source with generator
        var (compilation, generatorDiagnostics) = CompileWithGenerator(ThreeThreadsSource);

        // Verify no compilation errors from generator
        var errors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // Act - load assembly and execute
        var assembly = LoadAssembly(compilation);

        // Get types via reflection
        var actionLogType = assembly.GetType("TestApp.ActionLog")
                            ?? throw new InvalidOperationException("Could not find TestApp.ActionLog type");
        var containerType = assembly.GetType("DependencyManager.Generated.Internal.SingletonDIContainer")
                            ?? throw new InvalidOperationException("Could not find SingletonDIContainer type");

        // Clear log before test
        var clearMethod = actionLogType.GetMethod("Clear")
                          ?? throw new InvalidOperationException("Could not find Clear method");
        clearMethod.Invoke(null, null);

        // Initialize container
        var initializeMethod = containerType.GetMethod("InitializeAsync")
                               ?? throw new InvalidOperationException("Could not find InitializeAsync method");
        var initTask = (Task) initializeMethod.Invoke(null, null)!
                       ?? throw new InvalidOperationException("InitializeAsync returned null");
        await initTask;

        // Dispose container
        var disposeMethod = containerType.GetMethod("DisposeAsync")
                            ?? throw new InvalidOperationException("Could not find DisposeAsync method");
        var disposeResult = disposeMethod.Invoke(null, null);
        if (disposeResult is ValueTask disposeTask)
        {
            await disposeTask;
        }

        // Get log
        var getActionsMethod = actionLogType.GetMethod("GetActions")
                               ?? throw new InvalidOperationException("Could not find GetActions method");
        var actions = (IReadOnlyList<string>) getActionsMethod.Invoke(null, null)!
                      ?? throw new InvalidOperationException("GetActions returned null");

        // Assert - validate log
        var result = LogValidator.Validate(actions);

        Assert.True(result.RegisterBeforeDisposeValid,
                    $"Register must be called before Dispose for each class.\nLog:\n{FormatLog(actions)}");
        Assert.True(result.RegisterOrderValid,
                    $"Register order must be: Level 0, then Level 1, then Level 2.\nLog:\n{FormatLog(actions)}");
        Assert.True(result.DisposeOrderValid,
                    $"Dispose order must be: Level 2, then Level 1, then Level 0.\nLog:\n{FormatLog(actions)}");
        Assert.True(result.AllClassesCreated,
                    $"All 9 classes must be created and disposed.\nLog:\n{FormatLog(actions)}");
    }

    [Fact]
    public async Task DisposeOrder_SingleThread_CorrectOrder()
    {
        // Arrange - single thread with 3 levels
        const string source =
            """
            using System;
            using System.Collections.Generic;
            using SingletonDI.Attributes;

            namespace TestApp
            {
                [SingletonDIProvide]
                public class Config : IDisposable
                {
                    public Config() => ActionLog.Add("Register Level 0 Config");
                    public void Dispose() => ActionLog.Add("Dispose Level 0 Config");
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(Config))]
                public partial class Connection : IDisposable
                {
                    public Connection() => ActionLog.Add("Register Level 1 Connection");
                    public void Dispose() => ActionLog.Add("Dispose Level 1 Connection");
                }

                [SingletonDIProvide]
                [SingletonDIConsume(typeof(Connection))]
                public partial class Repository : IDisposable
                {
                    public Repository() => ActionLog.Add("Register Level 2 Repository");
                    public void Dispose() => ActionLog.Add("Dispose Level 2 Repository");
                }

                public static class ActionLog
                {
                    private static readonly List<string> _actions = new();
                    
                    public static void Add(string action)
                    {
                        lock (_actions)
                        {
                            _actions.Add(action);
                        }
                    }
                    
                    public static void Clear()
                    {
                        lock (_actions)
                        {
                            _actions.Clear();
                        }
                    }
                    
                    public static IReadOnlyList<string> GetActions()
                    {
                        lock (_actions)
                        {
                            return new List<string>(_actions).AsReadOnly();
                        }
                    }
                }
            }
            """;

        var (compilation, generatorDiagnostics) = CompileWithGenerator(source);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var assembly = LoadAssembly(compilation);
        var actionLogType = assembly.GetType("TestApp.ActionLog")!;
        var containerType = assembly.GetType("DependencyManager.Generated.Internal.SingletonDIContainer")!;

        actionLogType.GetMethod("Clear")!.Invoke(null, null);

        var initTask = (Task) containerType.GetMethod("InitializeAsync")!.Invoke(null, null)!;
        await initTask;

        var disposeResult = containerType.GetMethod("DisposeAsync")!.Invoke(null, null);
        if (disposeResult is ValueTask disposeTask)
        {
            await disposeTask;
        }

        var actions = (IReadOnlyList<string>) actionLogType.GetMethod("GetActions")!.Invoke(null, null)!;

        // Assert exact order for single thread
        Assert.Equal(6, actions.Count);

        // Register order: Config, Connection, Repository
        Assert.Equal("Register Level 0 Config", actions[0]);
        Assert.Equal("Register Level 1 Connection", actions[1]);
        Assert.Equal("Register Level 2 Repository", actions[2]);

        // Dispose order: Repository, Connection, Config
        Assert.Equal("Dispose Level 2 Repository", actions[3]);
        Assert.Equal("Dispose Level 1 Connection", actions[4]);
        Assert.Equal("Dispose Level 0 Config", actions[5]);
    }

#region Helper Methods

    private static (CSharpCompilation compilation, ImmutableArray<Diagnostic> diagnostics) CompileWithGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new SingletonDIGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return ((CSharpCompilation) outputCompilation, diagnostics);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IDisposable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attributes.SingletonDIProvideAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        };

        // Add all referenced assemblies
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[] { "System.Runtime", "System.Collections", "System.Linq", "netstandard", "System.Threading.Tasks", "System.Console" })
        {
            var path = Path.Combine(assemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "TestAssembly_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Assembly LoadAssembly(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                                                     .Where(d => d.Severity == DiagnosticSeverity.Error)
                                                     .Select(d => d.GetMessage()));
            throw new InvalidOperationException($"Compilation failed:\n{errors}");
        }

        return Assembly.Load(ms.ToArray());
    }

    private static string FormatLog(IReadOnlyList<string> actions)
    {
        return string.Join("\n", actions.Select((a, i) => $"  [{i}] {a}"));
    }

#endregion

#region Log Validator

    /// <summary>
    /// Validates the order of Register and Dispose operations in the log.
    /// </summary>
    private static class LogValidator
    {
        public static ValidationResult Validate(IReadOnlyList<string> logActions)
        {
            var entries = ParseLogEntries(logActions);

            return new ValidationResult
            {
                RegisterBeforeDisposeValid = ValidateRegisterBeforeDispose(entries),
                RegisterOrderValid = ValidateRegisterOrder(entries),
                DisposeOrderValid = ValidateDisposeOrder(entries),
                AllClassesCreated = ValidateAllClassesCreated(entries)
            };
        }

        private static List<LogEntry> ParseLogEntries(IReadOnlyList<string> logActions)
        {
            var entries = new List<LogEntry>();

            for (var i = 0; i < logActions.Count; i++)
            {
                var parts = logActions[i].Split(' ');
                // Format: "Register Level 0 DatabaseConfigA" or "Dispose Level 0 DatabaseConfigA"
                if (parts.Length >= 4)
                {
                    entries.Add(new LogEntry
                    {
                        Action = parts[0], // "Register" or "Dispose"
                        Level = int.Parse(parts[2]), // 0, 1, 2
                        ClassName = parts[3], // Class name
                        Order = i
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// Rule 1: Register must be called before Dispose for each class.
        /// </summary>
        private static bool ValidateRegisterBeforeDispose(IEnumerable<LogEntry> entries)
        {
            var registerMap = new Dictionary<string, int>(); // className -> order
            var disposeMap = new Dictionary<string, int>();

            foreach (var entry in entries)
            {
                if (entry.Action == "Register")
                    registerMap[entry.ClassName] = entry.Order;
                else if (entry.Action == "Dispose")
                    disposeMap[entry.ClassName] = entry.Order;
            }

            // Check that for each Dispose there was a Register before it
            foreach (var (className, disposeOrder) in disposeMap)
            {
                if (!registerMap.TryGetValue(className, out var registerOrder))
                    return false; // Dispose without Register

                if (registerOrder >= disposeOrder)
                    return false; // Dispose before Register
            }

            return true;
        }

        /// <summary>
        /// Rule 2: All Register operations at level N must complete before any Register at level N+1.
        /// </summary>
        private static bool ValidateRegisterOrder(IEnumerable<LogEntry> entries)
        {
            var registers = entries
                            .Where(e => e.Action == "Register")
                            .OrderBy(e => e.Order)
                            .ToList();

            for (var i = 1; i < registers.Count; i++)
            {
                // Level should never decrease during Register phase
                if (registers[i].Level < registers[i - 1].Level)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Rule 3: All Dispose operations at level N must complete before any Dispose at level N-1.
        /// </summary>
        private static bool ValidateDisposeOrder(IEnumerable<LogEntry> entries)
        {
            var disposes = entries
                           .Where(e => e.Action == "Dispose")
                           .OrderBy(e => e.Order)
                           .ToList();

            for (var i = 1; i < disposes.Count; i++)
            {
                // Level should never increase during Dispose phase
                if (disposes[i].Level > disposes[i - 1].Level)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Rule 4: All expected classes must be created and disposed.
        /// </summary>
        private static bool ValidateAllClassesCreated(IEnumerable<LogEntry> entries)
        {
            var expectedClasses = new HashSet<string>
            {
                "DatabaseConfigA", "DatabaseConnectionA", "RepositoryA",
                "CacheConfigB", "CacheConnectionB", "CacheRepositoryB",
                "QueueConfigC", "QueueConnectionC", "QueueProcessorC"
            };

            var registeredClasses = entries
                                    .Where(e => e.Action == "Register")
                                    .Select(e => e.ClassName)
                                    .ToHashSet();

            var disposedClasses = entries
                                  .Where(e => e.Action == "Dispose")
                                  .Select(e => e.ClassName)
                                  .ToHashSet();

            return expectedClasses.SetEquals(registeredClasses) &&
                   expectedClasses.SetEquals(disposedClasses);
        }
    }

    private record LogEntry
    {
        public string Action { get; init; } = "";
        public int Level { get; init; }
        public string ClassName { get; init; } = "";
        public int Order { get; init; }
    }

    private record ValidationResult
    {
        public bool RegisterBeforeDisposeValid { get; init; }
        public bool RegisterOrderValid { get; init; }
        public bool DisposeOrderValid { get; init; }
        public bool AllClassesCreated { get; init; }

        public bool IsValid =>
            RegisterBeforeDisposeValid &&
            RegisterOrderValid &&
            DisposeOrderValid &&
            AllClassesCreated;
    }

#endregion
}