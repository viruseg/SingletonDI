# AGENTS.md — Architect Mode

This file provides guidance to agents when working with code in this repository.

## Architect Mode — SingletonDI Generator

### Architecture decisions (confirmed with user)

- **Consumer initialization**: Option A — lazy property through Container (`Prop => Container.Prop ?? throw`)
- **Async initialization**: Uses lock + GetAwaiter().GetResult() in ModuleInitializer (not Task.Run())
- **IDisposable**: If provider implements IDisposable, Container generates Dispose() method
- **Inheritance**: [Provide] is NOT inherited, [Consume] IS inherited (Inherited = true)
- **Type conflicts**: Namespace_TypeName format with underscore separator

### Non-obvious constraints

- [Provide] allowed on: class, record class
- [Provide] forbidden on: struct, record struct, abstract class, interface
- Consumer MUST be partial — generator cannot add members otherwise
- Circular dependencies detected at compile time via topological sort
- record class allowed for [Provide], but record struct is error

### Performance considerations

- Use IIncrementalGenerator (not ISourceGenerator) for incremental compilation
- Cache all models as ImmutableArray<T> for pipeline optimization
- Minimize allocations in validation stages
- Each pipeline branch should be independent to maximize caching

### Exception handling

- All exceptions thrown via generated ExceptionHelper class
- ExceptionHelper methods marked with [DoesNotReturn] for compiler optimization
- Container initialization is fail-fast: stop on first initialization exception
