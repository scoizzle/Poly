# Introspection Test Coverage Notes

This document tracks broad coverage areas for `Poly.Tests/Introspection` and highlights remaining opportunities without tying guidance to a single historical test count.

## Covered Areas

Current tests cover:

- CLR type definition basics (name/namespace/full name/members)
- Registry behavior (lookup, add/remove, deferred resolution)
- Member metadata and accessors (fields, properties, methods, parameters)
- Provider composition and LIFO resolution order
- Indexer and enumerable-member matching scenarios
- Edge cases (nullable, generic, abstract/interface, nested, delegate, enum, struct)
- Inheritance-heavy scenarios and complex member access patterns

## Lower-Priority Gaps

Potential additions if further hardening is needed:

1. Interface default-member behavior checks for `ITypeDefinition` convenience methods.
2. Focused caching tests (registry and lazy resolver lifecycle behavior).
3. Stress/perf-oriented tests over large member sets and deep type hierarchies.

## How To Re-Validate Coverage

Use the repository test command and then inspect `Poly.Tests/Introspection` for failing or missing scenarios:

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

When adding new Introspection functionality, prefer adding tests in the closest existing file before creating a new one.
