# AGENTS.md — Code Mode

This file provides guidance to agents when working with code in this repository.

## Code Mode — SingletonDI Generator

### Required patterns for Source Generator

- Use `IIncrementalGenerator` interface, not `ISourceGenerator` for better performance
- All models must be `readonly record struct` or `readonly record class` with `IEquatable<T>`
- Use `ImmutableArray<T>` for collections in pipeline stages
- Register attributes via `ForAttributeWithMetadataName()` with full assembly-qualified name

### Code generation rules

- ExceptionHelper must be generated separately with `[DoesNotReturn]` attributes
- Container class goes to `SingletonDI.Generated.Internal` namespace
- Consumer properties use lazy initialization: `=> Container.Prop ?? throw ExceptionHelper.ThrowContainerNotInitialized()`
- Namespace conflict resolution: `Namespace_TypeName` (underscore separator)

### Validation requirements

- Provider must have parameterless public constructor
- Consumer must be `partial`
- No circular dependencies between providers (validate at compile time)
- Check for both `Initialize()` and `InitializeAsync()` methods via interface implementation
