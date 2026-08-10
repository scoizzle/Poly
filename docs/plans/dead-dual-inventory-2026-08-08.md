# Dead dual inventory — Validation, Text, second evaluators

**Date:** 2026-08-08  
**Kind:** Inventory only (no deletes in this change).  
**Method:** repo-wide `rg` for product/test/benchmark callers; SDK-style include (all `Poly/**/*.cs` compile into `Poly.dll`).  
**Status truth:** [`simple-agent-tasks/PIPELINE-STATUS.md`](simple-agent-tasks/PIPELINE-STATUS.md) — this is **PULL**, not CURRENT.

---

## Verdict summary

| Module / path | ~LOC (`.cs`) | Live product callers | Active tests | Recommendation |
|---------------|-------------:|----------------------|--------------|----------------|
| **`Poly/Validation/`** | ~501 | **None** outside itself | **None** (all test files fully commented out) | **Delete candidate** (or quarantine + un-place from AGENTS) |
| **`Poly/Text/`** (all) | ~2993 | **None** in DomainModeling / Grammar / Interpretation / Mcp / Ast | **None** in Poly.Tests | **Split:** Matching ≈ dead dual of Grammar; StringView/Parsers need keep/move decision |
| **`Poly/Text/Matching/`** | (subset of Text) | None | Benchmarks only | **Delete / extract** — superseded by `Poly/Grammar` for product media. **Not** required to rebuild in [`grammar-revision.md`](grammar-revision.md) tier A (rebuild optional later / greedy-only; `*abc` needs Capture) |
| **LINQ path** (`LinqExpressionGenerator` + `PolicyEvaluator.CompileLinq*`) | large | Product path is **VM**; LINQ is oracle + Validation | Yes (parity / correctness) | **Keep** as secondary oracle until deliberate retirement |
| **`DomainExpressionDispatch` / rewrite** | small | Live (printer, lower, runtime quantifier rewrite) | Yes | **Keep** — not a second evaluator |
| **`CSharpGenerator` / Mermaid** | generators | Export / tooling / tests | Yes | **Keep** — not evaluators of domain policy |

---

## 1. `Poly.Validation` — dead product dual

### What it is

A **third constraint language**:

- `Rule` / `RuleSet<T>` compose rules  
- `BuildInterpretationTree` → **Ast** nodes  
- Analyze → **`LinqExpressionGenerator`** → `Predicate<T>`  

Parallel to:

| Concern | Product home today |
|---------|-------------------|
| Property constraints on domain | `DomainModeling.Constraints` + domain analysis |
| Policy guards | `DomainExpression` → lower → **VM** |
| Effect/policy lint | `EffectAnalyzer`, `PolicyConstraintAnalyzer`, … |

### Evidence

```text
# Active using Poly.Validation outside Validation itself: none
# Product new RuleSet / RuleSet<: none (only commented Benchmarks)
# Poly.Tests/Validation/* : entire files commented out (// using … // namespace …)
```

Benchmarks reference Validation only in **commented** blocks (`Poly.Benchmarks/Program.cs`).

### Placement debt

`AGENTS.md` still says:

> Validation rules → `Poly/Validation/Rules/` (register subtypes on `Rule.cs`)

That **teaches agents to grow a dead module**. CORE lists Validation as a first-class concern.

### Kill list (when admitted as a cleanup suite)

1. Delete `Poly/Validation/**` (~12 `.cs`, ~501 LOC).  
2. Delete or remove commented `Poly.Tests/Validation/**`.  
3. Strip commented Validation samples from benchmarks (optional hygiene).  
4. Update `AGENTS.md` Placement + `docs/CORE.md` Separation table (remove or mark **dormant / removed**).  
5. Confirm build + suite (no product test depends on Validation today).

**Do not** re-home “validation rules” into this folder without a second real consumer — domain constraints already live under DomainModeling.

---

## 2. `Poly.Text` — historical text stack vs Grammar

### What it is

| Subfolder | Role |
|-----------|------|
| `StringView/` | Low-allocation string views |
| `Matching/` | Pattern expression / parser / linker (string-level) |
| `Parsers/` | Numeric parse helpers |
| `Extensions/` | Text helpers |

`Poly/Text/README.md` presents Matching as the pattern engine example. **Product `.poly` uses `Poly.Grammar` + `DslTokenReader`**, not Text.Matching.

### Evidence

```text
# Poly.Text / StringView / Text.Matching in DomainModeling, Grammar,
# Interpretation, Ast, Poly.Mcp product code: zero hits
# Poly.Tests: zero hits
# Callers: Poly/Text itself + Poly.Benchmarks/String/*
```

### Recommendation (split, don’t nuke blindly)

| Piece | Action |
|-------|--------|
| **`Text/Matching`** | **Kill or extract** — genuine dual of Grammar’s pattern idea at char/string level; no product consumer |
| **`StringView` / `Parsers` / `Extensions`** | Inventory again before delete: may be useful substrate; today **unused by product**. Options: (a) keep as dormant utility, (b) move to a non-core package later, (c) delete if unused after Matching removal |
| **Benchmarks** under `Poly.Benchmarks/String/` | Drop with Matching, or keep only if StringView retained |

Historical plan notes already flagged Text for kill/fix (`docs/plans/archive/…`).

---

## 3. Second evaluators (policy / program)

### 3.1 Canonical product path — **keep**

```text
DomainExpression → DomainExpressionLoweringPass → Ast
  → Interpreter.Analyze / Compile → VM execute
```

Used by:

- `PolicyEvaluator.Evaluate` / `CompileVMPredicate` (VM-primary)  
- `DomainEntityInstance.EvaluatePolicy` (VM + quantifier preprocess rewrite)  
- MCP `simulate_policy` / `evaluate_policy`

### 3.2 LINQ oracle — **keep (not dead)**

| API | Role |
|-----|------|
| `LinqExpressionGenerator` | Secondary program backend |
| `PolicyEvaluator.CompileLinqPredicate` / `EvaluateWithDualOracle` | Explicit dual-oracle |

**Callers:** tests (`VmCorrectnessTests`, `LinqExpressionGeneratorTests`, dual-oracle domain tests), `NodeTestHelpers`, **and** `Validation.RuleSet` (only product-ish consumer of LINQ outside tests).

**First principle:** VM is canonical (ADR). LINQ stays until parity suite is retired on purpose.  
**After Validation delete:** LINQ’s only non-test product call site is PolicyEvaluator’s secondary APIs — still fine for oracle.

### 3.3 DomainExpression rewrite — **keep (not an evaluator)**

`DomainExpressionDispatch` / `DomainExpressionRewriteBase`:

- Lowering visitor  
- Printer (`DomainDslPrinter.ExpressionPrinter`)  
- Runtime quantifier / path preprocess on instances  

These **transform** DomainExpression; they do not define a second execution semantics for policies.

### 3.4 Codegen / viz — **keep**

| Backend | Role |
|---------|------|
| `CSharpGenerator` | Export / DslCompiler / MCP export |
| `MermaidAstGenerator` | Tests + optional viz |
| `DomainToCSharpExporter` | Domain → C# surface |

Not competing policy evaluators.

### 3.5 Already dead (confirm stay dead)

| Item | Status |
|------|--------|
| Tree-walker interpreter | Removed (ADR VM canonical) |
| Primitive IR / `ToPrimitives` | Removed (ADR superseded) |
| `DomainExpressionJsonParser` | Deleted mcp-minify 2026-08-08 |
| Per-type MCP `add_entity` tools | Deleted mcp-minify |

---

## 4. What this inventory is *not*

- **Not** a merge of DomainExpression and Ast (intentional layers).  
- **Not** a merge of domain analysis and program analysis (different objects).  
- **Not** permission to delete LINQ without a parity replacement.  
- **Not** CURRENT work — do not admit a “delete Validation” suite while mut-safety is the admit-next product stream **unless** human prioritizes cleanup.

---

## 5. Suggested future suite (parked)

**Name sketch:** `dead-dual` or fold into idle naming cleanup.

| Task | Scope |
|------|--------|
| 0 | Re-verify greps green before delete |
| 1 | Delete Validation + commented tests + AGENTS/CORE rows |
| 2 | Delete or extract Text.Matching + matching benchmarks |
| 3 | Decide StringView/Parsers keep vs delete; document in CORE |
| G | Build + full suite; no resurrect of JSON expr / tree-walker |

**Prefer after mut-safety** (or idle green tree), not parallel with product MCP work.

---

## 6. Grep cheat sheet (re-run before kill)

```bash
# Validation must stay empty outside Poly/Validation
rg -n "using Poly\.Validation|new RuleSet" --glob '*.cs' -g '!Poly/Validation/**' -g '!**/obj/**'

# Text must stay empty outside Poly/Text (+ optional benchmarks)
rg -n "Poly\.Text|StringView" --glob '*.cs' \
  -g '!Poly/Text/**' -g '!Poly.Benchmarks/**' -g '!**/obj/**'

# Product policy path still VM
rg -n "CompileVMPredicate|EvaluateWithDualOracle" Poly --glob '*.cs'
```
