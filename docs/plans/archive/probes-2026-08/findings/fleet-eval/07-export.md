# Fleet-eval 2026-08-12 — slice: C# entity export (07-export)

Agent: discovery agent — C# entity export slice
Probes: `probes/fleet-eval/07-export/*.poly` (10 files)
Pipeline: `scripts/run-probe.sh` (parse/analyze → export → Roslyn 0 errors/0 warnings gate) + static review of generated C#.

## F1 — `for`-invoke inside a `-> EntityType` action returns non-generic `DomainResult` (CS0029)
- **Signal:** compile-fail (late-rung — analysis accepts, compiler rejects)
- **Severity:** 🔴
- **Slice:** C# entity export (for-invoke fan-out + entity-return actions)
- **Repro:** `probes/fleet-eval/07-export/export-edges.poly` — `FulfillmentCenter.ProcessToDone: action -> Order { for orders as order where order in Open invoke order.Submit(); create in orders { Total: 50 } }`. `scripts/run-probe.sh` → `error CS0029: Cannot implicitly convert type 'DomainResult' to 'DomainResult<Order>'` at the for-loop's fail-fast `return result0;` AND the zero-match `return DomainResult.Failure(...)`.
- **Expected:** `for` is a valid action effect and DMEFF010 only pins the *create* to the last statement — the DSL is valid surface. The export must fail loud at analysis (reject `for` in non-void actions) or lower the for-block's returns to the generic `DomainResult<T>` shape.
- **Actual:** analysis passes; the exporter's `ForEachInvoke` lowering hardcodes `return result0;` and `return DomainResult.Failure(...)` (void shape) regardless of the enclosing action's return type (`EffectLoweringPass.cs:365-372`). Compile fails on valid DSL.
- **Proposed patch:** in `EffectLoweringPass.ForEachInvoke`, thread the enclosing action's return-type context and emit `DomainResult<T>.Failure(...)`/`return DomainResult<T>.Failure(...)` for non-void actions (or have analysis reject `for` in `-> Entity` actions at DMEFF009/010 time).

## F2 — two `create`/`create in` effects for the same entity type in one action → duplicate locals (CS0128)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** C# entity export (create/create-in factories)
- **Repro:** `probes/fleet-eval/07-export/isolated-two-creates.poly` — `MakeTwo: action { create Label { Body: "a" } create Label { Body: "b" } }` → `error CS0128: A local variable or function named 'labelResult' is already defined` + `'label'`. Also `probes/fleet-eval/07-export/export-edges.poly` `AddLineLiteralEnum` (two `create in lines`) → `CS0128: 'orderLine'`.
- **Expected:** multiple creates per action are valid (the guide's effect table allows repeated `create`); each generated local must be distinct.
- **Actual:** `EffectLoweringPass.CreateEntityInstance`/`CreateEntityInRelationship` name the unwrap locals `{camel}Result`/`{camel}` from the target type name with no per-statement sequence — the second create in the same scope redeclares them.
- **Proposed patch:** reuse the `_forEachInvokeSequence`-style counter (or a shared create-sequence counter) in `CreateEntityInstance`/`CreateEntityInRelationship` local names, matching the disambiguation used by `ForEachInvoke`.

## F3 — singular-nav subscription registration dereferences a nullable nav unguarded (CS8602 + ctor NRE)
- **Signal:** compile-fail (0/0 gate — 1 warning) + reliability (NRE on valid unlinked state)
- **Severity:** 🔴
- **Slice:** C# entity export (subscriptions)
- **Repro:** `probes/fleet-eval/07-export/isolated-stagescoped.poly` — `Device` has singular `node: Node` and `when node Online as n`; export emits `private void InitializeSubscriptions() { this.Node.RegisterDeviceOnlineSubscriber(this); }` where `Node` is `Node?` → `warning CS8602: Dereference of a possibly null reference` (breaks the 0/0 gate). At runtime `Device.Create(null)` (unlinked nav — valid) → NRE in the private ctor.
- **Expected:** 0 warnings; unlinked singular navs must not crash construction (the runtime subscribes on link, and an unlinked source has nothing to subscribe to).
- **Actual:** `AddSubscriberRegistrationNodes` (`DomainToCSharpExporter.cs:721-728`) emits `this.{PascalNav}.Register…(this)` with no null guard for singular navs (many navs are safe — ctor-initialized empty lists).
- **Proposed patch:** guard singular-nav registration with `if (this.{Nav} != null) this.{Nav}.Register…(this);`.

## F4 — C# reserved words as DSL identifiers are emitted raw (CS0065/CS1519/CS0119/CS1511…)
- **Signal:** compile-fail (identifier injection — no `@` escaping, no analysis rejection)
- **Severity:** 🔴
- **Slice:** C# entity export (naming/escaping, security lens)
- **Repro:** `probes/fleet-eval/07-export/isolated-keyword-ident.poly` — entity `namespace`, property `event` → CS0065/CS1055/CS1519. `probes/fleet-eval/07-export/export-collisions.poly` — entity `object` (`'object' does not contain a constructor…`), entity `class`, property `string` (CS0119), enum members `class`/`base` (CS1519/CS1511). All are valid DSL identifiers (none are DSL keywords — verified against `DslTokenReader.WordToKind`).
- **Expected:** either analysis rejects C#-keyword names fail-closed, or the exporter `@`-escapes them (`@class`, `@event`).
- **Actual:** `DomainToCSharpExporter`/`CSharpGenerator` interpolate names verbatim. Parse + analysis accept; generated C# does not compile.
- **Proposed patch:** a `ToCSharpIdentifier` escape that prefixes `@` when the name is a C# keyword (or a CSharpGenerator-level identifier writer used for ALL names, props, methods, enum members, params).

## F5 — user property named `CurrentStage` collides with the generated stage-tracking property (CS0102/CS0229)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** C# entity export (entity/action/policy/stage codegen)
- **Repro:** `probes/fleet-eval/07-export/export-collisions.poly` — `CurrentStageHolder: entity { CurrentStage: Text  Active: stage { } }` → `CS0102: 'CurrentStageHolder' already contains a definition for 'CurrentStage'` + CS0229 ambiguity in `Create`.
- **Expected:** fail-closed at analysis (reserved generated member name) or collision-free codegen.
- **Actual:** the exporter appends `CurrentStage` to the same type without checking the DSL already declared it.
- **Proposed patch:** analysis diagnostic (structural error) for a property/nav named `CurrentStage` on a staged entity (same for `InitializeSubscriptions`, `Create`).

## F6 — user policy named `not_*` + `require not_X` → exporter strips the prefix and gates on the WRONG policy (silent divergence)
- **Signal:** export/runtime divergence (silently wrong guards + wrong failure message)
- **Severity:** 🟠
- **Slice:** C# entity export (action guards)
- **Repro:** `probes/fleet-eval/07-export/isolated-notpolicy2.poly` — `Paid: policy { Amount > 0 }`, `not_Paid: policy { Amount <= 100 }`, `Settle: action require not_Paid { … }`. Compiles 0/0; export emits `if (this.Paid()) return Failure("'Settle' blocked by policy 'Paid'.")` and `if (!this.not_Paid()) …`. Net: Settle proceeds only when `Amount <= 0`. Runtime (`DomainEntityInstance.InvokeActionInternal:434`) evaluates the policy named `not_Paid` body directly → proceeds when `Amount <= 100`.
- **Expected:** `require not_Paid` (single identifier — NOT the `not` keyword) must gate on the policy named `not_Paid` (its own body), matching the runtime.
- **Actual:** `BuildActionBodyWithGuards` (`DomainToCSharpExporter.cs:1214`) unconditionally treats any `not_`-prefixed action-policy name as a synthetic negation and calls the stripped name. It also emits the wrong failure message. The divergence is masked only when `not_Paid` happens to be an exact complement of `Paid`.
- **Proposed patch:** mark synthetic `not_Policy` requires explicitly (e.g. a flag on the action's policy entry, or resolve against the entity's real policy set) instead of string-prefix heuristics; skip prefix-stripping when `{stripped}` is itself a real entity policy.

## F7 — `default("A")` (string-literal form) on an enum-typed property emits a string default for an enum param (CS1750)
- **Signal:** compile-fail (sibling form of the documented `default(MemberName)`)
- **Severity:** 🟠
- **Slice:** C# entity export (enum-typed props + defaulted props)
- **Repro:** `probes/fleet-eval/07-export/isolated-enum-default.poly` — `Level: Level default("A")` → `error CS1750: A value of type 'string' cannot be used as a default parameter … to type 'Level'` (Create + ctor).
- **Expected:** the sibling authoring forms for enum values (assign RHS `assign L to "A"`, create-in initializer `Level: "A"`) are qualified to `Level.A` by the exporter; `default("A")` must be qualified the same way (or analysis-rejected).
- **Actual:** `LowerDefaultConstantNode` (`DomainToCSharpExporter.cs:1681-1687`) returns a raw `Constant(lit.Value)` for any Literal default without checking the property's enum type. Analysis accepts (bucket check) → compiler rejects.
- **Proposed patch:** in `LowerDefaultConstantNode`, when the property resolves to an enum type and the literal names a member, emit `new Member(new NamedTypeReference(enumType.Name), lit.Value)`.

## F8 — `require X` gate duplicated when X is also an entity-level always-on policy (redundant guard code)
- **Signal:** quality (faithful but duplicated guard emission)
- **Severity:** 🟡
- **Slice:** C# entity export (action guards)
- **Repro:** `probes/fleet-eval/07-export/library.poly` — `Patron.CheckOut` emits `if (!this.IsActive()) return Failure…` twice back-to-back (one from `require IsActive`, one from the entity-level always-on sweep).
- **Expected:** a single guard (the action-level require and the entity-level always-on gate are the same predicate).
- **Actual:** `BuildActionBodyWithGuards` appends the action's `require` guards, then re-appends every entity policy unless the action carries a `not_` inversion — no dedupe when the require names an entity policy. Runtime evaluates both loops too, so behavior matches, but the export is noisy.
- **Proposed patch:** skip an entity-level policy in the always-on sweep when the action already `require`s it (name match).

## F9 — DSL action named `Create` overloads the generated static `Create` factory (footgun)
- **Signal:** fail-loud-but-sharp (compiles; call-site resolution is surprising)
- **Severity:** 🟡
- **Slice:** C# entity export (create factories)
- **Repro:** `probes/fleet-eval/07-export/isolated-create-action.poly` — `Thing` with `Create: action` → `public DomainResult Create()` (instance action) + `public static DomainResult<Thing> Create(string name = "n")` (factory). `Thing.Create()` resolves to the static factory; `instance.Create()` resolves to the action.
- **Expected:** analysis diagnostic for a member named `Create` (or any generated infra member) on an entity — fail-closed.
- **Actual:** compiles 0/0 by overload luck (params differ); self-invoke `invoke Create` still works, but external callers face an accidental-overload trap.
- **Proposed patch:** analysis structural error for actions/policies named `Create`, `InitializeSubscriptions`, or `CurrentStage`.

## F10 — entity named `DomainResult` collides with the scaffolding record (CS0101)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** C# entity export (naming/security)
- **Repro:** `probes/fleet-eval/07-export/isolated-domainresult-name.poly` — `DomainResult: entity { Value: Text }` → `error CS0101: The namespace '<global namespace>' already contains a definition for 'DomainResult'`.
- **Expected:** fail-closed at analysis or collision-free emission (scaffolding is emitted into the same namespace as user entities).
- **Actual:** `DomainProgramProjection` emits `DomainResult`/`DomainResult<T>` scaffolding unconditionally; a user type with the same name duplicates it.
- **Proposed patch:** analysis structural error for entities named `DomainResult` (and `List`, `DateTime`, etc. — see note), or prefix the scaffolding namespace.

---

### Verified-OK on this sweep (not findings)
- Enum create-in initializers — both authoring forms (`Priority: High`, `Priority: "Med"`) qualify correctly to `Priority.High` / `Priority.Med` (`export-edges.poly`).
- Non-member enum literal in create-in is now rejected at analysis ("'Bogus' is not a member of enum 'Level'") — round5 F4 closed.
- Store-dependent `for` predicate (collection quantifier policy) rejected at analysis with the documented message (`isolated-store-predicate.poly`).
- Quantifier subscription codegen: `WhenAny`/`WhenAll`/`WhenEach{Target}{Stage}` naming, peer-binder arg (`WhenEach…(this)`), and the `all`-set gate all emit correctly and compile 0/0 (`isolated-quantifiers.poly`).
- Stage-scoped subscription handler stage gate (`if (this.CurrentStage != …) return;`) emits correctly.
- Cross-entity OneToOne invoke guard (`if (this.Customer == null) return Failure…`) — no bare `!` deref.
- `require not IsEmpty` (real negation, space form) inverts correctly; entity-level always-on policies emit per action.
- Backref auto-wire in `Create{Nav}` factories (`borrower: this`) and defaulted-prop trailing optional params line up between factory signatures and call sites (arity consistent).
- `default(now)` on DateTime and `default(Good)` on enum qualify correctly.
- `.poly` initializers are whitespace-separated, not comma-separated (documented surface).
