# Proposal: Decompose `Poly.DomainModeling`

**Status:** Draft — 2026-07-26; **agent queue ready** 2026-08-06  
**Author:** Agent (post-rewrite assessment)  
**Type:** Structural decomposition (behavior-preserving)  
**Execution queue (not CURRENT):** [`simple-agent-tasks/coh-README.md`](simple-agent-tasks/coh-README.md) — Runtime/ + dispatch + evolution helpers (no multi-assembly)  
**Related:**
- [`poly-ast-analysis-module-split.md`](poly-ast-analysis-module-split.md) (established pattern)
- [`../CORE.md`](../CORE.md) — platform ownership table
- [`../AGENTS.md`](../../AGENTS.md) — placement rules
- [`domainmodeling-cohesion-and-metadata-findings.md`](domainmodeling-cohesion-and-metadata-findings.md)

---

## 1. Current state

`Poly/DomainModeling/` is a **16.5K-line monolith** — 38% of the entire `Poly/` core library. It spans nine distinct concerns under one roof:

| Concern | Directory/File | Lines | % of DM |
|---------|---------------|-------|---------|
| **Domain-specific analysis** | `Analysis/` (35 files) | 5,258 | 32% |
| **DE→AST + C# export** | `Lowering/` (14 files) | 2,828 | 17% |
| **DSL text format** | `Parsing/` (3 files) | 2,337 | 14% |
| **Core type records** | Root files (~28 files) | 2,320 | 14% |
| **Evolution pipeline** | `Evolution/` (7 files) | 1,970 | 12% |
| **Fluent builders** | `Builders/` (8 files) | 575 | 3% |
| **Bootstrap / factory** | `Bootstrap/` (3 files) | 360 | 2% |
| **Query helpers** | `Queries/` (1 file) | 289 | 2% |
| **Effect records** | `Effects/` (11 files) | 143 | 1% |
| **Constraint records** | `Constraints/` (8 files) | 33 | 0.2% |
| **Examples** | `Examples/` (3 files) | 447 | 3% |

### Current coupling

- `DomainModeling` → `Poly.Ast` (pure lowering; no Interpretation dependency for node construction)
- `DomainModeling` → `Poly.Interpretation` (policy evaluation bridge — `PolicyEvaluator` calls `Interpreter`)
- `DomainModeling.Analysis` tightly coupled to `DomainModeling` core types (Domain, Entity, etc.)
- `DomainModeling.Lowering` depends on `DomainModeling` core types + `DomainModeling.Analysis` metadata
- `DomainModeling` root files have no sub-module boundary — `DomainEntityInstance` (1,104 lines) sits next to `PrimitiveType.cs` (10 lines)

---

## 2. Goals

1. **Reduce cognitive surface** — a developer or agent should not need to understand all 16.5K lines to work in one area.
2. **Enforce module boundaries** — lowering should not accidentally import analysis; analysis should not import evolution.
3. **Follow Ast/Analysis pattern** — the module split proved the value of named, bounded directories with README banners.
4. **Enable parallel work** — analysis pass authors should not wait for evolution changes (or vice versa).
5. **Prepare for multi-assembly** — if a consumer (e.g., `src/Poly.DslCompiler/`) needs only the DSL parser + core types, a tight dependency on the full monolith is wasteful.

### Non-goals

- Changing any API, type name, or behavior.
- Moving semantic passes (`Interpretation/Analysis/`) — those belong to Interpretation.
- Creating new abstractions or frameworks.
- Resurrecting V2 patterns.
- Splitting `Poly.csproj` into multiple assemblies **unless** a concrete consumer justifies it.

---

## 3. Option A: Tiered folders (recommended)

Follow the Ast/Analysis pattern: **keep a single project, split into tiered sub-namespaces + folders.** This is the lowest-risk path.

### Proposed layout

```
Poly/
  DomainModeling/
    README.md

    # Tier 1 — Core types (no internal sub-module dependencies)
    Domain.cs
    Entity.cs
    Action.cs
    Stage.cs
    Policy.cs
    Relationship.cs
    Property.cs
    Effect.cs           # + EffectDispatch.cs
    EffectBase.cs       # common base / marker for effect records
    Constraint.cs
    DomainExpression.cs # + DomainExpressionDispatch.cs
    DomainType.cs
    PrimitiveType.cs
    EnumType.cs
    ValueType.cs
    Facet.cs
    Annotation.cs
    AnnotationRegistry.cs
    AnnotationValue.cs
    IAnnotationSyntax.cs
    SqlAnnotationSyntax.cs
    DomainMember.cs
    DomainObject.cs
    ContractBinding.cs
    ContractEndpoint.cs
    ContractEndpointKind.cs
    ContractFieldMap.cs
    ImportedContract.cs
    InvocationResult.cs
    SubscriptionEventAccess.cs

    # Tier 2 — Domain operation groups (depend on Tier 1)
    Effects/            # ← existing, ~140 lines — keep as-is
    Constraints/        # ← existing, ~33 lines — keep as-is
    Bootstrap/          # ← existing, ~360 lines — keep as-is
    Queries/            # ← existing, ~290 lines — keep as-is

    # Tier 3 — Builders (fluent construction, depend on Tier 1–2)
    Builders/           # ← existing, ~575 lines — keep as-is

    # Tier 4 — Evolution (depends on Tier 1 + Builders)
    Evolution/          # ← existing, ~1,970 lines — keep as-is

    # Tier 5 — Domain-specific analysis (depends on Tier 1 + Poly.Analysis framework)
    Analysis/           # ← existing, ~5,260 lines — keep as-is

    # Tier 6 — Lowering (depends on Tier 1, Tier 5, Poly.Ast, Poly.Interpretation)
    Lowering/           # ← existing, ~2,830 lines — keep as-is

  # NEW: Dsl/
    Parsing/            # ← moved from DomainModeling/Parsing/, ~2,340 lines
    README.md           # "Poly DSL text format — tokenizer, parser, printer"
```

### Key move: extract `Parsing/` to `Poly/Dsl/`

The DSL parser, tokenizer, and printer (`PolyDslParser.cs`, `PolyDslTokenizer.cs`, `DomainDslPrinter.cs`) are the **only** sub-module that:
- Has zero dependency on domain analysis or lowering
- Could logically stand alone as a format library
- Is currently ~2,340 lines of text-processing code that inflates the DomainModeling surface
- Mirrors the `src/Poly.DslCompiler/` consumer (which generates code from `.poly` DSL output)

Moving `Parsing/` into `Poly/Dsl/` makes it discoverable as a standalone text-format module and keeps DomainModeling focused on model semantics.

### Effect on DomainModeling

After the move: **~14,100 lines remain** (down from 16,400). The monolith is still large but now has explicit tier structure:

```
Tier 1 (core types)      ~2,000 lines   — no sub-module deps
Tier 2 (ops: Effects/,
         Constraints/,
         Bootstrap/,
         Queries/)          ~830 lines   — depend only on core types
Tier 3 (Builders/)          ~580 lines   — depend on core + ops
Tier 4 (Evolution/)       ~1,970 lines   — depend on core + builders
Tier 5 (Analysis/)        ~5,260 lines   — depend on core + Poly.Analysis
Tier 6 (Lowering/)        ~2,830 lines   — depend on core + analysis + Poly.Ast/Interpretation
```

### Migration phases (Option A)

| Phase | Action | Lines moved |
|-------|--------|-------------|
| 1 | Move `Parsing/` → `Poly/Dsl/`. Fix namespaces and usings. | 2,340 |
| 2 | Add README banners to each tier directory. | 0 |
| 3 | Add tier markers to `CORE.md` ownership table. Update `AGENTS.md` placement rules. | 0 |
| 4 | Verify build + tests green. No behavior change. | 0 |

### Preconditions

- Working tree intentional (no mid-flight experiments)
- Build + DomainModeling + Interpretation tests green
- Owner bandwidth — this is a mechanical move; do not interleave with large semantic features

---

## 4. Option B: Multi-project split (future)

If a concrete consumer (e.g., `src/Poly.DslCompiler/`, a NuGet package, an external tool) needs a subset of DomainModeling without the full assembly, split into separate `.csproj` files:

```
Poly.DomainModeling/          → core types + builders + effects + constraints
Poly.DomainModeling.Evolution/ → evolution pipeline
Poly.DomainModeling.Analysis/  → domain analyzers
Poly.DomainModeling.Lowering/  → DE→AST + C# export
Poly.Dsl/                      → DSL parser/printer
```

This mirrors the Ast/Analysis plan's Option B. **Do not pursue until a real consumer forces it** — the tiered-folder approach (Option A) satisfies all current needs at lower cost.

### When to revisit

- An external tool wants to parse `.poly` files without referencing `Poly.dll`
- The `Poly` assembly exceeds a team-agreed size threshold (e.g., >50K lines)
- Build time becomes a measurable friction point
- A second consumer of domain model types emerges outside the repository

---

## 5. Option C: Incremental extraction (alternative path)

Instead of a planned multi-phase migration, extract pieces as they become natural:

1. **Extract `Parsing/` → `Poly/Dsl/` first** (lowest risk, highest autonomy)
2. **Extract `DomainEntityInstance` + `DomainInstanceStore` → `Poly/DomainModeling/Runtime/`** (these are runtime execution concerns, not model definition)
3. **Then evaluate**: does the tiered structure provide enough clarity, or is a multi-project split needed?

This is the most conservative approach and aligns with the "shipped capability over completeness" principle.

---

## 6. Dependency analysis (critical)

Before any move, verify dependency direction:

```
Poly.Ast
    ↑
Poly.Analysis
    ↑        ↑
Poly.Interpretation  ←── Poly.DomainModeling.Lowering
                              ↑
                    Poly.DomainModeling.Analysis
                              ↑
                    Poly.DomainModeling (core + evolution)
                              ↑
                    Poly.Dsl (Parsing/)  ← standalone, no DM dependency
```

**Key invariant:** `Poly.Dsl/` (former `Parsing/`) must **not** depend on `Poly.DomainModeling`. The tokenizer and parser should produce `Domain` records or an intermediate representation that `DomainModeling` consumes, not the other way around. Verify this before moving.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|------|------------|
| `Parsing/` has hidden dependency on DomainModeling types | Audit `import`/`using` before moving; break the cycle first if found |
| Tier confusion: agents put new code in wrong tier | README banners per tier; CORE table row for each |
| Merge conflicts with parallel evolution work | Freeze DomainModeling changes during migration window |
| Over-scoping into multi-project split | Explicit non-goal; Option A only unless preconditions met |
| Lost history on moved files | Use `git mv` / `git log --follow` |

---

## 8. Success criteria

- [ ] `Poly/DomainModeling/Parsing/` no longer exists (moved to `Poly/Dsl/`)
- [ ] Each sub-directory has a `README.md` with tier number and allowed dependencies
- [ ] `CORE.md` ownership table lists each DomainModeling tier separately
- [ ] `AGENTS.md` placement rules reference tiers where appropriate
- [ ] Build and tests green
- [ ] No behavior change — all 1,637+ tests pass

---

## 9. Recommendation

**Option A (tiered folders) + extract `Parsing/` → `Poly/Dsl/`** is the right next step. It follows the established Ast/Analysis pattern, reduces the DomainModeling surface by ~2,300 lines, and enforces clearer boundaries without the overhead of multi-project packaging.

Start with Phase 1 (`Parsing/` move), then add README banners. Evaluate multi-project split (Option B) only when a concrete consumer outside `Poly/` needs it.

---

## 10. Appendix: Line-count breakdown by concern within core types

Exact line counts for root files (to illustrate the range):

| File | Lines | Concern |
|------|-------|---------|
| `DomainEntityInstance.cs` | 1,104 | Runtime instance model |
| `DomainExpression.cs` | 274 | Expression model core |
| `DomainInstanceStore.cs` | 206 | Instance storage |
| `DomainExpressionDispatch.cs` | 66 | Visitor pattern |
| `SqlAnnotationSyntax.cs` | 64 | SQL annotations |
| `StageSubscription.cs` | 52 | Stage subscriptions |
| `EffectDispatch.cs` | 50 | Effect visitor |
| `DomainAuthoringContext.cs` | 50 | Authoring context |
| `AnnotationRegistry.cs` | 48 | Annotation registry |
| `Annotation.cs` | 41 | Annotation type |
| `Relationship.cs` | 30 | Relationship record |
| `Entity.cs` | 30 | Entity record |
| `PassRegistry.cs` | 28 | Pass registration |
| `Stage.cs` | 26 | Stage record |
| `AnnotationValue.cs` | 23 | Annotation value |
| `SubscriptionEventAccess.cs` | 20 | Event access flags |
| `DomainType.cs` | 20 | Domain type base |
| `Domain.cs` | 20 | Domain root record |
| `IAnnotationSyntax.cs` | 18 | Annotation syntax interface |
| `ContractEndpointKind.cs` | 16 | Endpoint kind enum |
| `Action.cs` | 16 | Action record |
| `Property.cs` | 15 | Property record |
| `InvocationResult.cs` | 14 | Invocation result |
| `Policy.cs` | 13 | Policy record |
| `Facet.cs` | 13 | Facet record |
| `Effect.cs` | 13 | Effect base record |
| `EnumType.cs` | 12 | Enum type record |
| `ValueType.cs` | 11 | Value type record |
| `ImportedContract.cs` | 11 | Imported contract |
| `PrimitiveType.cs` | 10 | Primitive type record |

The three runtime-oriented files (`DomainEntityInstance`, `DomainInstanceStore`, `InvocationResult`) account for **1,324 lines** — 57% of the root file line count. A future refinement may extract these into `Runtime/` as a Tier 2 sub-directory.
