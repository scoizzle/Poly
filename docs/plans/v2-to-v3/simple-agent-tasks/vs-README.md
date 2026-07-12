# Vertical Slice Micro-Tasks (Simple Agents)

**Parent plan:** [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md)  
**Last Updated:** 2026-07-12  
**Audience:** Smaller / cheaper agents — one file per claim, tiny reading list.

## Operating rules (mandatory)

1. **One task at a time.** Claim it (Status → In Progress) before coding.
2. **Pick the first `[ ] Not Started` in the ordered table below.** Do not skip ahead unless the task says “parallel OK.”
3. **Do not start Slice 2** until Slice 0 tasks marked **blocks Slice 2** are Done.
4. **Do not start Slice 3** until Slice 2 is Done.
5. **Do not pick Slice 4/5** unless an orchestrator reopens them (deferred).
6. After Done: write `../agent-summaries/vs-<task-id>-summary.md` using [`TEMPLATE-task-summary.md`](../agent-summaries/TEMPLATE-task-summary.md). Update only the Status line on the task file.
7. Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`  
   Tests: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (or filter to tests you added).
8. Principles: AGENTS.md — domain fidelity, thin slice, no domain VM opcodes, MCP honesty.

### Status marks

| Mark | Meaning |
|------|---------|
| `[ ]` | Not Started — pickable when previous required tasks Done |
| `[~]` | In Progress |
| `[x]` | Done |
| **Skip** | Do not execute (deferred / pull-only) |

---

## Pick order (do in sequence)

### Slice 0 — Honesty foundation

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **0.1** | Fail-loud evolution (core) | [`vs-s0-fail-loud-evolution.md`](vs-s0-fail-loud-evolution.md) | **[x]** | RequireUpdate + rollback |
| **0.1a** | Surface evalErrors in diagnostics | [`vs-s0-fail-loud-surface-eval-errors.md`](vs-s0-fail-loud-surface-eval-errors.md) | **[x]** | `EVOLUTION_TARGET` inject |
| **0.1b** | Fail-loud missing stage/property (child) | [`vs-s0-fail-loud-child-targets.md`](vs-s0-fail-loud-child-targets.md) | **[x]** | Child existence check + tests |
| **0.1c** | RequireUpdate on remaining ApplyTo | [`vs-s0-fail-loud-remaining-applyto.md`](vs-s0-fail-loud-remaining-applyto.md) | **[x]** | All Update* ApplyTo wrapped |
| **0.1d** | Fail-loud remove-by-name zero match *(optional)* | [`vs-s0-fail-loud-remove-zero-match.md`](vs-s0-fail-loud-remove-zero-match.md) | **[ ]** | Remove property/stage/action when name missing still succeeds if parent exists |
| **0.2** | `add_action_to_stage` honesty | [`vs-s0-add-action-to-stage-honesty.md`](vs-s0-add-action-to-stage-honesty.md) | **[x]** | Description + create-semantics test |
| **0.2a** | MCP README row for stage action *(nit)* | [`vs-s0-mcp-readme-add-action-to-stage.md`](vs-s0-mcp-readme-add-action-to-stage.md) | **[ ]** | README still “Places an existing action” |
| **0.3** | Wire PolicySubject fully | [`vs-s0-wire-policy-subject-validate.md`](vs-s0-wire-policy-subject-validate.md) | **[x]** | Validate + ValidateType |
| **0.4** | Fix instance EmitInvoke (receiver in Block) | [`vs-s0-fix-emit-invoke-instance.md`](vs-s0-fix-emit-invoke-instance.md) | **[x]** | instanceExpr + dual-oracle |
| **0.5** | MCP README V3-only | [`vs-s0-mcp-readme-honesty.md`](vs-s0-mcp-readme-honesty.md) | **[x]** | Done |

**Slice 0 required:** ✅ **Done.** Optional: **0.1d**, **0.2a** (do not block Slice 1).

### Slice 1 — Structure path (verify + pin) ✅ Done

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **1.1** | Verify structure e2e coverage | [`vs-s1-verify-structure-path.md`](vs-s1-verify-structure-path.md) | **[x]** | Inventory + GetDomainAnalysis smoke |
| **1.2** | Pin canonical entity (Person **or** Order) | [`vs-s1-pin-canonical-entity.md`](vs-s1-pin-canonical-entity.md) | **[x]** | **Person** — simplest numeric (Age), most test coverage |

### Slice 2 — Policy runtime (direct API only) ← **active**

| # | Task | File | Depends |
|---|------|------|---------|
| **2.1** | Subject helper + reject Dict/Expando at evaluate | [`vs-s2-subject-helper-and-reject.md`](vs-s2-subject-helper-and-reject.md) | **0.3** |
| **2.2** | Bool ABI adult assert | [`vs-s2-bool-abi-adult-assert.md`](vs-s2-bool-abi-adult-assert.md) | 2.1 or parallel after 0.3 |
| **2.3** | Age/numeric policy true **and** false e2e | [`vs-s2-policy-true-false-e2e.md`](vs-s2-policy-true-false-e2e.md) | 2.1, 2.2 |
| **2.4** | Property name alignment test | [`vs-s2-property-name-alignment.md`](vs-s2-property-name-alignment.md) | 2.1 |
| **2.5** | Domain-attached policy e2e on **canonical** entity | [`vs-s2-domain-attached-policy-e2e.md`](vs-s2-domain-attached-policy-e2e.md) | 1.2, 2.3 |

**Slice 2 done when:** 2.1–2.5 `[x]`. No MCP tools in this slice.

### Slice 3 — Policy MCP product loop

| # | Task | File | Depends |
|---|------|------|---------|
| **3.1** | Constrained expression contract for add_policy | [`vs-s3-add-policy-expression-contract.md`](vs-s3-add-policy-expression-contract.md) | Slice 2 |
| **3.2** | `add_policy` MCP tool | [`vs-s3-add-policy-tool.md`](vs-s3-add-policy-tool.md) | 3.1 |
| **3.3** | `evaluate_policy` MCP tool (VM bool) | [`vs-s3-evaluate-policy-tool.md`](vs-s3-evaluate-policy-tool.md) | 3.2, Slice 2 |
| **3.4** | MCP e2e smoke structure + policy + eval | [`vs-s3-mcp-policy-e2e-smoke.md`](vs-s3-mcp-policy-e2e-smoke.md) | 3.2, 3.3 |
| **3.5** | Polish affordances + MCP README policy section | [`vs-s3-policy-mcp-polish.md`](vs-s3-policy-mcp-polish.md) | 3.4 |

**Slice 3 done when:** 3.1–3.5 `[x]` → run checkpoint:

| # | Task | File |
|---|------|------|
| **M2** | Mark M2 product-complete in plans | [`vs-checkpoint-m2-close.md`](vs-checkpoint-m2-close.md) |

### Deferred (do not pick)

| Slice | Why |
|-------|-----|
| 4 First effect | After M2; orchestrator only |
| 5 Relationship | Pull-only |

---

## Map from older ws8-* tasks

If both exist, **prefer `vs-*`** (this suite owns order). Older files are specs/history:

| vs task | Related ws8 (optional reading) |
|---------|--------------------------------|
| 0.3, 2.1 | `ws8-invariant-policy-subject-types.md`, `ws8-invariant-no-dict-expando-subjects.md` |
| 2.2 | `ws8-spike-bool-abi-adult-assert.md` |
| 2.3 | `ws8-spike-matchnumeric-positive-control.md` |
| 2.4 | `ws8-invariant-policy-property-name-alignment.md` |
| 3.1–3.5 | `ws8-mcp-add-policy*.md`, `ws8-mcp-evaluate-policy-vm.md`, etc. |

---

## Canonical entity

**Person** — the canonical vertical-slice entity for all remaining policy work.

### Rationale

| Factor | Person | Order |
|--------|--------|-------|
| Policy property simplicity | `Age` (int) — simplest numeric guard | `Total` (decimal), `Status` (string) — more complex |
| Policy test files | 3 (`PolicyVmEvaluationTests`, `DomainValidatedEvaluationTests`, `EntityMutationRoundTripTests`) | 1 (`PolicyVmEvaluationTests`) |
| Mutation round-trip tests | 3 (`EntityMutationRoundTripTests`) | 0 |
| Type-mapper examples | Primary (`DomainTypeMapperTests`) | Secondary |
| ECommerce demo usage | Present (Library: Person base) | Core (`V3ECommerceDomain`) |
| Natural lifecycle stages | born → child → adult → senior | cart → pending → paid → shipped → delivered |

Person Age is the **minimum expressive type** for proving policy evaluation. Policy guards like `Age >= 18` compile to a single `Member(Parameter, "Age")` → `GreaterThanOrEqual` node — no composite or cross-property logic.

### Test files using Person

- `Poly.Tests/DomainModeling/Lowering/PolicyVmEvaluationTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/DomainModeling/Lowering/DomainValidatedEvaluationTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/DomainModeling/Evolution/EntityMutationRoundTripTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/TestHelpers/DomainTypeMapperTests.cs` — primary example type

## Next task right now

**Slice 1 done.** Start **Slice 2** policy API:

1. **`vs-s2-subject-helper-and-reject.md` (2.1)** ← **pick this**  
2. Then `vs-s2-bool-abi-adult-assert.md` (2.2)  
3. Then `vs-s2-policy-true-false-e2e.md` (2.3)  
4. Then `vs-s2-property-name-alignment.md` (2.4)  
5. Then `vs-s2-domain-attached-policy-e2e.md` (2.5)

**Optional polish (anytime, do not block Slice 1–2):**  
- **0.2a** [`vs-s0-mcp-readme-add-action-to-stage.md`](vs-s0-mcp-readme-add-action-to-stage.md) — README still “Places an existing action”  
- **0.1d** [`vs-s0-fail-loud-remove-zero-match.md`](vs-s0-fail-loud-remove-zero-match.md) — remove-by-name zero match
