# AGENTS.md — Ask Mode

This file provides guidance to agents when working with code in this repository.

## Ask Mode — SingletonDI Generator

### Project documentation

- Main plan: `Plan.md` — contains full implementation details
- Solution structure:
  - `src/SingletonDI.Attributes/` — [Provide], [Consume] attributes and IInitializeSync/IInitializeAsync interfaces
  - `src/SingletonDI.Generator/` — Incremental Source Generator
  - `src/SingletonDI.SampleApp/` — Demo application
  - `tests/SingletonDI.Tests/` — Unit tests

### Key concepts

- `[Provide]` — marks a singleton class (must have parameterless constructor)
- `[Consume]` — marks a consumer class that receives singleton properties (must be partial)
- `IInitializeSync` / `IInitializeAsync` — optional interfaces for initialization
- Container is auto-initialized via `[ModuleInitializer]` attribute

### Limitations

- Works only within single assembly (no cross-assembly [Provide])
- Requires .NET 5+ for ModuleInitializer support
- record struct not allowed for [Provide], record class allowed
