# pack-3b-3 — Producer-filled contract prints

**Claimed by:** fleet agent pack-3b-3  
**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** pack-3b-2 `[x]`  

## Objective

Filled contract round-trips as hand-authored `contract internal` DSL.

## Exact steps

1. Produce → print → parse → same types/endpoints.
2. Guide: one honesty sentence that InternalDomain producer may fill the body; hand-authored body is still legal.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --treenode-filter "/*/*/*/*/*PolyDslRoundTrip*"
```

- [x] Round-trip  
- [x] Guide sentence  

## File ownership

| Edit | Do not edit |
|------|-------------|
| printer only if contract print hole | core grammar keywords |
| `Poly.Mcp/Docs/poly-dsl-guide.md` (append only) | |

## Status

**Status:** Done — `InternalDomainResolutionTests`-style flow: produce from a loaded `billing` domain, `DomainSuite.FillInternalContracts`, `DomainDslPrinter.Print`, re-parse, same value types + endpoints (name, properties, kind, direction, payload). The printer had **no contract print hole** — `PrintContract` already emits `Types` + `Endpoints`. The round-trip exposed a pre-existing parser gap: `ParseContract` never called `EnsurePrimitivesOnce`, so a contract-only domain (a filled contract prints exactly that — `Amount: Number` with no entity present) failed apply-analysis on re-parse. One-line fix: ensure primitives when entering a contract body (mirrors `ParseEntity`; no grammar keyword change). Guide: one honesty sentence appended to the Contracts section of `poly-dsl-guide.md` (agent guide has no contract section). New test 2158 → 2159, suite green.

**Stopped at the slice gate (pr1) per pack-README — pack-3b-gate not started.**
