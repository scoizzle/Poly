# Dict + SQLite host (narrow bet)

**Date:** 2026-08-30  
**Status:** Proposal — not CURRENT, not a suite  
**Related:** [`docs/CORE.md`](../CORE.md) · [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../decisions/2026-08-15-domain-library-extensions-mcp-harness.md)

The architecture we want is: VM runs Syntax; `This` is a dictionary; SQLite is the store; host ABI is tiny. That would be a better situation.

A six-phase rewrite that deletes `DomainEntityInstance`, reshapes emit, and unifies every `LoweringContext` flag in the same campaign would **not**. This file is the narrower bet: two slices, then stop and reassess.

Do not admit this as PIPELINE CURRENT until P1 is a real suite with a gate.

---

## Diagnosis (do not re-litigate)

Simulation cost is a second product, not missing SQLite or dummy dictionaries.

| Piece | Problem |
|-------|---------|
| `DomainEntityInstance*` (~2k LOC) | C# state machine (`InvokeAction` / `ExecuteEffect`) plus host create |
| Quantifier preprocess | DE rewritten to literals before the VM |
| MCP SQLite store | Lagging replica of live objects (`SyncFromCache`) |
| Emit | Second **body** for create/notify (`CreateNav`, `NotifyXSubscribers`) |

Already true (do not rebuild):

- `This` is dictionary-backed (`TypeDefinitionNodeAnalyzer` → `IDictionary<string, object?>`)
- Create **call sites** are `SetCreateValue` + `CreateChild` on both paths (`LowerStageTransitions` / `EffectExecutor` are gone)
- Clocks lower to `DateTime.UtcNow` / `DateOnly.FromDateTime` (VM executes type-name static members)
- Core `Poly/` has zero NuGet deps — SQLite **implementation** stays out of `Poly.dll`

---

## Target (after the narrow slices)

```
named operation
  → one compiled Syntax Block
  → Interpreter.Execute
  → This = bag (IDictionary)
  → Store = SQLite (:memory: in tests/MCP), authority not a replica
  → Host ABI = Insert / Link / Unlink / Notify / Outbound
```

Emit factories (`Type.Create`, `CreateNav`) stay for REST/root sugar until a later decision. Pretty C# vs `Insert` is **not** this bet.

---

## In scope (the bet)

### P1 — Store is the authority

- SQLite `:memory:` is the store for MCP and for new store tests (unique index, link table, transactions).
- Implementation stays in `Poly.Mcp` (or a tiny `Poly.Store.Sqlite` project). Core keeps the interface.
- Kill live-object dual-write: no `SyncFromCache`, no identity map of `DomainEntityInstance` that mutates independently of rows.
- Hydrate a bag when the VM needs `This`; flush on host ops.

**Stop:** `rg SyncFromCache` empty; unique collision tests pass against SQLite without `DomainEntityInstance` as the store’s type.

### P2 — One action program

- Lower a **guard-free assign action** to one `Block`; `Interpreter.Execute(this: bag)`.
- `invoke_action` uses that path (not per-effect `ExecuteEffect`).
- Host ABI on this path: property writes on the bag; no pending-create keys.

**Stop:** `ExecuteEffect` is not on the assign-action path; one TUnit + one MCP smoke green.

After P2 is green **and has stayed green**, reassess. Only then consider: create as `Insert`+`Link`, Notify unification, Q3 as `Outbound`+`ForEachLoop`, deleting `DomainEntityInstance` orchestration, sharing trees with emit.

---

## Out of scope (this file)

- Deleting `DomainEntityInstance` in the same campaign
- Forcing emit onto `Insert`/`Link` (same-tree emit rewrite)
- TimeProvider / `CallExternal` for clocks
- Domain VM opcodes
- SQLite as a dependency of `Poly.dll`
- Rewriting `DomainEntityInstanceTests.cs` (3.8k lines) wholesale — new tests for P1/P2; port behaviors later

---

## Why not the full cut-and-rebuild

| Full rewrite | Narrow bet |
|--------------|------------|
| 6 phases, emit goldens, 120+ host tests rewritten | Two stop conditions |
| Same-tree emit is where pretty C# fights `Insert` | Simulation gets lighter without that fight |
| High stall risk (this repo’s partial-refactor pattern) | If P1–P2 fail, we stop with a store, not a half-deleted host |

The maintainability win is **stop growing the wrapper**. P1–P2 capture most of that. Emit unification is a second product question.

---

## Non-goals forever in this direction

- SQL inside Interpretation
- `DictionaryOp` / `OpCode.CheckPolicy`
- Making generated POCOs be the simulation dicts

---

## Done (narrow)

1. MCP simulate persistency is SQLite rows, not a cache of CLR instances.
2. At least one action compiles once and runs on a bag.
3. CORE/MCP README describe that host honestly.
4. No new consumer lowering flags; no new preprocess-to-literal.

Until P2 is green, this is still a plan, not a rebuild.
