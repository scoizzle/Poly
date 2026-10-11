# Poly convergence: running the remaining slices from a local agent

Single entry point as of 2026-10-10 21:33 CT. The bot pipeline (Foreman, 100x, Grug, mill runs) is suspended. Scot runs each slice himself with a local coding agent (Cursor, Claude Code or similar) in his own clone of `scoizzle/Poly`. Nothing here needs a bot, `mill.sh` or a box path.

## 1. What the plan is

1. Poly has two ways to run a domain: the old interpreter (DEI, `DomainEntityInstance`) with its own create, constraint, stage and link rules, and the compiled tree that prints C#.
2. The convergence plan ([`pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md)) moves each behavior onto the compiled tree, one small PR (a "slice") at a time.
3. When it is done there is one source of truth, so simulating a domain and the generated C# cannot drift apart.
4. Finish line (Scot, 2026-10-09 18:56 CT): C8d deletes the DEI class and C10 proves the interpreter runs exactly the compiled artifact.
5. The C8-tc to C8-clean slices were added on 2026-10-10 so the printed C# meets the production bar before the DEI goes.

## 2. Current state

- `master` (updated 2026-10-10 ~22:30 CT) has everything on the trimmed path through C7d: C7d `ef8f267b` (#159), the C8-vm plan `dafa6a72` (#158), this handoff (#160) and PR 155's docs (C8-lc, C8-clean, C8-num, C8-web task files, the C8-tc family plan cards and rulings). Earlier: C7c `78231090`, C5b `60089ef0`, C7b `67427b72`, C6b1 `931b1461`.
- Open PR: #157 C6c (implemented at `5cd51cd4`, CI green, **review NOT SHIP**: major R1, `DomainInstanceStore.Remove` calls `Unlink`, so the new owned-minimum guard throws during create-in rollback and leaves the child linked; nit R2, duplicated contract lookup in `Link`/`Unlink`). Fix R1 (and R2) on `slice/c6c-plan`, then re-review.
- `tasks/C8-tc.md` was not kept; `tasks/C8-tc1.md`, `C8-tc2.md` and `C8-tc3.md` replace it. The C8-tc plan card in `pipeline-convergence-plan.md` notes the split.
- Reviews behind the C8 work: [`research/2026-10-10-codegen-review.md`](research/2026-10-10-codegen-review.md) (finding ids K0, G1–G22, D1–D18) and [`research/2026-10-10-analysis-review.md`](research/2026-10-10-analysis-review.md) (VM-analyzer triage, used by C8-vm).
- Stopped C8-tc work-in-progress: [`research/c8-tc-wip.patch`](research/c8-tc-wip.patch) (93 KB, 15 files). It applies cleanly at `931b1461` but not on current master (`DomainToCSharpExporter.cs` and `DomainEntityInstance.HostAbi.cs` conflict). Use it as a reference or apply it with `git apply --3way`.

## 3. Remaining slices, in order (19)

Each slice's `Depends` line is authoritative. The task file is listed where one exists. "Plan card" means the card in `pipeline-convergence-plan.md` (search for `**<id>.`). Write the task file from that card if none exists.

| # | id | Goal | Depends (from task file or card) | PR / branch / task file | Done test | Risk |
|---|----|------|-------------------------------|-------------------------|-----------|------|
| 1 | C6c | Unlink refuses to drop an owned child's last owner (min 1); non-owned links stay min 0 (Scot 2026-10-10) | C6b (C6b1 merged #151) | PR 157 (implemented; NOT SHIP, fix R1/R2), `slice/c6c-plan`, `tasks/C6c.md` on branch | `DomainInstanceStoreFailClosedTests.Unlink_OwnedTarget_LastOwner_Throws` red on master, green after; MCP `unlink_instances` row; no golden change | M |
| 2 | C7d | Last `GetOrAnalyze` sites in `Runtime/` read one `Analysis(Domain)` accessor | C7c (merged `78231090`) | **Merged** `ef8f267b` (#159); `tasks/C7d.md` | `git grep GetOrAnalyze -- Poly/DomainModeling/Runtime` empty; suite green | S |
| 3 | C8-tc1a | Lowering emits typed by-type `Type.Create(...)` returning `DomainResult<T>`; interpreter binds static `Create` onto its dictionary instances (K0) | C6a, C6b1 (merged) | `tasks/C8-tc1a.md` (master); no branch yet | no string-named `this.Create("` in goldens; `ParityTests` green | M |
| 3b | C8-tc1b | `create in` lowers to `this.Create{Nav}(...)` (G1) | C8-tc1a | `tasks/C8-tc1b.md` (master) | no string-named `this.CreateIn("` in goldens; G1 golden clean | M |
| 4 | C8-tc2 | Delete `StoreBind.cs`; real `EnsureUnique` (G3/D1); side-effect-free typed probe (G2) | C8-tc1 | `tasks/C8-tc2.md` (master) | no `new Dictionary<string, object` in goldens; duplicate unique fails; failed probe attaches nothing | L (split into tc2a/tc2b if it stalls) |
| 5 | C8-tc3 | G4/D15 event create, G6 + folded C6b2 self-rel, G8 singular inverse, G9 public `Create{Nav}`, G12 failed `.Value` | C8-tc2 | `tasks/C8-tc3.md` (master) | each cited golden clean; printed C# compiles | M |
| 6 | C8-vm | Fix 4 VM-analyzer false positives and 5 real bugs, wire `ThrowIfVmHasErrors` into `Emit`, drop the allowlists, compile probes with warnings as errors | C8-tc2 (Foreman 2026-10-10: after tc2) | `tasks/C8-vm.md` (master, plan merged #158) | 0 VM errors and 0 Roslyn warnings on every sample; `Emit` refuses VM errors | M-L |
| 7 | C8-lc | Lifecycle in printed C#: G5, G7, G10, G17, G21, D10, D12 | C8-tc | `tasks/C8-lc.md` (master) | `Create` never runs first-stage entry or throws; one lifecycle enum on Order; goldens clean | M |
| 8 | C8-clean | Remaining nits plus D2 (contract adapter = debug trace + success for now). *Optional before C8d* (see note below) | C8-lc | `tasks/C8-clean.md` (master) | each cited golden clean; `Billing.Charge` succeeds | M |
| 9 | C8-num | A `Number`'s C# type comes from its applied constraints: integral = `long`, fractional bounds or precision = `decimal` (D8) | C8-clean (ordering for golden stability only) | `tasks/C8-num.md` (master) | money properties print `decimal`, integral `long` | S-M |
| 10 | C8-web | Web API host and DbContext: D3, D4, D5/G13, D6, G14, D16, D17. *Optional / parkable: not on the DEI-retirement path* | C8-num | `tasks/C8-web.md` (master) | one route per action; `Opportunities`; 400 vs 409 split | M |
| 11 | K2 | Session tables passed into lowering; delete the Domain-keyed cache lookups (~12 sites). **Runs alone** | PRs 82/83, C0 (merged) | plan card only | lowering tests and goldens byte-identical | High (many lowering files) |
| 12 | C8-pre | One forwarding test helper for `DomainEntityInstance.Create`, applied by a re-runnable scripted replace. **Runs alone** | C2b (merged), C7d | plan card only | re-running the command reproduces the diff; same test counts | Low (mechanical, large) |
| 13 | C8a1 | Session-aware storage type with no rules and no ontology references | C5b (merged), C6c, C7d, V2 (answered) | plan card only | storage structure test passes; link/unique/cardinality/notify parity green with a plain store | High |
| 14 | C8a2 | Dictionary-backed instance from a `TypeDefinitionNode` | C8a1, C8-tc (tc1–tc3) | plan card only | the factory runs the **typed** compiled Create tree for a sample domain; no `Domain` parameter | High |
| 15 | C8b | Point the C8-pre helper and `Create` at the new factory | C8a2, C8-pre | plan card only | whole suite passes through the helper, counts unchanged | M |
| 16 | C8c1 | MCP read tools (`get_instance`, `list_instances`, `evaluate_policy`, `oracle_expression`) move over | C8b | plan card only | MCP smoke tests pass, outputs unchanged | M |
| 17 | C8c2 | MCP mutating tools (`create_instance`, `link_instances`, `unlink_instances`, `invoke_action`) move over | C8c1 | plan card only | MCP smoke + G1 refuse-on-Errors tests pass | M |
| 18 | C8d | Delete the DEI class and leftovers, including C2a's default internal store. **Runs alone** | C8c2, T3, C8-lc, C8-web, C8-num, C8-clean | plan card only | `git grep DomainEntityInstance` empty in product code; parity suite passes | M |
| 19 | C10 | End check: the interpreter runs exactly the catalog artifact (node identity / structural hash) | C1c, C3b, C2b, E1 (merged), C8d | plan card only | a deliberately added rewrite step makes the test fail | M |

**Dependency conflicts Scot must settle (unverified as resolved):**
- C8d's card on master depends on **T3**, but the 2026-10-09 trim parked T3. Either un-park T3 (mutation-by-invariant matrix; depends on C4d and M1, merged, and C6c) or remove T3 from C8d's Depends. Without one of those, C8d cannot start as written.
- C8-web and C8-clean are listed as optional, but C8d's card also depends on them, and C8-num depends on C8-clean. Parking either one means editing C8d's Depends line (and C8-num's, if C8-clean is parked) in the same PR that parks it.
- The parent request listed C8-num → C8-web → C8-clean. The task files chain C8-lc → C8-clean → C8-num → C8-web (file overlap). The table follows the task files.
- In the C8-tc split, G2 moved from tc3 to tc2 (deleting `StoreBind.cs` removes the probe), and G9 and G12 moved from tc2 to tc3 (size). C6b2 is placed in tc3. These are handoff choices; adjust freely.
- C8-tc1 was split into C8-tc1a and C8-tc1b (2026-10-10) for the same reason: tc1 also needs the interpreter static-factory bind, so it is 6 files, not 3. The seam is **by context**, not by finding: a by-type create in a void context lowers to `this.Create{Nav}`, so tc1a is action-result `Type.Create` and tc1b is `create in` **plus** the void-context by-type create. See the parent card [`tasks/C8-tc1.md`](tasks/C8-tc1.md).

Out of the finish line and not listed: K3c, K3a, K3b and K5 come after C8d in §12 and are not needed for C10. Parked by the trim: F2–F6, R1, R2, A6, A7, H5, T3, Q1b, N1. Old `slice/*` branches (`a3b`, `a4-analysis-report`, `a5a-contributor-artifacts`, `c4e`, `m1`, `n4`, `c6b2-plan`) are leftovers. Their slices merged or were folded; that was not checked branch by branch.

## 4. Standing rules (carry into every slice)

- `Dictionary<string, object?>` instances belong only to the interpreter. The compiled tree and the printed C# express real typed semantics.
- The printed C# must be as good as, or better than, hand-written production C#. Every codegen finding is fixed, nits included.
- Decision 10 (no auto-link guessing) does not cover an explicit `create in`. The single back-reference still auto-fills.
- Function bodies are a single expression (decision 13).
- Database libraries register their own DbContext artifact (decision 19).
- `Text + Text` concatenates, and the result is `Text`.
- MCP and the DSL enforce identical validation.
- C6c: an owned child must keep its owner (min 1, unlink rejected). Non-owned links stay min 0 (Scot 2026-10-10).
- Code is truth. Re-check every file:line in a task file against current master before editing (line numbers drift every merge).
- Repo rules in [`AGENTS.md`](../../../AGENTS.md) win: minimal diffs, TUnit `async [Test]`, `Method_Condition_ExpectedResult` names, no `#region`, no drive-by comments. Use `git grep`, not `rg` (`rg` has hung in these clones).

## 5. How to run one slice

1. `git fetch origin && git switch master && git pull`. Pick the first row in §3 whose Depends have all merged.
2. Read `docs/plans/poly-eng/tasks/<id>.md`. If it is only on a branch, `git show origin/<branch>:docs/plans/poly-eng/tasks/<id>.md`. If there is none, write one from the §3 row and the plan card, using [`tasks/_TEMPLATE.md`](tasks/_TEMPLATE.md): Scope, Files, Shape matrix, Done when, SHIP/NOT SHIP, Tests, Depends, finding ids with file:line.
3. Branch: `git switch -c slice/<id>` (or check out the existing plan branch from §3 and rebase it on master).
4. Implement the smallest readable change that meets **Done when**. Scot must be able to open and hand-edit it cold.
5. Build and test:
   - `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
   - targeted: `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/<TestClass>/*'`
   - if the slice changes printed C#: `POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'`, then rerun without the variable and read the golden diff. Never hand-edit a golden.
   - full suite: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (the repo uses Microsoft.Testing.Platform; `AGENTS.md` says use this, not `dotnet test`). CI runs the same command.
6. Add a Log row to the task file, push, and open a PR titled `<id>: <goal>`, with the test count and the exact test commands in the body.
7. Review on a **different model** than the one that wrote it. Ask for one table covering every finding (`id | severity | file:line | finding | fix`), plus the hand-edit check. Fix every finding in one push, then have the same reviewer verify only those findings and real regressions.
8. Merge when CI is green and the review says SHIP.

**Tips learned:**
- Keep a slice to about 3 source files, or split it. Large slices hang agents (C8-tc froze four times before it was split).
- Have the agent commit after each item, so a hang loses minutes, not the slice.
- "Runs alone" slices (K2, C8-pre, C8d) should have no other PR open while they run.
- If a task file's line numbers have moved, say so in the PR and use the current ones.

## 6. Copy-paste prompt for the local agent

```
Do the next slice in docs/plans/poly-eng/LOCAL-AGENT.md.

1. Read LOCAL-AGENT.md (sections 3 to 5) and AGENTS.md. Pick the first row in section 3 whose Depends have all merged on origin/master (check with git log). Tell me which slice you picked and why before editing.
2. Read its task file (on master, or with git show from the branch named in the table). If there is none, write one from the table row and the plan card in docs/domain-modeling/pipeline-convergence-plan.md, using tasks/_TEMPLATE.md, and commit it first.
3. Re-check every file:line it cites against current master with git grep (not rg). Report anything that moved.
4. Work on branch slice/<id>. Make the smallest readable change that meets "Done when". Follow the standing rules in section 4. Commit after each item. If the change grows past about 3 source files, stop and propose a split.
5. Build (dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj), run the targeted tests from the task file, and regenerate goldens with POLY_UPDATE_GOLDEN=1 if printed C# changed (never hand-edit a golden). Then run the full suite: dotnet run --project Poly.Tests/Poly.Tests.csproj.
6. Add a Log row to the task file, push, and open a PR with the goal, the test count and the exact test commands. Do not merge. Stop and tell me the PR URL so I can have a different model review it.
```
