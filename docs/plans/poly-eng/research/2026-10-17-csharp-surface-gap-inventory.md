# C# surface gap inventory and slice plan

**Date:** 2026-10-17
**Status:** proposal / research. Not on the board `CURRENT` — nothing here is admitted work.
**Scope:** the C# language surface (`Poly/Interpretation/CSharp/`) and the VM envelope that
executes it. Goal: grow the *executed* subset toward "full, reasonable C#" incrementally.

## 0. Decisions (Scot, 2026-10-17)

1. **Target: everything, A–E.** Treat this as a program, not one change: each numbered slice
   is independently shippable and reviewed, and the board track that admits it is the human's
   to open (this document is the plan of record until then).
2. **Reach: console-only for Tier A/B.** A slice adds the AST node + analysis + VM + writer +
   tests, but not the `.poly` DSL parser, domain lowering, golden corpus, or `poly-dsl-guide`.
   (Tier D/E declaration items are read/execute in the console; authoring them in `.poly` is
   out of scope unless a slice says otherwise.)
3. **Interpolation lands as a real `InterpolatedString` AST node** (not a `String.Concat`
   desugar). Consequence to keep in view: a new executable kind, an inventory entry, and
   either writer support or a debugger-display fallback (the console already falls back to the
   node's type name), plus the per-construct Roslyn cross-check in §5.

## 1. What the surface is, and the two invariants

The C# surface is the **read half of the printed-C# oracle**, not a general C# front end.
`CSharpReader` states it (`CSharpReader.cs:15-19`): *"a recogniser for the exported subset,
not a C# compiler front end: constructs outside the table fail closed with a positioned
error rather than folding to a wrong shape."*

Invariants any change must keep:

1. **Roslyn is the external oracle** (`Poly.Tests/Interpretation/CSharpLanguageTests.cs:9-13`):
   printed output parses with **zero Roslyn syntax diagnostics**, and the reader's tree is
   compared against Roslyn's syntax tree on the golden corpus.
2. **`Shipped ⊆ complete generic Syntax tree` and `Shipped ⊆ lowerable`** (AGENTS.md frozen
   core). A reader that accepts code the emitter cannot lower is a "known-wrong shape"
   (tenet 5), which is why the executable envelope is an explicit inventory
   (`Poly.Tests/Interpretation/LanguageSurfaceTests.cs` — `Executable` / `CompileReject` /
   `AnalysisOnly`, kept "in lockstep with `DirectVmAbiEmitter.CompileNodeInner`").

So "support a construct" means **all three**: represent it (AST), execute it (analysis +
lowering), and print it (writer). Parsing alone is not support, and *printing* alone is not
either.

Sizes: tokenizer 331, token kinds 101, grammar 448, reader 805, writer 1274, script 226 lines.

## 2. Current inventory

### 2.1 Lexing (`CSharpTokenizer.cs`, `CSharpTokenKind.cs`)

| Supported | Missing / limits |
|---|---|
| decimal numbers, `_` separators, exponent, suffixes `L U UL f F d D m M` | `0x`/`0b` literals (`0x1F` scans as `0` + identifier), `nint`/`nuint` |
| `"…"` with `\n \r \t \0 \a \b \f \v \\ \' \" \u \x` | interpolated `$"…"` (bare `$` throws "Unexpected character"), verbatim `@"…"` (bare `@` before `"` also throws), raw `"""…"""`, `\e` |
| `'…'` chars, `//` comments, `/* */` comments | — |
| `#…` directive **lines** are trivia | directive lines are only skipped: `#if`/`#else` region **bodies are not excluded** — both branches parse as ordinary code |
| `@identifier` escaped identifiers | `global`/`file` modifiers |

### 2.2 Types (`CSharpGrammar.cs:169-195`)

Primitives, `named` + `type-args`, `?` suffix, `[]` suffix. **Missing:** tuple types
`(int, string)`, pointers, function pointers, `ref`/`ref readonly` types, `Span<T>`-style
ref structs.

### 2.3 Declarations (`CSharpGrammar.cs:198-287`)

Supported: `class`, `record` (positional params allowed), `interface`, `enum`; fields,
properties (auto/accessors/`init`), methods (block/arrow/semi), ctors (with `: base(...)`);
attributes; generic parameters (**no constraints**); lineage; file-scoped `namespace`;
`using` directives.

Limits on "supported": enum members **require** an explicit `= <number literal>`
(`enum E { A, B }` and `A = -1` fail, `CSharpGrammar.cs:228-231`); modifiers are limited to
`public private protected internal static const readonly volatile override async`
(`:156-166`).

**Missing:** `struct` (no `KwStruct`), `delegate`, `operator`/conversion operators, indexers
(`this[…]`), events, `partial`, `sealed`/`abstract`/`virtual`/`new`/`extern`, `where`
constraints, primary constructors on classes/structs, `required`, `global using`,
`using X = Y` aliases.

### 2.4 Statements (`CSharpGrammar.cs:290-345`)

Supported: block, `if/else`, `while`, `do…while`, `for`, `foreach (**var** x in …)`,
`switch`, `try/catch/finally`, `using (…) stmt`, `local-function`, `return throw break
continue goto label`, `var x = …`, expression statement.

Limits on "supported": empty `;` is **rejected by the fold** (`CSharpReader.cs:566-567`);
bare `catch { }` is unparseable (the parentheses are required, `:337`); a `case` body is
**exactly one statement** (`:333-335`), so `case 1: A(); break;` needs braces; `for-init` is
an expression only, so `for (int i = 0; …)` fails.

**Missing:** typed locals (`int x = 5;`, `string s = "hi";`) and typed/`var` `for`-init
declarations; `using var x = …;` declaration; `lock`; `checked`/`unchecked`; `yield return`/
`yield break`; `const` locals; typed `foreach (int x in …)`; `foreach` deconstruction;
`goto case`/`default`; exception filters (`catch (…) when (…)`); `switch` patterns
(constant arms only today).

### 2.5 Expressions (`CSharpGrammar.cs:349-445`)

Supported: `=` assignment; ternary; `??`; `|| && | ^ &`; `== !=`; relational; `<< >>`;
`+ - * / %`; unary `- ! ~`; `await` (parses, see A1); postfix `.x`, invocation, generic
invocation, `[index]`, `!`; literals, `true/false/null`, identifiers, `this`, `base`,
parentheses, casts, `typeof`, `default(T)`/`default`, `new T(args)`, `new T[expr]`, four
lambda forms (`async` optional), `is` **only** as `expr is Type [var]`, `as`.

Limits on "supported": `?.x` and `?.()` are **fail-closed** by the reader
(`CSharpReader.cs:717-718`: "not part of the exported subset", pinned by
`CSharpReaderTests.cs:121-125`) even though the token and grammar rule exist — treat as
missing. `nameof(…)` **parses and prints** (`CSharpReader.cs:699-700` folds to
`Invoke(TypeReference("nameof"), …)`; `CSharpWriter.cs:832-833`/`:1271`;
`CSharpReaderTests.cs:82-88`; `src/Poly.DslCompiler/DbContextGenerator.cs:269-278` relies on
the shape) but is **not executable** — `DirectVmAbiEmitter.Invoke.cs:351-355` rejects an
`Invoke` whose callee is a `TypeReference`. `throw` as an *expression* is executable and
printed (`CSharpWriter.cs:901-902`) but **cannot be read back** — no expression-level
`throw` rule; safe today only because no producer lowers to it.

**Missing:** `++`/`--`; compound assignment (`+= -= *= /= %= &= |= ^= <<= >>= ??=`); `switch`
expressions; patterns beyond `is Type`; tuples/deconstruction; ranges `a..b` and
index-from-end `^n`; `with`; target-typed `new()`; collection expressions `[1, 2]`;
object/collection initializers `new T { X = 1 }`; anonymous objects `new { X = 1 }`; named
arguments; `checked`; `sizeof`; method groups; `ref`/`out`/`in`/`params` arguments; `::`; an
expression-level `throw` rule.

### 2.6 Executable envelope (`LanguageSurfaceTests.Kinds`)

- **Executable (60 kinds):** arithmetic/bitwise/comparison, `Variable`, `Member`, `Invoke`,
  `New`, `NewArray`, `IndexAccess`, `Lambda`, `Block`, `Assignment`, `IfStatement`, all
  loops, `SwitchStatement`, `TryCatchFinally`, `UsingStatement`, `Return`, `ThrowStatement`,
  `ThrowExpression`, `BreakStatement`, `ContinueStatement`, `GotoStatement`,
  `LabelDeclaration`, `TypeIs`, `TypeAs`, `TypeCast`, `TypeOf`, `Coalesce`, `Conditional`,
  `SuspendNode`, `ThisReference`, `NullForgiving`, `Default`, `Comment`, `Parameter`,
  `PopCount`, `StridedSetBits`.
- **`CompileReject`:** `Await`, `ParameterReference`, all type-reference kinds as program roots.
- **`AnalysisOnly`:** compilation unit, type/member declarations, attributes, base-ctor
  invocation, `ResolvedTypeReference`, collection/map/optional/union type references.

### 2.7 Analysis ripple (the real cost driver)

Each executable node must be understood by the passes in
`Poly/Interpretation/Analysis/Semantics/`: `TypeAndMemberResolutionPass`,
`ValueRepresentationPass`, `DefiniteAssignmentAnalyzer`, `SideEffectAnalysisPass`,
`CallSiteCatalogPass`, `LambdaCaptureCollector`, `LambdaReturnTypeAnalyzer`,
`SyntaxTypeCompatibilityAnalyzer`, `ThisReferenceContextPass`, `JumpTargetPass`,
`ExceptionRegionAnalysisPass`, `TypeDefinitionNodeAnalyzer`, `VariableLifetimePass`, plus
`ConstantFolding/` and `ControlFlow/`.

## 3. Gap inventory

Cost: **S** ≈ a day, **M** a slice or two, **L** multi-slice, **XL** a track. "Dest" = where
it must land: **console** (read + execute in the REPL) or **authorable** (also usable inside
a `.poly` domain action: DSL parser + `DomainExpressionLowering` + exporter + golden corpus +
`poly-dsl-guide.md`).

| # | Missing | Dest | Cost | Notes / risk |
|---|---|---|---|---|
| L1 | interpolated strings `$"…"` | console | — | **Shipped 2026-10-17** as a real node, not a desugar. New `InterpolatedStringLiteral` token carrying the **raw body**; new `InterpolatedString(Literals, Holes)` node (only holes are children; `Literals.Count == Holes.Count + 1`); the reader splits `{{`/`}}` literals from holes and parses each hole with the same `expr` rule; analysis resolves it as `string` and classifies `(HeapRef, string)`; the emitter lowers it to one `string.Concat(object[])` with each hole boxed from its **value representation** (a scalar hole is boxed by CLR type — the `object` parameter path would have read it as a heap handle); the writer re-emits `$"…"` so write→read→write is idempotent and Roslyn-clean. Fail-closed with positioned errors: format specifiers, alignment, empty hole, unmatched `}`, nested quote in a hole, `$@`/`@$`/raw. |
| L2 | verbatim/raw strings, `\e` | console | S | Tokenizer only; no AST change |
| L3 | `0x`/`0b` literals | console | S | `0x1F` currently scans as `0` + identifier |
| O1 | `++`/`--`, compound assignment | console | M | **Shipped (2026-10-17):** the 8 single-token compounds `+= -= *= /= %= &= |= ^=` fold to `target = target op value`, guarded by a purity check on the target's receiver/index (`IsPureExpression`) — the desugar re-evaluates the target, so impure receivers fail closed. **Deferred:** `++`/`--`, and `<<= >>= ??=` (they need tokens/grammar the precedence chain cannot express). Note the desugar's read of the target is masked by `DefiniteAssignmentAnalyzer` (`DefiniteAssignmentAnalyzer.cs:31`), same as the hand-written `x = x + 1`; a read-before-write check is a follow-up, not part of what shipped. |
| L4 | string `+` on non-literal operands | console | — | **Discovered during O1, fixed 2026-10-17.** `var s = "a"; s + "b"` evaluated to `3` (heap-handle arithmetic). Root cause: a local's type is inferred on the declaring `Variable` (`ResolveBlockType`), but every *use* of the name is a distinct `Variable` node, and `GetResolvedType` is keyed by node instance — so `ClrTypeOf(use)` was null, `CheckArithmetic` bailed early, and no type-directed rewrite ever fired for locals. **Fix: propagate the declaration's resolved type onto the use** in `VariableLifetimePass.ValidateVariableReference` (the pass already builds `VariableReferences`; nothing read it). This is the correct model — a local's type belongs to every use. |
| O2 | conditional access `?.x` / `?.()` | console | — | **Shipped 2026-10-17.** Folds to `!(recv == null) ? <rest of the chain> : null`: the remainder folds inside the true branch so a null receiver short-circuits the whole chain, and the *orientation matters* — the analysis classifies (and root-extracts) a `Conditional` from its `IfTrue` branch, so putting the `null` there typed `s?.Length` as `object` and made a bare `"hi"?.Length` evaluate to the receiver. Side-effecting receivers fail closed. Still blocked in practice for locals by §3.3. |
| O3 | `checked`/`unchecked`, `sizeof` | console | M | Semantics choice; `sizeof` needs CLR |
| P1 | `switch` **expression** | console | M | New node; first slice = constant/type arms only (no exhaustiveness) |
| P2 | patterns: declaration/const/type + `when` | console | M | `TypeIs` is already three-way lowered |
| P3 | patterns: property/positional/list, `not`/`and`/`or`, exhaustiveness | console | XL | Positional needs `Deconstruct` discovery; list needs length/index machinery; property needs heap member matching |
| T1 | tuples + deconstruction | console | L | Tuple **types**, literals, nesting, named elements, `var (a, b) = t` (touches `Block.variables` + DefiniteAssignment), **and C# structural equality** `(1,2) == (1,2)` — a heap object without value equality is a known-wrong shape (tenet 5) |
| T2 | object/collection initializers, `[1, 2]` collection expressions | console | M | Overlaps `New` + member sets |
| T3 | target-typed `new()`, `with` | console | M | `with` needs record clone semantics |
| T4 | ranges `..`, index-from-end `^`, slicing | console | M | Shares `Caret` token with `BitwiseXor` |
| T5 | anonymous objects `new { X = 1 }` | console | M | LINQ prerequisite |
| S1 | `using var x = …;` declaration | console | M | Scope is the **rest of the block**: the fold must hoist remaining statements into a `UsingStatement` body and register `x` in `Block.variables`, or locals/DefiniteAssignment break |
| S2 | typed `foreach (int x in …)` | console | — | **Shipped 2026-10-17.** Grammar `foreach-decl` (`var` \| `type`) plus a writer fill for the declarator. **The declared type is accepted but not enforced** — the loop variable keeps the collection's element type (`ResolveForEachLoopType`) and the writer prints `var`, so read→write is stable but lossy. See §3.2. |
| S3 | typed locals + typed/var `for`-init | console | — | **Shipped 2026-10-17 (initialized declarations, enforced):** `int x = 5;` / `double x = 1;` establish the declared type (initializer cast), destination typing, and conversion on assignment — `double x = 1; x = 2; x / 2` is `1`, not `5E-324`. See §3.2. **Still missing:** `for (int i = 0; …)` (for-init is `Ref("expr")`; needs a `ForLoop` scope arm in `VariableLifetimePass`) and bare `T x;`. |
| S4 | exception filters `catch (…) when (…)` | console | M | `CatchClause` has **no filter slot** — changes a shipped AST record's shape and every construction site, plus grammar, fold, writer, `ExceptionRegionAnalysisPass`, catch lowering |
| S5 | `lock`, `checked` blocks, `const` locals | console | M | `lock` needs monitor lowering |
| S6 | iterators (`yield return`/`break`) | console | L | State machine; related to `SuspendNode` |
| Y1 | `async`/`await` state machine | console | XL | Parsed but `CompileReject`; open docs PR #193 (in flight, not parked) |
| G1 | user-defined generic instantiation in the VM | console | L | VM resolves at lowering time today |
| G2 | LINQ method syntax / extension resolution | console | L | Generic inference + extension methods; needs T5 |
| G3 | virtual/interface dispatch | console | L | `vm-gap-analysis.md` notes `CallExternal` today (that doc is historical) |
| D1 | `struct` | authorable | M | Absent entirely (no `KwStruct`) |
| D2 | modifier set (`sealed abstract virtual new partial`), `where` constraints | authorable | M | Declarations are `AnalysisOnly`; value is reading code |
| D3 | primary constructors (class/struct) | authorable | M | Records already parse positional params |
| D4 | `operator`, indexers, events, `delegate`, anonymous methods | authorable | L | New member kinds |
| D5 | `required`, `using static`, default interface members | authorable | M | — |
| M1 | `ref`/`out`/`in`/`params`, named arguments | authorable | M | Parameter/argument model + ABI |
| M2 | `nameof` **execution** | console | M | Already parses/prints. Two parts: (a) fold `Invoke(TypeReference("nameof"), [arg])` → `Constant(lastIdentifier(arg))` in `ConstantFoldingPass` (the emitter honours replacements); (b) the resolution/lifetime passes must treat the argument as **opaque** — `VariableLifetimePass.cs:204` reports an error for the undeclared `Variable` that `nameof(SomeType)`/`nameof(A.B)` produces, so the fold alone only rescues `nameof(local)`. Do **not** add a keyword or change the reader fold (the round-trip test and `DbContextGenerator.cs:269` depend on the shape). Not a warm-up slice. |
| M3 | `throw` **expression** read-back | console | S | Executable + printed already; add the grammar/reader rule so read-back is symmetric |
| X1 | deliberate exclusion: `unsafe`, pointers, `ref struct`/`Span`, `dynamic`, COM, preprocessor *semantics*, query-syntax LINQ, `static` lambdas/`using static` beyond D5 | — | — | — |

### 3.1 Exposed by O2 and by enforcing typed locals

Two gaps that the O2/enforced-types work made visible and did **not** fix.

**Member access on a local variable does not resolve — FIXED 2026-10-17.** `var s = "hi"; s.Length` failed with `Member 'Length' is not resolved` because a local's type is attached to its *declaration* node and propagated to uses in `VariableLifetimePass` (pipeline position 24), while `TypeAndMemberResolutionPass` resolves members at position 22 — so member resolution always saw an untyped receiver for a local, and `?.` on a local failed the same way. It was invisible because the DEI's `TryCompileAstCallable` silently falls back to name-based dispatch when a body's analysis errors. **Fix:** the resolver now seeds a declared variable's type over the declaring scope's *statements* as it computes the block's types (`ResolveBlockType`), reusing the existing name-based seeder (renamed `SeedVariableType`, which already stops at a shadowing block); the same seeding was added for `foreach` loop variables. Watch the seeding root: passing the declaring block itself to the seeder trips its shadowing guard and seeds nothing — seed the block's statements instead. Verified: `var s = "hi"; s.Length` → `2`, `s?.Length` → `2`, `s?.Length + 1` → `3`, `s?.Length == 2` → `true`.

Still open from this: seeding does not yet cover **lambda parameters** (uses of a lambda parameter inside its body are `Variable` nodes whose type lives on the `Parameter`), and the pipeline-position mismatch remains a design smell — a proper declared-type scope in the resolver would subsume both the seeding here and the `L4` propagation in `VariableLifetimePass`.

Coverage as verified by the review round: block locals and `foreach` loop variables resolve. Three pre-existing issues were surfaced along the way, all confirmed **not** caused by this change (attributed by disabling the seeding and re-running):

- **Lambda parameters — FIXED 2026-10-17.** `((long a) => a + 1)(4)` used to fail with `Variable 'a' not declared in any scope`, and the shadowing variant silently read the *outer* local. Root cause: the inline parameter map was keyed by the `Parameter` node and consulted only when compiling a `Parameter`, while the reader emits a **fresh `Variable` per identifier** for the body's uses; the scope validator had no `Lambda` case, and `LambdaCaptureCollector` counted a parameter-named body use as a free capture. Fix: name-keyed parameter scopes (innermost-first) for both inline ring slots and frame parameter slots, consulted *before* locals so a parameter shadows an outer local on read **and** write; arguments compiled before any parameter is mapped (so an argument sees the caller's scope); a scope-validator `Lambda` arm registering parameters as scope names (generalising `VariablesByName` to `Stack<Node>`); the capture collector excluding own parameters; and the resolver seeding a typed parameter's uses so `((string s) => s.Length)` resolves. Verified: plain → 5, shadowing → 5, member access → 2, parameter write, caller-scoped arguments, nested same-name shadowing, capture alongside a parameter, and an untyped-parameter member access failing closed.
  Still unsupported (pre-existing, unchanged): a **stored** source lambda — `var f = (long a) => a + 1; f(4)` — fails with `Invoke target must be a member, lambda, or stored closure, got Variable`, because `StoredLambdaMetadata` is attached to the declaration `Variable` while the invoke's delegate is a fresh `Variable`. Same reader-identity class, different path; the new frame-parameter mapping would make it work once the delegate resolves.
- **Inner-block shadowing returned a wrong value — FIXED 2026-10-17.** `var s = "hi"; var n = 0; { var s = 5; n = s + 1; } n` returned `2` (the raw heap handle of the *outer* `"hi1"`). Two independent identity holes, both from the reader making a fresh `Variable` node per identifier:
  1. `DirectVmAbiEmitter.AbiCtx.TryGetRegister` resolved locals with an **unordered flat name scan** over `_variableRegisters` that ran *before* the scope-aware `TryGetVariable`, so a use bound to the **first same-named entry — the outer shadowed local**. Now identity-first, then a name fallback from the innermost scope outward (reporting not-found when the innermost binding is slot-backed, so the scope-aware path wins).
  2. `SyntaxTypeCompatibilityAnalyzer.CheckVariableAssign` keyed its prior/current type on the assignment's **Destination node**, and every write is a fresh node, so the incompatibility check never ran. It now keys on the **binding** via `VariableAnalysisMetadata.VariableReferences` — the map the scope validator had been populating with no consumer since it was written. `var n = 0; n = "hi"` now fails closed at compile time.
  Verified: `6`, two-level shadowing `110`, outer-after-inner `106`, heap-into-scalar rejected (`ShadowedVariableVmTests`). Existing tests missed this because they reuse the declaration node as the use, exercising only the identity path.
  Follow-on: with the check live, C# **widening** had to be re-allowed explicitly and **narrowing** rejected. The precise scope matters: a scalar widening into an **IEEE slot** (`double x = 1; x = 2;`) is legal and converted on store, while `double → scalar` truncates where C# rejects it and stays rejected — but scalar↔scalar writes (`int`↔`long`) are the *same slot encoding* and are deliberately allowed (pinned by `SequentialIntThenLong_SameSlotEncoding_StillRuns`). A first attempt rejected all narrowing and broke that pin; the exemption is now IEEE-only, and decimal is caught by the Kind check (heap slot) rather than by a numeric ladder.
  Review residuals, recorded not fixed: `char` is absent from the numeric ladder (so a `char → int` write is rejected via the Text category — pre-existing); `BindingOf` degrades **silently** to the node itself when a destination was never visited by the scope validator (a node minted after pass 24 would make the check quietly stop firing — a fail-closed diagnostic would be better); `TryGetVariable`'s name fallback walks a `Dictionary` with unspecified in-scope order while `TryGetRegister` walks the ordered list (they can disagree if one block declares the same name twice, which generated trees can produce); and cell-backed locals, frame-local arrays, and parameters (`TryGetInlineParameter`/`TryGetCapture`) still use flat name scans — the same mis-binding class remains there.

  One review question was settled by reading, not deferred: **a newly-live analysis error in a generated (DEI) body cannot pass silently.** `DomainEntityInstance.CompileBody` analyses and then calls `Interpreter.Compile`, which fails loud on analysis errors; `DomainInstanceStore` catches that `InvalidOperationException` and turns it into `DomainResult.Failure`. So the failure mode is a *failed action*, which the dogfood tests assert against — which is exactly how the earlier `Create{Nav}` breakage showed up ("OpenDeal failed: …"). A green suite therefore does cover the DEI risk here.

### 3.4 Review round: lambda parameters + interpolation (2026-10-17)

Both slices went through the independent review after integration. Three 🟠, all fixed:

- **A local could shadow a lambda parameter** and then never be read or written, because the emitter resolves parameter *uses* by name and consults parameters before locals — e.g. `((long a) => { var a = 9; a; })(4)` mis-bound. C# forbids it (CS0136); the scope validator now **errors** on a local that shadows a parameter (the pre-existing *warning* is kept for local-shadows-local, which this VM deliberately allows).
- **The flat inline-parameter map was never trimmed on pop** and `TryGetInlineParameter(Parameter)` fell back to scanning it by name, so a popped parameter could bind to a ring slot the emitter had reused. The fallback now scans only the live parameter scopes.
- **The 0-arg frame-path invoke hard-wired its result into ring slot 0** and reset the depth to 1, discarding the caller's live slots (an earlier interpolation hole, or an inline parameter's argument slot). It now writes to the caller's first free slot, with a regression test for both shapes.

Also fixed from the same round: the reader's hole scanner now skips char literals, so a hole whose printed form contains a raw `}` (e.g. `$"{'}'}"`) round-trips; and `MapFrameParameter`'s dead auto-create (a leak path) is gone.

Residuals, recorded: the emit-time push/pop pairs are not in `try/finally` (the only catch-and-continue compiles through a fresh context, so a leak is unreachable today); `InterpolatedString`'s `Literals.Count == Holes.Count + 1` invariant is not enforced by the record; `string.Concat` renders holes with the current culture (faithful to C#, but the interpolation tests' expected text is locale-sensitive); `EmitInlineInvoke`'s doc comment is stale; and whether `MermaidAstGenerator`/`DomainDslPrinter`/`ConstantFolding` need an explicit `InterpolatedString` case is unverified — they would only see one if a producer emitted it, and none does (interpolation is console-only).
- **Use before declaration now resolves and fails at runtime** (`s.Length; var s = "hi";` → "Member 'Length' requires a non-null instance") instead of failing to resolve. An error either way, but the message moves from analysis to execution; nothing anywhere reports use-before-declaration (`DefiniteAssignmentAnalyzer` is metadata-only).
- Also pre-existing, met while probing: invoking a variable-bound lambda (`var f = () => 1; f()`) is unsupported ("Invoke target must be a member, lambda, or stored closure, got Variable").

**Narrowing assignment is fail-open.** `int x = 1; x = 2.5; x` yields `2` (the coercion
truncates) where C# rejects the program. `SyntaxTypeCompatibilityAnalyzer.CheckVariableAssign`
keys its "prior type" check on the destination *node*, and every assignment's
destination is a fresh `Variable` instance, so the check never fires. Fixing it needs
the project's implicit-conversion rules (the same machinery as `ArgumentMatches`);
a hand-rolled check would reject valid widening (`double x = 1;` assigns an int-valued
initializer before the cast). Recorded, not fixed.

Also noted: `CSharpReader` now has two purity predicates with opposite answers for a
`Member` read — `IsSideEffectFreeReceiver` (O2; treats member reads as pure, so
`a?.b?.c` double-reads `a.b`) and `IsPureExpression` (compound assignment; rejects
`Member`). Unifying them means choosing one policy, which decides whether chains are
supported at all.

### 3.2 Declared types: locals enforced, `foreach` still not

**Locals are enforced as of 2026-10-17.** `int x = 5;` / `double x = 1;` fold the
initializer through a `TypeCast` so the variable *is* the declared type, and two
companions make that hold:
- `VariableLifetimePass` types an assignment's **destination use** from the
  declaration, not from the assigned value (the previous asymmetry meant
  `var x = 1; x = 2.5;` left `x`'s uses disagreeing).
- `DirectVmAbiEmitter.CoerceSlotToDestination` converts on write: doubles/floats live
  in slots as IEEE bits, so without it `double x = 1; x = 2;` stored the integer `2L`
  where the double path read bits — verified returning `5E-324` before the fix, and
  `1` after.

Covered by tests: `double x = 1; x / 2` → `0.5`, `x = 2; x / 2` → `1`,
`x = 2.5; x * 2` → `5`, `int x = 7; x / 2` → `3`.

**`foreach`'s declared type is still not honored.** `foreach (int n in xs)` parses,
but the loop variable keeps the collection's element type (`ResolveForEachLoopType`),
and the writer prints `var`, so read→write is stable but lossy. For a *cross-type*
annotation (`foreach (double d in intArray)`) that is a wrong value, not just a lost
annotation. Honoring it needs `ForEachLoop` to carry the declared type (a new
field), `ResolveForEachLoopType` to prefer it, and `ForEachText` to print
`Render("foreach-decl", "typed", …)`. Also still unparsed: `for (int i = 0; …)`
(for-init is `Ref("expr")`; a declaration there needs a `ForLoop` scope arm) and bare
`T x;`.

### 3.3 Residual defect found while doing L4 (DEI-only)

Making a local's *use* carry its declaration's type is correct, but it made the
compatibility checker actually inspect overload candidates for a typed receiver —
which exposed a real bug in the scratch bind. `DomainEntityInstance` merged the
lowered module's methods into its runtime-shaped entity keyed by **name only**
(`names.Add(m.Name)`, `DomainEntityInstance.cs:992-1001`), so a same-name overload
(`WhenEachX(peer)`) was dropped whenever a stub of that name already existed. The
call then had no candidate, the body failed analysis, `Create{Nav}` fell off the
direct AST path to the name-based `InvokeNamed` path, and the walk reported
"Action not found".

Fixed by keying the merge on name+arity (`MethodSignatureKey`). Two things to
record: (a) the emitted module was **byte-identical** with and without the type fix
(`git diff` of the DslCompiler output for `crm.poly` was empty) — the lowering was
never at fault, so an earlier "ABI blast radius" reading of this was wrong; (b) this
class of bug disappears with DEI retirement (board `CURRENT`: C8d + C10), so the fix
is a stopgap, not a foundation to build on.

## 4. Slice plan

Each slice is self-contained: tokenizer kind + scan → grammar rule → reader fold → AST node
→ analysis passes → emitter lowering → writer print → tests → docs. Console-only slices leave
the domain DSL, exporter, and golden corpus untouched.

**Tier A — console ergonomics, small and high value (recommended first)**

1. **O1 compound assignment (`+= -= …`) for simple variables** (S) — tokenizer kind + grammar
   tail + reader desugar to `Assignment(v, Add(v, expr))`; correct in this scope, no
   double-evaluation because the target is a plain variable. Highest value for the smallest
   change; wider value semantics follow.
2. **S2 typed `foreach`** (S) and **S3 typed locals** (`int x = 5;`) (M) — grammar + reader
   declaration tracking; ends the "you must write `var`" restriction.
3. **O2 conditional access `?.`** (S/M).
4. **S1 `using` declaration** (M), **S4 exception filters** (M).
5. **M2 `nameof` execution** (M) — fold + resolver opacity, per the row above.
6. **L1 interpolation** (M, real AST node) — Tokenizer `$"`, grammar with `{hole}` and
   `{{`/`}}`, `InterpolatedString` node, lowering to `String.Concat`/`Format`, writer,
   `Kinds` entry.
7. **L2/L3 lexical breadth** (S each).

**Tier B — patterns and `switch`**

7. **P1 `switch` expression** (M, constant/type arms), **P2 declaration/const/type patterns +
   `when`** (M). Then, as its own track, **P3** property/positional/list patterns +
   exhaustiveness (XL).

**Tier C — types and collections**

8. **T1 tuples/deconstruction** (L, with structural equality or an explicit scope-out),
   **T2 initializers + collection expressions** (M), **T5 anonymous objects** (M),
   **T3 target-typed `new` and `with`** (M), **T4 ranges/index-from-end** (M).

**Tier D — platform-level tracks (decide explicitly)**

9. **G2 LINQ** (L), **G1 user generics** (L), **G3 virtual dispatch** (L), **Y1 async/await**
   (XL), **S6 iterators** (L).

**Tier E — declaration breadth**

10. **D1 `struct`** (M), **D2 modifiers + constraints** (M), **D3 primary constructors** (M),
    **D4 operators/indexers/events/delegates** (L), **D5 `required`/`using static`/DIM** (M),
    **M1 `ref`/`out`/`in`/`params` + named args** (M).

**Suggested first release:** Tier A (1–6) plus **P1+P2** (constant/type arms). That is where

```csharp
var total = 0;
foreach (var n in numbers) { total += n; }
$"total = {total}, max = {nameof(total)}"
```

runs. Tier C+ needs an explicit "yes, this far" decision, because each item either adds an
AST node with runtime representation (tuples, ranges) or needs a VM capability that does not
exist (generics, dispatch, async).

## 5. Definition of done (per slice)

- `LanguageSurfaceTests.Kinds` updated in lockstep with `DirectVmAbiEmitter.CompileNodeInner`;
  `EveryConcreteNodeType_IsInventoried` stays green.
- **Roslyn cross-check for the new construct**, not just today's category counts: the current
  oracle compares counts over seven categories (`CSharpLanguageTests.cs:81-89`) and has no
  category for switch expressions, patterns, or tuples, so a wrong-shape fold
  (`?.` → `.`) passes with equal counts. Extend the categories or add a targeted
  `CSharpReaderTests`-style tree assertion per construct.
- **Sweep the other `Node` consumers** when a kind is added: `CSharpGenerator`,
  `MermaidAstGenerator`, `DomainDslPrinter`, and the domain DSL parser are not guarded by
  `EveryConcreteNodeType_IsInventoried`.
- An **analysis-pass test** (the ripple in §2.7 is the real cost driver).
- A `LanguageVmTests` oracle (compile+execute or compile-reject) for the new kind.
- Write→read→write stable; golden corpus unchanged; unsupported input still fails closed
  with a positioned error.
- If authorable: DSL parser + lowering + exporter + golden files + `poly-dsl-guide.md` in the
  same change.

## 6. Open questions

1. Console-only or authorable for each tier? (Tier A as console-only avoids DSL/corpus churn.)
2. How far into Tier C/D/E is "reasonable"? Each is a decision, not a default.
3. Interpolation by desugaring (no new node) or a real node? Desugaring keeps the corpus
   stable; a node is more faithful but touches writer + reader + a new executable kind.
4. Tuples: implement structural equality, or scope it out explicitly (tenet 5)?
5. Confirm the Roslyn-scripting escape hatch stays rejected (it would make the console a C#
   scratchpad rather than a Poly-VM surface).
6. Where this plan lives / how it is admitted (board `THEN` is owned by a human).
