# Copilot Instructions for the Poly Workspace

These instructions help AI coding agents work productively in this .NET solution. Focus on concrete project patterns and commands established in this repo.

## Overview & Architecture
- **Goal:** Define a common shared abstraction layer and interface into varying type systems for supporting dynamic code generation and execution in other components of this project. Fluent, strongly-typed domain modeling for validation, serialization, and future codegen.
- **Primary library:** `Poly/` — core DSL and engines for Data Modeling, Interpretation, Introspection, Text, and Validation.
- **Examples/benchmarks:** `Poly.Benchmarks/` — runnable sample scenarios (e.g., `FluentBuilderExample.cs`).
- **Tests:** `Poly.Tests/` — unit tests for core components.
- **Key builders & flow:**
  - `DataModelBuilder` → collects types and relationships.
  - `DataTypeBuilder` → defines a type, properties, and rules.
  - `PropertyBuilder` → sets property types and constraints.
  - `RelationshipBuilder` → creates `HasOne/HasMany` and `WithOne/WithMany` relationships.
- **Serialization:** Portable JSON for models; see `DataModeling/DataModelPropertyPolymorphicJsonTypeResolver.cs` for custom polymorphic handling.
 - **Module boundaries (critical):** One-way dependencies are enforced.
   - `Interpretation` depends on `Introspection` interfaces.
   - `Validation` depends on `Interpretation` (rules and evaluation consume the DSL).
   - `Introspection` must not depend on `Interpretation`.
   - The only exception is CLR-specific implementations under [Poly/Introspection/CommonLanguageRuntime](Poly/Introspection/CommonLanguageRuntime), which provide concrete types but do not introduce reverse dependencies.

## Build & Run
- **Workspace tasks (preferred):** Use VS Code tasks defined by the workspace.
  - Build: Task `build` runs `dotnet build` on [Poly.Benchmarks/Poly.Benchmarks.csproj](Poly.Benchmarks/Poly.Benchmarks.csproj).
  - Publish: Task `publish` runs `dotnet publish` for benchmarks.
  - Watch+Run: Task `watch` runs `dotnet watch run --project` on benchmarks.
- **CLI examples:**
  - Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
  - Run sample: `dotnet run --project Poly.Benchmarks/Poly.Benchmarks.csproj`

## Conventions & Patterns
- **Fluent builders:** Prefer builder methods for types/properties/relationships rather than constructing DTOs directly.
  - Example in [Poly.Benchmarks/FluentBuilderExample.cs](Poly.Benchmarks/FluentBuilderExample.cs).
- **Constraints:** Property constraints live under [Poly/Validation/Constraints](Poly/Validation/Constraints). Add via `WithConstraint()` or `WithConstraints()`.
- **Rules:** Type-level rules in [Poly/Validation/Rules](Poly/Validation/Rules). Attach via `DataTypeBuilder.AddRule()`.
- **Relationships:** Define with `HasOne`/`HasMany` then refine with `WithOne`/`WithMany` and source/target constraints.
- **Interpretation/Operators:** The DSL includes operators under [Poly/Interpretation/Operators](Poly/Interpretation/Operators) (e.g., invocation). Extend by following existing operator structure.
- **Introspection:** Interfaces for type definitions are under [Poly/Introspection](Poly/Introspection) to abstract the modeling layer.
- **Extensions:** Shared helpers under [Poly/Extensions](Poly/Extensions) (e.g., enumerable/dictionary utilities).
 - **Dependency discipline:** When adding new APIs:
   - Place shared abstractions in `Introspection`.
   - Keep evaluators and operator logic in `Interpretation`, consuming `Introspection` only.
   - Implement constraints and rules in `Validation`, consuming `Interpretation`.
   - Avoid circular or cross-layer references; validate new files adhere to the one-way boundary above.

## Testing
- Tests live in [Poly.Tests](Poly.Tests). Use standard `dotnet test` or run the test project directly:

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

- Add tests alongside feature changes; mirror the builder-first style.

## Serialization & JSON
- Models serialize to JSON with clear `Types`, `Properties`, and `Relationships` sections.
- When adding new constraint or rule types, ensure JSON shape stays consistent and update resolvers if polymorphism is involved.

## Adding New Modeling Features
- **New constraint:** Implement in [Poly/Validation/Constraints](Poly/Validation/Constraints) and integrate via `PropertyBuilder`.
- **New rule:** Implement in [Poly/Validation/Rules](Poly/Validation/Rules) and expose through `DataTypeBuilder.AddRule()`.
- **New operator:** Add under [Poly/Interpretation/Operators](Poly/Interpretation/Operators), ensuring compatibility with `InterpretationContext`.
- **New relationship types:** Extend `Relationship` in [Poly/DataModeling/Relationship.cs](Poly/DataModeling/Relationship.cs) and builder methods in `DataTypeBuilder`.

## Coding Style
- Keep changes minimal and aligned to existing fluent APIs.
- Avoid inline comments unless explicitly required; follow method naming and builder chaining patterns present in the repo.
- **Region blocks:** Do not use C# `#region`/`#endregion` directives. They are code noise and add no value; rely on clear class structure and method organization instead.

## Useful References
- High-level intro and examples: [README.md](README.md)
- Core modeling: [Poly/DataModeling](Poly/DataModeling)
- Validation: [Poly/Validation](Poly/Validation)
- Interpretation DSL: [Poly/Interpretation](Poly/Interpretation)
- Benchmarks/samples: [Poly.Benchmarks](Poly.Benchmarks)
- Tests: [Poly.Tests](Poly.Tests)

## Agent Tips
- Prefer enhancing builder flows over raw object instantiation.
- Reuse constraint and rule primitives; do not duplicate logic.
- Validate by running benchmarks or tests after changes using workspace tasks.
 - Favor extending existing subsystems over bespoke implementations:
   - For evaluation semantics, add operators/concepts under `Interpretation/Operators` using `InterpretationContext` rather than simulating evaluation elsewhere.
   - For validation, add constraints/rules under `Validation` consuming `Interpretation` outputs; avoid standalone validators outside the rule/constraint framework.
   - For type abstractions, place interfaces in `Introspection` and avoid reverse dependencies.

If any sections are unclear or incomplete, please comment with specific paths or scenarios to refine these instructions.