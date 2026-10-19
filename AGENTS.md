# Poly Workspace Instructions

Authoritative always-on policy. User-wide agent defaults are a baseline; this file wins. Open [`docs/CORE.md`](docs/CORE.md) before changing platform machinery.

**Before you edit, check:** Is this a CORE mechanism change (then update `docs/CORE.md` in the same change)? Does the board's `CURRENT` admit the work? Does a new `Node` need a `LanguageSurfaceTests.Kinds` entry plus a `CompileNodeInner` arm in lockstep? Does the file already carry edits you did not make — then preserve them; never revert, stage, or clean them up.

## Core tenets

Depth: [`docs/decisions/2026-core-engineering-principles.md`](docs/decisions/2026-core-engineering-principles.md). Contract: Frozen core and Agent target below. Machinery: [`docs/CORE.md`](docs/CORE.md).

A domain is a library of legal operations. Session compile is Load → one analyze (fail closed) → `session.Lower` → artifact set. The Syntax module is operation meaning and the Interpreter sim. Other producer artifacts call that module. Shipped ⊆ lowerable. Product doors are opt-in `uses`. Poly.MCP is the harness, not the customer API.

1. **Domain model is the artifact** — when infra and the domain disagree, fix lower, analyze, or node replacement.
2. **End-to-end ownership** — one path through CORE seams.
3. **Only what helps the customer** — time-to-value, correctness, or operability.
4. **Go well to go fast** — small test→code loops; tests specific, production generic.
5. **Shipped capability over completeness** — thinnest slice through the right seam. Shrink the language before a host escape. Do not ship a known-wrong shape.
6. **Seam when multiplicity is known** — libraries, artifact producers, and `uses` doors get a named seam. No framework for an imaginary second use. `DomainEntityInstance` as a public path is the cautionary tale.
7. **Guardrails only with real consumers.**

When tenets conflict: domain fidelity and CORE seams over a smaller wrong path; a smaller tested loop over a large untested batch; a known multi-impl seam over a one-off; no speculative framework.

**Hard nos:** `Main` in core · `Comment` / `null` lower / second interpreter as shipped meaning · consumer-specific lowering flag · Lower-inside-analysis · twin trees (`UseThisReference`; historical name, do not reintroduce) · `LowerActionBody` as shipped execute input · host-escape keyword · DEI/MCP walk as product proof · empty artifact catalog when required · a second CURRENT.

## Frozen core

Depth: [`docs/CORE.md`](docs/CORE.md) §0 · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](docs/decisions/2026-09-04-frozen-core-pipeline.md).

Architecture is AST / Node / Analysis, plus libraries that publish bags and artifacts. Scratch store, C# print, HTTP Minimal API, and Store job names are consumers.

| Frozen | Do not |
|--------|--------|
| `Node` / `NodeId` as symbolic primary | Parallel product IR; Effect walk as shipped meaning |
| Analysis: bags + node replacement | Side tables; semantic consume without `AnalysisResult` |
| `Domain` = facts (`uses` ids); session loads libraries | `Domain.ResolveHost`; dialects; `Main` in core |
| Shipped ⊆ complete generic Syntax tree | `Comment` / `null` lower; second interpreter; domain opcodes |
| Clean analyze → `session.Lower` → artifact set | Lower-inside-analysis; empty catalog when required; consumer-specific lowering flag |
| Syntax module privileged for meaning + Interpreter sim | Twin trees; producers / DEI / Effect-IR as a second sim |
| New meaning: lower, analyze, or replace nodes | Emitter patch; ABI one-off |

Compose Interpreter, exporter, DEI, and `uses http`. MCP tool `Description` is usage text, not AST or store types.

## Agent target

The lowered operation module is the domain. Depth: [`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md).

Compilation unit = library. Interpreter sim and C# print share the Syntax module body. When they diverge, fix Lower. Put meaning in the operation AST. Prove it on that body (VM on bound `This`, or generated CLR). Lower subs, OnEntry/OnExit, and entity policies into the module. `DomainEntityInstance` is scratch bind for MCP and authoring. Residual, do not grow: [`docs/plans/parked/p3b-followups-2026-09-27.md`](docs/plans/parked/p3b-followups-2026-09-27.md).

## Ops

- **TFM:** `net10.0`, nullable on.
- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj` — not `dotnet test`. While iterating, add `-- --treenode-filter` for the tests you changed. The full suite runs at the pre-ship gate, in the parent session: run it there and record the pass count in the summary — the `reviewer` cannot build or test. Do not weaken audit in-repo.
- **MCP after code changes:** run `scripts/restart-poly-mcp.sh` after touching anything the MCP server exposes — Ast, analysis, the VM, `Poly.Mcp`, or the DSL guide.
- A red build means the task is incomplete; the user may waive that, an agent may not. Tests ship with features. TUnit: `async [Test]`, `await Assert.That(…).IsEqualTo(…)`, `Method_Condition_ExpectedResult`. `Poly.Tests/TestHelpers/` stays test-only.
- Minimal diffs. Names say what they are (`UopCompiler`). No drive-by comments. No new `#region`.
- **DSL:** before authoring, call `get_dsl_guide` (short body). Pass `section` for one heading, or `all` for [`Poly.Mcp/Docs/poly-dsl-guide.md`](Poly.Mcp/Docs/poly-dsl-guide.md). Update that file in the same change as the parser, printer, tokenizer, or an MCP tool that authors DSL.
- **Placement:** Ast → `Poly/Ast/`; analysis framework → `Poly/Analysis/`; semantic passes → `Poly/Interpretation/Analysis/`; VM → `Poly/Interpretation/Vm/`; C# language surface → `Poly/Interpretation/CSharp/`; types → `Poly/Introspection/`; domain → `Poly/DomainModeling/`; grammar engine → `Poly/Grammar/`; MCP → `Poly.Mcp/`.
- **Admission:** [`docs/plans/poly-eng/board.md`](docs/plans/poly-eng/board.md) is the only CURRENT, and its row bookkeeping is [`docs/plans/poly-eng/README.md`](docs/plans/poly-eng/README.md) §Record rules — no docs-only PRs, no second status schema. If the board is `(none)`, open only the suite `THEN` names, via [`docs/plans/parked/README.md`](docs/plans/parked/README.md); a label with no entry there is reported, not guessed at. `docs/plans/archive/` and `docs/plans/parked/` are not queues.
- **Review:** adversarial → [`docs/agent/phenomenal-review.md`](docs/agent/phenomenal-review.md) (severity: `bug` | `suggestion` | `nit`, §6). Follow-ups are evidence until a human adds them to `THEN`. Pre-ship → [`docs/agent/pr1-uncommitted-review-gate.md`](docs/agent/pr1-uncommitted-review-gate.md) (fail closed; 🔴 and 🟠 block, per phenomenal §6; pr1 Step 6; suite green).
- **Doc roles:** CORE = machinery map · `docs/decisions/` = why · `docs/plans/` = execution (archive DONE) · `docs/agent/` = protocols. A CORE mechanism change updates CORE in the same change.

## C# surface

`Poly/Interpretation/CSharp/` is a reader *and* a writer, and everything it accepts must execute on the VM.

- One grammar table drives reader and writer. A pattern-shape change updates the writer's positional fills in the same change.
- A new `Node` lands in lockstep with its `LanguageSurfaceTests.Kinds` entry and its `DirectVmAbiEmitter.CompileNodeInner` arm (plus a `CompileValue` arm if it is a value).
- Console-only work leaves the DSL parser, the exporter, and the golden corpus untouched.
- Fail closed: a form that cannot execute soundly throws a positioned error; it never folds to a wrong shape.
- Tests ship with the feature, `Write(Read(x))` stays idempotent, and every feature gets its fail-closed case.

## Branching and PRs

- Base a feature branch on `origin/master`, not on a local `master` that may be behind.
- Never stage or commit files you do not own, or pre-existing modifications you did not make. If a file is already dirty when you start, leave it dirty; do not `git add` in the user's tree.
- A PR contains the task's work only. Check `git status` against the PR diff before you push.
- Removing a worktree whose untracked `bin/`/`obj/` blocks the delete needs `force`, and only with the user's confirmation.
