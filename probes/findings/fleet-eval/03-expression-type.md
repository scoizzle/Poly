# Fleet-eval 2026-08-12 — slice: Expression IR & type analysis

Slice files: `Poly/DomainModeling/DomainExpression.cs`, `DomainExpressionDispatch.cs`,
`Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs`, `ConstraintQualityAnalyzer.cs`,
`ConstraintValidation.cs`.

Probes: `probes/fleet-eval/03-expression-type/` — `expr-ok-library.poly` (passes 0/0),
`expr-wrong-types.poly` (every form rejected at ANALYSIS — correct rung), plus one
repro per finding (`expr-f*.poly`). All repros run through `scripts/run-probe.sh`.
Late-rung sweep via `--mode all` on the OK probe: no codegen exceptions on valid surface.

---

## F1 — Invoke-argument type check misses caller-property and caller-parameter args (only literals + binder roots are checked)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** invoke argument type analysis
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f1-invoke-args.poly` —
  `invoke SetAge(age: Name)` (self), `invoke SetAge(age: s)` (self, `s: Text` param),
  `invoke service.SetAge(age: Name)` (cross) where `SetAge(age: Number)`.
  All three pass analysis; export emits `this.SetAge(this.Name)` etc → `error CS1503:
  cannot convert from 'string' to 'long'` ×3.
- **Expected:** wrong-typed invoke argument bindings are rejected at analysis (guide:
  "Expressions are type-checked at analysis"; fail-closed).
- **Actual:** `CheckInvokeArgumentTypes` (ExpressionTypeAnalyzer.cs:158) calls
  `InferLiteralAware(binding.Expression, paramType, enumTypes)` WITHOUT `props`/`parameters`,
  so every bare PropertyAccess/ParameterAccess arg infers `Unknown` and the mismatch is
  skipped. The round-5 F7 fix covered only literal args and binder-rooted args; the
  caller-entity-property and caller-parameter forms (the most common way to forward an
  arg) are still late-rung (CS1503).
- **Proposed patch:** thread `callerProps` + `parameters` into the `InferLiteralAware`
  call at the invoke site (signature already supports optional props/parameters).

## F2 — Runtime-keyword assign RHS to a wrong-typed target passes analysis → CS0029
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** assign RHS type check (keyword forms)
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f2-keyword-assign.poly` —
  `assign OpenedAt to guid` (Date = Guid) and `assign Qty to now` (Number = DateOnly)
  pass analysis; export emits `this.OpenedAt = Guid.NewGuid()` and
  `this.Qty = DateOnly.FromDateTime(DateTime.UtcNow)` → CS0029 ×2. The valid sibling
  `assign OpenedAt to now` compiles.
- **Expected:** `now`/`today`/`guid` in assign RHS are type-checked against the target
  like the `default(...)` form already is (the default form rejects these pairs at
  analysis — `expr-wrong-types.poly` H/I/J).
- **Actual:** `CheckCompatible`/`InferType` treat `now`/`today`/`guid` PropertyAccess as
  Unknown and skip; only `CheckDefault` is keyword-aware. The export's type adaptation
  (EffectLoweringPass.Assign) then emits the wrong CLR expression and the compile fails.
- **Proposed patch:** keyword-aware inference in `CheckCompatible` mirroring
  `CheckDefault`: `now`/`utcnow`/`today` require a Date-category target, `guid` a
  Guid/Text target; report SemanticTypeCompatibility otherwise.

## F3 — Date arithmetic on a date PARAMETER passes analysis → CS0019
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** date arithmetic type analysis + lowering
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f3-date-param-arith.poly` —
  `Set: action (d: Date) { assign DueDate to d + 30 }`. `CheckArithmetic` accepts
  date+number for ANY operand shape; the AddDays rewrite
  (`DomainExpressionLoweringPass.LowerDateArithmetic`) keys on `PropertyAccess` only,
  so the parameter operand reaches the export as `this.DueDate = d + 30L` → CS0019.
- **Expected:** date + number is either lowered to `AddDays` for every date operand
  shape (property, parameter, and, by extension, any Date-category expression) or the
  analyzer rejects the non-rewritable form at analysis. Property-form date arithmetic
  (`assign DueDate to DueDate + 30`, and in create-in initializers) compiles — the
  parameter sibling is the gap.
- **Actual:** analysis silent; export CS0019.
- **Proposed patch:** make `LowerDateArithmetic` operand-type-aware (resolve
  ParameterAccess/parameter types too) or have `CheckArithmetic` reject date+number
  when the date operand is not a rewrite-able shape.

## F4 — Non-boolean `if` conditions pass analysis → CS0029/CS1061
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** conditional-effect condition type check
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f4-f5-if-condition.poly` —
  `if (Qty)` (Number) and `if (Name)` (Text) pass analysis; export emits
  `if (this.Qty)` → CS0029 (long→bool), `if (this.Name)` → CS0029 (string→bool).
- **Expected:** an `if` condition must be Boolean-typed; `not`/`and`/`or` operands are
  checked (and are), so the bare condition root should be too.
- **Actual:** `WalkExpression`'s default arm only recurses; nothing checks the
  condition node's own inferred type (a `ConditionalEffect` never routes the condition
  through a Boolean check). Literal conditions (`if (5)`) and unknown identifiers
  (`if (Bogus)`) also pass.
- **Proposed patch:** in `CheckEffect`'s `ConditionalEffect` arm, infer the condition's
  type and report unless `Boolean`/`Unknown`; for `Unknown` (unresolvable identifier)
  let the structural reference pass reject it (see F9).

## F5 — Bare enum-member comparison inside an action-body `if` passes analysis → CS1061 (policy sibling is rejected)
- **Signal:** compile-fail (sibling-form inconsistency)
- **Severity:** 🔴
- **Slice:** enum member handling across contexts
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f4-f5-if-condition.poly` —
  `C3: action { if (Genre is Fiction) { … } }`. Passes analysis; export emits
  `this.Genre == this.Fiction` → CS1061. The SAME expression in a policy body is
  rejected at analysis: `Policy references property 'Fiction' which does not exist`
  (PolicyConstraintAnalyzer).
- **Expected:** enum-member comparisons are authored as string literals
  (`Genre is "Fiction"`, which works); a bare member on the comparison RHS should be
  rejected (or lowered to `BookGenre.Fiction`) in every context — policies AND
  action-body if-conditions — since both share the expression grammar.
- **Actual:** only the policy body has the unknown-identifier walker; action-body
  if-conditions (and presumably assign RHS / create-in initializer conditions) do not.
- **Proposed patch:** reuse the policy expression-reference walker (or the
  enum-member inference) for effect-body conditions; alternatively lower bare enum
  members on comparison RHS to qualified members like the assign path does.

## F6 — Binder-rooted invoke arg referencing an unknown target property passes analysis → CS1061
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** for-fan-out invoke argument typing
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f6-binder-unknown.poly` —
  `for targets as target invoke target.Bump(n: target Title)` where `Target` has no
  `Title` property. Passes analysis; export emits `target0.Bump(target0.Title)` →
  CS1061.
- **Expected:** an unknown binder-root property in an invoke arg is a reference error,
  caught at analysis (the binder-root type inference already resolves KNOWN props — the
  round-5 F7 fix — so the unknown case is a straight sibling).
- **Actual:** `InferBinderExpressionType` returns `Unknown` for an unresolvable binder
  prop and the mismatch is skipped; no other pass validates binder-root references in
  invoke args (EffectAnalyzer validates binding NAMES and predicate policies, not arg
  expressions).
- **Proposed patch:** when the binder root is a `RelationshipNavigation` and its
  target property does not resolve on the target entity, report a reference-resolution
  error at the binding site.

## F7 — Decimal literals lex as string literals: shipped arithmetic/comparisons/defaults falsely rejected as Text
- **Signal:** guide-drift (false positive; shipped surface dead-ends)
- **Severity:** 🟠
- **Slice:** literal type inference
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f7-decimal-literal.poly` —
  `assign Total to Total * 0.9`, `assign Fine to Fine + 0.5`, `policy { Total > 0.5 }`,
  and `Rate: Number default(0.5)` are ALL rejected at analysis:
  `arithmetic operand is not numeric (got 'Number' and 'Text')` /
  `comparison between incompatible types 'Number' and 'Text'` /
  `default value of type 'Text' is not compatible with property type 'Number'`.
- **Expected:** the guide §8 ships arithmetic with the literal example `Total * 0.9`
  ("Arithmetic (`+`, `-`, `*`, `/`) … `Total * 0.9`"); decimal literals are numeric.
- **Actual:** `DslExpressionParser.ParsePrimary` falls back to
  `DomainExpression.Literal(numText)` (a STRING) when `long.TryParse` fails
  (DslExpressionParser.cs:114-116); `InferType` then classifies it `Text`. The DSL
  effectively supports integer literals only — every decimal form is a false positive.
  A `double` literal never exists in the IR (`Literal` has no double path).
- **Proposed patch:** parse Number tokens with `double.TryParse` (or decimal) and
  emit a numeric `Literal`; add a double literal arm to `InferType`. `range(0.01, 1.0)`
  bounds already parse as doubles on the constraint side, so the expression side should
  match.

## F8 — `default(<bare non-member identifier>)` on an enum property escapes analysis; fails at codegen with a misleading message
- **Signal:** fail-loud-but-sharp (wrong rung; confusing message)
- **Severity:** 🟠
- **Slice:** default(...) enum-member validation
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f8-enum-default.poly` —
  `Status: MemberStatus default(Bogus)` passes analysis; codegen throws
  `NotSupportedException: default(Bogus) on property 'Status' (type 'MemberStatus')
  cannot be lowered: 'Bogus' is not a member of an enum that 'Status' is typed with`
  → "Code generation failed".
- **Expected:** the sibling forms are all rejected at analysis with a clear message
  (`expr-wrong-types.poly` L/N/P/Q: create-in string, create-in bare, assign bare,
  assign string), so `default(Bogus)` should be too.
- **Actual:** `CheckDefault`'s `PropertyAccess` branch returns WITHOUT membership
  validation when the target IS enum-typed (ExpressionTypeAnalyzer.cs:497-500) — it
  only validates that the target is an enum, not that the name is a member. Same for a
  member of a DIFFERENT enum (`BookGenre default(Active)`).
- **Proposed patch:** in `CheckDefault`'s PropertyAccess branch, when the target is
  enum-typed, verify `pa.Name` is a member of that enum (else report like
  `CheckEnumMember`); only `now`/`utcnow`/`today`/`guid` are the keyword exemptions.

## F9 — Unresolvable bare identifiers in assign RHS (non-enum targets) pass analysis → CS1061
- **Signal:** compile-fail
- **Severity:** 🟠
- **Slice:** assign RHS reference/type analysis
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f9-unknown-value.poly` —
  `assign Name to Bogus` and `assign Qty to Bogus` (no such property/member/param)
  pass analysis; export emits `this.Name = this.Bogus` → CS1061 ×2.
- **Expected:** an identifier that is neither a property, a parameter, nor an enum
  member is a reference error at analysis — the enum-target special-case in
  `CheckCompatible` (ExpressionTypeAnalyzer.cs:450-454) already rejects this for
  enum-typed targets, and PolicyConstraintAnalyzer rejects unknown identifiers in
  policies. The non-enum-target assign path is the hole.
- **Actual:** `InferType` returns `Unknown` for an unresolvable `PropertyAccess` and
  `CheckCompatible` returns early; EffectAnalyzer validates the assign TARGET only, not
  value-side identifiers.
- **Proposed patch:** when `InferType` yields `Unknown` for a bare `PropertyAccess`
  that is neither a known property nor parameter in the current scope, report a
  reference-resolution error (reuse the policy walker for effect expressions).

## F10 — Enum-member invoke args are entirely broken (both authoring forms) → CS1503/CS1061
- **Signal:** compile-fail (sibling-form gap)
- **Severity:** 🟠
- **Slice:** enum handling in invoke arguments
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f10-invoke-enum-literal.poly` —
  `invoke service.SetStatus(status: "Active")` (valid member!) and
  `status: "Bogus"` BOTH pass analysis → CS1503 `cannot convert from 'string' to
  'MemberStatus'`. The bare form `status: Active` passes analysis too → CS1061
  `'Caller' does not contain a definition for 'Active'`.
- **Expected:** enum params accept members like every other enum authoring form
  (`assign Status to "Active"` and `Status: Active` both lower to `MemberStatus.Active`);
  non-members are rejected at analysis.
- **Actual:** (a) the invoke-arg type check's `Compatible(Text, Enum)` returns true
  and never calls `CheckEnumMember` (unlike CheckComparison/CheckCompatible); (b) the
  invoke-arg LOWERING (`EffectLoweringPass.InvokeAction`) uses the raw expression pass
  — no `LowerEnumAwareValue` — so string literals are not qualified and bare members
  lower to `this.Active`. No way to pass an enum argument.
- **Proposed patch:** in `CheckInvokeArgumentTypes`, run `CheckEnumMember` for
  Text→Enum bindings; in `EffectLoweringPass.InvokeAction` (and
  `ForEachInvoke`'s arg pass), lower enum-typed bindings with `LowerEnumAwareValue`
  using the callee parameter's type.

## F11 — Null is universally "compatible": null-assign to non-nullable value properties passes analysis → CS0037
- **Signal:** compile-fail
- **Severity:** 🟠
- **Slice:** null-literal type handling
- **Repro:** `probes/fleet-eval/03-expression-type/expr-f11-null-assign.poly` —
  `assign Qty to null` (Number) and `assign DueDate to null` (Date) pass analysis;
  export emits `this.Qty = null` → CS0037 ×2 (plus a CS0472 always-false warning for
  `Qty is null`).
- **Expected:** `null` is assignable only to nullable categories (Text/reference
  props); for Number/Date/Boolean value categories the analyzer should reject it
  (fail-closed), or the export must fail loud deliberately.
- **Actual:** `Compatible` treats `TypeCategory.Null` as universally compatible
  (ExpressionTypeAnalyzer.cs:594-596).
- **Proposed patch:** in `CheckCompatible`/`CheckComparison`, allow `Null` only when
  the other category is `Text` (reference-typed) or itself `Null`; report otherwise.

---

## Verified-OK in this slice (not findings)
- `expr-ok-library.poly` — full library domain (enum defaults + members, policies with
  arithmetic/comparison/boolean logic, date arithmetic on properties, runtime-keyword
  defaults `now`/`today`/`guid`, self/cross/fan-out invoke, create-in enum members,
  `-> Entity` return) compiles 0/0 and `--mode all` has no codegen exceptions.
- `expr-wrong-types.poly` — 18 wrong-type forms (Text≥Number, string→Number assign,
  bool→Text assign, Number+string, Text+Text, `not`/`and` on non-boolean,
  default(now/today/guid) on Number, default(Active) on Text, non-member enum in
  create-in/assign/comparison, Date vs string, wrong-enum member, wrong-typed literal
  invoke arg) are ALL rejected at ANALYSIS with clear messages (earliest rung).
- ConstraintQualityAnalyzer/ConstraintValidation: `range` on Text, `length` on Number,
  default outside range, and `Date default("2024-01-01")` (round-2 F4 sibling) are all
  caught at analysis.
- Correctly-typed binder-root invoke args (`loan Count + 1`) and wrong-typed
  binder-root args (`loan Status` → Number param) are caught (round-5 F7 fix works for
  KNOWN binder props).
- Cross-type Date/DateTime comparisons and date-vs-string comparisons are rejected at
  analysis (round-2 F9/F4 siblings fixed).
- Known issue, still open (NOT re-reported): round-2 F7 — `create in Rel { <bound
  defaulted prop> }` on a target with a runtime-keyword default (`now`/`today`/`guid`)
  on another prop dies at codegen (`AppendDefaultedPropArgs` calls
  `LowerDefaultConstantNode` unconditionally). Re-confirmed on this round's OK-probe
  iteration; the proposed patch (prefer `LowerDefaultExpression` first) is unapplied.
