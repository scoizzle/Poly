# Infrastructure Pass Suite — Executable Task List

> **Agents — start here:** [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md)  
> Micro-tasks: [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)

**Date:** 2026-07-23  
**Status:** Groups 1–5 ✅ | Group 6 ✅ `c5d2220` | **G6.5+G7 product bar met** — commit pending (§ Review G7′′ on NEXT)  
**Derived from:** `docs/plans/infrastructure-concern-analyzer-suite.md`

---

## Done bar (read this first)

| Bar | Meaning | Status |
|-----|---------|--------|
| **A — IR side-path** | Valid main-path C#; thin smokes; **renormed** IR dialects | ✅ Group 2 Done |
| **Production IR** | `DslCompiler` emits DbContext + Program via IR + `CSharpGenerator` | ✅ **Done** `c5d2220` |
| **Structural generator tests** | Assert IR nodes, not string dual-parity | ✅ code; ⬜ commit — § Review G7′′ |
| **G6.5 one emit body** | `Generate()` → IR + `CSharpGenerator`; dead string path gone | ✅ code; ⬜ commit |
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

Group 6 committed production IR; G6.5 makes `Generate()` IR-only (uncommitted cleanup).

---

## Task Group 6: Production IR wire-up ✅ COMPLETE (`c5d2220`)

**Micro-tasks:** [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)  
**Reviews:** NEXT § Review G6 / G6′ (historical)

| Unit | Status |
|------|--------|
| G6.0–G6.4, G6.6, AllMode smoke | `[x]` committed |
| **G6.5** string Generate→IR | `[x]` code; ⬜ commit (Group 7 batch) |

---

## Task Group 7: Structural IR + G6.5 ✅ PRODUCT BAR MET — COMMIT PENDING

**Review:** [`infrastructure-pass-NEXT.md`](infrastructure-pass-NEXT.md) **§ Review G7′′**

| Unit | Status |
|------|--------|
| G6.5 `Generate()` → IR (both generators) | `[x]` |
| DbContext + MinimalApi string-path delete | `[x]` |
| `GenerationAssertions` + suites (11+24) + AllMode | `[x]` green |
| **Gate / product proof** | `[x]` |
| **Commit** | `[ ]` **G7′′.4** |

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
| Group 1–5 Done | ✅ |
| IR production path | ✅ `c5d2220` |
| G6.5 / structural tests / dead-code delete | ✅ code green; ⬜ **commit** |
| Full oracle parity | ❌ Bar B deferred |

**Next agent action:** **Commit** G6.5+G7 batch — NEXT § Review G7′′.
