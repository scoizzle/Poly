# Plan: Post–System-Review Correctness Hardening

**Status:** Active  
**Date:** 2026-07-11  
**Updated:** 2026-07-12 — M2 Done (slices 0–3); debugger StepOver fixed; post-M2: multi-property evaluate sample, affordances, naming cleanup  
**Source:** Full-system review (`/tmp/grok-review-01ce9db4.md` and module deep-dives)  
**Lens:** AGENTS.md §1–§7 + `docs/CORE.md`  
**Not this plan:** Multi-host completeness, JIT, effect framework completion, Syntax→Ast module split.

**Trust doctrine:** This plan is **trust stack layer 1** (ground truth: fail-loud, fail-closed, VM honesty) so **we as first customer** can build the real product surface through domain + modules without multiplying lies. Market platform trust is **T2**. Policy: [`docs/decisions/2026-07-11-platform-trust-bar-and-dogfood.md`](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md).

---

## Goal

Make product claims **true**: fail loud when work does not apply, fail closed when a node/op is unshipped, and pin the fixed paths with **VM-primary** tests. Prefer thin vertical slices over new subsystems.

**Done when:**

1. Instance CLR invoke on the VM matches LINQ oracle for at least one real method.
2. Domain evolution rejects missing targets (domain API, not only MCP fingerprint).
3. MCP `add_action_to_stage` description matches implementation (or behavior matches description).
4. `PolicySubject` is enforced on the product evaluate/compile path.
5. Unshipped VM node shapes cannot silently produce wrong answers (analysis Error and/or `NotSupportedException`).
6. Orphan/stale surfaces are either green with a consumer or quarantined/removed (Validation, Text, stub analyzers, MCP README).
7. One optional **effect execution** micro-slice only after (1)–(5).

---

## Non-goals

- Completing every domain analyzer or every effect kind.
- Multi-host Introspection beyond fixing load-bearing fake CLR identities that already affect the CLR path.
- Self-hosting / “domain defines Poly” (see design commentary at end of this plan’s PR discussion — not an execution milestone here).
- Growing MCP tool count or policy-evaluate-on-MCP until subjects and fail-loud evolve are solid.

---

## Work packages

Ordered for dependency and risk. Each package is one or more **§4 loops**: one failing/tightened test → smallest production fix → green → optional cleanup under green.

### WP-A — VM invoke correctness (P0)

**Status:** ✅ **Done** (vs **0.4**) — `instanceExpr` sequenced + dual-oracle instance method tests.

**Why:** Unblocks date ops, method-backed domain expressions, and honest dual-oracle for CLR interop.

| Step | Work | Status |
|------|------|--------|
| A1 | Sequence `instanceExpr` into returned expression tree before args/call | ✅ Done |
| A1b | `Convert` heap object to declaring type for `Expression.Call` | ✅ Done |
| A2 | Dual-oracle instance method (+ arg) tests | ✅ Done — VmCorrectnessTests |
| A3 | `char`/small scalar marshal only if a test goes red | ⬜ Pull |

**Exit:** ✅ Green dual-oracle instance CLR methods with receiver evaluation.

**Files:** `DirectVmAbiEmitter.cs`, `Poly.Tests/Interpretation/*`.

---

### WP-B — Fail-loud domain evolution (P0)

**Why:** Silent success breaks end-to-end honesty for every non-MCP caller; fingerprint is a band-aid.

| Step | Work | Status / tests |
|------|------|----------------|
| B1 | `RequireUpdate` when `Update*` returns false; Apply rolls back | ✅ Done |
| B2 | `UpdateAction` false when action missing | ✅ Done |
| B3 | MCP fingerprint as defense-in-depth only (optional) | Optional |
| **B4** | Inject `evalErrors` as `EVOLUTION_TARGET` Error diagnostics | ✅ Done — vs **0.1a** |
| **B5** | Child stage/property/relationship-stage missing → false | ✅ Done — vs **0.1b** + tests |
| **B6** | `RequireUpdate` on remaining ApplyTo paths | ✅ Done — vs **0.1c** |
| **B7** | Remove-by-name zero match fails loud *(optional)* | ⬜ vs **0.1d** |

**Exit (core + child transform):** ✅  
**Optional:** B7 remove-filter silent success.

**Slice 0 required Done.** Optional: **0.1d** remove-zero-match; **0.2a** README nit.

---

### WP-C — MCP `add_action_to_stage` honesty (P0)

**Status:** ✅ **Done** (vs **0.2**) — tool Description = create stage-local; code creates empty stage action; unit test locks behavior.

**Residual:** README table still says “Places an existing action” → optional vs **0.2a**.

---

### WP-D — Policy subject enforcement (P0, tiny)

**Status:** ✅ **Done** (vs **0.3**) — `Validate` on `Evaluate`; `ValidateType<T>` on `CompileVMPredicate`; invariant tests.

**Files:** `PolicyEvaluator.cs`, `PolicySubject.cs`.

---

### WP-E — Fail closed unshipped VM shapes (P1)

**Principle:** Prefer analysis Error / `NotSupportedException` over identity passthrough that yields wrong values.

| Node / path | Target behavior |
|-------------|-----------------|
| `TypeCast` / `TypeAs` / `Await` | Fail closed **or** implement correctly once dual-oracle tests exist. Default: fail closed until implemented. |
| `UsingStatement` Dispose | Implement Dispose via heap/`IDisposable` **or** fail closed (empty finally is wrong). |
| `ParameterReference` → `0L` | Fail closed unless analysis guarantees rewrite-away; do not return zero. |
| `object[]` index ABI | Fail closed for unsupported element kinds **or** fix heap marshalling with tests (only if a consumer needs it now). |

| Step | Work | Tests |
|------|------|--------|
| E1 | Inventory: which of the above appear on DomainExpression / policy / demo paths? | Grep + matrix note in PR |
| E2 | For each unshipped shape on product path: throw or diagnostic; add test expecting failure. | Negative tests |
| E3 | Optionally implement **one** high-value cast path if a domain test needs it — not a cast framework. | Dual-oracle |

**Exit:** No silent identity for the listed nodes on `Interpreter.Compile` product path.

**Files:** `DirectVmAbiEmitter.cs`, optionally a small analysis diagnostic pass, VM tests.

---

### WP-F — DiffDays / date ops (P1, after A)

Depends on **WP-A** for `Invoke` receiver correctness.

| Step | Work | Tests |
|------|------|--------|
| F1 | Lower `DiffDays` to a days-producing form (e.g. subtract → `TotalDays` cast, or explicit helper). | Lowering unit test asserts shape **and** VM eval returns expected day count |
| F2 | `AddDays` / `AddMonths` VM smoke if not already green after A. | Dual-oracle or expected numeric |

**Exit:** DiffDays is either green on VM or explicitly not supported (fail closed) — no TimeSpan-as-days trap.

**Files:** `DomainExpressionLoweringPass.cs`, lowering + VM tests under `Poly.Tests/DomainModeling/Lowering/`.

---

### WP-G — Docs and API honesty (P1, parallelizable)

| Step | Work |
|------|------|
| G1 | Rewrite `Poly.Mcp/README.md` Deprecated/V2 section to V3-only reality. |
| G2 | Remove “legacy DomainMutationIntent adapter” wording from `DomainChange` if obsolete. |
| G3 | Optional: top of `docs/ARCHITECTURE.md` hard-redirect to CORE (already labeled historical — strengthen). |

**Exit:** No agent-facing claim that V2 tools still ship.

**No new product features.**

---

### WP-H — Keep-or-kill orphans (P2)

Decision per package: **revive with one consumer + tests** or **quarantine/delete**.

| Surface | Prefer | Action |
|---------|--------|--------|
| `Poly.Validation` + commented tests | Kill or quarantine | Either one green `RuleSet` test on VM **or** CORE/README “offline / non-product”; remove from implied pipeline. |
| `Poly.Text` / `StringView` | Kill or fix+test | Fix inverted `ThrowIfGreaterThan` **if kept**; else extract/remove from core. |
| Stub domain analyzers not on pipeline | Kill | Delete or `internal` + unexported until real consumer. |
| Dead emitter helpers / unused marshaller | Kill under green | Delete unreachable `Emit*` / unused types after grep confirms. |

Do **not** expand Validation or Text as part of “hardening” without a named first consumer.

---

### WP-I — Introspection host-identity hygiene (P2, targeted)

Only the **load-bearing lie**, not multi-host completeness.

| Step | Work | Tests |
|------|------|--------|
| I1 | Stop advertising AST types as `IClrTypeDefinition` with `typeof(IDictionary<string, object>)` unless that is truly the runtime representation. Prefer host-neutral `ITypeDefinition` + explicit provider composition. | Existing type-def node tests stay green; add “not IClrType / not dictionary” if applicable |
| I2 | Defer default `PrimitiveType → GetClrType()` redesign until a **second** provider exists (principle 6). Document CLR default as intentional for sole host if I1 is enough. | — |

**Exit:** No silent assignability/codegen against dictionary for domain AST types.

---

### WP-J — First effect execution slice (P3, only after A–E)

**Thin vertical slice:** one effect kind, lower to generic Syntax, execute via VM or a clearly owned applicator — **not** a full effect engine.

Candidate order (pick one):

1. `AssignEffect` (property write on subject) — most like existing policy/CLR property path.  
2. `StageTransitionEffect` — high domain value; may need runtime instance model first.

| Step | Work | Tests |
|------|------|--------|
| J1 | Spike: what is the **runtime subject**? (CLR record vs future domain instance). Document in PR; do not invent a parallel VM. |
| J2 | Lower one effect → AST → `Interpreter` execute or apply-on-instance API. | One end-to-end test |
| J3 | Leave other effect kinds modeled/analyzed only; no MCP expand until J2 green. | — |

**Exit:** One effect kind is executable and tested; roadmap notes the rest as deferred.

---

## Suggested execution order (calendar-agnostic)

```text
WP-A  VM invoke
  ↓
WP-B  fail-loud evolve     ──┬── parallel with G (docs)
WP-C  stage action honesty ──┤
WP-D  PolicySubject        ──┘
  ↓
WP-E  fail closed VM
  ↓
WP-F  DiffDays (needs A)
  ↓
WP-H  keep/kill orphans     ── parallel with I (AST type identity)
  ↓
WP-J  first effect (optional)
```

**Parallelism:** A can start immediately. B/C/D independent of A after A1 if no shared files thrash. G anytime. F after A. J last.

---

## Test conventions (all packages)

- TUnit: `async [Test]`, `await Assert.That(...).IsEqualTo(...)`, names `Method_Condition_ExpectedResult`.
- Prefer **VM product path** (`Interpreter.Compile` / `Execute` / `PolicyEvaluator.Evaluate`) for product claims.
- Use dual-oracle (`EvaluateWithDualOracle` or test helpers) when both engines claim support.
- Negative tests for fail-loud / fail-closed paths.
- Run: `dotnet run --project Poly.Tests/Poly.Tests.csproj` before calling a package done.
- Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` (or solution build) must stay green.

---

## Tracking

| WP | Status | Notes |
|----|--------|-------|
| A | ✅ Done | instanceExpr + dual-oracle — vs **0.4** |
| B | ✅ Core Done | optional B7 remove-zero-match — vs **0.1d** |
| C | ✅ Done | stage-action create honesty |
| D | ✅ Done | Validate + ValidateType — vs **0.3** |
| E | Pending | P1 fail-closed unshipped VM nodes |
| F | Pending | P1 DiffDays when needed |
| G | ✅ Done | README V3-only + stage-action wording |
| H | Pending | P2 orphan keep/kill |
| I | Pending | P2 Introspection hygiene |
| J | Pending | P3 first effect |
| **Dbg** | ✅ Done | CaptureResult + ValueStack clear — suite green |

**M2 Done (slices 0–3).** Post-M2: pm2-1 multi-property evaluate sample; pm2-2 affordances; optional 0.1d; naming cleanup.

Update this table as packages close. Link PRs / commits in Notes.

---

## Relationship to other plans

| Doc | Relation |
|-----|----------|
| `docs/plans/v2-to-v3/simple-agent-tasks/vs-README.md` | **Simple-agent entry point** — M2 Done; post-M2 picks |
| `docs/plans/archive/v2-to-v3-migration/` | Superseded WP/ws/WS8 — do not execute |
| `docs/CORE.md` | Update only if mechanisms change (e.g. fail-closed policy for unshipped nodes). |
| Review artifacts | `/tmp/grok-review-01ce9db4.md` (consolidated findings) |

---

## Explicitly deferred (do not sneak into this plan)

- Full multi-host Introspection.
- Effect framework completeness.
- MCP `evaluate_policy` end-to-end (wait for subjects + A/D).
- `Poly.Ast` / `Poly.Analysis` split.
- **T2/T3 dogfood** (Poly product domain + derived interaction modules) — [trust ADR](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md); start only after this plan’s P0/P1 honesty items.
- Performance / JIT work.
