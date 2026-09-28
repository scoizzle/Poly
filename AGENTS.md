# Poly Workspace Instructions

**This file** is the authoritative always-on policy for agents. User-wide defaults (e.g. `~/.agents/AGENTS.md`) are a baseline when present; rules here take precedence. Start with **Core tenets**. Open [`docs/CORE.md`](docs/CORE.md) before changing platform machinery.

---

## Core tenets

Non-negotiable. Depth: [`docs/decisions/2026-core-engineering-principles.md`](docs/decisions/2026-core-engineering-principles.md) (Rule + How) · Frozen core / Agent target below · [`docs/CORE.md`](docs/CORE.md).

**What this platform is:** A domain is a **library of legal operations**, not a process. Session Compile is Load → **one** analyze (fail-closed) → **`session.Lower`** → **artifact set** on the session. The **Syntax module** is privileged for operation meaning + Interpreter sim; other library producer artifacts are delivery (call/bind the module — **not** a second sim). Shipped ⊆ lowerable. Product doors are opt-in `uses`. **Poly.MCP** is the interactive harness, not the customer API. We are our own first customer (T2 = market trust).

**How we work** (order intentional):

1. **Domain model is the key artifact** — tools serve domain expression; fix lower/analyze/replace when infra and domain disagree.
2. **End-to-end ownership** — coherent path through CORE seams; no accidental side paths.
3. **Only what helps the customer** — time-to-value, correctness, or operability; cut the rest.
4. **Go well to go fast** — small test→code loops; tests more specific, production more generic.
5. **Shipped capability over completeness** — thinnest vertical slice through the *right* seam; shrink the language before host escapes. Completeness theater is out; so is shipping a known-wrong single-path shape and calling it done.
6. **Seam when multiplicity is known** — if the design already needs multiple implementations (libraries, artifact producers, `uses` doors), name the seam and ship against it. Do not wait for a second copy of a one-off. Speculative frameworks for imagined futures stay out. Cautionary tale: DEI (`DomainEntityInstance`) as “working” public path.
7. **Guardrails only with real consumers** — no ceremony for zero callers.

**When tenets pull opposite ways:** prefer **domain fidelity and end-to-end ownership via CORE seams** over a locally smaller wrong path; prefer a **smaller tested loop** over a larger untested batch; prefer a **known multi-impl seam** over a single-path hack that will be ripped out; prefer **no speculative framework** when the second use is imaginary.

**Hard nos:** `Main` in core · `Comment` / `null` lower / second interpreter as shipped meaning · consumer lowering flags · Lower-inside-analysis · twin trees (`UseThisReference`) · DEI/MCP walk as product-surface proof · empty artifact catalog when required · inventing a second CURRENT.

---

## Frozen core

**Must respect.** Depth: [`docs/CORE.md`](docs/CORE.md) §0 · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](docs/decisions/2026-09-04-frozen-core-pipeline.md).

Architecture = **AST / Node / Analysis** + libraries that publish bags and artifacts. Scratch store, C# print, HTTP Minimal API, Store job names — **current consumers**, not the architecture. Do not grow a second pipeline for a consumer.

| Frozen | Do not |
|--------|--------|
| `Node` / `NodeId` as symbolic primary | Parallel product IR; Effect walk as shipped meaning |
| Analysis: bags + **node replacement** | Side tables; semantic consume without `AnalysisResult` |
| `Domain` = facts (`uses` ids); session loads libraries | `Domain.ResolveHost`; dialects; `Main` in core |
| Shipped ⊆ complete generic Syntax tree | `Comment` / `null` lower / second interpreter / domain opcodes |
| Clean analyze → **`session.Lower`** → **artifact set** on session | Lower-inside-analysis; empty catalog when required; consumer lowering flag |
| Syntax module privileged for **meaning + Interpreter sim** | Twin trees; producers / DEI / Effect-IR as second sim |
| New meaning: lower / analyze / **replace nodes** | Emitter patch, ABI one-off |

Compose current machinery (Interpreter, exporter, DEI, `uses http`, …); do not fork it. MCP tool `Description` = usage text, not AST/store types.

---

## Agent target

**Session Compile → artifact set; Syntax module privileged for meaning + sim.**  
[`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md) · [`docs/plans/session-lower-plan-2026-09-21.md`](docs/plans/session-lower-plan-2026-09-21.md) · [`docs/plans/session-lower-abstractions-2026-09-21.md`](docs/plans/session-lower-abstractions-2026-09-21.md).

Load → one analyze (dirty → **STOP**) → `session.Lower` → artifact set on the session (fail closed if empty when required). **Compilation unit** = library. Simulate and C# print share the **same** Syntax module body. When they diverge, fix **Lower**.

| Do | Do not |
|----|--------|
| Put meaning in the operation AST | DEI preludes, Effect-IR execute, MCP inventing ops |
| Prove via the body print uses (VM on bound `This`, or generated CLR) | Green DEI/MCP walk as product proof |
| Lower subs / OnEntry/OnExit / entity policies into the module | `LowerActionBody` as shipped execute input; revive twin trees |
| Shrink the language if it cannot lower | Host-escape keyword |

`DomainEntityInstance` = scratch bind (MCP/authoring), not customer API / T2 proof. Residual (do not grow): [`docs/plans/p3b-followups-2026-09-27.md`](docs/plans/p3b-followups-2026-09-27.md).

---

## Ops

- **TFM:** `net10.0`, nullable on.
- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj` — not `dotnet test` (MTP). Local audit path needs `-p:NuGetAudit=false` (NU1903); do not weaken audit in-repo.
- **MCP after code changes:** `scripts/restart-poly-mcp.sh`
- Work incomplete while build fails (unless user blocks). Add tests with features. TUnit: `async [Test]`, `await Assert.That(…).IsEqualTo(…)`, `Method_Condition_ExpectedResult`. `Poly.Tests/TestHelpers/` stays test-only.
- **Diffs:** minimal; match fluent naming. No drive-by comments; no `#region`.
- **Names:** for what they are (`UopCompiler`, not `UopLoweringVisitor`).
- **DSL:** before authoring domains, read [`Poly.Mcp/Docs/poly-dsl-guide.md`](Poly.Mcp/Docs/poly-dsl-guide.md); keep it in sync with parser/printer/tokenizer changes.
- **Placement:** Ast → `Poly/Ast/`; analysis framework → `Poly/Analysis/`; semantic passes → `Poly/Interpretation/Analysis/`; VM → `Poly/Interpretation/Vm/`; types → `Poly/Introspection/`; domain → `Poly/DomainModeling/`; grammar → `Poly/Grammar/`; MCP → `Poly.Mcp/`. Detail: [`docs/CORE.md`](docs/CORE.md).
- **Review:** adversarial → [`docs/agent/phenomenal-review.md`](docs/agent/phenomenal-review.md). Pre-ship Done → [`docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md) (fail-closed; 🔴🟠 clear; suite green).
- **Doc roles:** CORE = machinery map · `docs/decisions/` = why · `docs/plans/` = execution (archive DONE) · [`PIPELINE-STATUS.md`](docs/plans/simple-agent-tasks/PIPELINE-STATUS.md) = sole CURRENT · [`docs/agent/`](docs/agent/) = protocols. Change a CORE mechanism → update CORE in the same change.
