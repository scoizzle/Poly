# Discovery-c findings — CONSTRAINTS + CREATE PATHS + ENUMS

Agent: `discovery-c`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../docs/agent/poly-discovery-loop.md).
Slice: required/unique/range/length/pattern/default constraints; `create Type` vs `create in Rel`;
`-> EntityType` return contract (create-as-last-statement); enum types, enum-typed properties,
enum members in assigns and policy comparisons.

Probes: `probes/discovery-c/inventory.poly`, `accounts.poly`, `catalog.poly`, `enum-literal.poly`.
Every probe went through `scripts/run-probe.sh` (parse → analyze → export → Roslyn 0-errors/0-warnings).

---

## F1 — create Type overriding a defaulted property emits a private-setter assignment → CS0272
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** create paths / default constraints
- **Repro:** `probes/discovery-c/catalog.poly` — `makePlus: action { create Product { SKU: "P1" Tier: Plus } }`
  with `Product { Tier: Tier default(Basic) }`. `scripts/run-probe.sh probes/discovery-c/catalog.poly`
  → `error (95,9): error CS0272: The property or indexer 'Product.Tier' cannot be used in this context because the set accessor is inaccessible`.
- **Expected:** the defaulted-property override flows through construction (as it does for `create in Rel`,
  which passes it as a trailing optional `CreateN` factory parameter — see the passing
  `Export_DefaultedPropOverride_FlowsThroughConstructor` test).
- **Actual:** `EffectLoweringPass.CreateEntityInstance` applies the override as a **post-create
  assignment** (`product.Tier = Tier.Plus;`, `EffectLoweringPass.cs:374-377`) while the exporter emits
  **private** setters on all properties (`DomainToCSharpExporter.cs:138-139`). The `create Type` path
  therefore can never compile when a create initializer binds any property that has a `default`.
  The create-in path is unaffected (its override travels as a constructor argument).
- **Proposed patch (not applied):** make `CreateEntityInstance` pass defaulted-prop overrides as
  trailing optional arguments to `Target.Create(...)` (mirroring `CreateEntityInRelationship`,
  `EffectLoweringPass.cs:442-464`) instead of emitting `child.Prop = value;`, or give the exporter
  internal setters. The create-in call-site code already proves the argument-forwarding shape works.

## F2 — string-literal enum members in create/create-in initializers → CS1503 (runtime accepts them)
- **Signal:** compile-fail (+ runtime divergence)
- **Severity:** 🔴
- **Slice:** enums / create paths
- **Repro:** `probes/discovery-c/enum-literal.poly` —
  `lexString: action { create in tokens { Lexeme: "let" Kind: "Keyword" } }` and
  `makeTokenString: action -> Token { create Token { Lexeme: "x" Kind: "Identifier" } }` with
  `Token { Kind: TokenKind }`. `scripts/run-probe.sh` →
  `error (38,39): error CS1503: cannot convert from 'string' to 'TokenKind'` (and line 52).
- **Expected:** enum-typed properties accept a member by name — the assign path already qualifies
  string literals (`assign Status to "Out"` → `StockLevel.Out`, `EffectLoweringPass.Assign.cs:122-129`),
  and `LowerEnumAwareValue` qualifies bare identifiers. A string literal in a create initializer
  should qualify the same way (`TokenKind.Keyword`).
- **Actual:** `LowerEnumAwareValue` (`EffectLoweringPass.cs:559-567`) only qualifies
  `PropertyAccess` values; a `Literal` string passes through as a `string` constant → CS1503.
  The **runtime** accepts the string (enum-typed props are stored as strings in the value bag;
  verified with a throwaway TUnit test: `create in`/`create Type` with `Kind: "Keyword"` succeed,
  child `Kind == "Keyword"`).
- **Proposed patch (not applied):** in `LowerEnumAwareValue`, handle `Literal { Value: string s }`
  where `s` is a member of the target enum type, emitting `Member(EnumType, s)` — symmetric with
  `EffectLoweringPass.Assign`.

## F3 — constraints enforced only in the C# export's Create factory; the runtime never validates
- **Signal:** divergence
- **Severity:** 🟠
- **Slice:** required/range/length/pattern/default constraints
- **Repro:** `probes/discovery-c/inventory.poly` (Item with `Name length(2,50) required`,
  `Category pattern("^[A-Z][0-9]{3}$")`, `Qty range(0, 100)`). The export's `Create` factory
  guard-returns `DomainResult<T>.Failure` for violations; `CreateN`/create unwraps throw.
  At runtime, `DomainEntityInstance.Create` (used by `create_instance` and by `create`/`create in`
  effects) performs **zero** constraint checks — verified with a throwaway TUnit test:
  `Qty = 500`, `Category = "bad!"`, `Name = "x"` all accepted with no failure.
- **Expected:** same DSL → same behavior; an out-of-range / pattern-violating create must fail
  loudly on both paths (protocol: "the export and runtime must agree; where they can't, the export
  must fail loud"). `DomainEntityInstance.Create` should run the same required/range/length/pattern
  guards as the export's factory (it already has the property/constraint model).
- **Actual:** export rejects (Failure/throw); runtime silently creates an invalid instance. The
  same divergence surfaces for a default that violates its own range: `Qty: Number range(0, 10)
  default(99)` → export `Create(qty = 99)` guard-fails on every unoverridden create; runtime
  applies default 99 silently (verified).
- **Proposed patch (not applied):** add the guard set from `BuildCreateConstraintChecks` to
  `DomainEntityInstance.Create` (fail closed with a clear message), or centralize constraint
  validation in a shared helper used by both the export and the runtime factory.

## F4 — create/create-in inside a conditional: runtime silently drops it; `-> T` export always throws
- **Signal:** divergence (+ silent gap)
- **Severity:** 🟠
- **Slice:** create paths / `-> EntityType` return contract
- **Repro:**
  - Void action: `maybeTag: action (rush: Boolean) { if (rush is true) { create in products { SKU: "COND" } } }`
    → runtime `InvokeAction("maybeTag", rush=true)` **succeeds with 0 created children** (verified).
    The export instead emits `this.CreateProducts(...)` inside the `if` and does create.
  - Return action (guide's "✅ Correct — final conditional, every branch produces", §6):
    `enrollRush: action (rush: Boolean) -> Product { if (rush is true) { create in products {…} } else { create in products {…} } }`
    → runtime returns `MissingReturn` (`"no create/create-in produced an instance of that type"`,
    verified) because the creates were dropped; the export compiles to the creates **plus** a tail
    `throw new NotSupportedException("…its last effect does not produce a value…")`, so the action
    always throws after performing the side effect (`DomainToCSharpExporter.cs:1221-1229`).
- **Expected:** DMEFF010 explicitly accepts the final-conditional shape (analysis passes); the
  runtime `InvokeAction` P3 should return the created instance; the export should wrap the branch
  results in `DomainResult<T>.Success(...)`.
- **Actual:** at runtime `ConditionalEffect` is VM-lowered; its `CreateEntityInstance` /
  `CreateEntityInRelationship` sub-effects lower to `null` and are replaced with `Comment` nodes
  (`EffectLoweringPass.cs:272-282, 284-308`) — a silent no-op for void actions, MissingReturn for
  return-typed ones. In the export the non-void wrapper only recognizes terminal
  `Variable`/`Assignment`/… nodes, not an `IfStatement`, so it emits a structural throw.
- **Proposed patch (not applied):** (a) runtime: route conditional sub-effects to
  `EffectExecutor.Run` (direct execution) instead of the VM `Comment` drop, matching top-level
  create handling; (b) export: when the last node is an `IfStatement` and the action has a return
  type, rewrite each branch to `return DomainResult<T>.Success(<branch's create>)` (or declare a
  result variable in each branch and return after the `if`), instead of throwing.

## F5 — `unique` is enforced nowhere: silent no-op on both export and runtime
- **Signal:** silent gap
- **Severity:** 🟠
- **Slice:** unique constraint
- **Repro:** `probes/discovery-c/accounts.poly` — `Customer { Email: Text unique … }`.
  Duplicate `Email` values pass the export `Create` factory and the runtime instance store with no
  error; the generated C# emits no uniqueness check, index, or attribute (only natural-key storage
  metadata is derived, `EntityStructureAnalyzer.cs:49-55`).
- **Expected:** a documented `unique` constraint should fail loudly on a duplicate (at least on the
  store `Link`/`Create` path) or the guide should scope `unique` to storage projection only.
- **Actual:** `ConstraintValidation.IsSatisfiedBy(UniqueConstraint, …) => true`; export comment
  ("Unique requires store awareness") and runtime both skip it. Consistent across paths — a shared
  product gap, but the constraint is documented as shipped and currently validates nothing.
- **Proposed patch (not applied):** add a uniqueness check in `DomainInstanceStore` (keyed on
  natural-key property) on add/link, or narrow the guide's claim.

## F6 — range bounds cannot be negative or fractional: `range(-500, )` / `range(0.01, …)` fail parse
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** range constraint
- **Repro:** `probes/discovery-c/accounts.poly` `Balance: Number range(-500, )` →
  `Parse error: Expected RParen, got '-' (Minus)`; `inventory.poly` `range(0.01, 9999.99)` →
  `Expected RParen, got '.' (Dot)`.
- **Expected:** `range(min, max)` with signed numeric bounds (overdraft-style limits) and/or
  fractional bounds (pricing), since `Number` props can legitimately hold negatives and the runtime
  evaluates them.
- **Actual:** `ScanNumber` (`DslTokenReader.cs:117-126`) only scans digit runs; the `range` grammar
  (`PolyDslParser.cs:1152-1168`) accepts only unsigned integer literals. The natural modeling
  surface (negative bounds) dead-ends with a parse error.
- **Proposed patch (not applied):** scan `-`/`.` in `ScanNumber` (or parse a signed number in the
  `range` grammar) and convert bounds to a numeric value; the export's guard code already compares
  `long`/`double` fine.

## F7 — multi-initializer create with a non-final bare-identifier value is misparsed as path-prefix
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Slice:** create/create-in initializers
- **Repro:** `create in accounts { Email: email Status: Suspended }` and
  `create Product { SKU: sku Tier: Plus }` →
  `Parse error: Expected property name, got ':'`.
- **Expected:** initializer values are expressions terminated by the next `Prop:`; a parameter or
  enum-member value followed by another initializer should parse (the guide shows several
  initializers per create, e.g. §0.3).
- **Actual:** `DslExpressionParser.ParsePrimary` treats `Identifier Identifier` as a
  path-prefix / `RelationshipNavigation` (`DslExpressionParser.cs:147-149`), so the following
  initializer's name is consumed as a path continuation and the `:` then fails. Only literal-first
  or bare-identifier-last orderings parse (all shipped examples avoid the shape).
- **Proposed patch (not applied):** in `ParsePropertyInitializers`, parse the value with an
  expression grammar that stops at the next `Identifier :` boundary (lookahead for `Colon`), or
  document/require that bare-identifier initializer values must be last.

## F8 — entity-level policies gate every action (export + runtime parity, but surprising)
- **Signal:** modeling trap
- **Severity:** 🟡
- **Slice:** constraints → policy gating of create paths
- **Repro:** `probes/discovery-c/inventory.poly` — `addTag` (create Tag), `receive` (create-in),
  `markLow`, `markOut` are all guarded by `IsLow` and `NeedsRestock` entity-level policies in the
  export; `accounts.poly` — `deposit`, `suspend`, `close` guarded by `CanWithdraw`.
- **Expected:** the guide (§8, Require Gates §6) documents `require PolicyName` on actions; nothing
  documents that a bare entity-level policy silently gates *every* action on the entity.
- **Actual:** `DomainToCSharpExporter.BuildActionBodyWithGuards` (`:1163-1174`) and the runtime
  (`DomainEntityInstance.InvokeActionInternal`, `:336-344`) both treat every entity policy as an
  always-on action guard. Parity, but a freshly-declared policy can silently block unrelated
  create/create-in actions (e.g. `addTag` blocked by stock-level policies).
- **Proposed patch (not applied):** none — intended behavior; recommend the guide document that
  entity-level policies act as action gates, or scope to `require`-only.

---

## Verified-positive (no finding)

- Partial ranges both ways in the export: `range(0, )` → `qty < 0L`; `range(, 100)` →
  `reorderPoint > 100L` (inventory export).
- `create in` with `-> EntityType` as the last statement returns the instance
  (`openAccount => DomainResult<Account>.Success(this.CreateAccounts(0L, email, 0L))`).
- Defaulted-property overrides in `create in` pass through trailing optional factory params in the
  correct positional order, incl. enum values (`CreateAccounts(0L, "x@y.z", 0L, "guest",
  AccountStatus.Suspended)`).
- Bare-identifier enum members in create/create-in initializers qualify correctly
  (`Tier.Pro`, `AccountStatus.Suspended`, `TokenKind.Keyword`).
- Enum members in `assign` and in policy comparisons qualify correctly (bare and string-literal
  forms); enum `default(Member)` emits `EnumType.Member` on ctor/factory defaults.
- Required/length/pattern/range guards emit correctly in the Create factory (incl. defaulted props).
