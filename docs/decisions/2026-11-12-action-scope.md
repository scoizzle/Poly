# ADR: ActionScope — the module's one undo log (#677 R-08)

## Status

**Accepted** — slice R-08 of epic [#677](https://github.com/scoizzle/Poly/issues/677), theme R
([#682](https://github.com/scoizzle/Poly/issues/682)), plan lock 2026-11-12
([comment 6823125842](https://github.com/scoizzle/Poly/issues/682#issuecomment-6823125842)).
Plan-review-deep round 2 verdict **LOCK** with amendments B1–B7. Sign-off is `review-critical`,
raised by orchestrator judgement because this is a shipped, packable public contract with five
consumers and no type-forwarders.

## Context

Q1 (#659) puts atomicity in the lowered module: every host gets all-or-nothing, and a handler
failure aborts the whole action. Q3 keeps a domain failure a returned `DomainResult`, never an
exception. Before this slice the only undo was executor-private: `ModuleInstanceExecutor.Capture`
and `Restore` snapshot every bag the executor knows about, walk them **forward**, and revert the
storage port **before** restoring entity state. That is a host convenience, not a contract the
lowered module can name, and it cannot be reached from printed C#.

## Decision

### 1. Surface (`Poly.Runtime`, BCL-only)

```csharp
namespace Poly.Runtime;

public readonly struct ActionScopeToken {
    public bool IsRoot { get; }            // true only for the token that opened the frame
    // internal Frame Frame; internal int ScopeId
}

public static class ActionScope {
    public static ActionScopeToken Begin(IStorageJournal? journal = null);
    public static void Touch(object instance);
    public static void Create(object instance, string entity);
    public static IReadOnlyList<(object Instance, string Entity)> CreatedInstances { get; }
    public static bool HasOpenScope { get; }
    public static void Commit(ActionScopeToken token);
    public static void Rollback(ActionScopeToken token);
}

public interface IActionScoped {
    [EditorBrowsable(EditorBrowsableState.Never)] object __Snapshot();
    [EditorBrowsable(EditorBrowsableState.Never)] void __Restore(object state);
}

public interface IStorageJournal { int Mark(); void RollbackTo(int mark); }
```

`IStorageJournal` exists because `Poly.Runtime` may not name the internal `ModuleStoragePort`;
`ModuleStoragePort` implements it **explicitly**, so an `IStoragePort` caller acquires no second
journal surface. There is no `Depth`, no `Reset` and no public `ScopeId` (A11).
`ActionScopeTests.ActionScope_PublicSurface_IsExactlyDeclared` pins this member set exactly, so a
later addition is a deliberate test edit.

### 2. Rules (A1–A6, B1)

- **Ambient frame.** One `AsyncLocal<Frame?>` holds the flow's frame, and **every** frame
  mutation is under one static lock. Entity data is not synchronised (Q2): parallel actions need
  distinct journals or host serialisation.
- **Frame states.** `Open`, `Committed`, `RolledBack`. `Commit` and `Rollback` resolve their
  frame from the token, never from the ambient frame, so a token that outlives its flow still
  completes the frame it opened.
- **`Begin(journal)`.** With no ambient frame, or a closed one, it opens a root: it registers the
  journal as a live root (throwing when another root already holds it, because the journal is
  flat and supports one root mark) and records `rootMark = journal?.Mark()`. With an `Open`
  ambient frame it joins as nested when `journal` is null or is the frame's journal; any other
  journal throws.
- **`Touch` / `Create`.** A no-op with no ambient frame; recorded on an `Open` frame; throw on a
  closed ambient frame, because that means a leaked frame. A null instance or entity throws
  `ArgumentNullException`.
- **`Touch` targets (A1/F6).** An `IActionScoped` entity, or a simulator bag that is an
  `IDictionary<string, object?>`; `IActionScoped` wins when an object is both. Anything else
  throws `ArgumentException` naming the type. An instance is snapshotted once per frame.
- **Bag snapshot.** Entries by reference plus the contents of each non-fixed, non-read-only
  `IList` value; a restore clears, re-adds and re-fills. **Shallow by contract:** a mutated child
  object is only restored when the action also touched it.
- **`Commit(token)`.** A default token, or a closed frame, throws. A nested token removes its own
  id and a missing id throws. A root token throws while any nested id is open; otherwise it sets
  the frame `Committed`, deregisters the journal and keeps the journal's contents.
- **`Rollback(token)`.** A default token throws. On an `Open` frame **any** token rolls back the
  whole frame (Q1): restores in reverse touch order, then always `journal.RollbackTo(rootMark)`,
  then closes the frame. On `RolledBack` it is a no-op; on `Committed` it throws.
- **Order (A7).** Entity state is restored **before** the journal is reverted, the reverse of the
  executor's order.
- **No poison state (A6/F3).** Every restore is attempted, the journal is always reverted and the
  frame is always closed. Failures surface together as one `AggregateException`; the next
  `Begin` starts a clean root. There is no `Reset`.
- **Closing.** `Commit` on the root and `Rollback` clear the closing flow's ambient frame, so a
  write after a normal close in the committing flow is a no-op.
- **`CreatedInstances` (A2).** A copy taken under the lock, read before the root closes; empty
  outside an open scope.

### 3. Consumer obligations (A12, B3, B4, B6)

Recorded here and in `docs/CORE.md`; each consumer's plan lock must confirm them.

- **Prologue.** `var __scope = Poly.Runtime.ActionScope.Begin(__journal)`, where `__journal` is
  the module's `IStorageJournal`, or `null` in a host with no storage.
- **Before each write.** `Poly.Runtime.ActionScope.Touch(target)`. In the simulator the receiver
  is the entity's `IDictionary<string, object?>` bag, and `Touch` accepts a bag directly, so no
  `IActionScoped` is needed there.
- **Created instances.** `Poly.Runtime.ActionScope.Create(instance, "EntityName")`.
- **Epilogue (B3).**
  `DomainResult __r = default; bool __ok = false; try { __r = <body>; __ok = __r.IsSuccess; } finally { if (__ok) ActionScope.Commit(__scope); else ActionScope.Rollback(__scope); }`
  followed by `return __r;`. A `finally` cannot see a returned `DomainResult`, so the `__ok` flag
  is what distinguishes success from a returned `Failure`; without it a bare `finally` would
  commit failed actions. `<body>` yields its result into `__r` with no `Return` node inside the
  try block, for example as an inner method or local function.
- **Explicit implementation (B4).** Generated entities implement `IActionScoped` **explicitly**
  (`object IActionScoped.__Snapshot()`, `void IActionScoped.__Restore(object)`), so no member name
  enters the type's declaration space and `ReservedNames` needs no row. That needs an
  explicit-interface qualifier on `MethodDefinitionNode` printed with no access modifier, and
  neither exists today (`MethodDefinitionNode.cs:12-21`, `AccessModifier.cs:6-24`). L-03 owns
  adding it, with its reader/writer grammar-table row, its idempotence test and its fail-closed
  case. The fallback is public implicit `__Snapshot`/`__Restore` members plus two `ReservedNames`
  rows. `__Snapshot` and `__Restore` must not call the storage port.
- **Nested failures (B6).** Lowered code returns immediately on any failed nested `DomainResult`,
  before another `Touch` or `Create`; a throw after a whole-frame rollback is a host bug.
- **Host obligation.** The executor opens the root per call and asserts `token.IsRoot`; a false
  value means a leaked frame. It reads `CreatedInstances` before closing the root. It closes the
  scope **before** taking or restoring its own port mark, because the executor's mark must sit
  outside the scope's, and a flat journal cannot survive an outer mark rolled back first. F-32
  awaits every branch before `Rollback`.

### 4. Accepted risks

- **`AggregateException` inside a `finally`.** A rollback failure raised while an exception is
  propagating supersedes the original exception. Accepted: both are host bugs, the rollback
  failure is the more actionable one, and the frame still closes.
- **`[EditorBrowsable(Never)]` does not hide members from reflection**, so the surface pin sees
  `__Snapshot`/`__Restore`. Intended.
- **Shallow bag restore.** L-03 must emit `Touch` for every written target, not just the
  receiver; a mutated child object reached through a bag is restored only if it was touched.
- **Q2.** Parallel roots need distinct journals (T16, T21) or host serialisation; the lock covers
  scope bookkeeping only, never entity data.

## Forks

- **F1 (ruled, reshaped).** `Create(object instance, string entity)` plus a
  `(object Instance, string Entity)` list (A2).
- **F2 (ruled, not as written).** A closed frame is ignored by `Begin`; only a live frame is
  subject to the journal-mismatch throw; a token completes only its own id once.
- **F3 (ruled).** No `Reset`; rollback always closes (A6).
- **F4 (ruled).** `ModuleStoragePort` implements `IStorageJournal` explicitly, and its stale
  "token journal is R-08's" remark is rewritten.
- **F5 (ruled).** CORE and this ADR land in this slice (A8).
- **F6 (ruled, widened).** `IDictionary<string, object?>` is a first-class `Touch` path.

## References

- Plan lock and amendments: [#682 comment 6823125842](https://github.com/scoizzle/Poly/issues/682#issuecomment-6823125842).
- ActionBodyPhases contract amendment: [#683 comment 6823126271](https://github.com/scoizzle/Poly/issues/683#issuecomment-6823126271).
- Tests: `Poly.Tests/Runtime/ActionScopeTests.cs` (31 tests, T1–T31).

Part of #677, theme R (#682). No closing keyword: the row carries `closes —`.