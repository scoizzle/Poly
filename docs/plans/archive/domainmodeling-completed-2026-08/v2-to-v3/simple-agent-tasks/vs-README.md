# Vertical Slice Micro-Tasks (Simple Agents)

**Parent plan:** [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md)  
**Last Updated:** 2026-07-24 — M2 complete; **qe suite also complete**  
**Audience:** Smaller / cheaper agents — one file per claim, tiny reading list.

> **Do not pick new work from this file.** Historical M2 suite is done. Query/effect queue: **[`qe-README.md`](qe-README.md)** (also complete — dogfood / pull only).

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
| **0.1d** | Fail-loud remove-by-name zero match *(optional)* | [`vs-s0-fail-loud-remove-zero-match.md`](vs-s0-fail-loud-remove-zero-match.md) | **[x]** | `RequireTarget` helper; RemoveProperty/Stage/Action/Policy fail when child missing |
| **0.2** | `add_action_to_stage` honesty | [`vs-s0-add-action-to-stage-honesty.md`](vs-s0-add-action-to-stage-honesty.md) | **[x]** | Description + create-semantics test |
| **0.2a** | MCP README row for stage action *(nit)* | [`vs-s0-mcp-readme-add-action-to-stage.md`](vs-s0-mcp-readme-add-action-to-stage.md) | **[x]** | README: "Creates a new action on a stage" |
| **0.3** | Wire PolicySubject fully | [`vs-s0-wire-policy-subject-validate.md`](vs-s0-wire-policy-subject-validate.md) | **[x]** | Validate + ValidateType |
| **0.4** | Fix instance EmitInvoke (receiver in Block) | [`vs-s0-fix-emit-invoke-instance.md`](vs-s0-fix-emit-invoke-instance.md) | **[x]** | instanceExpr + dual-oracle |
| **0.5** | MCP README V3-only | [`vs-s0-mcp-readme-honesty.md`](vs-s0-mcp-readme-honesty.md) | **[x]** | Done |

**Slice 0 required:** ✅ **Done.** Optional: **0.1d**, **0.2a** (do not block Slice 1).

### Slice 1 — Structure path (verify + pin) ✅ Done

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **1.1** | Verify structure e2e coverage | [`vs-s1-verify-structure-path.md`](vs-s1-verify-structure-path.md) | **[x]** | Inventory + GetDomainAnalysis smoke |
| **1.2** | Pin canonical entity (Person **or** Order) | [`vs-s1-pin-canonical-entity.md`](vs-s1-pin-canonical-entity.md) | **[x]** | **Person** — simplest numeric (Age), most test coverage |

### Slice 2 — Policy runtime (direct API only) ✅ Done

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **2.1** | Subject helper + reject Dict/Expando at evaluate | [`vs-s2-subject-helper-and-reject.md`](vs-s2-subject-helper-and-reject.md) | **[x]** | `PolicySubject.Validate` + `ValidateType<T>`; `PolicyTestSubjects` helpers |
| **2.2** | Bool ABI adult assert | [`vs-s2-bool-abi-adult-assert.md`](vs-s2-bool-abi-adult-assert.md) | **[x]** | `Evaluate` returns `bool`; tests use `IsTrue()/IsFalse()` |
| **2.3** | Age/numeric policy true **and** false e2e | [`vs-s2-policy-true-false-e2e.md`](vs-s2-policy-true-false-e2e.md) | **[x]** | `Evaluate_AgePolicy_TrueAndFalse_ExpectedResults` |
| **2.4** | Property name alignment test | [`vs-s2-property-name-alignment.md`](vs-s2-property-name-alignment.md) | **[x]** | Test + doc in PolicyEvaluator |
| **2.5** | Domain-attached policy e2e on **canonical** entity | [`vs-s2-domain-attached-policy-e2e.md`](vs-s2-domain-attached-policy-e2e.md) | **[x]** | `DomainAttached_CanonicalPerson_EvaluatesTrueAndFalse` |

### Slice 3 — Policy MCP product loop ✅ Done

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **3.1** | Constrained expression contract for add_policy | [`vs-s3-add-policy-expression-contract.md`](vs-s3-add-policy-expression-contract.md) | **[x]** | `PolicyExpressionContract` + `PolicyExpressionParser` + 14 tests |
| **3.2** | `add_policy` MCP tool | [`vs-s3-add-policy-tool.md`](vs-s3-add-policy-tool.md) | **[x]** | Flat args + contract parser |
| **3.3** | `evaluate_policy` MCP tool (VM bool) | [`vs-s3-evaluate-policy-tool.md`](vs-s3-evaluate-policy-tool.md) | **[x]** | Accepts Age; uses VM via `EvaluationSubject` |
| **3.4** | MCP e2e smoke structure + policy + eval | [`vs-s3-mcp-policy-e2e-smoke.md`](vs-s3-mcp-policy-e2e-smoke.md) | **[x]** | True/false + missing policy/entity |
| **3.5** | Polish affordances + MCP README policy section | [`vs-s3-policy-mcp-polish.md`](vs-s3-policy-mcp-polish.md) | **[x]** | README table updated with 3 new tools |

**Slice 3 done when:** 3.1–3.5 `[x]` → run checkpoint:

| # | Task | File | Status |
|---|------|------|--------|
| **M2** | Mark M2 product-complete in plans | [`vs-checkpoint-m2-close.md`](vs-checkpoint-m2-close.md) | **[x]** **Done** |

### Parallel polish

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **polish-dbg** | Fix flaky `VmDebugger_StepOver_TraversesStatements` | [`vs-fix-vmdebugger-stepover-locals.md`](vs-fix-vmdebugger-stepover-locals.md) | **[x]** | CaptureResult → hook snapshot + `ValueStack` clear on rent |
| **0.2a** | MCP README `add_action_to_stage` wording | [`vs-s0-mcp-readme-add-action-to-stage.md`](vs-s0-mcp-readme-add-action-to-stage.md) | **[x]** | README: “Creates a new action on a stage” |
| **0.1d** | Remove-by-name zero-match fail-loud | [`vs-s0-fail-loud-remove-zero-match.md`](vs-s0-fail-loud-remove-zero-match.md) | **[x]** | `RequireTarget` helper; 6 tests |

### Post-M2 (orchestrator picks by scenario)

| # | Task | File | Status | Notes |
|---|------|------|--------|-------|
| **pm2-1** | Multi-property MCP sample subject for `evaluate_policy` | [`vs-pm2-evaluate-policy-sample-bag.md`](vs-pm2-evaluate-policy-sample-bag.md) | **[x]** | McpSubjectBag (8 props); JSON + backward-compat Age; 1178 green |
| **pm2-2** | Affordance: `add_policy` success → include `evaluate_policy` | [`vs-pm2-add-policy-evaluate-affordance.md`](vs-pm2-add-policy-evaluate-affordance.md) | **[x]** | `add_policy` response now includes `evaluate_policy` affordance |
| **pm2-3** | First effect execution (Slice 4) | (use vertical-slice S4 / open when pulled) | **[ ]** | Pull when product needs behavior beyond guards |
| **pm2-4** | Naming cleanup drop V3\* | [`../../post-v2-delete-naming-cleanup.md`](../../post-v2-delete-naming-cleanup.md) | **[x]** | R0–R2 done: MCP tools, demos, analyzer extensions renamed |

### Deferred (do not pick without scenario)

| Item | Why |
|------|-----|
| Slice 5 Relationship | Pull-only (tool exists; deepen when needed) |
| T2 dogfood / product domain | Trust ADR — after stable M2 + generation loop |
| Actor / codegen / DiffDays | WP9 / review-fix when consumer pulls |

---

## Archived pre-vs micro-tasks

WP/ws/WS8 micro-tasks moved to  
[`../../archive/v2-to-v3-migration/simple-agent-tasks/`](../../archive/v2-to-v3-migration/simple-agent-tasks/).  
**Do not execute** — use this `vs-*` suite only.

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

**M2 product-complete** (structure + policy API + policy MCP). Full suite **1178** green.

**Recommended post-M2 picks (in order of leverage):**

1. **`vs-pm2-evaluate-policy-sample-bag.md` (pm2-1)** — multi-property sample subject (Age-only is M2-thin)  
2. **`vs-pm2-add-policy-evaluate-affordance.md` (pm2-2)** — affordance chain polish  
3. **`vs-s0-fail-loud-remove-zero-match.md` (0.1d)** — optional evolve honesty  
4. **Naming cleanup R0–R1** — [`../../post-v2-delete-naming-cleanup.md`](../../post-v2-delete-naming-cleanup.md) when no feature thrash  
5. **Slice 4 first effect** — only with a named product scenario  

Orchestrator: prefer (1)–(2) if agents hit Total/Status policies or missing evaluate affordances.
