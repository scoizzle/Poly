# Experiment: Variable multi-assign union types

**Date:** 2026-08-26
**Status:** **Phase 1a shipped** — mixed assigns fail closed at analysis (`SyntaxTypeCompatibilityAnalyzer.CheckVariableAssign`). Union locals are **not** product on `net10.0`. **Future (gated):** when the host has stable C# 15 / .NET 11 type unions, deduce a union type from the assignment set and project that — do not keep last-wins or `object`.
**Home:** `docs/experiments/`
**Lab notebook:** `Poly.Tests/Interpretation/VariableMultiAssignTypeExperimentTests.cs`

---

## Research charter

### Problem

A `Variable` is one ABI slot. Analysis gives it **one** resolved CLR type and **one** `ValueRepresentationKind`. User writes are `Assignment`. Nothing stops two assignments from storing technically incompatible types into that slot:

```text
x : declared on Block.Variables
x = 1L          // StackScalar long
x = "hi"        // HeapRef string
read x          // classified as the last assignment's type, not a join
```

Today that is not a union. It is **last-write-wins typing** (tree-walk order, not path-sensitive). `SyntaxTypeCompatibilityAnalyzer` checks `Assignment` to a `Member`, not to a `Variable`. Mixed successive assigns compile.

### Why look now

Declare-init-as-`Assignment` made every write visible as the same node. That is the right time to ask whether the *slot* should join those writes, fail closed, or keep last-wins. Sticky `Initializer` hid a second write on the binding; that lie is gone.

### Hypothesis (two admissible outcomes)

1. **Fail closed (likely cheaper, likely righter for this VM).** Incompatible successive assigns — or incompatible join of if/else writes — are an analysis error. One slot, one kind. Dual-oracle stays simple. C# `var x = 1L; x = "hi";` is already illegal; the projection would match.
2. **True union.** The variable's type is `UnionTypeReference` of all assigned RHS types (or a flow-sensitive join). Reads that need a single kind (`Add`, `Invoke` target, member) fail closed unless narrowed. ABI must **box or tag** so a `StackScalar` write cannot be read as `HeapRef`.

Either outcome is a successful experiment. Shipping a CLR `object` collapse and calling it a union is a failed experiment (`ResolveUnion` already does that for property types).

---

## Current machinery (do not reinvent)

| Piece | Today |
|-------|--------|
| `UnionTypeReference` | Exists on type-definition properties (`Poly/Ast/Nodes/TypeDefinitions/UnionTypeReference.cs`) |
| `AstTypeReferenceResolver.ResolveUnion` | Same CLR runtime type → keep it; **mixed options → `object`** |
| `TypeAndMemberResolver.ResolveBlockType` | First **direct-child** `Assignment` dest identity sets type + `StoredLambdaMetadata` |
| `ResolveAssignmentType` | **Every** `Assignment` to a `Variable` **overwrites** resolved type; non-lambda RHS removes `StoredLambdaMetadata` |
| `ValueRepresentationAnalyzer` | `Variable` classified from that single resolved type |
| `SyntaxTypeCompatibilityAnalyzer` | `CheckVariableAssign`: first write in tree order is the slot; later writes (including if/else) must match kind / category |
| VM slot | One `long`; kind of the **variable** (not the write) decides decode on read |
| C# printer | Direct first assign fuses `var x = e`; later assigns are `x = e` (may be illegal C#) |
| LINQ checker | Same resolved type as VM compile |

Characterization tests lock the last-wins facts in `VariableMultiAssignTypeExperimentTests`.

### Observed baseline (2026-08-26)

| Tree | Analysis | Runtime / print |
|------|----------|-----------------|
| `x=1L; x="hi"; x` | No type-compat error. Resolved type `string`, kind `HeapRef` | Execute yields `"hi"` |
| `x="hi"; x=1L; x` | Resolved type `long` | Execute yields `1L` |
| `if (true) x=1L; else x="hi"; x` | Resolved type is **last assignment in the tree** (`string` / `HeapRef`), not the taken path | Taken-then path: `Result.Value` is `1L` (the written scalar). Analysis still says `string`. Last-in-tree type did not govern the runtime word |
| C# of sequential long then string | — | `var x = 1L; x = "hi";` (would not compile as C#) |
| Property `UnionTypeReference(int32 \| string)` | — | CLR type `object` |

That if/else row is the ABI bomb: write uses the RHS representation; read uses the variable's (last-in-tree) kind.

---

## What "union" would have to mean here

Not TypeScript `x: string | number` as a documentation nicety. For Poly it has to survive three consumers of the **same** tree:

1. **Analysis** — join of assignment RHS types; reads that require one kind fail or narrow.
2. **VM (canonical)** — slot contents must be decodable without guessing. Options:
   - **Fail closed** before emit (no union at runtime).
   - **Always heap-box** mixed-kind slots (handle + boxed CLR value). Loses scalar ABI. Dual-oracle must box too.
   - **Tagged word** (tag in unused bits or a side cell). New ABI. Needs debugger/`GetLocals` siblings (same class as frame high-water vs live count).
3. **C# projection** — `object` + casts is the current union collapse and is a lie for `Add`/`Invoke`. `OneOf<T,U>` is a host library we do not take. Honest C# on **net10** is fail closed. On a future **net11+** TFM, honest C# is the host’s `union` (or ad hoc `T | U` if that is the stable form) — not a Poly-owned tagged struct unless the host still cannot print it.

LINQ is the same-tree checker, not a second language. If VM tags or boxes, LINQ must match or the experiment stops.

---

## Future goal: assignment-set union deduction (host C# 15 / .NET 11)

C# 15 (shipping with .NET 11) adds **type unions**: a closed set of existing case types, e.g. `public union Pet(Cat, Dog, Bird)`. Cases are the types themselves (no extra tag names). Implicit conversion from each case, exhaustive `switch`. That is the first time the **C# projection** can tell the truth about a mixed local without `object` or a third-party `OneOf`.

This is **not** a product tuple. Tuples are products (`(long, string)` both at once). Unions are sums (`long` **or** `string`). The goal is: **deduce the sum of types actually written to the binding**, then use that as the variable’s type.

```text
x = 1L
x = "hi"
  → analysis type UnionTypeReference(Int64, String)
  → C# 15 projection: a host union of those cases (named or ad hoc), not `object`
```

**When, not now**

| Gate | Why |
|------|-----|
| TFM is `net11.0` (or later), language version has **stable** unions | Poly is `net10.0` today. Preview `union` is not a ship surface. |
| Printer compile oracle | Generated C# must compile under that TFM. |
| Dual-oracle | VM and LINQ must decode the same word the C# union would. |
| No `object` collapse | `ResolveUnion` → `object` stays forbidden as “support.” |

**What the host actually stores (do not ignore)**

C# 15 unions are untagged at the *language* level (cases are types). The default runtime encoding is still a union struct whose payload is `object?` — value-type cases box unless a non-boxing `IUnion` layout is used ([CS9371](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/union-declaration-errors): case types must convert to `object`). So “.NET added unions” does **not** give Poly a free untagged `long` slot. The matching VM ABI is still **heap-box mixed slots** (or later an `IUnion`-shaped layout if we opt into the non-boxing path). Fail-closed on net10 remains the cheaper honest answer until that TFM move.

**Deduction rule (when gated)**

1. Collect assignment RHS types per `Variable` identity (same walk as `CheckVariableAssign`).
2. If all writes share kind + compatible category → keep a single type (today’s success path).
3. If they form a finite set of case types the host union can hold → `UnionTypeReference(options)` on the variable; reads that need one kind (`Add`, `Invoke` target, member) still fail closed unless narrowed (`TypeIs` / `TypeAs` / `TypeCast` / pattern).
4. C# printer emits the host union type for declare-only / fusion, not `var x = 1L; x = "hi";`.

Until those gates, mixed assigns stay **analysis-illegal**. Do not weaken `CheckVariableAssign` in anticipation.

---

## Protocol (small loops)

Do **not** start with an ABI tagged cell.

### Phase 0 — characterize

Done. Mixed sequential / if-else trees are now **analysis-illegal** (see Phase 1a). Same-kind reassign and property-union collapse remain in the lab notebook.

### Phase 1 — analysis join, still fail-closed at use

1. Collect every `Assignment` dest identity for each declared `Variable` (direct and nested; if/else both branches).
2. If all RHS types share one `ValueRepresentationKind` and compatible CLR category, keep a single type.
3. If kinds or CLR types differ:
   - **1a (chosen, shipped):** `SyntaxTypeCompatibilityAnalyzer.CheckVariableAssign` errors on the later `Assignment`. First write in tree order is the slot type; later writes must match kind (and not mix IEEE vs integer scalars, and not mix categories / distinct uncategorized CLR types). Tests: `InvalidProgramTests.Assignment_*` / `IfElse_LongThenString_*`. Same-kind: `SequentialLongThenLong_StillRuns`, `SequentialIntThenLong_SameSlotEncoding_StillRuns`.
   - **1b (not chosen):** `UnionTypeReference` on the variable. Do not implement.

### Phase 2 — host-union deduction (future, gated)

Only after net11+ / stable C# 15 unions (see **Future goal**). Then 1b is: deduce `UnionTypeReference` from the assignment set; VM heap-boxes mixed slots to match the host’s default union encoding; C# printer emits `union` / `T | U`; LINQ matches. Not `object`. Not a Poly-invented tag cell.

Reject a tagged-word ABI unless heap-box (or host `IUnion` non-boxing) is proven insufficient **and** a second real consumer needs it.

### Phase 3 — 1a is product

- Fail-closed mixed assigns: `InvalidProgramTests`, CORE one line. **Done.**
- 1b+2 wait on TFM + printer oracle. Keep this file as the promotion brief.

---

## Anti-goals

- Do not treat `ResolveUnion` → `object` as "union support."
- Do not add `Comment` / a second interpreter / host `dynamic`.
- Do not invent `IUnionSlot` wrappers (same bar as deleted `UpvalueCell`: detect at analysis).
- Do not path-sensitive type the VM until analysis join is specified; SSA-split of the binding is a different experiment (would contradict "same node identity is the slot").
- Do not teach DomainModeling mixed-type locals. Domain is a client; this is a language-VM question.

---

## Existing AST hook

`UnionTypeReference` is already the node for "this type is one of these." Reuse it if 1b happens. Give it an `ITypeDefinition` that is **not** silently `object` (a real union definition with options), or fail closed in `ResolveUnion` when options disagree — today's collapse is the trap.

---

## Success criteria

1. Mixed assign to one `Variable` is **analysis-illegal** (1a). Answered: yes, fail closed.
2. No mixed-kind ABI: we refuse the tree before emit.
3. VM/LINQ never see the tree (`Interpreter.Compile` fail-closed). C# print of a constructed illegal tree may still emit `var x = 1L; x = "hi";` — print is not the gate.

Union locals stay out of CORE. CORE states the fail-closed assign rule.
