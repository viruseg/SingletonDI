# Hardening SingletonDI: исправление подтверждённых проблем

**Дата:** 2026-09-24
**Статус:** дизайн утверждён
**Область:** runtime, incremental source generator, refactoring, NuGet packaging, тестовая инфраструктура и документация

## Контекст

Ревью текущего `main` выявило подтверждённые дефекты, которые не ловятся существующими 162 тестами на каждом из `net8.0`, `net9.0`, `net10.0`:

- упакованный analyzer требует Roslyn 5.0 и выдаёт `CS9057` на SDK 8/9;
- composition root может запускать пользовательский static constructor внешнего provider-а;
- поддерживаемые атрибутом формы consumer-а не генерируются корректно;
- private nested provider/service types приводят к ошибкам generated C#;
- заявленное наследование consumer-а не работает;
- generic `InitializeAsync<T>` и open generic contracts не диагностируются;
- referenced consumers не проверяются в executable без composition root;
- signal handlers подавляют default termination, но сами не завершают процесс;
- synchronous exception initializer-а запускает disposal при незавершённых initializer-ах;
- consumer pipeline и metadata scanning имеют лишнюю инвалидацию и повторные обходы;
- code-fix tests не исполняют production providers, refactoring provider не покрыт;
- solution pack создаёт посторонние пакеты, а analyzer paths жёстко привязаны к `bin`.

Цель — исправить каждую подтверждённую проблему отдельным зелёным коммитом, не скрывая дефекты ослаблением тестов или удалением публичных возможностей.

## Цели

- Сохранять текущие публичные атрибуты, namespace и сигнатуры `SingletonDIInitializer`.
- Поддерживать class, struct, record, nested, generic и file consumer-формы, которые допускает текущий атрибут.
- Сохранять documented inherited consumer behavior без повторного атрибута на derived-типе.
- Передавать provider-registration только через generated module bootstrap, не запуская пользовательские `.cctor`.
- Диагностировать все неподдерживаемые или конфликтующие declarations до генерации некомпилируемого source.
- Сделать incremental pipeline чувствительным только к attribute/model/reference changes, а не к телу любого метода.
- Один раз обходить metadata references и использовать O(V+E) cycle detection.
- Выпускать только `SingletonDI` NuGet package версии `1.1.0` с analyzer, refactoring и runtime assets из фактических build outputs.
- Проверять production code fixes, refactorings, diagnostics, generated output, package consumption и inter-project lifecycle реальными, а не копирующими harness-ами.

## Не входит в задачу

- Несколько независимых process-wide composition roots.
- Reflection-based service discovery.
- Поддержка C# 7.3. Generated module initializer требует C# 9; более старый `LangVersion` получает явную diagnostic до генерации.
- File-scoped consumer declarations требуют C# 10; при C# 9 такой consumer получает отдельную feature diagnostic.
- Изменение стратегии names, кроме исправления нестабильных hint names и member collisions.
- Публикация NuGet-пакета и изменение git remote.

## Архитектурные решения

### 1. Consumer declaration snapshot

`ConsumerValidator` больше не передаёт raw `TypeDeclarationSyntax` в финальный output. Transform pipeline создаёт immutable `ConsumerCandidate` со следующими данными:

- `TypeKind` (`class`, `struct`, `record`, `record struct`);
- containing type chain с names, generic arity и escaped identifiers;
- namespace и file-scoped flag;
- partial-modifier result;
- dependency identities, short names, contract flags и property names;
- source locations для diagnostics;
- self-reference и duplicate-dependency results.

`ConsumerModel` хранит declaration snapshot и typed dependencies. `ConsumerEmitter` реконструирует partial declaration через `SyntaxFactory`/`SyntaxGenerator` с правильным keyword, type parameter list, containing types и `file` qualifier. Generated property accessibility выбирается по declaration:

- `private` для struct, record struct и sealed class;
- `protected` для unsealed class, чтобы derived-класс мог использовать унаследованный consumer без повторного атрибута.

Если containing type не является partial, nested consumer получает отдельную diagnostic: partial declaration нельзя открыть без изменения containing type. Generic, nested, struct, record и file consumers получают compile-and-run tests; file-scoped source требует C# 10. Keyword-escaped identifiers сохраняются при генерации.

Hint name строится из sanitized fully qualified identity и стабильного hash. В hint name не попадают `<`, `>`, `,`, assembly separators и другие недопустимые символы. Два разных consumer-а не могут перезаписать generated source друг друга.

### 2. Provider validation и generated bootstrap

Provider validator проверяет не только assignability, но и доступность implementation/service types из предполагаемого generated namespace. Private, file-local и другие недоступные nested types получают diagnostic вместо `CS0122`.

Каждый provider assembly получает generated public hidden type:

```csharp
namespace SingletonDI.Generated
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class __SingletonDIProviderModule__
    {
        public static void Bootstrap();
    }
}
```

`Bootstrap()` регистрирует providers через thread-safe idempotent guard. Generated module initializer вызывает этот метод. Composition root вызывает `Bootstrap()` внешней provider-сборки напрямую и никогда не использует `RuntimeHelpers.RunClassConstructor` для пользовательского provider type. Старые provider assemblies без bootstrap method получают diagnostic о несовместимом module marker.

`InitializeAsync` candidates должны иметь `Arity == 0`, поддерживать только `Task`/`ValueTask` и иметь instance accessibility, доступную generated module. Generic, static, private, protected и inaccessible методы получают diagnostic. Open generic `typeof(IBox<>)` отклоняется до создания `ServiceTypeIdentity`.

После разрешения final property name проверяются members исходного consumer symbol. При collision generated property не добавляется, а выдаётся diagnostic с точным location. Проверка учитывает nested/generic containing types и унаследованные members.

### 3. Cross-assembly validation

Referenced providers и referenced consumer dependencies собираются одним metadata snapshot. Snapshot использует reference-only compilation projection, сравнивает только assembly/reference/options fingerprint и не меняется при редактировании source body.

Composition root использует snapshot для provider graph, consumer validation, marker checks, missing dependencies, conflicts и cycles. Executable без composition root также проверяет referenced consumer dependency sets:

- если provider найден, consumer library компилируется без лишней root diagnostics;
- если provider отсутствует, executable получает missing-provider diagnostic;
- если dependency может быть предоставлена только внешней assembly, executable получает missing-composition-root diagnostic;
- library projects не требуют root и не получают executable-only diagnostics.

Пустой local consumer list больше не отключает referenced-consumer validation.

### 4. Incremental performance

`ConsumerCandidate` не содержит syntax node, `Compilation` или semantic model. Diagnostics и models вычисляются внутри attribute transform. Final consumer output зависит от candidates, provider candidates и generator options, но не от `CompilationProvider`.

Metadata providers и referenced consumers сканируются одним проходом по reference-only snapshot. Повторный полный обход BCL/reference graph выполняется только при изменении references или compilation options.

Production cycle detection использует Kahn algorithm с adjacency/in-degree maps и residual-node analysis за O(V+E). Generated levels остаются только для callers, которым они действительно нужны; production path не строит неиспользуемый список уровней.

### 5. Runtime lifecycle

`ServiceGraph.InitializeAsync` сначала запускает все initializer delegates текущего уровня. Synchronous exception оборачивается в faulted task; после завершения `Task.WhenAll` запускается cleanup. До завершения всех уже запущенных initializers disposal не начинается. Original exception сохраняется, cleanup errors не заменяют исходную ошибку.

`ShutdownManager` получает internal termination delegate для тестируемости. `Ctrl+C`/SIGINT, SIGTERM и SIGQUIT:

1. отменяют default signal termination;
2. запускают единственную async disposal operation;
3. после завершения вызывают process termination с кодами 130, 143 и 131 соответственно.

Повторный signal не запускает вторую disposal operation. `ProcessExit` сохраняет best-effort semantics; документация явно говорит, что гарантированный async cleanup требует await `SingletonDIInitializer.DisposeAsync()`.

### 6. Language и package compatibility

Generated output требует C# 9 или новее. Generator проверяет effective `LanguageVersion` и выдаёт diagnostic до `AddSource`, если версия ниже. Runtime остаётся `netstandard2.0;net8.0`.

Generator и refactoring projects собираются против минимальной Roslyn 4.8 API, чтобы packed analyzer работал на SDK 8, 9 и 10. Private Roslyn dependencies не попадают в runtime dependency group. На `net8.0` compatibility packages не добавляются без необходимости.

В packable solution остаётся только `SingletonDI.Attributes`. Analyzer/refactoring assemblies добавляются через `TargetsForTfmSpecificBuildOutput`/`TargetPath`, а не через hardcoded `bin` paths. `GeneratePackageOnBuild` выключается. Версия, assembly version и release notes обновляются до `1.1.0`; создаётся symbols package при включённом Source Link.

### 7. Test infrastructure

CodeFix tests используют настоящий `CodeFixContext`, `RegisterCodeFixesAsync`, зарегистрированный `CodeAction`, применение operations и компиляцию исправленного document. Проверяются action title/equivalence key, отсутствие diagnostic, Fix-All и compile result. Test-local копии transformation logic удаляются.

Refactoring tests используют `CodeRefactoringContext` и применяют реальный refactoring action. ValueTask, существующий Task, overloads, cancellation и missing using покрываются отдельно.

Diagnostic assertions проверяют exact ID, severity, category, enabled-by-default, message, arguments, location и count. Output compilation добавляется в harness и проверяется на compiler diagnostics. Generated output проверяется semantic model, compilation и execution behavior, а не только substring.

Incremental tests используют native `IIncrementalGenerator` с включённым output tracking и матрицей no-op, body edit, provider add/remove/change, option change, referenced assembly replacement и cancellation. Проверяется именно нужный tracked stage, а не любой cached upstream step.

Package smoke собирает только freshly packed `SingletonDI.1.1.0.nupkg` из isolated feed на SDK 8, 9 и 10, затем компилирует и запускает provider/consumer fixture. Inter-project runtime scenario выполняется в отдельном process/load context, чтобы static registry и memoized scenario не загрязняли следующие тесты.

## Атомарные коммиты

Каждый пункт ниже получает отдельный commit. В commit входят regression tests, необходимые для именно этого пункта. Документационный и release commits не смешиваются с production code.

1. `test: execute production code fixes` — заменить fake harness на real `CodeAction` pipeline.
2. `test: cover production refactorings` — добавить real `CodeRefactoringContext` tests без изменения production transformation logic.
3. `fix: handle refactoring task types and usings` — исправить `ValueTask` detection и генерацию unresolved `Task` без `using`.
4. `test: assert diagnostics and generated output` — exact diagnostics, DM0008, output compilation/semantic assertions.
5. `test: cover native incremental invalidation` — native generator tracking matrix.
6. `test: isolate process-wide runtime scenarios` — убрать memoization/state leakage inter-project tests.
7. `test: verify packed package consumption` — isolated NuGet feed и SDK 8/9/10 smoke fixture.
8. `build: isolate the packable runtime package` — убрать build-time pack и закрыть не-publishable projects.
9. `build: pack analyzers from target paths` — TargetPath-based analyzer/refactoring assets.
10. `build: support Roslyn 4.8 consumers` — downgrade API baseline, SDK 8/9/10 smoke, clean dependency groups.
11. `release: prepare SingletonDI 1.1.0` — version, release notes, symbols, package metadata.
12. `fix: await started initializers before cleanup` — runtime graph race.
13. `fix: complete graceful signal shutdown` — ShutdownManager termination semantics и lifecycle tests.
14. `fix: bootstrap provider modules explicitly` — generated Bootstrap API и запрет user cctor.
15. `fix: model complete consumer declarations` — snapshot/emitter для struct, record, nested, generic, file и escaped names.
16. `fix: inherit consumer dependencies` — protected generated properties и derived test без attribute.
17. `fix: validate provider accessibility` — nested/private/file service diagnostics.
18. `fix: reject unsupported initializer and contracts` — generic initializer, open generic, member collision.
19. `fix: validate referenced consumers without root` — executable metadata validation.
20. `fix: diagnose unsupported generated language` — C# 9 minimum diagnostic.
21. `perf: make consumer generation incremental` — убрать raw syntax/Compilation dependency.
22. `perf: scan referenced metadata once` — one snapshot/pass и reference-only comparer.
23. `perf: use linear cycle detection` — O(V+E) production sorter.
24. `docs: synchronize project guidance` — README/AGENTS/API/shutdown/language requirements.

Если реализация выявляет независимую причину в пределах одного пункта, она исправляется в том же commit только если без неё пункт нельзя сделать зелёным. Несвязанный cleanup запрещён.

## Критерии готовности

- Каждый commit проходит целевые regression tests и не содержит tracked changes вне своей области.
- Полный `dotnet build SingletonDI.slnx -c Release` проходит без warnings/errors.
- `dotnet test` проходит на `net8.0`, `net9.0`, `net10.0`.
- Packed package smoke проходит на SDK 8, 9 и 10.
- Sample app запускается и корректно освобождает providers.
- `git status` чист после каждого commit.
- В конце `git log` содержит отдельный commit для каждого пункта, без amend и force-push.

## Спецификация совместимости

- `SingletonDIProvideAttribute`, `SingletonDIConsumeAttribute`, `SingletonDIProviderModuleAttribute` и `SingletonDIInitializer` сохраняют существующие public signatures.
- Существующие single-project и cross-project concrete/contract scenarios продолжают работать.
- Generated property naming для неконфликтных top-level consumers не меняется.
- Новые diagnostics не заменяют compiler errors для cases, которые можно классифицировать на source-generation stage.
- Старые provider packages без generated `Bootstrap()` диагностируются, а не приводят к случайному запуску user `.cctor`.
