# Poly Workspace Instructions

## Doc roles

| Doc | Role |
|-----|------|
| **[`docs/CORE.md`](docs/CORE.md)** | Platform map: purpose, ownership, critical machinery, “use this / not that.” **Read before changing Syntax, Interpretation, Introspection, DomainModeling, or Poly.Mcp.** |
| **This file** | Principles, placement, build/test, coding ops |
| **`docs/decisions/`** | Why (ADRs). Index: [`docs/decisions/README.md`](docs/decisions/README.md) |
| **`docs/plans/`** | Execution work only — not evergreen rules |
| **[`docs/agent/`](docs/agent/)** | Tool-agnostic agent protocols (review, etc.). Index: [`docs/agent/README.md`](docs/agent/README.md) |

When a change alters a mechanism listed in CORE, update CORE in the same change. Significant cross-cutting choices get an ADR.

---

## Core principles

Non-negotiable. Each principle has a **one-line rule** and a short **how** for agents and humans who will not open the ADR. Depth and history: [`docs/decisions/2026-core-engineering-principles.md`](docs/decisions/2026-core-engineering-principles.md).

**Order is intentional** (why this repo → how pieces connect → whether to do it → how we move → how much → when to abstract → when to add process).

**When principles pull opposite ways:** prefer **domain fidelity and end-to-end ownership via CORE seams** over a locally smaller wrong path; prefer a **smaller tested loop** over a larger untested batch; prefer **no new abstraction** over a “cleaner” framework without a second real use. “More generic” production under green means fewer special cases — not a premature pattern catalog.

**Platform trust bar:** **We are our own first customer.** Product surface (including **external contracts**) is built *through* domain + modules; substrate ops glue is separate. **Customer product generation funds neurosymbolic work over time** — generation is the engine, not a side demo; substrate depth is steered by what generation and honesty need. Contract surface pains *us* first by design. Market platform trust = **T2**; **T1** = design partners. Dogfood pain → fix the seam or narrow the claim. Policy: [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md).

### 1. The domain model is the key artifact

**Rule:** Tools, languages, and infrastructure serve **domain expression**. Fashion and familiarity do not override correctness, operability, or shipped capability.

**How:**
1. Ask: does this change make domain intent clearer, safer to evolve, or more faithfully executed?
2. Prefer fidelity to the domain (and CORE’s lower → analyze → execute path) over a convenient host trick or trendy library shape.
3. If infra and domain disagree, fix the mapping (lower/analyze/replace) — do not warp the domain to match the tool.
4. Judge success by domain/customer outcomes, not by elegance of the infrastructure alone.

### 2. Engineer end-to-end behavior with clear ownership

**Rule:** Build coherent system behavior; avoid isolated parts whose interactions are accidental.

**How:**
1. State the end-to-end path (e.g. domain → AST → analyze → VM, or MCP → DomainModeling → result).
2. Place each piece in the module that **owns** that concern ([Placement](#placement), [`docs/CORE.md`](docs/CORE.md)).
3. Prefer composing existing seams over a new side path that only works if no one else looks.
4. If two modules must meet, make the boundary explicit (lower / analyze / replace / call) — not a silent dependency or ABI one-off.

### 3. Keep only what measurably helps the customer

**Rule:** Requirements must improve **time-to-value**, **correctness**, or **operability**. Everything else is removed.

**How:**
1. Name the customer-visible or operator-visible outcome this change improves (or say “none — don’t do it”).
2. Prefer the smallest change that delivers that outcome **without** violating §1–§2 (smallest *coherent* path, not smallest hack).
3. Before merging extras (frameworks, options, “while we’re here”), ask: does this improve one of the three measures *now*? If not, cut or defer to a plan.

### 4. Go well to go fast

**Rule:** The only sustainable speed is quality under feedback. Small **test-authoring → production-authoring** loops: tests grow **more specific**; production code grows **more generic**. Large untested leaps are deferred rework.

**How:**
1. Write or tighten **one** failing/incomplete check for the *next* behavior.
2. Change production with the **smallest** fix that makes it pass.
3. Under green, remove duplication and special cases (production gets simpler/more general — not a new framework).
4. Run the relevant tests before calling the work done.
5. Spikes for learning are fine; they are not the product path until pulled through this loop.

### 5. Shipped capability over completeness

**Rule:** Deliver the smallest coherent slice that proves the capability. Framework completeness is not a goal.

**How:**
1. Define the **thinnest vertical slice** that a real consumer can exercise (one path green end-to-end).
2. Grow that slice with §4 loops; leave adjacent cases for the next loop.
3. Reject “complete the subsystem first” unless the slice cannot work without it.
4. Document remaining gaps in `docs/plans/`, not as unfinished abstractions in product code.

### 6. Working code before extracted abstractions

**Rule:** Abstractions are extracted **after** working code exists. Pattern catalogs are not a design-first checklist.

**How:**
1. Get one concrete path working under §4 (tests + production).
2. Only when a **second** real use forces sharing, extract the shared form.
3. Name the result for **what it is**, not which pattern it resembles ([Naming](#naming)).
4. Do not add interfaces, visitors, or plugin hosts “for the future” without a current second consumer.

### 7. Guardrails only with real first consumers

**Rule:** Placement rules, conventions, ADRs, and similar guardrails exist when they unblock **identifiable** people or agents — not as ceremony. Always-on docs (AGENTS, CORE) already have consumers; this principle limits *new* process.

**How:**
1. Name the consumer of the guardrail (who hits the wall without it?).
2. Add the thinnest rule or doc that unblocks them; link from AGENTS/CORE only if it must be always-on.
3. Do not invent process, templates, or validators with zero callers.
4. When the consumer disappears, delete or demote the guardrail.

### Naming

- Name types and directories for **what they are**, not which pattern they use (`UopCompiler`, not `UopLoweringVisitor`; `Backends/`, not `Visitors/`).
- A concrete type **is** its concept: `CSharpCodeGenerator`, `Inliner`, `RingAnalyzer`.

### DSL Guide

The **Modeling Principles** in [`Poly.Mcp/Docs/poly-dsl-guide.md`](Poly.Mcp/Docs/poly-dsl-guide.md) must be read before authoring or modifying any domain model. The same document is the product-true syntax reference for the shipped `apply_dsl` surface.

**Keep it in sync.** Any change to the parser, printer, or tokenizer that alters what DSL constructs are valid or emitted must update the guide in the same change. The smoke test `GetDslGuide_ReturnsProductSurface` will catch some drift, but guide content must be updated proactively — especially when adding/removing keywords, effect types, constraint syntax, or relationship forms.

Do not let experimental or lab grammar docs (`docs/experiments/`) become the de-facto agent reference.
The product guide is the single source of truth for MCP `apply_dsl`.

---

## Placement

| What | Where |
|------|--------|
| AST nodes | `Poly/Ast/Nodes/` |
| AST base (Node, NodeId, fluent API) | `Poly/Ast/` |
| Analysis **framework** (context, metadata, node replacement) | `Poly/Analysis/` |
| Semantic analysis **passes** | `Poly/Interpretation/Analysis/` |
| VM + direct AST→VM compile | `Poly/Interpretation/Vm/` (`DirectVmAbiEmitter`, `Interpreter` façade) |
| Type/member model + CLR host | `Poly/Introspection/` |
| Domain model, evolution, DE→AST | `Poly/DomainModeling/` |
| Validation rules | `Poly/Validation/Rules/` (register subtypes on `Rule.cs`) |
| MCP session + tools | `Poly.Mcp/` |
| Shared helpers | `Poly/Extensions/` |

Module boundaries, pipeline, and anti-reinvention rules: **[`docs/CORE.md`](docs/CORE.md)** — do not restate them here.

Layout sketch: `Poly/` (core library), `Poly.Tests/` (TUnit), `Poly.Benchmarks/`, `Poly.Mcp/`. Modeling stack is **`DomainModeling` only** (`Poly/Data/Modeling` / V2 is gone). Product types still named `V3*` are legacy labels — cleanup plan: [`docs/plans/post-v2-delete-naming-cleanup.md`](docs/plans/post-v2-delete-naming-cleanup.md) (after M2; do not invent V4).

---

## Build & test

- **TFM:** `net10.0`, nullable enabled.
- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj`
- Work is **not complete** while the build is failing (unless the user explicitly blocks).
- Add tests with feature changes.
- **Test style:** TUnit — `async [Test]`, `await Assert.That(result).IsEqualTo(expected)`, names `Method_Condition_ExpectedResult`.
- Helpers under `Poly.Tests/TestHelpers/` are **test-only** — never promote into core `Poly/`.
- Isolated single-file prototyping: [`docs/file-based-csharp-apps.md`](docs/file-based-csharp-apps.md).

### Deep review (any agent)

For an **adversarial correctness / contract** review of a diff (findings only; follow-ups must land in docs), follow the portable protocol:

**[`docs/agent/phenomenal-review.md`](docs/agent/phenomenal-review.md)**

Any tool can run it with: *“Read docs/agent/phenomenal-review.md and execute it against local changes. Assume the code is wrong until evidence says otherwise.”*  
Optional multi-pass (independent second context): add `mode: multi`.  
Thin wrappers (same bar): Grok `/phenomenal-review` → [`.grok/skills/phenomenal-review/`](.grok/skills/phenomenal-review/); Copilot → [`.github/skills/phenomenal-review/`](.github/skills/phenomenal-review/).  
This is **not** the pre-ship fix loop below and not a maintainability refactor pass. Inspired in part by [Bun’s adversarial review loops](https://bun.com/blog/bun-in-rust).

### Pre-ship review gate

Before marking any slice or feature "Done", execute the **[uncommitted-change review gate](docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)**:
1. Review dirty files — `git diff --stat HEAD` then `git diff HEAD`.
2. Categorize findings by severity (🔴 Structure / 🟠 Contract / 🟡 Edge case / ⚪ Hygiene).
3. For every contract/structure finding, verify **three-layer defense**: parse-time rejects, analyze-time catches, runtime fails loud.
4. **Fail-closed:** Empty sets, missing matches, and invalid configs fail loud — no vacuous success.
5. Apply smallest fix that passes a failing test; re-review.
6. Only ship when tree is clean, all 🔴🟠 resolved, build + suite green.

This gate is **mandatory before `[x]`** on any task in the active suite. The full process with examples: [`docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md).

---

## Coding ops

- Prefer **minimal diffs**; match existing fluent naming and chaining.
- `Expression` is often aliased to `System.Linq.Expressions.Expression` (`Poly/GlobalUsings.cs` / test file-locals as `Expr`).
- No inline comments unless the logic is genuinely non-obvious.
- No `#region` / `#endregion` in new code.
- Prefer `Interpreter.Analyze` / `Compile` / `Execute` over hand-rolling analyzer pipelines unless tests intentionally isolate a pass.
