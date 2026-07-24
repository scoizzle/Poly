# Infrastructure Pass — WHAT TO DO NEXT

> **For agents:** Open this file first.  
> Full ladder: [`infrastructure-pass-task-list.md`](infrastructure-pass-task-list.md)  
> Micro-tasks: [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)

---

## Status

| Field | Value |
|-------|--------|
| **Groups 1–5** | ✅ **Done** under current bar (Group 2 = Bar A only) |
| **3y.1** | ✅ Fail-closed storage / behavior / aggregate |
| **Commit G1–5** | ✅ On branch (`3d276a6` family) |
| **Production IR code** | ✅ Working tree: DbContext + Program via IR + All-mode smoke |
| **HttpFileGenerator** | ✅ Still string (no IR surface) — per plan |
| **Group 6 product bar** | ✅ Met (wire-up + smoke) — **commit still pending** |
| **Plan package** | 🟡 task-list was stale vs NEXT — fixed in § Review G6′ |

---

## Agent pick (one line)

```text
DONE:    G6 product path (IR wire-up + AllMode smoke); R.1 closed
CURRENT: Commit G6 batch (exclude demo.http / library.db); sync any leftover plan drift
THEN:    Optional G6.5 string→IR; G6.h1 TransportPass
PULL:    Bar B; RestApiSurfacePass; StorageAccessPass
```

**Primary entry:** [`simple-agent-tasks/ip-README.md`](simple-agent-tasks/ip-README.md)

---

## Round goal (Group 6)

**Ship one production path through Syntax IR**, not more analyzer decomposition.

Now (`src/Poly.DslCompiler/DslCompiler.cs` ~239–257):

```text
CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())      // ✅ IR-backed
CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbCtx)) // ✅ IR-backed
httpGen.Generate()                                                // string (no IR)
```

**Done bar for Group 6 (not Bar B):**

| Required | Not required |
|----------|----------------|
| `CompileMode.Db` / `All` emit IR-backed DbContext + Program.cs | Byte-identical to old string oracle |
| Existing Bar A renorms remain acceptable | Anonymous `{ error = }` objects |
| Smoke: key markers / compile files non-empty | Dual SequenceEqual goldens |
| Fail-closed pipeline unchanged | RestApiSurfacePass / new analyzers |

---

## Task order

| # | ID | Work | Sev |
|---|-----|------|-----|
| **1** | **G6.0** | Inventory production call sites + test helpers | ✅ |
| **2** | **G6.1** | Wire **DbContext** production path to IR | ✅ |
| **3** | **G6.2** | Wire **MinimalApi** production path to IR | ✅ |
| **4** | **G6.3** | Compiler-level smoke (Db + All) | ✅ |
| **5** | **G6.4** | Plan/docs honesty: production uses IR for those files | ✅ |
| **6** | **G6.5** | Optional: string `Generate()` delegates to IR | `[ ]` pull |
| **7** | **G6.6** | Update `Program.cs` usage text | ✅ |
| **Gate** | — | Pre-ship review | ✅ 1603 tests, 0 errors |

Optional parallel hygiene (do not block G6.1–G6.3):

| ID | Work |
|----|------|
| **G6.h1** | TransportPass keep-or-drop decision (one paragraph in suite doc) |
| **G6.h2** | CrossReferencePass: leave deferred; no code |

---

## Explicit non-goals this round

- **Bar B** full string-oracle parity  
- **RestApiSurfacePass** / **StorageAccessPass**  
- New domain query surface (Q4, dates)  
- Re-opening Group 2 as incomplete  
- Rebuilding analysis pipeline  

---

## Verify

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
# subset during loop:
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DbContext*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*MinimalApi*'
```

---

## History (closed)

- **3y.1** fail-closed behavior/aggregate  
- Groups 1–5 under Bar A  
- Former “commit working tree” for G1–5 — **done on branch**

---

## Review G6 — production IR wire-up (first pass, 2026-07-23) *[historical]*

First review blocked ship on missing **All-mode** smoke and plan overclaim. Residuals tracked as G6.R.1–R.7.

---

## Review G6′ — re-review after All-mode smoke (2026-07-23)

**Scope (working tree vs `7d067c0`)**

| Area | Change |
|------|--------|
| Product | `DslCompiler`: DbContext + Program via `GenerateCompilationUnit` + `CSharpGenerator` |
| Product | File name `{domain.Name}DbContext.cs` (was hard-coded `LibraryDbContext.cs`) |
| CLI | `--mode all` → “via Syntax IR” (not “not yet ready”) |
| Tests | `DslCompiler_AllMode_EmitsDbContextAndProgramViaIr` (Catalog domain structural markers) |
| Plans | `ip-*` suite + NEXT/task-list |
| Drift | `demo/Poly.RestApi/demo.http` still dirty; untracked `library.db` |

**Re-verified this pass:** AllMode smoke **1/1 green**; product IR wire-up unchanged and sound.

**Verdict:** **Product bar for Group 6 is met.** Remaining work is **ops + plan hygiene**, not more IR architecture. **Commit** the G6 batch; exclude demo drift. Optional G6.5 / pull list stay non-blocking.

### Solid

| Item | Notes |
|------|--------|
| IR production wire | Thin, correct, matches Bar A generator path |
| All-mode smoke (G6.R.1) | Asserts `CatalogDbContext.cs`, `Program.cs`, `demo.http`; DbSet/OnModelCreating; MapGet/MapPost |
| Filename | Domain-named; smoke locks `CatalogDbContext` for sample domain |
| HttpFile string | Still intentional |
| Fail-closed infra metadata | Unchanged |
| CLI honesty | G6.6 correct |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **G6.R′.1** | **Med (plan honesty)** | task-list still said “ship gate open / G6.3 reopen” while NEXT claimed complete — **R.2 incomplete**. | Sync task-list + stopping point to “product met; commit pending” (this write-back). |
| **G6.R′.2** | **Ops** | Exit text said “committed”; tree still dirty. | **Commit** product + tests + plans (not demo). |
| **G6.R′.3** | Low | `SqlitePackTests.cs` missing trailing newline. | Add newline on touch/commit. |
| **G6.R′.4** | Hygiene | `demo.http` + `library.db` still present as dirty/untracked. | Exclude from G6 commit (restore demo.http if unwanted). |
| **G6.R′.5** | Low | All-mode smoke does not assert Program references `CatalogDbContext` by name. | Optional one `Contains("CatalogDbContext")` on Program.cs if agent confusion. |
| **G6.R.4** | Pull | Dual string `Generate()` vs IR | G6.5 optional |
| **G6.R.7** | Pull | Bar B / RestApi / StorageAccess / Transport | Unchanged |

### Three-layer

| Concern | Emit path | Proof |
|---------|-----------|--------|
| Db IR | production | Db pack tests + AllMode |
| Program IR | production | AllMode ✅ |
| Missing metadata | fail-closed | unchanged |
| Filename | domain-named | AllMode asserts Catalog* |

### Follow-up checklist

- [x] **G6.R.1** All-mode smoke  
- [x] **G6.R′.1** task-list status sync (this re-review)  
- [ ] **G6.R′.2** **Commit** G6 batch  
- [ ] **G6.R′.3** trailing newline on SqlitePackTests  
- [ ] **G6.R′.4** Exclude demo drift / library.db  
- [ ] **G6.R′.5** (optional) Program contains context type name  
- [ ] **G6.R.4 / G6.5** string Generate → IR (pull)  
- [ ] **G6.R.7** Bar B / RestApi / StorageAccess / Transport (pull)  

**Recommended next:** **Commit** (product + tests + infra plans + `ip-*`). Then post-suite. Do not open Bar B in the same commit.
