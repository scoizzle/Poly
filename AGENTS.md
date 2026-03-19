# Agent Instructions for the Poly Workspace

Keep only requirements that measurably improve customer time-to-value, correctness, or operability; remove the rest. Engineer end-to-end system behavior with clear ownership boundaries, not isolated parts. Optimize for shipped capability by delivering the smallest coherent platform that proves the business model, not framework completeness. Build working code before abstraction: pattern catalogs (GoF, POSA, PoEAA, DDD) describe recurring outcomes observed in implementations, so extracting abstractions after implementation is required, while designing them first is speculation that burns irrecoverable time. Operational guardrails (ADR templates, compatibility policies, test conventions, CI config) are allowed because they are enabling constraints with identifiable first consumers that unblock implementation. Tools and infrastructure serve domain intent, and the domain serves system capability; the domain model is the key artifact, tool choices are judged by fidelity to domain expression rather than familiarity, and no tool preference may override correctness, operability, or shipped capability.

## Overview & Architecture
**Goal:** Shared abstraction layer into varying type systems for dynamic code generation and execution. Fluent, strongly-typed domain modeling for validation, serialization, and codegen.

- `Poly/` — core DSL: Data Modeling, Interpretation, Introspection, Text, Validation.
- `Poly.Benchmarks/` — runnable samples (see [FluentApiExample.cs](Poly.Benchmarks/FluentApiExample.cs)).
- `Poly.Tests/` — unit tests.
- Builders: `DataModelBuilder` → `DataTypeBuilder` → `PropertyBuilder` / `RelationshipBuilder`.
- Serialization: portable JSON; polymorphic handling in `DomainModeling/DataModelPropertyPolymorphicJsonTypeResolver.cs`.

**Module boundaries (enforced, one-way):**
- `Interpretation` → `Introspection`
- `Validation` → `Interpretation`
- `Introspection` must not depend on `Interpretation`.
- Exception: CLR implementations under [Poly/Introspection/CommonLanguageRuntime](Poly/Introspection/CommonLanguageRuntime) add concrete types without introducing reverse dependencies.

## Build & Test
- Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- Test: `dotnet run --project Poly.Tests/Poly.Tests.csproj`
- Work is not complete while the build is failing; keep iterating until build failures are resolved or explicitly blocked by the user.
- Add tests alongside feature changes; mirror the builder-first style.

## Placement Rules
| What | Where |
|---|---|
| Shared abstractions | `Introspection` |
| Evaluators, operators | `Interpretation/Operators` (use `InterpretationContext`) |
| Constraints | `Validation/Constraints` — add via `WithConstraint()` |
| Rules | `Validation/Rules` — attach via `DataTypeBuilder.AddRule()` |
| Relationships | `HasOne`/`HasMany` + `WithOne`/`WithMany` on `DataTypeBuilder` |
| Shared helpers | `Extensions/` |

New JSON constraint/rule types: keep `Types`/`Properties`/`Relationships` shape consistent; update polymorphic resolvers.

## Coding Style
- Minimal changes; match existing fluent API naming and chaining patterns.
- No inline comments unless logic is non-obvious.
- No `#region`/`#endregion` directives.
