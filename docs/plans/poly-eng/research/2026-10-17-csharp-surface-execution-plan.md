# C# surface — execution plan (2026-10-17)

**Status:** proposal. Not on the board `CURRENT`, and **not admitted work**. The board is
the only queue; a human opens the row that admits this program (Phase 0).

**Role:** this file is the *sequence*. The two research docs are the *source of truth* for
content:

- [`2026-10-17-csharp-surface-gap-inventory.md`](2026-10-17-csharp-surface-gap-inventory.md) — what is missing, by cost.
- [`2026-10-17-csharp-surface-task-breakdown.md`](2026-10-17-csharp-surface-task-breakdown.md) — what each piece of work is (`T-*` ids, waves, acceptance).

Every slice below **is a `T-*` task in the breakdown**; this file only orders them, names
dependencies, and records the gate. It is not a second status schema and adds no new
inventory.

**Resequencing (recorded, not silent).** The breakdown's Wave 0 says "fix before adding
anything" and mixes three groups: the two silent wrong values, input-validation sharpeners,
and internal hygiene. This file keeps the *wrong values* first (Phase 1) but moves the hygiene
items (`T-A5`, `T-A6`, `T-A7`, `T-A8`, `T-A9`, `T-J1`) to Phase 7 — they are real but do not
produce wrong values and would delay the features. `T-A10` (qualified names) stays in Phase 2
because `T-E6` needs it. No task is dropped.

**Evidence caveat.** The C# surface is under active edit in the working tree. Every claim
below was checked by reading the files on `origin/master` (`2883a409`) unless noted; no claim
was confirmed by building or running the suite.

**Defect fixed 2026-10-18 (array element access).** `new int[2]` (or `long[]`, `double[]`)
then `a[0] = 7` or `a[0]` used to throw
`InvalidCastException: Unable to cast object of type 'System.Object[]' to type 'System.Int64[]'`,
and a reference element read unwritten threw `NullReferenceException`. `EmitNewArray` (and the
assignment path) mapped a `PrimitiveTypeReference` element to `object`, so scalar arrays were
created as `object[]` while the index routes cast them to `long[]`. Creation now derives the
array from the element's CLR value-type-ness (`long[]` for a value type, `object[]` for a
reference), a reference element read goes through `BoxToAbi` so null is the zero handle, and the
frame-local small-array encoding was removed — it was not readable through `.Length` and no
caller tracked its base offset.

## Phase 0 — admission (human)

`CURRENT` is the DEI-retirement track. Nothing below starts until a board row admits it.
Suggested first admission: **Phase 1 as one slice `csurface-truth`** — it removes the two
silent wrong values and unblocks patterns.

## Phase 1 — the surface lies today

All four are small-to-medium; land them before feature work.

| Order | Task | Edit | Why first |
|---|---|---|---|
| 1a | **T-A1** `is` always false | `EmitTypeIs` (`Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs:484-515`) resolves the target from `t.TargetTypeReference` via `ResolveClrType(t.TargetTypeReference, t.TargetTypeReference)`; it drops the `ctx.Analysis.GetResolvedType(t)` fallback and throws a positioned error instead of `return Constant(0L)`. A scalar operand is answered from its static type (no boxing — a scalar's static type is a value type); a heap operand is tested at runtime, so a null handle is false. The reader must keep an unsuffixed integer literal `int` (it widened every literal to `long`). Tests: `"hi" is string`, `1 is int` true, `1 is long` false, `o is object`, `null is string` false. Declaration-pattern binding (`o is string s`) moves to T-D2. | `is` is the base of patterns and `switch`; T-D2/T-D1 depend on it. |
| 1b | **T-A1** narrowing | **Landed** (`SyntaxTypeCompatibilityAnalyzer.BindingOf` keys `VariableAssignedTypeMetadata` on the declaration, so `int x = 1; x = 2.5;` is rejected and `double x = 1; x = 2;` accepted — verified on `873ce606`). | Stops the narrowing lie; shares the analyzer region with 1d. |
| 1c | **T-A2** `foreach` declared type | **Landed.** `ForEachLoop.DeclaredType` (last positional, default null); the reader folds the `foreach-decl` `typed` arm; `ResolveForEachLoopType` prefers it (and publishes the element type); `SyntaxTypeCompatibilityAnalyzer` reports an error when no per-item conversion exists; the emitter converts the item **in slot space** for scalar pairs; the writer prints the typed form. Scope: scalar element/loop-type pairs only — a value element with a reference loop type fails closed (needs box-per-item). | `foreach (double d in intArray)` returned a wrong value before. |
| 1d | **T-A3** `char` ladder | **Landed.** `CategoryOf(char)` stays `Cat.Text`, and `IsNumericLike` adds `char` for arithmetic, comparison, implicit char→numeric assignment, and explicit numeric casts; `char + char` is numeric (195) rather than concatenation. The `string.Concat` rewrite folds literal non-string parts (`"a" + 1`→`"a1"`, `'a' + "b"`→`"ab"`) and **fails closed** for a non-literal non-string part, which the emitter cannot box into a `Concat(object)` argument. | Fixed the wrong char values, `int ← 'a'` rejection, and the `'a' + "b"` → `"b"` lie. |

**Mechanism of T-A1 (verified on `873ce606`, and by running the cases).** The reader folds `x is T` to a
`PrimitiveTypeReference`/`NamedTypeReference`, never a `ClrTypeReference`. The emitter's only
`ClrTypeReference` branch misses, and its fallback asks for the `TypeIs` node's own type —
which `TypeAndMemberResolutionPass.cs:153` sets to `bool`. So the surface compiled
`heapObj is bool`, which is `false` for every operand. Passing the `TypeIs` node as `typedNode`
to `ResolveClrType` would re-resolve `bool`, so resolve from `t.TargetTypeReference`. A scalar
operand is a ring value, not a heap handle: answer it from its static type (`StaticTypeIsMatch`),
which is also what makes `1 is long` false — `is` tests the runtime type and never widens.
Heap operands keep the runtime check, where a null handle (0) is false.

## Phase 2 — cheap console ergonomics (one slice each; mostly independent)

- **T-A10** qualified type names (`System.X.Y`) — **Landed 2026-10-18.** A dotted member chain that names a CLR type is rewritten to a `ClrTypeReference`, so `System.String.Empty`, `System.Math.Abs(-1)`, `System.Int32.MaxValue`, `System.Threading.Tasks.Task.CompletedTask` and `typeof(System.String)` execute. Known limits: a nested type whose proper prefix is a type (`System.Environment.SpecialFolder`) reports the prefix as a missing member; `typeof(<keyword>)` (e.g. `typeof(string)`) is still unresolved (pre-existing, not a qualified name); and an *untyped* lambda parameter named like a namespace root is treated as the namespace (a typed parameter or a local shadows it correctly). Do this before `T-E6`.
- **T-E6** `nameof` execution — **Landed 2026-10-18** (folded to a constant by the constant-folding pass; the reader fold and tokenizer are unchanged).
- **T-C8** `throw` expression read-back; **T-F4** `const` locals.
- **T-B1** verbatim strings; **T-B2** raw strings (dep T-B1); **T-B3** `0x`/`0b`/`_` literals.
- **T-C2** `??=`; **T-C3** `<<=`/`>>=`; **T-C1** `++`/`--` (see Q2).
- **T-F7** bare `T x;`; **T-F6** `for`-init declarations.
- **T-F1** `using var` declarations; **T-F2** exception filters (changes the shipped `CatchClause` record); **T-C5** `checked`/`unchecked` (see Q3).
- **T-E11** conditional element access `a?[0]`; **T-C4** compound assignment for impure targets (dep T-C1).

## Phase 3 — patterns and `switch` (depends on 1a)

- **T-D2** declaration/const/type patterns + `when`.
- **T-D1** `switch` expressions; **T-D3** relational/logical patterns; **T-D5** switch-statement patterns + `goto case`.
- Exhaustiveness is deferred; a non-exhaustive form fails closed.

## Phase 4 — types and collections (medium)

- **T-E1** object/collection initializers; **T-E3** target-typed `new()` and `with` (dep T-E1).
- **T-E4** tuples + structural equality (Q4); **T-E2** collection expressions (dep T-E1/T-E4); **T-C7** ranges and index-from-end.
- **T-E5** indexers (dep T-E1); **T-E8** anonymous objects; **T-E9** stored source lambdas; **T-E10** method groups.
- **T-B5** interpolated verbatim/raw strings (`$@"…"`, dep T-B1/T-B2).

## Phase 5 — platform tracks (each its own design doc and admission)

- **T-I1/I2** LINQ; **T-I3** user generics; **T-I4** virtual/interface dispatch; **T-I6** iterators.
- **T-I5** async/await state machine — wait for open PR #193.

## Phase 6 — declarations (authorable; separate admission)

- **T-G1** `struct`; **T-G2** modifiers + `where`; **T-G3** primary constructors; **T-G4** `base.M()`/base-initializer; **T-G5** operators; **T-G6** events/delegates; **T-G7** `required`; **T-G8** `using` forms; **T-G9** DIM; **T-G10** records (positional bodies, `with`, structural equality); **T-G11** attributes on more targets.
- **T-H1** `ref`/`out`/`in`; **T-H2** `params` (dep T-E2); **T-H3** named arguments.
- Authorable items touch the DSL parser, exporter, golden corpus, and `poly-dsl-guide.md` in the same change (see Q6).

## Phase 7 — quality and tooling

- **T-A4** DEI name-keyed scans — **check first**: DEI is being retired, so this may close as obsolete.
- **T-A5** destination-node silent degrade (follow-up to 1b's keying fix, which supersedes the old `BindingOf` note in the inventory); **T-A6** unordered name fallback; **T-A7** emit-time scope unwind; **T-A8** node/API hygiene; **T-A9** culture-sensitive interpolation; **T-J1** two purity predicates.
- **T-J2** audit other `Node` consumers for new kinds; **T-J3** round-trip edges; **T-J4** diagnostics; **T-J5** console UX; **T-J6** browser harness; **T-J7** regression pins; **T-J8** plan/inventory upkeep.

## Gate for every slice

- `LanguageSurfaceTests.Kinds` in lockstep with `DirectVmAbiEmitter.CompileNodeInner`; `EveryConcreteNodeType_IsInventoried` green.
- A Roslyn check for the **new construct**, not only the seven-category counts — a wrong-shape fold (`?.` → `.`) passes equal counts today.
- Sweep `ConstantFolding`, `MermaidAstGenerator`, `DomainDslPrinter`, and the DSL parser for the new kind.
- A `LanguageVmTests`/`CSharpScriptTests` oracle and a fail-closed case; `Write(Read(x))` idempotent; golden corpus unchanged for console-only work.
- Full suite at the pre-ship gate, in the parent session: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (warnings fail the build).

## Decisions still open (block admission of the named phase)

| Q | Question | Blocks |
|---|---|---|
| Q1 | Is the literal `1` statically `int` or `long` in the VM? Scalars share a slot encoding, so `1 is int` needs a literal-typing answer. | Phase 1a acceptance |
| Q2 | `++`/`--`: new node, or a reader desugar with a synthetic temp? A desugar cannot introduce scope by itself. | Phase 2 |
| Q3 | `checked`/`unchecked` overflow semantics for the VM's long arithmetic. | Phase 2 |
| Q4 | Tuples: implement structural equality, or scope tuples out explicitly? | Phase 4 |
| Q5 | Which board row admits Phase 1, and when do later phases enter `THEN`? | Phase 0 |
| Q6 | Console-only or authorable, per Phase 6 item? | Phase 6 |

## Deliberate exclusions

`unsafe`, pointers, `ref struct`/`Span`, `dynamic`, COM, preprocessor *semantics*, query-syntax
LINQ (unless decided in T-I2), and `Main`-style entry-point lowering.
