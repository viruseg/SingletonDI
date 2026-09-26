using Xunit;

namespace SingletonDI.Tests.Coverage;

public sealed class DeclarationMatrixAnchorTests
{
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
            }

            [SingletonDIConsume(typeof(App.Service))]
            public partial class GlobalConsumer
            {
            }
            """;

        var result = DeclarationMatrixHarness.Run(source, new MatrixRunOptions());

        Assert.True(
            result.GeneratorErrorIds.IsEmpty,
            string.Join(", ", result.GeneratorErrorIds));
        Assert.True(
            result.CompilerErrors.IsEmpty,
            string.Join(
                Environment.NewLine,
                result.CompilerErrors.Select(error => error.ToString())));
        Assert.True(result.EmitSucceeded);

        var lines = result.GeneratedSource
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .ToArray();

        // Девятнадцать консьюмеров в исходнике, у каждого ровно одно сгенерированное свойство, и
        // ни одного лишнего: `/// <summary>` печатается один раз на свойство и ещё дважды — в
        // модуле провайдеров, который выдаётся в ту же сборку.
        Assert.Equal(21, lines.Count(line => line == "/// <summary>"));
        Assert.Equal(19, lines.Count(line => line.Contains("__SingletonDIHost__.Resolve<", StringComparison.Ordinal)));

        // Двадцать две строки объявления: девятнадцать консьюмеров плюс два содержащих типа
        // `Host` и `Host.Middle<TItem>`. `Host` печатается дважды — по разу в файле вложенного
        // консьюмера `Nested` и в файле вложенного консьюмера `Deep`.
        var declarations = lines
            .Where(line => line.StartsWith("partial ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(22, declarations.Length);
        Assert.Equal(2, declarations.Count(line => line == "partial class Host"));
        Assert.Contains("partial class Unsealed", declarations);
        Assert.Contains("partial class Sealed", declarations);
        Assert.Contains("partial class Internal", declarations);
        Assert.Contains("partial class ByContract", declarations);
        Assert.Contains("partial class ByRecord", declarations);
        Assert.Contains("partial class ByNestedProvider", declarations);
        Assert.Contains("partial class AsAbstract", declarations);
        Assert.Contains("partial class Generic<T>", declarations);
        Assert.Contains("partial class GenericPair<TFirst, TSecond>", declarations);
        Assert.Contains("partial class Nested", declarations);
        Assert.Contains("partial class Middle<TItem>", declarations);
        Assert.Contains("partial class Deep", declarations);
        Assert.Contains("partial class GlobalConsumer", declarations);
        Assert.Contains("partial struct AsStruct", declarations);
        Assert.Contains("partial struct AsReadonlyStruct", declarations);
        Assert.Contains("partial struct AsRefStruct", declarations);
        Assert.Contains("partial record class AsRecord", declarations);
        Assert.Contains("partial record class AsRecordClass", declarations);
        Assert.Contains("partial record class AsSealedRecord", declarations);
        Assert.Contains("partial record struct AsRecordStruct", declarations);

        // Предложения ограничений эмиттер печатает отдельными строками, без отступа после обрезки.
        Assert.Contains("where T : class", lines);
        Assert.Contains("where TFirst : class,global::System.IDisposable,new()", lines);

        // Каждое из девятнадцати свойств обязано быть одним из этих пяти: доступность, имя и тип
        // выводятся из формы потребителя и формы зависимости, и любая подмена означала бы, что
        // эмиттер перепутал объявления, когда их много в одной сборке. Скобки отсеивают два
        // вспомогательных метода `ToTask` модуля провайдеров — они тоже начинаются с
        // `private static global::`, но свойствами не являются.
        string[] expectedProperties =
        [
            "protected static global::App.Service ServiceInstance",
            "private static global::App.Service ServiceInstance",
            "protected static global::App.IContract IContractInstance",
            "protected static global::App.RecordService RecordServiceInstance",
            "protected static global::App.ProviderHost.NestedService NestedServiceInstance",
        ];
        var properties = lines
            .Where(line =>
                !line.Contains('(') &&
                (line.StartsWith("protected static global::", StringComparison.Ordinal) ||
                 line.StartsWith("private static global::", StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(19, properties.Length);
        Assert.All(properties, property => Assert.Contains(property, expectedProperties));

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
        Assert.Contains("static value => ToTask(value.InitializeAsync()),", lines);
    }
}
