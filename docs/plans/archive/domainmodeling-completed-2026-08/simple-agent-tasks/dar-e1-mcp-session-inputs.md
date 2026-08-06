# Micro-Task: DAR.E1 - MCP Session Inputs

Parent: ../domain-authoring-context-removal-plan.md (Phase E)
Queue: ./dar-README.md
Difficulty: Large
Status: [x] Completed 2026-07-28
Prereq: DAR.D1

## Objective

Replace mutable MCP session context singleton usage with explicit persisted
analysis/parser inputs per session.

## Tasks

- [x] E1.1 Remove `McpSessionStore.Context` mutable singleton usage.
- [x] E1.2 Add explicit session input persistence model for parse/analyze.
- [x] E1.3 Update MCP tools to read/write explicit session inputs.
- [x] E1.4 Add backward-compatibility migration for existing sessions that
      currently rely on context-derived behavior.

## Primary Files

- Poly.Mcp/Sessions/McpSessionStore.cs
- Poly.Mcp/Tools/*
- Poly.Tests/Mcp/*

## Acceptance Criteria

- [x] MCP semantic operations no longer depend on global mutable authoring
      context state.
- [x] Session snapshots contain explicit inputs needed to replay parse/analyze.
- [x] Legacy sessions use default explicit inputs for new sessions; unsupported
      legacy context paths now fail at compile-time rather than silently diverging.

## Verification

- [x] Build green.
- [x] MCP tests/smokes green.

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*Mcp*'
```

## Out of Scope

- DslCompiler and pack default API migration.

## Progress Notes

- Added `Inputs` to MCP session state.
- `apply_dsl`, `export_dsl`, and evolve helpers now consume session-scoped explicit inputs.
