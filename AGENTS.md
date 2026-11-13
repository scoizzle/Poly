# Poly Workspace Instructions

Authoritative always-on policy. User-wide agent defaults are a baseline; this file wins. Open [`docs/CORE.md`](docs/CORE.md) before changing platform machinery.

**Before you edit, check:** Is this a CORE mechanism change (then update `docs/CORE.md` in the same change)? Is the work an open GitHub issue (the queue is open issues, not a file)? Does a new `Node` need a `NodeFacts` row plus a `CompileNodeInner` arm in lockstep? Does the file already carry edits you did not make — then preserve them; never revert, stage, or clean them up.

## Core tenets

Depth: [`docs/decisions/2026-core-engineering-principles.md`](docs/decisions/2026-core-engineering-principles.md). Contract: Frozen core and Agent target below. Machinery: [`docs/CORE.md`](docs/CORE.md).

A domain is a library of legal operations. Session compile is Load → one analyze (fail closed) → `session.Lower` → artifact set. The Syntax module is operation meaning and the Interpreter sim. Other producer artifacts call that module. Shipped ⊆ lowerable. Product doors are opt-in `uses`. Poly.MCP is the harness, not the customer API.

1. **Domain model is the artifact** — when infra and the domain disagree, fix lower, analyze, or node replacement.
2. **End-to-end ownership** — one path through CORE seams.
3. **Only what helps the customer** — time-to-value, correctness, or operability. A workaround that hides a wrong answer helps no customer. "Nothing shipped reaches it" is a reason not to build a feature, never a reason to park a real fix.
4. **Go well to go fast** — small test→code loops; tests specific, production generic.
5. **Shipped capability over completeness** — thinnest slice through the right seam. Shrink the language before a host escape. Do not ship a known-wrong shape. No temporary fix in place of the real one: a pinned wrong result, a gate that skips the hard case, or an interim rule is not a fix. When the real fix does not fit the slice, refuse the shape (fail closed) or name the interim rule, the row that removes it and its test. A workstream is not shippable while an interim rule remains.
6. **Seam when multiplicity is known** — libraries, artifact producers, and `uses` doors get a named seam. No framework for an imaginary second use. A public instance runtime type is the cautionary tale.
7. **Guardrails only with real consumers.**

When tenets conflict: domain fidelity and CORE seams over a smaller wrong path; a smaller tested loop over a large untested batch; a known multi-impl seam over a one-off; no speculative framework.

**Hard nos:** `Main` in core · `Comment` / `null` lower / second interpreter as shipped meaning · consumer-specific lowering flag · Lower-inside-analysis · twin trees (`UseThisReference`; historical name, do not reintroduce) · `LowerActionBody` as shipped execute input · host-escape keyword · DEI/MCP walk as product proof · empty artifact catalog when required · a second CURRENT · an interim rule with no removing row.

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

Compose Interpreter, exporter, and `uses http`. MCP tool `Description` is usage text, not AST or store types.

## Agent target

The lowered operation module is the domain. Depth: [`docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md`](docs/decisions/2026-09-05-lowered-module-is-domain-meaning.md).

Compilation unit = library. Interpreter sim and C# print share the Syntax module body. When they diverge, fix Lower. Put meaning in the operation AST. Prove it on that body (VM on bound `This`, or generated CLR). Lower subs, OnEntry/OnExit, and entity policies into the module. Simulation is the interpreter host on the module dictionary; there is no instance runtime type. A bag handle (`ModuleInstance`, id + entity + dictionary) is the host's index into that dictionary, not a runtime type: it carries no behaviour.

## Ops

- **TFM:** `net10.0`, nullable on.
- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj` — not `dotnet test`. While iterating, add `-- --treenode-filter` for the tests you changed. The full suite runs at the pre-ship gate, in the parent session: run it there and record the pass count in the summary — the `verifier` runs the build and changed-area tests and cannot run the full suite. Do not weaken audit in-repo. Exception for stream work (see Integration branch): the unit that produced the slice head runs the full suite and records the command, totals and sha.
- **Commit hook:** the `pre-commit` hook runs `dotnet format` on staged `.cs` files and re-stages them, so a commit can carry formatting your verification run never saw. Formatting is semantically inert — do not re-run the build or suite after a commit that changes only formatting; the green run before the commit still stands.
- **Workstreams:** one workstream at a time, taken to shippable (every row merged, every interim rule it introduced removed, release notes final); at most one filler stream that shares no files with it; no new row starts while an approved pull request waits for its closing round; the stream's owner questions are asked in one sitting before it starts. Owner ruling, [#677 comment](https://github.com/scoizzle/Poly/issues/677#issuecomment-6855444684).
- **Integration branch:** a stream's slices are small pull requests against `stream/<name>`, cut from master when the stream starts. A slice merges when its head is up to date with that branch and ONE full local suite is green on exactly that head (the command, totals and sha recorded), with the Benchmarks build at 0 warnings. A draft pull request from the stream branch to master runs GitHub CI and CodeQL in the background; a red run there stops the stream. The stream lands on master as one merge. Owner ruling, [#677 comment](https://github.com/scoizzle/Poly/issues/677#issuecomment-6855770465).
- **Behaviour that runs today:** anything that runs end to end on master comes to the owner, with the concrete example, numbered options and a recommendation, before a slice rejects it, unless the row's approved text names the class. Owner precedent, e.g. [#677 comment](https://github.com/scoizzle/Poly/issues/677#issuecomment-6854270157).
- **MCP after code changes:** run `scripts/restart-poly-mcp.sh` after touching anything the MCP server exposes — Ast, analysis, the VM, `Poly.Mcp` tool code, the `polyc mcp` host, or the DSL guide.
- A red build means the task is incomplete; the user may waive that, an agent may not. **Test-first:** every behavior change starts with a test that fails for the stated reason — run it red, record the command and output, then implement to green and keep the test. Mechanical work (typo, rename, version bump) uses the existing suite as its oracle instead. TUnit: `async [Test]`, `await Assert.That(…).IsEqualTo(…)`, `Method_Condition_ExpectedResult`. `Poly.Tests/TestHelpers/` stays test-only.
- Minimal diffs. Names say what they are (`UopCompiler`). No drive-by comments. No new `#region`.
- **DSL:** before authoring, call `get_dsl_guide` (short body). Pass `section` for one heading, or `all` for [`Poly.Mcp/Docs/poly-dsl-guide.md`](Poly.Mcp/Docs/poly-dsl-guide.md). Update that file in the same change as the parser, printer, tokenizer, or an MCP tool that authors DSL.
- **Placement:** Ast → `Poly/Ast/`; analysis framework → `Poly/Analysis/`; semantic passes → `Poly/Interpretation/Analysis/`; VM → `Poly/Interpretation/Vm/`; C# language surface → `Poly/Interpretation/CSharp/`; types → `Poly/Introspection/`; domain → `Poly/DomainModeling/`; grammar engine → `Poly/Grammar/`; MCP tool code → `Poly.Mcp/` — a class library; the host is `polyc mcp`.
- **Admission:** The queue is open GitHub issues. Umbrella [#305](https://github.com/scoizzle/Poly/issues/305) — the Parse → Load → Resolve → Analyze → Lower → Produce pipeline — closed 2026-11-04; [#386](https://github.com/scoizzle/Poly/issues/386) carries the state-of-union follow-ups. The slice plan is the draft pull-request body. The first pinned pull-request comment that starts with `PLAN |` freezes that body as mill plan-first proof. Do not add plan or task files under `docs/`; open or update a GitHub issue. Plans removed on 2026-11-10 stay in git history at `067bde75`.
- **Review:** adversarial → [`docs/agent/phenomenal-review.md`](docs/agent/phenomenal-review.md) (severity: `bug` | `suggestion` | `nit`, §6; every `bug` ships a validated failing test in the report, §3.10 — no red test, no bug). Follow-ups are evidence until a human files them as a GitHub issue. Pre-ship → [`docs/agent/pr1-uncommitted-review-gate.md`](docs/agent/pr1-uncommitted-review-gate.md) (fail closed; 🔴 and 🟠 block, per phenomenal §6; pr1 Step 6; suite green).
- **Doc roles:** CORE = machinery map · `docs/decisions/` = why · `docs/reference/` = design records and research notes an issue may name · `docs/agent/` = protocols. A CORE mechanism change updates CORE in the same change.

## C# surface

`Poly/Interpretation/CSharp/` is a reader *and* a writer, and everything it accepts must execute on the VM.

- One grammar table drives reader and writer. A pattern-shape change updates the writer's positional fills in the same change.
- A new `Node` lands in lockstep with its `NodeFacts` row and its `DirectVmAbiEmitter.CompileNodeInner` arm (plus a `CompileValue` arm if it is a value).
- Console-only work leaves the DSL parser, the exporter, and the golden corpus untouched.
- Fail closed: a form that cannot execute soundly throws a positioned error; it never folds to a wrong shape.
- Tests ship with the feature, `Write(Read(x))` stays idempotent, and every feature gets its fail-closed case.

## Branching and PRs

- Base a feature branch on `origin/master`, not on a local `master` that may be behind. Exception: a stream slice bases on `origin/stream/<name>`.
- Never stage or commit files you do not own, or pre-existing modifications you did not make. If a file is already dirty when you start, leave it dirty; do not `git add` in the user's tree.
- A PR contains the task's work only. Check `git status` against the PR diff before you push.
- Removing a worktree whose untracked `bin/`/`obj/` blocks the delete needs `force`, and only with the user's confirmation.
