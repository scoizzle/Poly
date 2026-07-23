# Infrastructure Pass Suite — Executable Task List

> **Agents — start here:** [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md)  
> (Single source of “what is next.” Do not skip units.)

**Date:** 2026-07-23  
**Status:** Group 1 ✅ | Group 2 ✅ **Bar A** | Groups 3–5 ✅ **Done** (3y.1 closed) — commit working tree  
**Derived from:** `docs/plans/infrastructure-concern-analyzer-suite.md`

---

## Done bar (read this first)

| Bar | Meaning | Group 2? |
|-----|---------|----------|
| **A — IR side-path** | Valid main-path C#; thin non-vacuous smokes; **renormed** IR dialects documented suite-wide | ✅ Required — **Done** |
| **B — Full string oracle** | Anonymous `{ error = }`, switch arms, Created interpolation, dual SequenceEqual goldens | ❌ Deferred — **not** Group 2 |

**Why Bar B is deferred:** Syntax has no anonymous-object node; production still uses string `Generate()`; insisting on byte/oracle identity blocked shipping a usable IR side path (see agent note: tests aimed too high while gates stayed weak).

**Doc rule:** Dual-`Contains` alone is not a Bar A gate. Known renorms must be explicit. Do not reopen Group 2 for Bar B without a new plan item.

---

## Task Group 1: Layer 0 — Entity Syntax ✅ COMPLETE

Committed. EntitySyntax metadata + pass + DslCompiler entity consumer.

---

## Task Group 2: Syntax IR Growth + Generator Conversion ✅ COMPLETE (Bar A)

IR is **not production-wired**. Production still uses string `Generate()`.

| Unit | Status | Notes |
|------|--------|--------|
| `2.4-substrate` | ✅ | CompilationUnit, empty Block, TLS, async/typed lambdas, using `var`, TypeIs binding |
| `2.5-dbcontext-parity` | ✅ | IR fidelity + 5 `b.*` fluent parity tests |
| `2.6-minimalapi-parity` | ✅ Bar A | Create/list/detail/seed/actions; MapPost Create vs Seed isolation; action try/StatusCode/IsSuccess; error-payload renorm |
| Mark Group 2 Done | ✅ | Under Bar A only |

### Suite-wide renorm (IR dialect)

| String oracle | IR accepted |
|---------------|-------------|
| `BadRequest(new { error = "…" })` | `BadRequest("…")` (same text when possible) |
| `Conflict(new { error = msg })` | `Conflict(msg)` / `ErrorMessage` |
| `NotFound(new { error = "…" })` | `NotFound("…")` message Constant preferred |
| Result `switch` | `if (IsSuccess) … else Conflict` |
| Interpolated Created URI | `string.Concat` + key access |
| `Problem(detail:, statusCode: 500)` | `StatusCode(500)` (avoids bad positional Problem) |

### Optional hygiene (non-blocking; do not reopen Group 2 as incomplete)

- Child test with real relationship + `Collection(e => e.`  
- BadRequest entity-ref skip domain asserting message on both S and IR  
- Bar B later: anonymous-object Syntax node + full dual goldens  

---

## Task Group 3: Extract Analysis Passes ✅ (with deferred CrossReference)

| Task | Status |
|------|--------|
| 3.1 Metadata records | ✅ |
| 3.2–3.5 Core passes + StoragePass(analysis) | ✅ |
| 3.4 CrossReferencePass | ⏸️ deferred (no consumer) |
| 3.6–3.7 StorageAccess / RestApi | ⬜ deferred |
| 3.8 PassRegistry | ✅ |

---

## Task Group 4: Wire Generators to Metadata ✅

Generators take required non-nullable sub-models (tests inject explicitly).

---

## Task Group 5: DslCompiler Wiring ✅

Pipeline + priorAnalysis + TransportPass + PassRegistry.  
Fail-closed: **storage** (db/all), **behavior** + **aggregate** (all). No pack-dropping re-analyze.

Production string Generate().

---

## Closed residuals

| ID | Work | Status |
|----|------|--------|
| **3y.1** | Fail-closed behavior + aggregate | ✅ |
| 3x′′.1 storage fail-closed | ✅ |
| 3x′′.2 pipeline + TransportMetadata | ✅ |
| 3x′′.3 Transport unused documented | ✅ |
| 3x′′.4 CrossReference deferred | ✅ |

**Verify:** `dotnet build` + `dotnet run --project Poly.Tests`

---

## Stopping point — honest “where we are”

| Claim | Reality |
|-------|---------|
| Group 1–5 Done | ✅ under current bar (Group 2 = Bar A only) |
| IR production path | ❌ string Generate() (correct until pull) |
| Full oracle parity | ❌ Bar B deferred |

**Next agent action:** Commit working tree. Pull units: production IR, Bar B — see [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md).
