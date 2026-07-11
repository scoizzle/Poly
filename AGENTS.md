# Poly Workspace Instructions

## Doc roles

| Doc | Role |
|-----|------|
| **[`docs/CORE.md`](docs/CORE.md)** | Platform map: purpose, ownership, critical machinery, “use this / not that.” **Read before changing Syntax, Interpretation, Introspection, DomainModeling, or Poly.Mcp.** |
| **This file** | Principles, placement, build/test, coding ops |
| **`docs/decisions/`** | Why (ADRs). Index: [`docs/decisions/README.md`](docs/decisions/README.md) |
| **`docs/plans/`** | Execution work only — not evergreen rules |

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

---

## Placement

| What | Where |
|------|--------|
| AST nodes | `Poly/Syntax/Nodes/` |
| Analysis **framework** (context, metadata, node replacement) | `Poly/Syntax/Analysis/` |
| Semantic analysis **passes** | `Poly/Interpretation/Analysis/` |
| VM + direct AST→VM compile | `Poly/Interpretation/Vm/` (`DirectVmAbiEmitter`, `Interpreter` façade) |
| Type/member model + CLR host | `Poly/Introspection/` |
| Domain model, evolution, DE→AST | `Poly/DomainModeling/` |
| Validation rules | `Poly/Validation/Rules/` (register subtypes on `Rule.cs`) |
| MCP session + tools | `Poly.Mcp/` |
| Shared helpers | `Poly/Extensions/` |

Module boundaries, pipeline, and anti-reinvention rules: **[`docs/CORE.md`](docs/CORE.md)** — do not restate them here.

Layout sketch: `Poly/` (core library), `Poly.Tests/` (TUnit), `Poly.Benchmarks/`, `Poly.Mcp/`. Modeling stack is **V3 only** (`DomainModeling`); `Poly/Data/Modeling` is gone.

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

---

## Coding ops

- Prefer **minimal diffs**; match existing fluent naming and chaining.
- `Expression` is often aliased to `System.Linq.Expressions.Expression` (`Poly/GlobalUsings.cs` / test file-locals as `Expr`).
- No inline comments unless the logic is genuinely non-obvious.
- No `#region` / `#endregion` in new code.
- Prefer `Interpreter.Analyze` / `Compile` / `Execute` over hand-rolling analyzer pipelines unless tests intentionally isolate a pass.
