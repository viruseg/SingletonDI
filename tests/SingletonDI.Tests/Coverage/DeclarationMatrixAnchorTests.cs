using System.Collections.Immutable;
using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed class DeclarationMatrixAnchorTests
{
    private sealed record CoexistenceCase(
        string Name,
        string SourceDeclaration,
        string GeneratedDeclaration,
        string ExpectedProperty);

    [Fact]
    public void MatrixDataIsWellFormed()
    {
        DeclarationCaseVerifier.VerifyData(ProviderDeclarationMatrixTests.VisibilityRows, "PROVIDER_VISIBILITY");
        DeclarationCaseVerifier.VerifyData(ProviderDeclarationMatrixTests.NestingRows, "PROVIDER_NESTING");
        DeclarationCaseVerifier.VerifyData(ProviderDeclarationMatrixTests.TypeKindRows, "PROVIDER_TYPE_KIND");
        DeclarationCaseVerifier.VerifyData(ProviderDeclarationMatrixTests.ConstructorRows, "PROVIDER_CONSTRUCTOR");
        DeclarationCaseVerifier.VerifyData(ProviderDeclarationMatrixTests.InitializerRows, "PROVIDER_INITIALIZER");
        DeclarationCaseVerifier.VerifyData(ConsumerDeclarationMatrixTests.ShapeRows, "CONSUMER_SHAPE");
        DeclarationCaseVerifier.VerifyData(ConsumerDeclarationMatrixTests.InheritanceRows, "CONSUMER_INHERITANCE");
        DeclarationCaseVerifier.VerifyData(ConsumerDeclarationMatrixTests.NestingRows, "CONSUMER_NESTING");
        DeclarationCaseVerifier.VerifyData(ConsumerDeclarationMatrixTests.NamespaceRows, "CONSUMER_NAMESPACE");
        DeclarationCaseVerifier.VerifyData(ConsumerDeclarationMatrixTests.GenericRows, "CONSUMER_GENERIC");
        DeclarationCaseVerifier.VerifyData(DeclarationContextMatrixTests.NullableRows, "NULLABILITY");
        DeclarationCaseVerifier.VerifyData(DeclarationContextMatrixTests.LanguageVersionRows, "LANGUAGE_VERSION");
        DeclarationCaseVerifier.VerifyData(DeclarationContextMatrixTests.ServiceTypeRows, "SERVICE_TYPE");
        DeclarationCaseVerifier.VerifyData(DeclarationContextMatrixTests.ConditionalCompilationRows, "CONDITIONAL_COMPILATION");
        DeclarationCaseVerifier.VerifyData(DeclarationContractMatrixTests.AttributeFormRows, "ATTRIBUTE_FORM");
        DeclarationCaseVerifier.VerifyData(DeclarationContractMatrixTests.PropertyNameRows, "PROPERTY_NAME");
    }

    [Fact]
    public void AllShapesCoexistInOneCompilation()
    {
        // Комбинация форм, которую нельзя выразить одной строкой матрицы: проверяет, что эмиттер
        // не теряет модификаторы и не путает типы, когда их много в одной сборке. Файловое
        // пространство имён сюда не входит по построению: прибор компилирует один исходник, а
        // файловое пространство имён обязано предшествовать всем объявлениям в файле, поэтому
        // второй блок с ним в одном файле несовместим; его закрывает строка `CONS-NSM-02`.
        const string source = """
            using System.Threading.Tasks;
            using SingletonDI.Attributes;

            namespace App
            {
                public interface IContract
                {
                }

                [SingletonDIProvide]
                public class Service
                {
                    public Task InitializeAsync() => Task.CompletedTask;
                }

                [SingletonDIProvide(ServiceType = typeof(IContract))]
                internal sealed class InternalService : IContract
                {
                }

                [SingletonDIProvide]
                public record RecordService;

                public class ProviderHost
                {
                    [SingletonDIProvide]
                    public class NestedService
                    {
                    }
                }
            }

            namespace App.Consumers
            {
                [SingletonDIConsume(typeof(App.Service))]
                public partial class Unsealed
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public sealed partial class Sealed
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                internal partial class Internal
                {
                }

                [SingletonDIConsume(typeof(App.IContract))]
                public partial class ByContract
                {
                }

                [SingletonDIConsume(typeof(App.RecordService))]
                public partial class ByRecord
                {
                }

                [SingletonDIConsume(typeof(App.ProviderHost.NestedService))]
                public partial class ByNestedProvider
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public partial struct AsStruct
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public readonly partial struct AsReadonlyStruct
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public ref partial struct AsRefStruct
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public partial record AsRecord;

                [SingletonDIConsume(typeof(App.Service))]
                public partial record class AsRecordClass;

                [SingletonDIConsume(typeof(App.Service))]
                public sealed partial record AsSealedRecord;

                [SingletonDIConsume(typeof(App.Service))]
                public partial record struct AsRecordStruct
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public abstract partial class AsAbstract
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public partial class Generic<T> where T : class
                {
                }

                [SingletonDIConsume(typeof(App.Service))]
                public partial class GenericPair<TFirst, TSecond>
                    where TFirst : class, global::System.IDisposable, new()
                {
                }

                public partial class Host
                {
                    [SingletonDIConsume(typeof(App.Service))]
                    public partial class Nested
                    {
                    }

                    public partial class Middle<TItem>
                    {
                        [SingletonDIConsume(typeof(App.Service))]
                        public partial class Deep
                        {
                        }
                    }
                }

                public partial record RecordHost
                {
                    [SingletonDIConsume(typeof(App.Service))]
                    public partial class NestedInRecord
                    {
                    }
                }
            }

            [SingletonDIConsume(typeof(App.Service))]
            public partial class GlobalConsumer
            {
            }
            """;

        // Ожидание написано один раз здесь и сверяется с напечатанным, а не с прошлым выводом:
        // строка сопоставляет объявление консьюмера из исходника с его же сгенерированным
        // свойством, поэтому перестановка двух зависимостей падает с именем потребителя, а не
        // проходит под предлогом «свойство по-прежнему из разрешённого набора».
        CoexistenceCase[] coexistenceCases =
        [
            new("Unsealed",
                "public partial class Unsealed",
                "partial class Unsealed",
                "protected static global::App.Service ServiceInstance"),
            new("Sealed",
                "public sealed partial class Sealed",
                "partial class Sealed",
                "private static global::App.Service ServiceInstance"),
            new("Internal",
                "internal partial class Internal",
                "partial class Internal",
                "protected static global::App.Service ServiceInstance"),
            new("ByContract",
                "public partial class ByContract",
                "partial class ByContract",
                "protected static global::App.IContract IContractInstance"),
            new("ByRecord",
                "public partial class ByRecord",
                "partial class ByRecord",
                "protected static global::App.RecordService RecordServiceInstance"),
            new("ByNestedProvider",
                "public partial class ByNestedProvider",
                "partial class ByNestedProvider",
                "protected static global::App.ProviderHost.NestedService NestedServiceInstance"),
            new("AsStruct",
                "public partial struct AsStruct",
                "partial struct AsStruct",
                "private static global::App.Service ServiceInstance"),
            new("AsReadonlyStruct",
                "public readonly partial struct AsReadonlyStruct",
                "partial struct AsReadonlyStruct",
                "private static global::App.Service ServiceInstance"),
            new("AsRefStruct",
                "public ref partial struct AsRefStruct",
                "partial struct AsRefStruct",
                "private static global::App.Service ServiceInstance"),
            new("AsRecord",
                "public partial record AsRecord;",
                "partial record class AsRecord",
                "protected static global::App.Service ServiceInstance"),
            new("AsRecordClass",
                "public partial record class AsRecordClass;",
                "partial record class AsRecordClass",
                "protected static global::App.Service ServiceInstance"),
            new("AsSealedRecord",
                "public sealed partial record AsSealedRecord;",
                "partial record class AsSealedRecord",
                "private static global::App.Service ServiceInstance"),
            new("AsRecordStruct",
                "public partial record struct AsRecordStruct",
                "partial record struct AsRecordStruct",
                "private static global::App.Service ServiceInstance"),
            new("AsAbstract",
                "public abstract partial class AsAbstract",
                "partial class AsAbstract",
                "protected static global::App.Service ServiceInstance"),
            new("Generic",
                "public partial class Generic<T> where T : class",
                "partial class Generic<T>",
                "protected static global::App.Service ServiceInstance"),
            new("GenericPair",
                "public partial class GenericPair<TFirst, TSecond>",
                "partial class GenericPair<TFirst, TSecond>",
                "protected static global::App.Service ServiceInstance"),
            new("Nested",
                "public partial class Nested",
                "partial class Nested",
                "protected static global::App.Service ServiceInstance"),
            new("Deep",
                "public partial class Deep",
                "partial class Deep",
                "protected static global::App.Service ServiceInstance"),
            new("NestedInRecord",
                "public partial class NestedInRecord",
                "partial class NestedInRecord",
                "protected static global::App.Service ServiceInstance"),
            new("GlobalConsumer",
                "public partial class GlobalConsumer",
                "partial class GlobalConsumer",
                "protected static global::App.Service ServiceInstance"),
        ];

        var result = DeclarationMatrixHarness.Run(source, new MatrixRunOptions());

        Assert.True(
            result.GeneratorErrorIds.IsEmpty,
            string.Join(", ", result.GeneratorErrorIds));
        Assert.True(
            result.CompilerErrors.IsEmpty,
            string.Join(
                Environment.NewLine,
                result.CompilerErrors.Select(error => error.ToString())));
        Assert.True(
            result.EmitSucceeded,
            $"The output compilation failed to emit. Generated:{Environment.NewLine}" +
            $"{result.GeneratedSource}");

        var lines = TrimLines(result.GeneratedSource);
        var sourceLines = source.Split('\n').Select(line => line.Trim()).ToArray();

        // Прибор склеивает все сгенерированные файлы в одну строку, а разделяет их только маркер
        // `// <auto-generated/>`, который печатает эмиттер потребителей. Модуль провайдеров живёт в
        // `SingletonDI.Generated` и такого маркера не печатает, поэтому отсечение по пространству
        // имён отделяет файлы потребителей от модуля и не зависит от порядка выдачи файлов.
        var consumerFiles = result.GeneratedSource
            .Split("// <auto-generated/>")
            .Where(file => !file.Contains("namespace SingletonDI.Generated", StringComparison.Ordinal))
            .Select(TrimLines)
            .ToImmutableArray();
        Assert.Equal(coexistenceCases.Length, consumerFiles.Length);

        // Собственное объявление консьюмера — последнее парциальное объявление его файла:
        // содержащие типы эмиттер печатает от внешнего к внутреннему, и последним идёт консьюмер.
        var resolvedProperties = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var consumerFile in consumerFiles)
        {
            var consumerDeclarations = consumerFile
                .Where(line => line.StartsWith("partial ", StringComparison.Ordinal))
                .ToArray();
            Assert.True(
                consumerDeclarations.Length > 0,
                $"A generated consumer file declares nothing. Generated:{Environment.NewLine}" +
                $"{string.Join(Environment.NewLine, consumerFile)}");
            var properties = consumerFile.Where(IsPropertyDeclaration).ToArray();
            Assert.True(
                resolvedProperties.TryAdd(consumerDeclarations[^1], properties),
                $"Two consumer files declare the same consumer [{consumerDeclarations[^1]}].");
        }

        // Сопоставление объявления и свойства: единственное место, где потребитель связан со
        // своей зависимостью. Свойств ровно одно на потребителя, поэтому и появление второго, и
        // отсутствие первого, и подмена типа падают здесь — с именем потребителя в сообщении.
        foreach (var coexistenceCase in coexistenceCases)
        {
            Assert.Contains(coexistenceCase.SourceDeclaration, sourceLines);
            Assert.True(
                resolvedProperties.TryGetValue(coexistenceCase.GeneratedDeclaration, out var actual),
                $"{coexistenceCase.Name}: the generated source has no consumer file declaring " +
                $"[{coexistenceCase.GeneratedDeclaration}]. Generated:{Environment.NewLine}" +
                result.GeneratedSource);
            Assert.True(
                actual.Length == 1 && actual[0] == coexistenceCase.ExpectedProperty,
                $"{coexistenceCase.Name}: the property must be " +
                $"[{coexistenceCase.ExpectedProperty}], actual [{string.Join(" | ", actual)}].");
        }

        Assert.Equal(coexistenceCases.Length, resolvedProperties.Count);

        // Собственных потребителей в исходнике ровно столько же, сколько записей в
        // `coexistenceCases`, у каждого ровно одно сгенерированное свойство, своя документация
        // и своё разрешение. Счётчики смотрят только на файлы потребителей, поэтому
        // документация модуля провайдеров в них не смешивается.
        var consumerDocComments = consumerFiles.Sum(file => file.Count(line => line == "/// <summary>"));
        Assert.Equal(coexistenceCases.Length, consumerDocComments);
        var consumerResolutions = consumerFiles.Sum(file => file.Count(line => line.Contains("__SingletonDIHost__.Resolve<", StringComparison.Ordinal)));
        Assert.Equal(coexistenceCases.Length, consumerResolutions);

        // Строк объявления на четыре больше, чем консьюмеров: четыре содержащих типа — `Host`
        // и `RecordHost` по одному разу за консьюмер, который в них вложен, и `Middle<TItem>`
        // один раз. `Host` печатается дважды — по разу в файле вложенного консьюмера `Nested` и в
        // файле вложенного консьюмера `Deep`. Содержащий тип эмиттер печатает тем же кодом, что и
        // сам консьюмер, поэтому здесь же закреплена и форма содержащей записи.
        var declarations = lines
            .Where(line => line.StartsWith("partial ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(coexistenceCases.Length + 4, declarations.Length);
        Assert.Equal(2, declarations.Count(line => line == "partial class Host"));
        Assert.Contains("partial class Middle<TItem>", declarations);
        Assert.Contains("partial record class RecordHost", declarations);

        // Предложения ограничений эмиттер печатает отдельными строками, без отступа после обрезки.
        Assert.Contains("where T : class", lines);
        Assert.Contains("where TFirst : class,global::System.IDisposable,new()", lines);

        // Четыре провайдера в одной сборке: контракт, вложенный, запись и инициализатор.
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.IContract, global::App.InternalService>(",
            lines);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.ProviderHost.NestedService, global::App.ProviderHost.NestedService>(",
            lines);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.RecordService, global::App.RecordService>(",
            lines);
        Assert.Contains(
            "global::SingletonDI.Generated.__SingletonDIHost__.RegisterProvider<global::App.Service, global::App.Service>(",
            lines);
        Assert.Contains("() => new global::App.InternalService(),", lines);
        Assert.Contains("() => new global::App.ProviderHost.NestedService(),", lines);
        Assert.Contains("() => new global::App.RecordService(),", lines);
        Assert.Contains("() => new global::App.Service(),", lines);
        Assert.Contains("static value => ToTask(value.InitializeAsync()),", lines);
    }

    private static string[] TrimLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r').Trim()).ToArray();

    private static bool IsPropertyDeclaration(string line) =>
        line.StartsWith("protected static global::", StringComparison.Ordinal) ||
        line.StartsWith("private static global::", StringComparison.Ordinal);
}
