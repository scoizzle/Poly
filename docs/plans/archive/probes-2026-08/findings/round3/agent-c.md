# Discovery-c findings — round 3 (ADVERSARIAL: CONTROL-FLOW + COMPOSITION PATHOLOGIES)

Agent: `agent-c`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../../docs/agent/poly-discovery-loop.md).
Round: 3 (findings to `probes/findings/round3/agent-c.md`).
Slice: control-flow + composition pathologies — same-stage transitions, transitions inside
entry/exit, chained transitions, recursive/mutual invoke, deep conditionals, empty `if`
branches, `require`-path failures, subscription pathologies, create/create-in inside
if/entry/exit/subscriptions, invoke arity and self-invoke-with-args.

Probes (new, round 3) under `probes/agent-c/`:
- `samestage.poly` — `transition to Active` while in Active (entry increments a counter).
- `chained.poly` — A→B→C in one action.
- `entrytrans.poly` — `entry { transition to B }` + action transitions to A.
- `nestedentry.poly` — action Draft→Active where Active.entry → Done.
- `recursive.poly` — self-invoke + mutual invoke (A→B→A) cycles.
- `deepcond.poly` — 11-way else-if, empty branches, if-with-only-transition.
- `requirepaths.poly` — `require not` on a nonexistent policy (parse-reject), `require`
  on a store-dependent quantifier policy.
- `subpaths.poly` — `when Rel Stage` where stage doesn't exist; quantifier on singular nav;
  duplicate subs; nested nav under peer binder (all rejected — fail-closed).
- `subdup.poly` — two identical `when targets Active` subscriptions.
- `subinvoke.poly` — `invoke` inside a subscription handler.
- `createpaths.poly` — create-in inside if / entry / exit / subscription.
- `invokeargs.poly` / `selfargs.poly` — missing/extra invoke args; self-invoke with args.
- `reqquant.poly` — `require` on `count children > 1` policy.
- `ifinvoke.poly` — `if (Flag is true) { invoke mark }` (export-side compiles clean and runs
  the branch; runtime drops it — see F1).

All probes pass `scripts/run-probe.sh` with 0/0 **except** `invokeargs.poly` (analysis
reject: missing/extra bindings — correct fail-closed), `requirepaths.poly` (parse reject:
`require not` nonexistent policy — correct fail-closed), `subpaths.poly` (parse reject:
nonexistent stage + nested peer nav — correct), `subquant-singular.poly` (DMSS003 reject —
correct), and `subdup.poly` (**compile-fail** — finding F8).

Runtime evidence: throwaway TUnit probes + standalone harness (MCP tool layer, same
session/runtime path as `McpSmokeTests`); all throwaway tests deleted. Note: another agent's
in-progress file (`Poly.Tests/DiscoveryAgentB2/TypeAbuseRuntimeTests.cs`) was blocking the
test project build, so isolation runs used a standalone console harness referencing `Poly` +
`Poly.Mcp` (assembly-aliased `Poly.Tests` for InternalsVisibleTo). The harness is in
`/var/folders/.../T/opencode/c3probe/` (deleted after use).

---

## F1 — `if (…)` blocks silently DROP transition / invoke / create-in effects at runtime; only `assign` runs
- **Signal:** silent-gap + export/runtime divergence
- **Severity:** 🟠 (top of round — silent wrong data on a shipped effect form)
- **Slice:** control-flow (conditional effects in action/entry/exit)
- **Repro:** standalone runtime harness on
  `if (Flag is true) { transition to B }` → stage stays `A`;
  `if (Flag is true) { invoke mark }` → `mark` never runs (Status unchanged);
  `if (Flag is true) { create in children { Name: "if_child" } }` → children count `0`.
  Control: `if (Flag is true) { assign Status to "cond_true" }` → **works**.
  (Full domains in `probes/agent-c/deepcond.poly` `ifonlytrans`/`empty`; runtime exercised
  both `Flag=true` and `Flag=false` cases.)
- **Expected:** the guide ships `if (expr) { effects } [else …]` for action/entry/exit
  (`Poly.Mcp/Docs/poly-dsl-guide.md` §6, §9). The export **does** run the branch: it emits
  `if (this.Flag) { this.mark(); }` / `if (this.Flag) { this.CurrentStage = ItemStage.B; }`.
  The runtime must run the same branch.
- **Actual:** `ExecuteEffect` (`DomainEntityInstance.cs:478-494`) prefers the VM path:
  `EffectLoweringPass.Conditional` (`EffectLoweringPass.cs:312-336`) routes each sub-effect
  via `Route(sub)`; `StageTransition`/`InvokeAction` return `null` when
  `_lowerStageTransitions` is false (runtime mode), and `Conditional` converts those to
  `Comment` placeholder nodes. The `IfStatement` lowers to a VM node, so `EffectExecutor.Run`
  is never reached for the branch — the transition/invoke/create inside the `if` becomes a
  **silent no-op**. `assign` survives because `AssignEffect` VM-lowers even in runtime mode.
- **Proposed patch (not applied):** in `Conditional` (or `ExecuteEffect`), when a branch
  sub-effect does not VM-lower, fall back to direct execution (`EffectExecutor`) for that
  branch, or reject non-`assign` effects inside `if` at analysis (fail loud). Round-2 F4
  (create in conditional) is the same family; this extends to **transition and invoke**.

## F2 — Same-stage `transition to Active` from Active: export re-runs exit+entry effects; runtime short-circuits silently
- **Signal:** export/runtime divergence (silent)
- **Severity:** 🟠
- **Slice:** transitions (same-stage)
- **Repro:** `probes/agent-c/samestage.poly` — `Active: stage { entry { Counter += 1 } exit { Status = "exited" } refresh: action { transition to Active } }`.
  Runtime (TUnit): after `refresh` on a fresh instance, `Counter` stays `1`. Export emits:
  `this.Status = "exited"; this.Counter = this.Counter + 1L; this.CurrentStage = ItemStage.Active;`
  → `Counter` would become `2`.
- **Expected:** identical behavior on both paths (either both short-circuit a same-stage
  transition, or both re-run exit+entry). The guide is silent on same-stage transitions.
- **Actual:** `TransitionStage` (`DomainEntityInstance.cs:678-679`) early-returns when
  `targetStageName == CurrentStage` — a **silent no-op**. The export inlines exit+entry for
  every transition regardless of source==target (`EffectLoweringPass.StageTransition`,
  `EffectLoweringPass.cs:196-255`), so the two paths diverge on any same-stage transition
  with exit/entry side effects.
- **Proposed patch (not applied):** either short-circuit same-stage transitions in the
  exporter (mirror runtime), or reject `transition to <current-stage>` at analysis
  (fail loud).

## F3 — Chained transitions A→B→C in one action: export runs the SOURCE stage's exit TWICE and never B's exit; runtime interleaves correctly
- **Signal:** export/runtime divergence (silent wrong effects)
- **Severity:** 🟠
- **Slice:** transitions (chained)
- **Repro:** `probes/agent-c/chained.poly` (and `ChainExit` harness with exit/entry logging).
  Export `go()`:
  ```
  Log += "|exitA"; Log += "|entryB"; CurrentStage=B;
  Log += "|exitA"; Log += "|entryC"; CurrentStage=C;   // exitA again, exitB never
  ```
  Runtime `go()`: `|exitA|entryB|exitB|entryC` (TUnit assertion passed with this value).
- **Expected:** A→B runs `exit A, entry B`; then B→C runs `exit B, entry C`. Both paths must
  run the same interleaving.
- **Actual:** `EffectLoweringPass.StageTransition` always lowers the **action's** source stage
  exit (`_sourceStageName`) for every transition in the body, so the second hop reuses A's
  exit and never emits B's. The runtime `TransitionStage` correctly exits the current stage
  at each hop. Export silently skips B's exit and double-runs A's.
- **Proposed patch (not applied):** after the first lowered transition, update the
  "current stage" used as the source for subsequent transitions in the same action body, or
  fail loud on multiple transitions per action.

## F4 — Transition inside an entry effect: export ends in the WRONG final stage (outer transition overwrites the nested one)
- **Signal:** export/runtime divergence (round-2 F2 — **re-confirmed still open**)
- **Severity:** 🟠
- **Slice:** entry/exit effects (nested transitions)
- **Repro:** `probes/agent-c/entrytrans.poly` and `nestedentry.poly`.
  - `entrytrans`: `A.entry { transition to B }`, `go: action { transition to A }`. Export:
    `{ Status="in_b"; CurrentStage=B; } CurrentStage=A;` → **ends A, Status "in_b"**.
    Runtime: `TransitionStage(A)` short-circuits (same stage) → stays A, Status untouched.
  - `nestedentry`: `go: Draft→Active`, `Active.entry { transition to Done }`. Export:
    `{ Status="in_done"; CurrentStage=Done; } CurrentStage=Active;` → **ends Active**.
    Runtime (TUnit `NestedEntryTransition`): ends **Done** (nested transition wins).
- **Expected:** the nested transition must win (runtime does this: `TransitionStage` sets
  `CurrentStage=target` first, then runs entry; entry's transition re-targets).
- **Actual:** export inlines target entry effects BEFORE the `CurrentStage = target`
  assignment (`EffectLoweringPass.StageTransition`, lines 232-245), so a nested transition
  inside entry is overwritten by the outer assignment. Same root cause as round-2 agent-c F2
  (still open; the round-2 fix commit did not cover it).
- **Proposed patch (not applied):** set `CurrentStage` to the target before inlining entry
  effects (mirror runtime ordering), or detect a nested transition inside entry/exit and
  fail loud.

## F5 — Recursive / mutual invoke: runtime fails loud with a depth message; export emits UNGUARDED recursion → StackOverflowException
- **Signal:** export/runtime divergence (export crashes opaquely; guide-drift)
- **Severity:** 🟠
- **Slice:** invoke (recursive cycles)
- **Repro:** `probes/agent-c/recursive.poly` — `tick { Depth+=1; invoke tick }` (self) and
  mutual `b→a→start→b`. Export:
  ```csharp
  public DomainResult tick() { this.Depth += 1L; this.tick(); ... }   // no guard
  ```
  Runtime (TUnit): `InvokeAction("tick")` → `Success=false`, message contains `depth`
  (runs at depth 16 via `MaxInvokeDepth`, `DomainEntityInstance.cs:310-311`).
- **Expected:** guide §6: "Nested invoke depth is limited (max 16); recursive cycles fail
  loud." Both paths must fail loud (or export must also fail loud).
- **Actual:** the exporter emits a bare `this.Action()` call with **no depth counter**
  (`EffectLoweringPass.InvokeAction`, `EffectLoweringPass.cs:289`), so the exported C#
  stack-overflows instead of failing loud. Runtime behavior is correct.
- **Proposed patch (not applied):** emit a depth-guard wrapper in exported invoke chains
  (mirror runtime `MaxInvokeDepth`), or note the export cannot honor the limit.

## F6 — `invoke_action` with missing required args at runtime silently SUCCEEDS and writes `System.Reflection.Missing`
- **Signal:** silent-gap + divergence (DSL authoring fails closed, runtime tool path does not)
- **Severity:** 🟠
- **Slice:** invoke (arity)
- **Repro:** `invokeargs.poly` — `set: action (s: Text, n: Number)`. DSL-level
  `invoke set(s: "x")` is **rejected at analysis** ("missing required parameter binding 'n'")
  — correct fail-closed. But the **runtime tool** path `RuntimeTool.InvokeAction(sid, id,
  "set")` (no args) returns `Success=true` and the property `Status` becomes
  `System.Reflection.Missing` (TUnit harness: `Prop(Status)` printed `System.Reflection.Missing`).
  Extra args are silently ignored (`set` with `s`+`n`+extra `x` → works, extra dropped).
- **Expected:** the runtime `invoke_action` must fail loud on missing required parameters
  (mirror the analysis-side `invoke` binding contract and the export, which requires all
  params at compile time).
- **Actual:** `InvokeAction` (`DomainEntityInstance.cs:307-332`) injects whatever args are
  present; no arity check against `action.Parameters`. Missing params stay unresolved and the
  assign stores the sentinel `Missing` object into the property.
- **Proposed patch (not applied):** validate required parameters against `action.Parameters`
  in `InvokeAction` and return a loud failure when any are missing.

## F7 — Self-invoke with an action-parameter argument evaluates the arg to `1` at runtime (export forwards it correctly)
- **Signal:** export/runtime divergence (silent wrong value)
- **Severity:** 🟠
- **Slice:** invoke (self-invoke with args)
- **Repro:** `probes/agent-c/selfargs.poly` — `outer(x) { invoke set(m: x) }`, invoked with
  `x="inner"`. Export: `this.set(x);` (correct). Runtime (TUnit harness): `Status` becomes
  `"1"` instead of `"inner"`.
- **Expected:** the outer action parameter `x` must flow into the invoked action's `m`
  binding on both paths (export does).
- **Actual:** runtime invoke parameter bindings referencing an enclosing action parameter
  resolve to a garbage value (`1`) — the same root signature as round-2 agent-b F-R2-1
  (action params in create-in initializers → `1`), here surfaced through self-invoke arg
  bindings. Literal bindings (`invoke set(m: "literal")`) and property bindings
  (`invoke set(m: Status)`) work.
- **Proposed patch (not applied):** the invoke-arg binding compile path must resolve the
  caller's action parameters (parent type provider) before evaluating bindings.

## F8 — Duplicate subscriptions for the same Rel+Stage: analysis accepts, export emits duplicate method names → CS0111 (opaque)
- **Signal:** compile-fail (fail-loud-but-sharp — DSL accepted, export message is misleading)
- **Severity:** 🟡
- **Slice:** subscriptions (duplicates)
- **Repro:** `probes/agent-c/subdup.poly` — two `when targets Active { … }` subscriptions.
  `scripts/run-probe.sh` fails:
  ```
  error CS0111: Type 'Subscriber' already defines a member called 'WhenTargetActive' …
  error CS0121: The call is ambiguous … 'Subscriber.WhenTargetActive()' …
  ```
  Export generated two `internal void WhenTargetActive()` methods (both subscription bodies).
- **Expected:** two subscriptions on the same Rel+Stage is a plausible authoring error; the
  analyzer should reject it with a clear DSL-level message (or the exporter must de-duplicate /
  rename handlers). The current failure tells the author nothing about duplicate subscriptions.
- **Actual:** no analysis rejection; the exporter emits same-named handler methods and the
  project fails CS0111/CS0121 with an opaque compiler message. Same class for
  duplicate `when` on the same stage with different peer binders.
- **Proposed patch (not applied):** analyze-time diagnostic for duplicate
  (Rel, Stage, scope) subscription pairs, or emit uniquely-named handlers.

## Verified-clean (no finding)
- **`require not` on a nonexistent policy** → parse-time rejection with a clear message
  (`requirepaths.poly`: "requires policy 'NonExistentPolicy' which is not defined") — correct.
- **`require` on a store-dependent (quantifier) policy** → export emits `AtLimit()` that
  throws `NotSupportedException("requires store-aware evaluation")` (fail loud, documented);
  runtime evaluates the quantifier against the store and blocks correctly when the set is
  empty (fail-closed). Both paths honest.
- **`when Rel Stage` where Stage doesn't exist on the target** → analysis rejects
  (`subpaths.poly`) — correct.
- **`when any/all Rel Stage` on a singular nav** → DMSS003 `SubscriptionContractMismatch`
  rejection (`subquant-singular.poly`) — correct.
- **Nested path-prefix under a peer binder** (`when Rel Stage as t { t sub Nested }`) →
  analysis rejects (`subpaths.poly`) — correct.
- **`invoke` inside a subscription handler** → runs on both paths (export emits
  `this.poke();` in the handler; runtime fan-out executed it). Clean.
- **create/create-in inside entry and exit effects** → creates children on both paths
  (export `CreateChildren` in inlined entry/exit; runtime created the child). Clean.
- **Deeply nested if/else-if/else** (11-way) in an action → compiles 0/0 and exports as
  nested blocks. Clean (runtime `assign`-only branches also run — F1 only breaks
  transition/invoke/create).
- **Empty `if` branches** → export emits an empty block; compiles 0/0. Clean.
- **`if` with only a transition** → export correct; **runtime drops it** (part of F1, not a
  separate finding).
- **`invoke` with extra args at runtime** → extra args silently ignored; action still runs
  (argued as part of F6).

---

## Final report (ranked)

1. `[🟠] if-blocks silently drop transition/invoke/create-in at runtime (assign-only works) — `if (Flag is true) { transition to B }` → stage stays A; export runs it. EffectLoweringPass.Conditional turns non-assign sub-effects into Comment nodes on the VM path (DomainEntityInstance.cs:478, EffectLoweringPass.cs:312)`
2. `[🟠] Same-stage transition: export re-runs exit+entry, runtime short-circuits (silent no-op) — `refresh: transition to Active` from Active; Counter 1 (runtime) vs 2 (export). TransitionStage.cs:678 early-return`
3. `[🟠] Chained A→B→C: export runs source-exit twice, never B's exit; runtime interleaves correctly — `go: { transition to B transition to C }` → export `|exitA|entryB|exitA|entryC`, runtime `|exitA|entryB|exitB|entryC``
4. `[🟠] Transition inside entry: export ends in wrong stage (nested transition overwritten) — `Active.entry { transition to Done }`, go Draft→Active → export ends Active, runtime ends Done (round-2 F2 re-confirmed open)`
5. `[🟠] Recursive/mutual invoke: runtime fails loud at depth 16, export emits unguarded `this.tick()` → StackOverflowException (guide: cycles must fail loud)`
6. `[🟠] invoke_action missing required args silently succeeds and stores `System.Reflection.Missing` — DSL `invoke` binding rejects missing params at analysis, runtime tool path does not`
7. `[🟠] Self-invoke with action-param arg evaluates the arg to `1` (export forwards correctly) — `outer(x){ invoke set(m: x) }` x="inner" → runtime Status="1"`
8. `[🟡] Duplicate subscriptions for same Rel+Stage: no analysis rejection, export emits duplicate `WhenTargetActive()` → CS0111/CS0121 opaque compile-fail (subdup.poly)`

Paths:
- Findings: `probes/findings/round3/agent-c.md`
- Probes: `probes/agent-c/{samestage,chained,entrytrans,nestedentry,recursive,deepcond,requirepaths,subpaths,subdup,subinvoke,createpaths,invokeargs,selfargs,reqquant,ifinvoke}.poly`
