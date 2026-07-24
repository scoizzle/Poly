# Infrastructure Pass Suite — Executable Task List

> **Agents — start here:** [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md)  
> Micro-tasks: [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)

**Date:** 2026-07-23  
**Status:** Groups 1–5 ✅ | **Group 6 product bar met** — commit pending (§ Review G6′)  
**Derived from:** `docs/plans/infrastructure-concern-analyzer-suite.md`

---

## Done bar (read this first)

| Bar | Meaning | Status |
|-----|---------|--------|
| **A — IR side-path** | Valid main-path C#; thin smokes; **renormed** IR dialects | ✅ Group 2 Done |
| **Production IR** | `DslCompiler` emits DbContext + Program via IR + `CSharpGenerator` | ✅ wire-up + AllMode smoke; ⬜ commit |
| **B — Full string oracle** | Anonymous `{ error = }`, switch arms, dual SequenceEqual | ❌ Pull — not Group 6 |

**Why Bar B stays deferred:** Syntax has no anonymous-object node; Group 6 ships **production use of existing Bar A IR**, not oracle identity.

**Doc rule:** Dual-`Contains` alone is not a Bar A gate. Known renorms must be explicit. Do not reopen Group 2 as incomplete.

---

## Task Group 1: Layer 0 — Entity Syntax ✅ COMPLETE

Committed. EntitySyntax metadata + pass + DslCompiler entity consumer.

---

## Task Group 2: Syntax IR Growth + Generator Conversion ✅ COMPLETE (Bar A)

IR side-path proven in tests. Production still used string `Generate()` until Group 6.

| Unit | Status | Notes |
|------|--------|--------|
| `2.4-substrate` | ✅ | CompilationUnit, empty Block, TLS, async/typed lambdas, using `var`, TypeIs binding |
| `2.5-dbcontext-parity` | ✅ | IR fidelity + 5 `b.*` fluent parity tests |
| `2.6-minimalapi-parity` | ✅ Bar A | Create/list/detail/seed/actions; renorm dialect |
| Mark Group 2 Done | ✅ | Under Bar A only |

### Suite-wide renorm (IR dialect) — still legal in Group 6 production

| String oracle | IR accepted |
|---------------|-------------|
| `BadRequest(new { error = "…" })` | `BadRequest("…")` (same text when possible) |
| `Conflict(new { error = msg })` | `Conflict(msg)` / `ErrorMessage` |
| `NotFound(new { error = "…" })` | `NotFound("…")` message Constant preferred |
| Result `switch` | `if (IsSuccess) … else Conflict` |
| Interpolated Created URI | `string.Concat` + key access |
| `Problem(detail:, statusCode: 500)` | `StatusCode(500)` (avoids bad positional Problem) |

---

## Task Group 3: Extract Analysis Passes ✅

| Task | Status |
|------|--------|
| 3.1 Metadata records | ✅ |
| 3.2–3.5 Core passes + StoragePass(analysis) | ✅ |
| 3.4 CrossReferencePass | ⏸️ deferred (no consumer) — G6.h2 |
| 3.6–3.7 StorageAccess / RestApi | ⬜ **pull** after G6 |
| 3.8 PassRegistry | ✅ |

---

## Task Group 4: Wire Generators to Metadata ✅

Generators take required non-nullable sub-models.

---

## Task Group 5: DslCompiler Wiring ✅

Pipeline + priorAnalysis + TransportPass + PassRegistry.  
Fail-closed: **storage** (db/all), **behavior** + **aggregate** (all).

Group 6: production Db/API use IR (working tree); string `Generate()` still dual body (G6.5 pull).

---

## Task Group 6: Production IR wire-up ✅ PRODUCT BAR MET — COMMIT PENDING

**Micro-tasks:** [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)  
**Review:** [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md) **§ Review G6′**

| Unit | File | Status |
|------|------|--------|
| **G6.0** Inventory | [`ip-g6-0-inventory.md`](simple-agent-tasks/ip-g6-0-inventory.md) | `[x]` |
| **G6.1** DbContext production IR | [`ip-g6-1-dbcontext-production-ir.md`](simple-agent-tasks/ip-g6-1-dbcontext-production-ir.md) | `[x]` uncommitted |
| **G6.2** MinimalApi production IR | [`ip-g6-2-minimalapi-production-ir.md`](simple-agent-tasks/ip-g6-2-minimalapi-production-ir.md) | `[x]` uncommitted |
| **G6.3** Compiler smoke Db+All | [`ip-g6-3-compiler-ir-smoke.md`](simple-agent-tasks/ip-g6-3-compiler-ir-smoke.md) | `[x]` AllMode test |
| **G6.4** Plan honesty | [`ip-g6-4-plan-honesty.md`](simple-agent-tasks/ip-g6-4-plan-honesty.md) | `[~]` after commit |
| **G6.5** Optional Generate→IR delegate | [`ip-g6-5-generate-delegates-ir.md`](simple-agent-tasks/ip-g6-5-generate-delegates-ir.md) | `[ ]` pull |
| **G6.6** CLI usage text | `Program.cs` | `[x]` uncommitted |
| **G6.R / G6′** Review residuals | NEXT § Review G6′ | `[~]` commit + hygiene |
| **Gate** | Product proof | `[x]` AllMode green; **commit is remaining ops** |

**Production path (in working tree):**

```csharp
files.Add(($"{dbContextName}.cs", new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())));
files.Add(("Program.cs", new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName))));
files.Add(("demo.http", httpGen.Generate())); // still string
```

**Note:** Output file is `{domain.Name}DbContext.cs` (not hard-coded `LibraryDbContext.cs`).

---

## Closed residuals (Groups 1–5)

| ID | Work | Status |
|----|------|--------|
| **3y.1** | Fail-closed behavior + aggregate | ✅ |
| 3x′′.1 storage fail-closed | ✅ |
| 3x′′.2 pipeline + TransportMetadata | ✅ |
| 3x′′.3 Transport unused documented | ✅ |
| 3x′′.4 CrossReference deferred | ✅ |
| **3y.4** | Commit G1–5 working tree | ✅ on branch |

---

## Pull after Group 6

| ID | Work |
|----|------|
| **Bar B** | Anonymous-object Syntax + full dual goldens |
| **RestApiSurfacePass** | Routes/DTO surface when consumer needs it |
| **StorageAccessPass** | Query/mutation patterns when consumer needs it |
| **G6.h1** | TransportPass keep-or-drop (can run anytime) |

---

## Stopping point — honest “where we are”

| Claim | Reality |
|-------|---------|
| Group 1–5 Done | ✅ under current bar |
| IR production path | ✅ wired + AllMode smoke; ⬜ **commit** |
| Full oracle parity | ❌ Bar B deferred |

**Next agent action:** **Commit** G6 (exclude demo drift) — [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md) § Review G6′.
