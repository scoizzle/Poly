# C# surface — complete task breakdown (2026-10-17)

Companion to [`2026-10-17-csharp-surface-gap-inventory.md`](2026-10-17-csharp-surface-gap-inventory.md)
(the inventory says *what is missing*; this says *what each piece of work is*). Status is
research/planning — the board is the only queue, and admission is a human decision.

Every task below is independently shippable. **T0 tasks are not features: they are places
where the surface currently lies, errors unhelpfully, or mis-binds — fix them first.**

## How to read a task

`ID — title` · **dest**: `console` (read+execute in the REPL) or `authorable` (also usable
inside a `.poly` domain action: DSL parser + lowering + exporter + golden corpus + guide) ·
**size**: S/M/L/XL · **dep**: what must land first · **today**: current behaviour ·
**build**: the pipeline stages · **accept**: the tests that must exist.

## Standing discipline for every task

1. **Bidirectional surface.** One grammar table drives reader *and* writer. Changing a
   pattern's shape requires updating the writer's positional fills (a past attempt cost 109
   failures).
2. **Fail closed.** A form that cannot be executed soundly must throw a positioned error,
   never fold to a wrong shape.
3. **Kinds lockstep.** A new `Node` needs a `LanguageSurfaceTests.Kinds` entry *and* a
   `DirectVmAbiEmitter.CompileNodeInner` arm (and a `CompileValue` arm if it is a value).
4. **Tests ship with the feature.** Reader shape, execution, an oracle
   (`LanguageVmTests`/`CSharpScriptTests`), and the fail-closed case.
5. **Console-only means console-only**: no DSL parser, exporter, or golden-corpus change.
6. **`Write(Read(x))` stays idempotent** and Roslyn-clean; the golden corpus never changes
   for console-only work.

---

## Wave 0 — the surface lies today (fix before adding anything)

- **T-A1 — `is` always evaluates to `false`.** dest console · S · dep — · today `1 is int`,
  `"hi" is string`, `o is object` all return `false` (and `o is string s` errors) while the
  node-level oracle `TypeIs(Constant, TypeReference.To<string>())` returns `1`. The reader
  folds the type operand to a `PrimitiveTypeReference`, which the emitter's three-way `TypeIs`
  lowering does not match. · build: emitter `TypeIs` (resolve the primitive/CLR forms the
  reader produces), or fail closed in the reader; a scalar operand is answered from its
  static type, so the reader must keep an unsuffixed integer literal `int`. · accept:
  `1 is int`→true, `1 is long`→false, `var o = "hi"; o is string`→true, `o is object`→true,
  plus a VM oracle for the primitive-reference form. Declaration-pattern *binding* (`s`
  after `o is string s`) is **T-D2**; T-A1 evaluates the test and leaves `s` unbound.
- **T-A2 — `foreach` declared type is not enforced.** dest console · M · dep — · **Landed 2026-10-18** (`ForEachLoop.DeclaredType`; the reader folds the typed arm; the resolver prefers it; the analyzer rejects a pair with no per-item conversion; the emitter converts scalar items in slot space; the writer prints the typed form). Scope: scalar element/loop-type pairs only — a value element with a reference loop type fails closed until box-per-item lands. · accept (met): element-typed and cross-typed scalar cases execute with C# semantics; the writer round-trips `foreach (double d in …)`; a non-convertible pair fails closed.
- **T-A3 — `char` missing from the numeric ladder.** dest console · S · dep — · **Landed 2026-10-18.** `CategoryOf(char)` stays `Cat.Text` (string concat needs it); `IsNumericLike` carries `char` for arithmetic, comparison, implicit char→numeric assignment, and explicit numeric casts. `char + char` is numeric, and the `string.Concat` rewrite folds literal non-string parts. · accept (met): `char → int`/`long`/`double` widening succeeds, `int → char` stays rejected, `'a' + 1`→98, `'a' + 'b'`→195, `(int)'a'`→97, `"a" + 1`→`"a1"`, `'a' + "b"`→`"ab"`. A non-literal non-string concat part fails closed (the emitter cannot box it into `Concat(object)`).
- **T-A5 — `BindingOf` degrades silently.** dest console · S · dep — · today a destination
  `Variable` minted *after* the scope pass falls back to per-node keys, so the assignability
  check quietly stops firing. · build: report (or assert) when a destination resolves to
  itself and is not a declaration. · accept: a synthetic post-pass node produces a
  diagnostic instead of silence.
- **T-A6 — the in-scope name fallback is unordered.** dest console · S · dep — · today
  `TryGetVariable`'s per-scope fallback walks a `Dictionary` while `TryGetRegister` walks the
  declaration-ordered list, so duplicate names in one scope can resolve differently. · build:
  drive both from the same ordered list. · accept: a duplicate-name block resolves
  deterministically (or fails closed).
- **T-A7 — emit-time parameter scopes are not unwind-safe.** dest console · S · dep — ·
  today `Push/PopInlineParameters` and `Push/PopFrameParameters` are not in
  `try/finally`; the only catch-and-continue compiles through a fresh context, so a leak is
  unreachable *today*. · build: pair them with `finally`. · accept: no behaviour change; a
  deliberately-throwing compile leaves no leaked scope (unit-testable via an instrumented
  context).
- **T-A8 — small node/API hygiene.** dest console · S · dep — · `InterpolatedString`'s
  `Literals.Count == Holes.Count + 1` invariant is unenforced; `EmitInlineInvoke`'s doc
  comment is stale; `ClearInlineParameters` has no caller. · build: guard + docs. · accept:
  a malformed node throws; no stale comment.
- **T-A10 — qualified type-name access.** dest console · S · dep — · **Landed 2026-10-18.** A member chain over an identifier that spells a CLR type name is rewritten (in the resolver) to a `ClrTypeReference`, so the access above it is static; `TypeOf.Children` yields its type so `typeof` resolves. · accept (met): `System.String`/`System.Int32`/`System.Math.Abs(-1)` resolve; the `Task.CompletedTask` probe executes. Known limits: a nested type with a type prefix, `typeof(keyword)`, and an *untyped* lambda parameter named like a namespace root (the CLR rewrite wins there; a typed parameter or local shadows correctly).
- **T-A4 — DEI's remaining name-keyed scans.** dest console · M · dep — · same mis-binding
  class already fixed twice: cell-backed locals, frame-local arrays, and parameters
  (`TryGetInlineParameter`/`TryGetParameterSlot`/`TryGetCapture`). · **check first**: DEI is
  being retired (board CURRENT C8d+C10); if the retirement deletes these, close as obsolete.
  · accept: shadowing a cell-backed/captured local resolves to the innermost declaration.
- **T-A9 — interpolation output is culture-sensitive.** dest console · S · dep — · today the
  `Concat(object[])` render follows the current culture (faithful to C#, but the tests assert
  `"2.5"`-style strings). · build: none, or pin tests to an invariant culture. · accept: the
  interpolation tests do not depend on the machine locale.
- **T-J1 — two purity predicates with opposite `Member` answers.** dest console · S · dep — ·
  `IsSideEffectFreeReceiver` (O2) treats a member read as pure (so `a?.b?.c` double-reads
  `a.b`); `IsPureExpression` (compound assignment) rejects `Member`. · build: one predicate,
  or a documented divergence at both sites. · accept: chains either supported with a pinned
  double-read note, or rejected.

## Wave 1 — cheap console ergonomics (each small, high value)

- **T-C1 — `++` / `--`.** console · M · dep — · today a parse error. · build: tokenizer
  (`PlusPlus`/`MinusMinus`), grammar (prefix + postfix), reader desugar with **value
  semantics** (`y = x++` yields the *old* value) and lvalue-once for `a[i]++`. · accept:
  statement and expression positions, pre/post values, `a[i]++` evaluates the index once.
- **T-C2 — `??=`.** console · S · build: token, grammar tail, desugar to
  `x = x ?? v` (single-evaluation guarded like the other compounds). · accept: `null` and
  non-null receivers.
- **T-C3 — `<<=` and `>>=`.** console · S · build: 3-char tokens (the precedence chain cannot
  express them). · accept: both, on a simple target.
- **T-C4 — compound assignment for impure targets.** console · M · dep T-C1 · today
  `a[i] += f()` / `Get().P += 1` fail closed by design. · build: an lvalue temp so the target
  is evaluated once, then the purity guard can relax. · accept: `a[Bump()] += 1` calls `Bump`
  once and stores to the original index.
- **T-B1 — verbatim strings `@"…"`.** console · S · build: tokenizer (no escape decoding,
  `""` = a literal quote); writer prints them back. · accept: round-trip + `@"C:\x"`.
- **T-B2 — raw string literals `"""…"""`.** console · S · dep T-B1 · build: tokenizer +
  writer. · accept: multi-line raw content round-trips.
- **T-B3 — hex and binary literals.** console · S · build: `ScanNumber` for `0x`/`0b` (+ `_`
  separators everywhere), reader numeric fold. · accept: `0xFF`→255, `0b1010`→10, `0x1F` no
  longer scans as `0` + identifier.
- **T-C5 — `checked` / `unchecked`.** console · M · build: keywords, expression and block
  forms; decide semantics (overflow throwing vs wrapping) for the VM's long arithmetic.
  · accept: `checked { … }` overflows loudly, `unchecked` wraps, and the default is stated.
- **T-D2 — declaration/constant/type patterns + `when`.** console · M · dep T-A1 · build:
  extend the reader's `is` fold and the emitter's type tests; `when` guards on `case`.
  · accept: `o is string s` binds, `o is 3`, `case string s when s.Length > 1`.
- **T-E6 — `nameof` execution (M2).** console · M · dep T-A10 · **Landed 2026-10-18.** The
  constant-folding pass folds `Invoke(TypeReference("nameof"), [arg])` to `Constant(lastName)`
  and publishes the value, so a parent literal add folds it; the scope validator does not walk
  the argument (it is a name); `CheckInvokeTarget` accepts the callee. The reader fold and the
  tokenizer are unchanged (the round-trip shape and `DbContextGenerator` still hold). · accept
  (met): `nameof(x)`, `nameof(Type.Member)`, `nameof(System.String)` and `"a" + nameof(x)`.
  A bare keyword argument (`nameof(int)`) is extracted but not tested.
- **T-F1 — `using var x = …;` declarations.** console · M · build: hoist the rest of the
  block into a `UsingStatement` body and register `x` in `Block.variables`. · accept:
  dispose runs at scope exit; locals after the declaration still resolve.
- **T-F2 — exception filters `catch (…) when (…)`.** console · M · build: a filter slot on
  `CatchClause` (a shipped AST record — update every construction site), grammar, fold,
  writer, `ExceptionRegionAnalysisPass`. · accept: a filter decides the handler; no filter is
  unchanged.
- **T-F4 — `const` locals.** console · S · build: grammar + fold to a declaration with
  constant-folding semantics. · accept: `const int k = 3;` then `k + 1`.
- **T-F6 — `for`-init declarations.** console · M · today `for (var i = 0; …)` **and**
  `for (int i = 0; …)` are parse errors (for-init is `Ref("expr")`). · build: a for-init
  declaration form + a `ForLoop` scope arm in `VariableLifetimePass` so `i` is declared for
  the condition/increment/body; emitter slot allocation. · accept: both forms run; `i` is out
  of scope after the loop.
- **T-F7 — bare `T x;` declarations.** console · S · build: a declaration pattern without an
  initializer (definite-assignment implications). · accept: `int x; x = 1; x + 1`.
- **T-C8 — `throw` as an expression (read-back).** console · S · today it is executable and
  printed but cannot be read back. · build: an expression-level `throw` rule + fold.
  · accept: `x > 0 ? 1 : throw new E()` round-trips.
- **T-E11 — conditional element access `a?[0]` and `a?.b[c]`.** console · S/M · build: the
  `?[` token/grammar path plus the chain-short-circuit fold. · accept: null receiver
  short-circuits; the current fail-closed pin moves to a still-unsupported form.

## Wave 2 — expressions and types (medium)

- **T-D1 — `switch` expressions.** console · M · dep T-D2 · build: new node + emitter
  (first slice: constant/type arms, no exhaustiveness), writer. · accept: `x switch { 1 => 2,
  _ => 3 }` executes; a non-exhaustive switch fails closed.
- **T-D3 — relational / logical patterns.** console · M · dep T-D2 · build: `> 0`, `>=`,
  `and`/`or`/`not`, parentheses. · accept: `x is > 0 and < 10`.
- **T-D5 — `switch` statement patterns + `goto case`.** console · M · dep T-D2 · build:
  pattern arms in the statement form (today only constant `case`), `goto case/default`.
  · accept: a type/relational arm is selected; `goto default` works.
- **T-E1 — object and collection initializers.** console · M · build: `new T { X = 1 }`,
  `new T { [k] = v }`, nested; emitter member writes after construction. · accept:
  `new List<long> { 1, 2 }`-style and a property initializer.
- **T-E3 — target-typed `new()` and `with`.** console · M · dep T-E1 (for `with` clones on
  records). · accept: `List<long> xs = new();` and `r with { X = 1 }`.
- **T-E4 — tuples.** console · L · build: tuple *types*, literals, deconstruction
  (`var (a, b) = t`), named elements, and **structural equality** (a heap tuple without value
  equality is a wrong shape). · accept: construction, deconstruction, `(1,2) == (1,2)` true.
- **T-C7 — ranges and index-from-end.** console · M · build: `..`/`^` tokens (mind
  `Caret`'s reuse), range/index nodes, slicing on arrays/strings. · accept: `a[^1]`,
  `a[1..3]`, `s[^2..]`.
- **T-E2 — collection expressions `[1, 2]` (+ spread `..`).** console · M · dep T-E1/T-E4.
  · accept: `var xs = [1, 2]; xs[1]`.
- **T-E9 — stored source lambdas and `static` lambdas.** console · M · today
  `var f = (long a) => a + 1; f(4)` fails (`Invoke target … got Variable`) because
  `StoredLambdaMetadata` sits on the declaration `Variable` while the delegate is a fresh
  one. · build: resolve the invoke delegate through the binding. · accept: the snippet runs;
  `static x => …` parses.
- **T-E10 — method groups.** console · M · build: a member name as a delegate value.
  · accept: `xs.ForEach`-style only where sound, or fail closed with a clear message.
- **T-B5 — interpolated verbatim/raw strings.** console · S · dep T-B1/T-B2 · accept:
  `$@"C:\{x}"` and `$$"""…{{x}}…"""`.
- **T-E5 — user-defined indexers.** console · M · dep T-E1 · build: `this[…]` members.
  · accept: a type with an indexer reads and writes it.

## Wave 3 — declarations and metadata (authorable)

Each of these is about *reading and modelling* declarations; check with the board whether
they are `console` (read/execute) or `authorable` (usable in a domain action) before starting.

- **T-G1 — `struct`.** M · no `KwStruct` at all.
- **T-G2 — modifier set and `where` constraints.** M · `sealed abstract virtual new partial
  extern`, generic constraints.
- **T-G3 — primary constructors** (class/struct). M · records already parse positional
  params.
- **T-G4 — `base.M()` / base-initializer calls in method bodies.** S/M · `KwBase` exists;
  verify what is missing.
- **T-G5 — operators and conversion operators.** L.
- **T-G6 — events, `delegate`, anonymous methods.** L.
- **T-G7 — `required` members, `init` completeness.** M.
- **T-G8 — `using static`, `global using`, `using X = Y` aliases.** M.
- **T-G9 — default interface members.** M.
- **T-G10 — records: positional bodies, `with` (T-E3), structural equality.** M.
- **T-H1 — `ref` / `out` / `in` parameters and arguments.** M · parameter model + ABI.
- **T-H2 — `params`.** M · dep T-E2 for the array form.
- **T-H3 — named arguments.** M.
- **T-G11 — attributes on more targets** (parameters, returns, type parameters). S/M.

## Wave 4 — platform tracks (each its own programme)

- **T-I1 — LINQ (method syntax).** L · extension-method resolution + generic inference +
  `IEnumerable<T>` operators; depends on T-E1/T-E8.
- **T-I2 — LINQ (query syntax).** L or excluded — decide; currently a deliberate exclusion.
- **T-I3 — user-defined generic instantiation in the VM.** L · today the VM resolves
  everything at lowering time.
- **T-I4 — virtual / interface dispatch.** L · `vm-gap-analysis.md` notes `CallExternal`
  today (that doc is historical).
- **T-I5 — async/await state machine.** L · parts 1–2 landed on master (a blocking `await`);
  the state machine is the remainder.
- **T-I6 — iterators (`yield return` / `yield break`).** L · state machine, related to
  `SuspendNode`.
- **T-E8 — anonymous objects `new { X = 1 }`.** M · a LINQ prerequisite.
- **T-I7 — `dynamic`, `unsafe`, pointers, `ref struct`/`Span`, `stackalloc`, COM.**
  Deliberate exclusions — keep excluded unless the product changes.
- **T-I8 — preprocessor semantics (`#if`).** Deliberate exclusion: directive *lines* are
  trivia, and both branches of a region parse as ordinary code. Document or implement.

## Wave 5 — quality, tooling and hygiene

- **T-J2 — audit the other `Node` consumers for new kinds.** S · `ConstantFolding`,
  `MermaidAstGenerator`, `DomainDslPrinter`, and the DSL parser are not guarded by
  `EveryConcreteNodeType_IsInventoried`; any pass that switches on node type needs an arm (or
  an explicit default).
- **T-J3 — reader/writer round-trip edges.** S · a hole printed as a string literal cannot be
  re-read (unreachable from source today); hole error columns assume the token's start line.
- **T-J4 — console diagnostics quality.** S · a reader failure reports the wrapper brace
  (`Expected statement, got '{'`); report the user's own position and text.
- **T-J5 — console UX.** S · snippet picker refresh, per-entry copy, wrapping long output.
- **T-J6 — browser harness as a repo artifact.** S · the Playwright checks currently live
  outside the repo; decide whether the demo deserves a committed smoke test (the demo is not
  in CI).
- **T-J7 — `foreach`/`for` declared-type enforcement follow-ups** (T-A2, T-F6) as their own
  regression tests once the features land.
- **T-J8 — plan/inventory upkeep.** S · keep the inventory's status column and this file in
  step as tasks land; a landed task moves its row to "shipped" with its test names.

---

## Suggested first three

1. **T-A1** (`is` — it lies today, and patterns/`switch` build on it).
2. **T-A2** (`foreach` declared type — wrong values, already parseable).
3. **T-F6** + **T-F7** (`for`-init and bare declarations — sharp edges for anyone typing a
   loop; both are parse errors today).

## Deliberate exclusions (do not task)

`unsafe`, pointers, `ref struct`/`Span`, `dynamic`, COM, preprocessor *semantics*,
query-syntax LINQ (unless decided in T-I2), and `Main`-style entry-point lowering — per
AGENTS.md "shrink the language before a host escape".
