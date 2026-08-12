# Round-2 discovery findings — CONSTRAINTS + CREATE PATHS — RUNTIME PARITY

Agent: `discovery-agent-b`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../docs/agent/poly-discovery-loop.md).
Slice: required/unique/range/length/pattern constraints; `create Type` vs `create in Rel`;
`create_instance` vs effect-created children; action-param / enum-member initializer values;
`-> EntityType` return contract.

Probes: `probes/discovery-agent-b/{constraints,subscriptions,assign-param}.poly` (pass 0/0),
`f6-ranges.poly`, `f7-multiinit.poly` (parse-fail), `enum-nonmember.poly` (CS1503),
`length-open.poly` (silently-wrong bounds), `pattern-nontext.poly` (fail-closed analysis).
Runtime evidence: throwaway TUnit tests in `Poly.Tests/DiscoveryAgentB/` (deleted after use).

---

## Status of round-1 findings (from `probes/findings/discovery-c.md`)

| # | Title | Status |
|---|-------|--------|
| F1 | create Type defaulted-prop override → CS0272 | **FIXED** — all round-1 probes compile 0/0; `catalog.poly` passes. Defaulted-prop overrides now flow through trailing optional ctor args in both `create Type` and `create in` paths. |
| F2 | string-literal enum members in create initializers → CS1503 | **FIXED** for *valid* members — `"InStock"` → `StockLevel.InStock` in export (`LowerEnumAwareValue`). See **F-R2-4** below: **non-member** string literals now silently produce a *different* compile-fail (CS1503/CSE1061) with no analysis rejection and runtime acceptance. |
| F3 | constraints enforced only in export Create; runtime never validates | **CONFIRMED, still open** (extended in F-R2-1/2/3). |
| F4 | create/create-in inside conditional dropped at runtime; `-> T` export always throws | **CONFIRMED, still open** (F-R2-5). |
| F5 | `unique` enforced nowhere | **CONFIRMED, still open** (F-R2-6). |
| F6 | negative/fractional range bounds unparseable | **CONFIRMED, still open** (F-R2-7). |
| F7 | multi-initializer create with non-final bare-identifier value misparses | **CONFIRMED, still open** (F-R2-8). |

---

## F-R2-1 — action parameters in create/create-in initializers evaluate to garbage `1` at runtime
- **Signal:** divergence (silent wrong data) + silent-gap
- **Severity:** 🟠 (top of round)
- **Slice:** create paths / initializer values
- **Repro:** `probes/discovery-agent-b/constraints.poly` — `stock: action (qty: Number) -> Bin { create in bins { Code: "AB" Capacity: qty } }`.
  Runtime: `InvokeAction("stock", { qty: 42L })` succeeds, child `Capacity == 1` (not 42).
  Also `create Bin { Code: "XY" Capacity: qty }` → `Capacity == 1`. Verified with throwaway TUnit
  (`Inspect_ParamValue` → `cap=Int64:1`; `Param_InCreateInInitializer_Value`, `Param_InCreateTypeInitializer_Value`).
- **Expected:** export forwards the param correctly (`stock(qty) => DomainResult<Bin>.Success(this.CreateBins(qty, "AB", ...))`);
  runtime must agree (child `Capacity == 42`).
- **Actual:** `CreateChildInstance` (`DomainEntityInstance.cs:853`) compiles initializer expressions with
  `_typeDefAnalyzer` — the parent entity's type def with **no action parameters** — so `qty` lowers to a
  member access that VM member-passthrough resolves to garbage (`1`). The top-level effect path uses the
  action-scoped type provider (`BuildActionScopedTypeDefAnalyzer`, `:359-361`) but the create-initializer
  evaluation does not (it needs the injected args). **Silently stores wrong data — no error.**
- **Proposed patch (not applied):** thread the action-scoped type provider (or injected args) into
  `CreateChildInstance` / `ExecuteCreateInRelationship` so `PropertyAccess` to a declared action parameter
  resolves to the injected value; or resolve initializer `PropertyAccess` names against
  `action.Parameters` + `_values` before VM compile. Add a regression test asserting
  `create in { Capacity: qty }` with `qty=42` yields `42`.

## F-R2-2 — runtime `DomainEntityInstance.Create` / `create_instance` never enforces required/range/length/pattern (round-1 F3)
- **Signal:** divergence
- **Severity:** 🟠
- **Slice:** constraints / create_instance / runtime Create
- **Repro:** `constraints.poly` (Item `Name length(2,50) required`, `Category pattern("^[A-Z]{2}$")`, `Count range(1,99)`).
  `DomainEntityInstance.Create(item, { Name: "x", Category: "bad!", Count: 500 })` succeeds with no error
  (TUnit `F3_RuntimeCreate_IgnoresRangeConstraint`, `F3_RuntimeCreate_IgnoresLengthPatternRequired`).
  The export `Item.Create(...)` guard-fails on the same values.
- **Expected:** same DSL → same behavior; out-of-range / pattern-violating / under-length creates fail loud
  on both paths (protocol: export/runtime must agree).
- **Actual:** `DomainEntityInstance.Create` (`DomainEntityInstance.cs:97-149`) only validates property *names*
  and applies defaults — zero constraint checks. `create_instance` is a thin wrapper over it
  (`RuntimeTool.cs:159`). Export `Create` guard-returns `DomainResult<T>.Failure`.
- **Proposed patch (not applied):** add the `BuildCreateConstraintChecks` guard set (or a shared
  `ConstraintValidation`-based validator) to `DomainEntityInstance.Create`, failing closed with the same
  messages the export emits. Note the analysis-time literal path already rejects literal violations
  (`EffectAnalyzer.cs:1053`) — the gap is param-driven and `create_instance`-driven values.

## F-R2-3 — unbound (no-default, no-initializer) props diverge: export `0`/null-default vs runtime `null`
- **Signal:** divergence
- **Severity:** 🟠
- **Slice:** create paths / defaulted-prop + range coverage
- **Repro:** `create in bins { }` on `Bin { Capacity: Number range(1, 100) }` (probe `constraints.poly`/throwaway).
  Export: `CreateBins(0L)` → `Bin.Create(0)` → `0 < 1` guard → **Failure → throws** on every unoverridden create.
  Runtime: child `Capacity == null` (no failure, no error).
- **Expected:** guide §0.3 / export parity — an unbound required-by-range prop must fail loud, or the runtime
  must produce the same default the export does (0L) and run the same guard.
- **Actual:** export throws (fail-loud, arguably sharp: the whole `create in { }` shape is unusable for any
  entity with a positive-min range prop); runtime silently stores `null`. Divergence both in value (0 vs null)
  and in failure behavior.
- **Proposed patch (not applied):** apply CLR-appropriate defaults (0 for numbers) in
  `DomainEntityInstance.Create` before constraint checks (mirrors `DefaultForDomainType` in the export),
  then run F-R2-2's guards — unbound violations then fail closed consistently on both paths.

## F-R2-4 — non-member enum string/bare initializer values: export CS1503/CS1061, analysis silent, runtime accepts garbage
- **Signal:** compile-fail (+ divergence)
- **Severity:** 🔴
- **Slice:** enums / create initializers
- **Repro:** `probes/discovery-agent-b/enum-nonmember.poly` — `create in bins { Status: "Bogus" }` on
  `Bin { Status: StockLevel }` →
  `error CS1503: cannot convert from 'string' to 'StockLevel'`. Bare form `Status: Bogus` →
  `error CS1061: 'Box' does not contain a definition for 'Bogus'`. Runtime accepts both
  (`Snapshot()["Status"] == "Bogus"` for the string; bare form → `1` garbage). Analysis reports no error.
- **Expected:** a value that is not a member of the target enum should be rejected at analysis (fail-closed,
  like the literal constraint checks), or documented as an error — not a C#-layer compile-fail that analysis
  and runtime both silently pass.
- **Actual:** `LowerEnumAwareValue` (`EffectLoweringPass.cs:608-622`) only qualifies *known* members; any
  other string/bare identifier passes through as `string`/`this.Bogus` → C# compile error. The DSL parses and
  analyzes clean; the runtime stores the bogus string (or `1`).
- **Proposed patch (not applied):** in the parser or analysis, validate that string-literal/bare-identifier
  initializer values targeting an enum-typed property name an existing member (emit the export's
  `'X' is not a member of enum 'Y'` style diagnostic). At minimum the exporter should not emit code that fails
  to compile for a value analysis accepted.

## F-R2-5 — create/create-in inside a conditional: runtime silently drops; `-> T` export always throws (round-1 F4)
- **Signal:** divergence (+ silent gap)
- **Severity:** 🟠
- **Slice:** create paths / `-> EntityType` return contract
- **Repro:** `probes/discovery-agent-b/subscriptions.poly` —
  - `maybeTag: action (rush: Boolean) { if (rush is true) { create in orders { Total: 1 } } }`
    → runtime succeeds with **0 children** (TUnit `F4_RuntimeConditionalCreateIn_VoidSilentlyDrops`);
    export emits `this.CreateOrders(...)` inside the `if` (creates).
  - `tryOrder: action (rush: Boolean) -> Order { if … else … }` (DMEFF010-legal final conditional) →
    runtime returns `MissingReturn` (`F4_RuntimeConditionalCreateIn_ReturnActionMissingReturn`);
    export compiles to branch creates **plus a tail `throw new NotSupportedException(...)`** at
    `DomainToCSharpExporter.cs:1221-1229` — the action always throws after performing the side effect.
- **Expected:** the guide (✅ final conditional, §6) and DMEFF010 accept the shape; the runtime should return
  the created instance and the export should return `DomainResult<T>.Success(<branch create>)`.
- **Actual:** runtime `EffectLoweringPass.Conditional` (`:312-336`) replaces non-VM sub-effects
  (`create Type` / `create in`) with `Comment` nodes — silent no-op for void actions, MissingReturn for
  return-typed. Export's non-void wrapper doesn't recognize `IfStatement` as a terminal value node → throws.
- **Proposed patch (not applied):** (a) runtime: route conditional create sub-effects to
  `EffectExecutor.Run` (direct execution) instead of the VM `Comment` drop; (b) export: when the last node is
  an `IfStatement` on a return-typed action, rewrite each branch to
  `return DomainResult<T>.Success(<branch create>)`. Regression tests for both shapes.

## F-R2-6 — `unique` is enforced nowhere (export nor runtime) (round-1 F5)
- **Signal:** silent gap
- **Severity:** 🟠
- **Slice:** unique constraint
- **Repro:** `constraints.poly`/throwaway — `Account { Email: Text unique required }`; two
  `DomainEntityInstance.Create(…, { Email: "dup@x" })` added to the same `DomainInstanceStore` succeed with no
  error (TUnit `F5_Unique_DuplicatesAccepted_Store`). Export `Create` emits no uniqueness check (comment:
  "Unique requires store awareness", `DomainToCSharpExporter.cs:981-985`).
- **Expected:** a documented `unique` constraint should fail loud on a duplicate (at least on store
  add/link), or the guide must scope `unique` to storage projection only.
- **Actual:** `ConstraintValidation.IsSatisfiedBy(UniqueConstraint, …) => true` (`ConstraintValidation.cs:21`);
  no check on either path. Consistent silent no-op across export + runtime.
- **Proposed patch (not applied):** uniqueness check in `DomainInstanceStore.Add`/`Link` keyed on natural-key
  props, or narrow the guide's claim.

## F-R2-7 — range bounds cannot be negative or fractional (round-1 F6)
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** range constraint
- **Repro:** `probes/discovery-agent-b/f6-ranges.poly` — `range(-500, )` →
  `Parse error: Expected RParen, got '-' (Minus)`; `range(0.01, 1.0)` → `Expected RParen, got '.' (Dot)`.
- **Expected:** `Number` props can hold negatives/fractions; the runtime evaluates them; bounds should parse.
- **Actual:** `ScanNumber` (`DslTokenReader.cs:117-126`) scans digit runs only; `range` grammar
  (`PolyDslParser.cs:1152-1168`) accepts unsigned integers only.
- **Proposed patch (not applied):** scan `-`/`.` in `ScanNumber` (or parse a signed number in the `range`
  grammar); export guards already compare `long`/`double` fine.

## F-R2-8 — multi-initializer create with a non-final bare-identifier value misparses as path-prefix (round-1 F7)
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** create/create-in initializers
- **Repro:** `probes/discovery-agent-b/f7-multiinit.poly` — `create Member { Email: email Status: status }` →
  `Parse error: Expected property name, got ':'`. Same for `create in bins { Capacity: qty Label: label }` —
  even with a literal first, any bare-identifier value that is not the *last* initializer fails.
- **Expected:** initializer values are expressions terminated by the next `Prop:` (guide §0.3 shows multiple
  initializers).
- **Actual:** `DslExpressionParser.ParsePrimary` treats `Identifier Identifier` as a path-prefix
  (`DslExpressionParser.cs:147-149`), consuming the next initializer name; only literal-first or
  bare-identifier-last orderings parse. This also blocked my F-R2-1 probe authoring — it's a real authoring
  wall for param-driven creates with more than one value.
- **Proposed patch (not applied):** in `ParsePropertyInitializers`, stop the value expression at the next
  `Identifier :` boundary (Colon lookahead), or document that bare-identifier values must be last.

## F-R2-9 — `length(3, )` (open upper bound) silently collapses to `length(3, 3)`
- **Signal:** silent gap (wrong bound, no failure)
- **Severity:** 🟡
- **Slice:** length constraint
- **Repro:** `probes/discovery-agent-b/length-open.poly` — `Code: Text length(3, )` compiles 0/0, but the
  export `Create` emits both `code.Length < 3` AND `code.Length > 3` guards — the open upper bound becomes
  max=3. Runtime: a 4-char value violates `length(3, 3)` (TUnit `LengthMinOnly_Runtime`).
- **Expected:** `range(0, )`-style open bounds already work (`qty < 0L` only); `length(min, )` should mean
  "at least min" with no upper bound (and `length(, max)` should parse at all — currently
  `Parse error: The input string ',' was not in a correct format`).
- **Actual:** `PolyDslParser.cs:1170-1184` defaults `lenMax = lenMin` for the single-arg form and applies the
  same default when the trailing bound is absent, silently converting `length(3, )` into `length(3, 3)`.
- **Proposed patch (not applied):** mirror the `range` grammar: keep `min`, leave `max` unbounded
  (`int.MaxValue`) when the `, ` is followed by `)`. The exporter's `LengthConstraint` guard already checks
  `l.MaxLength < int.MaxValue` before emitting the upper guard (`DomainToCSharpExporter.cs:945`).

## F-R2-10 — `pattern` on a non-Text type parses + analyzes clean and is silently skipped
- **Signal:** silent gap (asymmetric with range/length)
- **Severity:** 🟡
- **Slice:** pattern constraint / type compatibility
- **Repro:** `Bin { Zone: Number pattern("^[A-Z]$") }` (probe `pattern-nontext.poly` minus the control line) —
  parses, analyzes clean, export `Create` has **no** pattern guard. A `RangeConstraint` on Text or
  `LengthConstraint` on Number is rejected at analysis (`ConstraintQualityAnalyzer.cs:179-191`), but
  `PatternConstraint` type compatibility is **not validated** (`ValidateConstraintTypeCompatibility` checks
  only range + length).
- **Expected:** type-incompatible `pattern` should be analysis-rejected like range/length (fail-closed), or
  the exporter should emit a guard that a Number can never satisfy (e.g. convert to string first).
- **Actual:** the constraint silently does nothing on both export and runtime (`ConstraintValidation` returns
  false for non-string, but nothing calls it at create time).
- **Proposed patch (not applied):** add a `pattern` case to `ValidateConstraintTypeCompatibility`
  (require Text/String type), or have the export convert the value for `Regex.IsMatch`.

---

## Verified-positive (no finding)

- **F1/F2 fixed:** `catalog.poly`, `enum-literal.poly` re-probe clean (0/0); defaulted-prop overrides flow
  through ctor args; valid enum member string/bare identifiers qualify in the export.
- **Control:** `assign Start to qty` with `qty=42` → runtime `Start == 42` (param injection works for
  assigns — the create-initializer path is the broken one). `assign-param.poly` compiles 0/0.
- **Analysis literal fail-closed:** literal create-initializer violations of range/pattern/length are
  rejected at analysis (`EffectAnalyzer.cs:1050-1062`) — runtime never sees them; the confirmed runtime gaps
  are param-driven and `create_instance` values (F-R2-2/3).
- **DMEFF011:** missing-required `create`/`create in` is rejected at analysis (TUnit `N5`, `N6` pass) —
  required-prop *coverage* is enforced authoring-side; enforcement of the *value* is the gap.
- **Return contract straight-line:** `create in` as the last statement returns the created instance at
  runtime (round-1 positive; `makeOrder`/`stock` straight-line shapes).
- **Enum-typed props as create params:** valid-member bare identifiers in create initializers qualify in the
  export (`Tier.Pro`); the runtime garbage issue (F-R2-1/4) is the open item.
