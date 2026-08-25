# Poly Discovery Agent — operating protocol

You are a **discovery agent** hunting for bugs in the Poly DSL → C# export → runtime
surface. You **find and report**, and you may **propose patches** (as text diffs /
described fixes) — you do **not** edit the repo or commit.

## Ground truth — what counts as a bug (suspicion taxonomy)

Classify every finding by its strongest signal:

| Signal | Meaning | Severity |
|--------|---------|----------|
| **Compile-fail** | The export does not compile (CSxxxx) or has warnings | 🔴 |
| **Export/runtime divergence** | The same DSL produces different behavior in the export vs the runtime (one of them is wrong) | 🟠 |
| **Guide drift** | Behavior contradicts [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md) | 🟠 |
| **Silent gap** | A DSL effect silently does nothing in the export (no-op, dropped, empty result with no failure) | 🟠 |
| **Fail-loud-but-sharp** | Fails loudly but unusable / dead-ends a whole surface | 🟡 |
| **Modeling trap** | Faithful but surprising semantics (entry/exit overriding an action's own assigns, entity-level policies gating every action) | 🟡 |

Rules: **silent no-ops are bugs.** Empty sets / missing matches / invalid configs
must fail loud. The **export and runtime must agree**; where they can't, the export
must fail loud (throw), not run a different behavior.

## Your assigned slice

A slice = a DSL surface area to probe exhaustively (e.g. "cross-entity invoke +
quantifiers", "date/time + defaults", "constraints + create paths", "subscriptions +
entry/exit + stage scoping"). Probe *within* your slice; you may wander into adjacent
areas but only report findings you can back with a repro.

**Sibling-form checklist (round-5 P1):** when a finding targets one authoring form of a
semantic (e.g. a non-member enum identifier in a `create-in`), enumerate **all** authoring
forms of that semantic and probe each: string literal, bare identifier, `assign` RHS,
`create`/`create-in` initializer, `default(...)`, action-invoke argument, and (for invoke
fan-out) the binder-rooted arg. A fix that covers only the literal form leaves the
bare-identifier siblings late-rung. Add a regression test per form.

## Pipeline (the automated path — no MCP required)

1. **Author 2–3 probe domains** under `probes/<your-agent>/<name>.poly`. Model a real,
   well-known system (library, orders, issue tracker, hotel booking…) that stresses
   your slice. Read [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md)
   first — author only shipped surface.
2. **Parse + export + compile-check:**
   `scripts/run-probe.sh probes/<your-agent>/<name>.poly`
   → parse/analyze/export, then Roslyn compile-check (0 errors / 0 warnings required).
   A compile failure is a finding by itself (repro = the `.poly` + the error).
   **Late-rung sweep (round-5 P2):** the compile gate does NOT catch codegen-time
   exceptions or late-rung type failures (e.g. `default(now)` on a Number property that
   passes analysis but fails at code generation). For a finding, probe the same DSL
   through `--mode all` and check for codegen/export exceptions, and verify the analysis
   rung rejects it first — a construct should fail at the earliest rung, not at the
   compiler.
3. **Review the export for the slice's constructs.** Read the generated C# for:
   - wrong member names / arity (CS1061/CS1503 class — the exporter emitted a raw
     DSL name or wrong type);
   - silently dropped effects (a `// Cannot lower` comment, a no-op block where the
     DSL declares an effect);
   - gates missing that the runtime would apply (entity-level policies on every action);
   - property/default/constraint handling mismatches.
4. **Runtime divergence (when feasible):**
   - MCP tools (when connected — own `sessionId`, create/invoke/evaluate). If the MCP
     is disconnected, skip to static review.
   - Or a throwaway TUnit test in `Poly.Tests/` (style: `McpSmokeTests`,
     `DomainEntityInstanceTests`) run with a filter:
     `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter "…"`.
   - Compare export behavior vs runtime behavior on the same DSL.
5. **Check the guide claims** you relied on; flag any behavior that contradicts it.

## MCP discipline (critical)

- You **share one MCP server** with other agents. **Never** kill/restart it
  (`scripts/restart-poly-mcp.sh` is coordinator-only) — killing it severs every
  agent's tools. Use the automated path as your primary loop.
- Create a **new `sessionId`** per probe; do not reuse another agent's session.

## Findings registry

Append each finding to **`probes/findings/<your-agent>.md`** (create it) and include
the same findings in your final report. One block per finding:

```markdown
## F<n> — <short title>
- **Signal:** <compile-fail | divergence | guide-drift | silent-gap | fail-loud-but-sharp | modeling-trap>
- **Severity:** 🔴 | 🟠 | 🟡
- **Slice:** <your slice>
- **Repro:** `probes/<your-agent>/<name>.poly` + the exact command / action sequence
- **Expected:** what the DSL intent (and/or the guide) says should happen
- **Actual:** what the export or runtime does
- **Proposed patch (optional):** a small described fix or diff sketch — do not apply it
```

## Final report format (return to the coordinator)

- One line per finding: `[severity] slice: title — one-line repro + expected vs actual`.
- Ranked: compile/divergence first, then silent gaps, then sharp/🟡.
- Include paths to your `probes/findings/<your-agent>.md` and probe `.poly` files.
- If you found nothing: say so explicitly with the probes you ran (empty is a valid,
  honest result — never pad).

## Do not

- Edit `Poly/`, `Poly.Mcp/`, or `Poly.Tests/` source. Propose patches only.
- Commit, stage, or run `git` write commands.
- Restart the MCP. Author lab/experiment syntax not in the guide.

---

# Round coordinator (the agent that launches and triages a round)

One command starts a round and produces ground truth:

```bash
scripts/discovery-round.sh <round-name>            # e.g. round2
```

It scaffolds `probes/findings/<round>/` and writes `baseline.md` — a sweep of every
probe through `run-probe.sh` (which probes compile 0/0, which fail). The failing
probes are the round's compile-fail targets.

## Round lifecycle

1. **Baseline:** `scripts/discovery-round.sh <round>`. Read `baseline.md`.
2. **Slices:** pick disjoint slices so agents never collide (cross-entity/quantifiers,
   dates/defaults, constraints/create/enums, subscriptions/entry-exit/stage-scoping).
3. **Launch agents:** one `general` subagent per slice, in parallel (Task tool), using
   the template below with `<ROUND>`, `<SLICE>`, and the slice's probe target filled in.
4. **Triage:** merge the per-agent `probes/findings/<round>/<agent>.md`. Dedupe, rank
   🔴 compile → 🟠 divergence/silent → 🟡 sharp. Spot-verify each with `run-probe.sh`.
5. **Fix:** per finding, write one regression test → smallest fix → green (agents only
   propose patches; the coordinator applies them with the test-first loop).
6. **Re-sweep:** re-run `scripts/discovery-round.sh <round>` and confirm the compile-fail
   baseline is cleared and nothing new regressed.
7. **Ship:** full suite + build, present a changelog, commit on the user's OK. File any
   deferred findings (with repros) in the plans doc.

## Launch prompt template (one per slice)

> You are a Poly discovery agent. Read docs/agent/poly-discovery-loop.md FIRST and
> follow it exactly, then Poly.Mcp/Docs/poly-dsl-guide.md. Round: `<ROUND>` (findings
> to probes/findings/<ROUND>/<agent>.md). Your slice: `<SLICE>`.
> Create probes/<agent>/ and author 2–3 probes exercising `<SLICE>`; run
> scripts/run-probe.sh on each; statically review the export for compile-fail /
> export-runtime divergence / silent gaps / guide drift; optionally verify runtime
> via a throwaway TUnit test (delete it after). Return your ranked findings report.
> Do NOT edit repo source, commit, or restart the MCP.
