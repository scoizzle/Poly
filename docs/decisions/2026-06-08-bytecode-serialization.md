# ADR: Bytecode Serialization Format

**Date:** 2026-06-08  
**Status:** Accepted — **Deferred** per resolution plan Q2. INT-019 not yet prioritized; no bytecode consumer exists.  

## Context

`Bytecode` is an in-memory CLR type with CLR object references:

- `CallSiteDelegate[]` — delegate instances bound to specific CLR methods
- `Type? ResultType` — CLR `Type` object
- `Dictionary<int, NodeId> SourceMap` — `NodeId` is a CLR type
- `FunctionEntry[]`, `ExceptionRegion[]` — CLR records

This cannot be serialized to disk, sent over a wire, or cached between process invocations. The entire lowering pass must re-run every time a program executes.

Serialization enables:
- Caching lowered programs across sessions
- Shipping compiled IR to remote executors
- Snapshotting suspended VM state for later resumption
- Deterministic replay for debugging

## Decision

Add a portable bytecode serialization format that replaces CLR object references with stable indices or string names.

### Contract

1. **CallSite references** are encoded as (`assemblyName`, `typeName`, `methodName`, `signature` tuple). On deserialization, these are resolved via `CallSiteCompiler` at load time — or left unresolved and resolved lazily on first execution.

2. **Type references** are encoded as `typeName` (full name) and resolved via `Type.GetType()` or a configurable type resolver.

3. **`NodeId`** is serialized as its string representation (`NodeId` has a `ToString()` / `Parse()` contract).

4. **Opcode bytecode** is copied verbatim (already portable).

5. **Constants** are serialized using a type discriminator + payload:
   - Primitives: discriminated binary encoding
   - Strings: length-prefixed UTF-8
   - Other: CLR `BinaryFormatter` fallback (or banned — TBD)

6. **Exception regions and function entries** are CLR records and serialize directly.

### Binary layout sketch (v0.1)

```
[magic: 4 bytes "POLY"]
[version: 2 bytes LE]
[constant_count: 4 bytes LE]
[constants...]
[function_count: 4 bytes LE]
[function_entries...]
[code_length: 4 bytes LE]
[code...]
[source_map_entry_count: 4 bytes LE]
[source_map_entries...]
[callsite_count: 4 bytes LE]
[callsite_entries...]
[exception_region_count: 4 bytes LE]
[exception_regions...]
[result_type_name: 2-byte-prefixed string]
```

### API

```csharp
internal static class BytecodeSerializer {
    public static byte[] Serialize(Bytecode program);
    public static Bytecode Deserialize(byte[] data, ITypeResolver? typeResolver = null);
}
```

## Rationale

- Enables caching of lowered IR across sessions and processes.
- Required for suspended VM state persistence.
- The format is compact, portable, and language-agnostic.
- `CallSite` resolution at load time keeps deserialization side-effect-free (no assembly load on read).

## Consequences

- Serialized bytecode depends on assembly/type name stability. Renaming a type breaks deserialization until re-lowering.
- `CallSiteCompiler.Compile` is called at deserialization time for each call site — this may be expensive for large programs.
- The magic + version header allows format evolution (v0.2, v0.3) with backward compatibility logic.
- `NodeId` string parsing is fast but not zero-alloc — acceptable for load-time deserialization.