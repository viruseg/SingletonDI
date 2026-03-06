# AGENTS.md

This file provides guidance to agents when working with code in this repository.

План проекта [Plan.md](Plan.md)

## Проект: SingletonDI

Incremental Source Generator для упрощённого DI-менеджера (только синглтоны).

## Структура решения

```
SingletonDI/
├── src/
│   ├── SingletonDI.Attributes/    # Атрибуты [Provide], [Consume] + интерфейсы IInitializeSync, IInitializeAsync
│   ├── SingletonDI.Generator/    # Incremental Source Generator
│   └── SingletonDI.SampleApp/    # Тестовое приложение
└── tests/
    └── SingletonDI.Tests/         # Unit-тесты через Microsoft.CodeAnalysis.Testing
```

## Команды

- **Build:** `dotnet build` (из корня solution)
- **Build Generator:** `dotnet build src/SingletonDI.Generator`
- **Run Sample:** `dotnet run --project src/SingletonDI.SampleApp`
- **Test:** `dotnet test` (или `dotnet test tests/SingletonDI.Tests` для конкретного проекта)
- **Run single test:** `dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"`

## Важные особенности

- Source Generator работает на этапе компиляции, не runtime
- Тесты используют `Microsoft.CodeAnalysis.Testing` для верификации диагностики
- Generator должен быть зарегистрирован в .csproj через `<GeneratorPackage>Microsoft.CodeAnalysis.CSharp.SourceGenerators</GeneratorPackage>`
- Incremental Source Generators (IIncrementalGenerator) предпочтительнее старых ISourceGenerator для производительности

## Кодогенерация

- Генерируется Container в `SingletonDI.Generated.Internal` namespace
- Потребители получают partial class с property типами синглтонов
- ExceptionHelper — генерируемый класс с [DoesNotReturn] методами

## Тестирование диагностики

Использовать `Verifier.VerifyAnalyzer()` и `Verifier.VerifyCodeFix()` из Microsoft.CodeAnalysis.Testing.
