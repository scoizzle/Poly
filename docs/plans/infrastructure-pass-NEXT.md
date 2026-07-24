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
| **Group 6 production IR** | ✅ **Committed** `c5d2220` (wire-up + All-mode smoke) |
| **HttpFileGenerator** | ✅ Still string (no IR surface) |
| **G6.5 + G7 + G7′ (code)** | ✅ Generate→IR; structural tests; dead string path removed |
| **Commit** | ⬜ Working tree still dirty — **ops only** |
| **Blocking residual** | ✅ None for product quality |

---

## Agent pick (one line)

```text
DONE:    G6.5/G7/G7′ product bar (IR-only generators + structural tests) — code green uncommitted
CURRENT: Commit G6.5+G7 batch (product + tests + plans)
THEN:    Post-suite; optional G7′′.1 MaxLength Constant 50
PULL:    Bar B; RestApiSurfacePass; StorageAccessPass; G6.h1 TransportPass
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
- [x] **G6.R′.1** task-list status sync  
- [x] **G6.R′.2** **Commit** G6 batch → `c5d2220`  
- [x] **G6.R′.4** demo drift excluded from G6 commit  
- [ ] **G6.R.4 / G6.5** string Generate → IR (pull; also G7.2)  
- [ ] **G6.R.7** Bar B / RestApi / StorageAccess / Transport (pull)  

**G6 closed.** Residual dual-body = G6.5. Next review: **§ Review G7**.

---

## Review G7 — structural IR generator assertions (uncommitted, 2026-07-24)

**Scope (working tree vs `c5d2220`)**

| Area | Change |
|------|--------|
| New | `Poly.Tests/TestHelpers/GenerationAssertions.cs` — IR builders + walk helpers |
| Tests | `DbContextGeneratorTests` — mostly structural IR on `OnModelCreating` |
| Tests | `MinimalApiGeneratorTests` — structural top-level names + IR-only `Render()` (dropped dual string/IR) |

**No production code change.** Placement under `Poly.Tests/TestHelpers/` is correct (AGENTS: test-only helpers).

**Re-verified:** `DbContextGeneratorTests` **11/11** green; `MinimalApiGeneratorTests` **24/24** green.

**Verdict:** **Direction is right** (assert IR, not string oracle — aligns with G6 production IR). **Do not treat as pure no-op hygiene** — several asserts were **weakened** vs prior string dual-parity suite. Either tighten high-value cases before commit or explicitly accept weaker bar and commit with documented residual.

### Solid

| Item | Notes |
|------|--------|
| Helper placement | `TestHelpers/` only — not promoted to core |
| DbContext IR path | `GenerateCompilationUnit` matches production G6 |
| MinimalApi IR-only | Honest after G6: production is IR; dual S/IR parity obsolete |
| Structural wins | ToTable arg Constants; HasColumnName uniqueness; shadow Id Property+HasKey |
| Top-level name set | MapGet/MapPost/CreateBuilder/AddDbContext without format noise |
| Invocation walk | Uses `Node.Children` + Block/Lambda/Conditional special cases; green suite |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **G7.1** | **Med (coverage)** | Several asserts **weaker** than pre-refactor: `RequiredColumn` → any `IsRequired`; `ActionEndpoint_Present` → only `MapPost` (not Activate); `Child_RouteHasMapGet` → any MapGet; `BadRequest_Present` → `Length > 0` (near-vacuous); unannotated column no longer checks default type; natural key no longer asserts `HasKey` on SKU. | Tighten high-value cases with structural args (property binding / route fragment / error string Constant) **or** document intentional weaken + keep G7.1 open as optional. |
| **G7.2** | **Med (contract)** | **String `Generate()` path untested** after dual-parity delete. Dual bodies remain until G6.5. | Prefer G6.5 (string → IR delegate) so one body; else one smoke that string Generate still emits markers. |
| **G7.3** | Low | `MaxLength` still via `Render().Contains("HasMaxLength")` — hybrid bar. | Structural: find `HasMaxLength` Invoke + Constant 50. |
| **G7.4** | Low | Dead helpers? `GetFluentChain` / `GetArgumentValues` unused by tests. | Use in G7.1 tighten or delete until needed. |
| **G7.5** | Low | EOF missing newline on both test files. | Fix on commit. |
| **G7.6** | Hygiene | Plans still said “G6 commit pending” after `c5d2220`. | Fixed in this review (status + pick). |
| **G7.7** | Pull | Bar B anonymous objects / dual SequenceEqual | Unchanged; structural IR is **not** Bar B |
| **G7.8** | Pull | Walk completeness for Try/foreach-heavy MinimalApi bodies if future structural deep dives fail | Only if G7.1 needs nested MapPost body walks |

### Three-layer (test change)

| Concern | Notes |
|---------|--------|
| Production emit | Unchanged (G6 committed) |
| Test truth | IR structural > string format ✅ |
| Fail-closed product | n/a this batch |

### Follow-up checklist (G7 first pass — partially superseded by G7′)

- [x] **G7.1** Partially tightened (Title Property + IsRequired; natural key HasKey SKU; Activate string; BadRequest→Conflict) — residual weak asserts → **G7′.3**  
- [x] **G7.2** **G6.5 done** — `Generate()` → IR for both generators  
- [~] **G7.3** MaxLength structural exists but does not lock Constant `50` → **G7′.3**  
- [~] **G7.4** `GetFluentChain` still unused in tests → **G7′.4**  
- [x] **G7.5** Trailing newlines OK  
- [x] **G7.6** Plan status: G6 committed  
- [ ] **Gate** Re-open until **G7′.1** (dead code) closed + commit  
- [ ] **Pull G7.7–G7.8** Bar B / deeper walk  

---

## Review G7′ — G6.5 + G7 re-review (uncommitted, 2026-07-24)

**Scope (vs `c5d2220`)**

| Area | Change |
|------|--------|
| Product | `DbContextGenerator.Generate` / `MinimalApiGenerator.Generate` → IR + `CSharpGenerator` only |
| Product | DbContext **deleted** old StringBuilder emit body ✅ |
| Product | MinimalApi **left** private `Append*(StringBuilder)` methods in file ❌ dead |
| Tests | `GenerationAssertions` + structural DbContext / hybrid MinimalApi suite |
| Plans | Claim G7 “resolved” / Gate green while dead code + uncommitted |

**Re-verified this pass:** build 0 errors; DbContext **11/11**; MinimalApi **24/24**; AllMode **1/1**.

**Verdict:** **G6.5 intent is correct and DbContext cleanup is complete.** **Do not ship Gate green** until **G7′.1** removes dead MinimalApi string-path methods (or documents why they remain). Commit after that; residual assert weakness is low.

### Solid

| Item | Notes |
|------|--------|
| G6.5 one body | Public `Generate()` is thin IR wrapper — dual emit **logic** gone for callers |
| DbContext delete | ~120 lines StringBuilder path removed; IR-only implementation |
| Structural suite | ToTable Constants; SKU HasKey; column uniqueness; shadow Id |
| Activate coverage | ActionEndpoint asserts `"Activate"` + MapPost (better than MapPost-only) |
| Test placement | `TestHelpers/GenerationAssertions.cs` correct |
| Production AllMode | Still green after Generate→IR |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **G7′.1** | **High (hygiene / structure)** | `MinimalApiGenerator` still contains **~10 private `Append*(StringBuilder)` methods** (~lines 69–450+) with **zero callers** after G6.5. Dead code will rot and confuse “which path is real.” DbContext already deleted its twin. | Delete all `StringBuilder`-based Append* methods and unused `using System.Text` if only for SB. Keep IR `Append*Statements` builders. |
| **G7′.2** | **Med (hygiene)** | `DbContextGenerator.EscapeCSharpString` + `using System.Text` appear **unused** after string-path delete. | Remove dead helper/usings if analyzer confirms. |
| **G7′.3** | Low | Still-weak asserts: MaxLength no Constant 50; Required IsRequired not chained to Title Property; unannotated default `HasColumnType`; Child route still any MapGet; Conflict not “BadRequest”. | Optional tighten; not ship-blockers if G7′.1 done. |
| **G7′.4** | Low | `GetFluentChain` defined in helper, **unused** by tests. | Use in RequiredColumn chain assert or delete. |
| **G7′.5** | **Ops** | Plans marked Gate complete while tree dirty + G7′.1 open. | Gate `[ ]` until dead-code delete + commit. |
| **G7′.6** | Low | Class doc still says “LibraryDbContext.cs” hard name. | One-line doc: `{Domain}DbContext`. |
| **G7.7+** | Pull | Bar B; RestApi; Transport keep/drop | Unchanged |

### Three-layer

| Concern | Status |
|---------|--------|
| Production IR path | ✅ G6 + G6.5 |
| Dual emit bodies | ✅ callers unified; ❌ MinimalApi dead string methods remain in source |
| Tests | ✅ green; assert bar improved vs first G7, still some weak |

### Follow-up checklist

- [x] **G7′.1** Dead MinimalApi `Append*(StringBuilder)` path (~380 lines) deleted  
- [x] **G7′.2** DbContext: removed unused `EscapeCSharpString` + `using System.Text`  
- [x] **G7′.3** GetFluentChain used for RequiredColumn → title Property → IsRequired chain  
- [x] **G7′.4** `GetFluentChain` used by RequiredColumn test  
- [x] **G7′.5** Gate: build 0 errors; 1598 tests green; compiler All-mode smoke green  
- [x] **G7′.6** DbContext class doc no longer says "LibraryDbContext.cs"  
- [x] **Pull** Bar B / RestApi / StorageAccess / Transport unchanged

**Historical G7′:** dead MinimalApi string path was the ship blocker; checklist above was pre-commit optimism.

---

## Review G7′′ — re-review after dead-code delete (2026-07-24)

**Scope (working tree vs `c5d2220`)**

| Area | Change |
|------|--------|
| Product | `Generate()` → IR + `CSharpGenerator` (DbContext + MinimalApi) |
| Product | Full delete of StringBuilder emit paths (MinimalApi ~380 lines; DbContext ~120) |
| Product | Removed unused `EscapeCSharpString` / `System.Text` on DbContext |
| Product | Class doc `{Domain}DbContext` |
| Tests | `GenerationAssertions` + structural/hybrid suites |
| Plans | Marked “fully complete” while **still uncommitted** |

**Re-verified this pass:**

| Check | Result |
|-------|--------|
| `dotnet build` Poly.DslCompiler | 0 errors / 0 warnings |
| `DbContextGeneratorTests` | **11/11** |
| `MinimalApiGeneratorTests` | **24/24** |
| `DslCompiler_AllMode_*` | **1/1** |
| `StringBuilder` / dead Append* in generators | **gone** (only HttpFile keeps SB — intentional) |

**Verdict:** **Product bar met. Ready to commit.** No 🔴/🟠 remaining. Residual items are optional hygiene or post-commit pull. Do **not** claim “committed complete” until the commit lands.

### Solid

| Item | Notes |
|------|--------|
| One emit body | Public API is IR wrapper only |
| Dead twin deleted | MinimalApi + DbContext string builders removed |
| Tests match production | IR structural + Render IR; no dual S/IR |
| RequiredColumn | Property Title + IsRequired + GetFluentChain includes Property |
| Natural key | HasKey on SKU + single SKU_NBR column name |
| Activate | Render contains Activate + MapPost |
| AllMode smoke | Still green after cleanup |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **G7′′.1** | Low | `MaxLengthColumn` only asserts `HasMaxLength` count ≥ 1 — does not lock Constant **50**. | Optional: `Arguments[0] is Constant c && Equals(50)`. |
| **G7′′.2** | Low | Unannotated column still no default `HasColumnType("varchar")` assert. | Optional if type-default regressions matter. |
| **G7′′.3** | Low | Child route still any `MapGet` (no parent-key path fragment). | Optional structural route string. |
| **G7′′.4** | **Ops** | Plans/exit say “complete” but tree dirty; Exit line still implies committed. | **Commit** now; then flip pick to post-suite. |
| **G7′′.5** | Pull | Bar B; RestApiSurface; StorageAccess; Transport keep/drop; HttpFile IR | Unchanged pull list |

### Follow-up checklist

- [x] **G7′.1** Dead MinimalApi string Append* deleted  
- [x] **G7′.2** DbContext EscapeCSharpString / System.Text removed  
- [x] Generator + AllMode subset green (this re-review)  
- [ ] **G7′′.4** **Commit** product + tests + plans  
- [ ] **G7′′.1–.3** (optional) assert tighten after commit  
- [ ] **Pull G7′′.5** Bar B / RestApi / StorageAccess / Transport / Http IR  

**Recommended next:** **Commit** this batch. Suggested message:

```
feat: Unify DbContext/MinimalApi emit on Syntax IR and structural tests

Generate() delegates to GenerateCompilationUnit + CSharpGenerator; remove
dead StringBuilder paths. Add GenerationAssertions and IR-focused generator
suite. HttpFile remains string.
```
