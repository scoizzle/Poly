# Plan: Split `Poly.Syntax` → `Poly.Ast` + `Poly.Analysis`

**Status:** **Deferred** — do not execute while post-M2 product work (or large renames) is in flight; prefer clean tree.  
**Created:** 2026-07-11  
**Resume when:** Current platform work is stable (see [Preconditions](#preconditions)).  
**Type:** Structural refactor / namespace migration (behavior-preserving).  
**Related:**
- [`docs/CORE.md`](../CORE.md) — current ownership map (still says `Syntax` until this lands)
- Conversation consensus: Syntax is **two domains** (interpretable AST + analysis substrate); nodes must **not** move under Interpretation
- Domain boundary: `DomainModeling` → AST only for pure lowering; Interpretation remains semantics + VM

---

## 1. Why

Today `Poly/Syntax/` combines:

| Domain | Contents (approx.) |
|--------|-------------------|
| **Interpretable AST** | `Node`, `NodeId`, `NodeExtensions`, `Syntax/Nodes/*` |
| **Analysis substrate** | `Syntax/Analysis/*` — `Analyzer`, `AnalysisContext`, metadata, diagnostics, **node replacement** |

That dual nature is correct as a *dependency story* (analysis is defined over nodes) but weak as *names and ownership*:

- **“Syntax”** suggests parsing / surface language; Poly’s tree is mostly **constructed IR**, not a classic compiler front-end.
- CORE calls the tree the **primary symbolic IR** and analysis the **metadata + replacement** machine — those deserve names that match.
- Agents reinvent rewriters and side tables partly because “Syntax” does not advertise the analysis contract.

**Target names (agreed as sound on logical, lexical, and CORE-canonical axes):**

| New home | Owns |
|----------|------|
| **`Poly.Ast`** | Immutable program tree: nodes, ids, construction helpers |
| **`Poly.Analysis`** | Pass pipeline, context, result, metadata store, diagnostics, node replacement |
| **`Poly.Interpretation`** | Unchanged role: semantic passes, `Interpreter`, `DirectVmAbiEmitter`, VM |
| **`Poly.Introspection`** | Unchanged: host-neutral types; CLR first provider |

**Explicit non-goal:** Moving node definitions into `Poly.Interpretation`. That would make the platform IR look like executor-private types and force `DomainModeling` onto Interpretation for pure DE→AST lowering.

---

## 2. Target end state

### 2.1 Logical layout (folders / namespaces)

```text
Poly/
  Ast/                          # was Syntax/ (nodes only)
    Node.cs
    NodeId.cs
    NodeExtensions.cs
    Nodes/                      # all node records
    README.md

  Analysis/                     # was Syntax/Analysis/ (framework only)
    Analyzer.cs
    AnalyzerBuilder.cs
    AnalysisContext.cs
    AnalysisResult.cs
    NodeMetadataStore.cs
    NodeReplacementMetadata.cs
    INodeAnalyzer.cs
    IAnalysisMetadata.cs
    Diagnostic.cs
    ...                         # remainder of analysis substrate
    README.md
    # NOT semantic passes (those stay under Interpretation/Analysis/)

  Interpretation/
    Analysis/                   # TypeAndMemberResolution, CFG, ConstantFolding, …
    Vm/
    ...

  DomainModeling/               # DE → Poly.Ast only for pure lowering
  Introspection/                # unchanged
```

### 2.2 Dependencies (canonical)

```text
Poly.Ast
   ▲
   │
Poly.Analysis ──────────────────► Poly.Ast
   ▲                    ▲
   │                    │
Poly.Interpretation ────┴───────► Poly.Ast, Poly.Introspection
   ▲
Poly.DomainModeling ────────────► Poly.Ast
   │                              (+ Interpretation only for PolicyEvaluator bridge — unchanged policy)
Poly.Validation / Poly.Mcp      ► Ast / Analysis as needed today
```

### 2.3 Assembly strategy (choose at resume time)

| Option | When to use |
|--------|-------------|
| **A. Namespace + folder only** (single `Poly` project) | Default. Lowest risk; matches current multi-root under one csproj. |
| **B. Separate projects** (`Poly.Ast`, `Poly.Analysis`, …) | Only if there is a real consumer need (shipping IR without VM, stricter compile-time boundaries). Defer unless justified. |

**Recommendation at first execution:** Option A. Revisit B as a later plan.

### 2.4 Naming map

| Old | New |
|-----|-----|
| `Poly.Syntax` | `Poly.Ast` (root types: `Node`, `NodeId`, extensions) |
| `Poly.Syntax.Nodes` | `Poly.Ast.Nodes` |
| `Poly.Syntax.Analysis` | `Poly.Analysis` |
| `Poly/Syntax/` | `Poly/Ast/` |
| `Poly/Syntax/Analysis/` | `Poly/Analysis/` |
| Docs / CORE “Syntax” as IR | “Ast” / `Poly.Ast` |
| Docs “analysis framework in Syntax” | `Poly.Analysis` |
| `Interpretation/Analysis` (passes) | **Keep path**; clarify in README: “semantic passes, not framework” |

Global usings (`Poly/GlobalUsings.cs`) and test aliases update accordingly.

---

## 3. Preconditions (do not start until true)

Resume this plan only when **all** of the following hold:

1. **Working tree intentional** — no half-finished experiments mixed with product paths (revert or finish per CORE: extend via analysis + node replacement, not emitter/ABI forks).  
2. **V2→V3 / WS8 critical path stable** — policy subject invariants and MCP honesty not mid-flight in a broken state; build + DomainModeling/Interpretation tests green.  
3. **Owner bandwidth** — this is a wide mechanical rename; do not interleave with large semantic features.  
4. **CORE still accurate for current tree** — update CORE *as part of* this migration, not weeks later.  
5. **Optional ADR** — short decision record under `docs/decisions/` when execution starts (name split + non-goal: nodes stay out of Interpretation).

**Blocked by (today):** in-progress DomainModeling / WS8 / uncommitted Interpretation experiments. Prefer fixing product path first.

---

## 4. Migration phases

Execute in order. Each phase should leave **build + tests green**.

### Phase 0 — Inventory and freeze

- [ ] Enumerate all `Poly.Syntax` / `Poly.Syntax.Nodes` / `Poly.Syntax.Analysis` references (code, tests, MCP, benchmarks, docs, global usings).  
- [ ] List public types that external consumers might use (even if only in-repo today).  
- [ ] Freeze unrelated refactors on `Syntax/` and `Syntax/Analysis/` during the migration window.  
- [ ] Confirm Option A vs B (default A).

### Phase 1 — Physical move + namespace rename (Ast)

- [ ] Create `Poly/Ast/` and `Poly/Ast/Nodes/`.  
- [ ] Move node-related types from `Poly/Syntax/` (everything **except** `Syntax/Analysis/`).  
- [ ] Namespace: `Poly.Ast`, `Poly.Ast.Nodes`.  
- [ ] Update `GlobalUsings` and project compile items if any are explicit.  
- [ ] Temporary type-forwarding **not** required if monorepo-only; if desired for soft landing, optional `Obsolete` aliases in empty `Poly.Syntax` stub for one PR — **prefer single-shot rename** if all callers are in-repo.

### Phase 2 — Physical move + namespace rename (Analysis framework)

- [ ] Create `Poly/Analysis/`.  
- [ ] Move `Poly/Syntax/Analysis/*` → `Poly/Analysis/`.  
- [ ] Namespace: `Poly.Analysis` (drop the extra `.Syntax` segment).  
- [ ] Ensure framework still references `Poly.Ast` only for nodes (no Interpretation dependency).  
- [ ] Delete empty `Poly/Syntax/` when empty.

### Phase 3 — Fix all callers

- [ ] `Poly/Interpretation/**` usings and fully-qualified names.  
- [ ] `Poly/DomainModeling/**` (esp. lowering, any analysis bridge).  
- [ ] `Poly/Validation/**`, `Poly.Mcp/**`, `Poly.Tests/**`, `Poly.Benchmarks/**`.  
- [ ] `using SN = …` aliases and file-local aliases in tests.  
- [ ] Any `nameof` / diagnostic strings that embed old namespace (rare).

### Phase 4 — Disambiguate “Analysis” in docs and READMEs

- [ ] `Poly/Analysis/README.md` — **framework only**; link Interpretation for semantic passes.  
- [ ] `Poly/Interpretation/Analysis/README.md` — “semantic passes on `Poly.Analysis` substrate.”  
- [ ] `Poly/DomainModeling/Analysis/` — domain-model analyzers (different domain); one-line note so agents do not merge the three “Analysis” folders.  
- [ ] Root `README.md` samples: build AST via `Poly.Ast`.

### Phase 5 — CORE / AGENTS / decisions

- [ ] Update [`docs/CORE.md`](../CORE.md): pipeline diagram, §2 table, §3.1–3.2 paths, stop-inventing table, doc map.  
- [ ] Update `AGENTS.md` placement rules and any `Syntax/` paths.  
- [ ] Module READMEs under Ast, Analysis, Interpretation, DomainModeling, Introspection.  
- [ ] Add or update ADR: **Ast + Analysis module split** (status Accepted when done).  
- [ ] Grep docs for `Poly.Syntax` / `Poly/Syntax` and fix live docs; leave archive/historical docs with a one-line “historical name” note only where needed.

### Phase 6 — Verification

- [ ] `dotnet build` (benchmarks or main solution entry used by CI).  
- [ ] Full `Poly.Tests` green.  
- [ ] Smoke: DomainExpression lower → analyze → `Interpreter.Compile` → execute.  
- [ ] Smoke: constant folding still registers node replacement; emitter honors it.  
- [ ] No remaining `Poly.Syntax` references in non-archive sources (or only intentional obsolete shims).

### Phase 7 — Close out

- [ ] Mark this plan **Done** with date.  
- [ ] Index in `docs/plans/README.md` as completed / historical.  
- [ ] Do **not** start separate-assembly split (Option B) in the same PR train unless preconditions for B are written.

---

## 5. What does *not* move

| Keep | Reason |
|------|--------|
| `Interpretation/Analysis/*` semantic passes | Pass *content*; depends on framework + Ast + Introspection |
| `DomainModeling/Analysis/*` | Domain-model graph analyzers; different subject (`Domain`, not `Node`) |
| `DirectVmAbiEmitter`, VM, `Interpreter` | Execution stack |
| Introspection CLR/providers | Type host model |
| Node replacement **behavior** | Only **location** changes (`Poly.Analysis`); contract unchanged |

---

## 6. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Wide merge conflicts with parallel work | Preconditions: freeze Syntax; land when tree is quiet |
| Agents keep writing `Poly.Syntax` | CORE + AGENTS + stub obsolete types for one release if needed |
| Confusion: three “Analysis” directories | README banners; CORE table row for each |
| Accidental behavior change | Pure move/rename PRs; no logic edits in same commits |
| Over-scoping into multi-project split | Option A only unless new plan |

---

## 7. Suggested PR shape (when executing)

1. **PR1:** Move to `Poly.Ast` + fix compile (namespaces).  
2. **PR2:** Move framework to `Poly.Analysis` + fix compile.  
3. **PR3:** Docs/CORE/AGENTS/ADR only.  

Or one PR if the team prefers atomic rename and CI is fast enough — still keep commits separable for revert.

---

## 8. Success criteria

- [ ] No product code under `Poly/Syntax/`.  
- [ ] `Poly.Ast` = nodes only; `Poly.Analysis` = analysis framework only.  
- [ ] DomainModeling pure lowering depends on Ast, not on Interpretation types for node construction.  
- [ ] CORE describes Ast + Analysis; node replacement still documented as the rewrite mechanism.  
- [ ] Build and tests green; no intentional behavior change.

---

## 9. Out of scope (future plans, not this one)

- Multi-assembly packaging (Option B).  
- Moving semantic passes out of Interpretation.  
- Changing node replacement API or analysis pass order.  
- Product features that only need desugar/replace or ABI changes — separate work; follow CORE principles when implementing.  
- Renaming `DomainModeling/Analysis`.  
- Parser / concrete syntax front-end (if ever added, it would *produce* `Poly.Ast`, not replace it).

---

## 10. Resume checklist (copy when unblocking)

```text
[ ] WIP / WS8 / mess cleaned; tests green
[ ] No concurrent large features on Syntax/Interpretation analysis
[ ] Confirmed Option A (folder+namespace)
[ ] Phase 0 inventory done
[ ] Execute Phases 1–7
[ ] ADR + CORE updated same change set as rename
[ ] Mark this plan Done
```
