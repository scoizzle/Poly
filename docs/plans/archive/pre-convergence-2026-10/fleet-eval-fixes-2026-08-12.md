# Plan: Fleet-evaluation fixes — 2026-08-12

**Date:** 2026-08-12 · **Status:** approved for execution (probe checklist). Representation sequence + what was absorbed vs left here: [`domainmodeling-e2e-representation-2026-08-13.md`](domainmodeling-e2e-representation-2026-08-13.md). Do not admit both as CURRENT for the same files.
**Source:** `docs/plans/archive/probes-2026-08/findings/fleet-eval/coordinator.md` (15 slice agents, read-only, five lenses:
quality / consistency / product / security / reliability). Findings per slice:
`docs/plans/archive/probes-2026-08/findings/fleet-eval/<slice>.md`. Historical probes: `docs/plans/archive/probes-2026-08/fleet-eval/<slice>/`. Live oracles: `docs/probes/`.
**Baseline:** 2063/2063 green, repo clean, all builds 0 warnings.

---

## 1. The gate blind spot — fix first (P0-0)

`scripts/run-probe.sh` compiles **entities mode only**; it never generates or compiles
`Program.cs` or the `DbContext`. Every transport (09) and storage/packs (13) defect below
compiles 0/0 through the gate. `scripts/probe-check` also returns 0 on warnings (15-F1).
The in-suite full-solution oracle is exactly one fixed domain (15-F3).

- [ ] **P0-0a — probe-check enforces warnings.** `scripts/probe-check/Program.cs:36` → exit 1 on any warning. Verify a warning-bearing export fails.
- [ ] **P0-0b — run-probe.sh compiles the full solution.** Add a `--mode all`/`--dbms sqlite` compile pass against ASP.NET Core + EF Core reference assemblies (a `demo/Poly.RestApi`-style fixture). The gate must compile entities + Program.cs + DbContext.
- [ ] **P0-0c — adopt the transport/storage probes as gate fixtures.** `probes/fleet-eval/09-transport/{warehouse,orders,clinic}.poly` and `probes/fleet-eval/13-packs/{warehouse,booking,library}.poly` must compile the FULL solution (they currently generate broken Program.cs/DbContext — the regression proof).
- [ ] **P0-0d — `discovery-round.sh` PASS criterion** = full-solution 0 errors / 0 warnings, not `^errors: 0`.

**Acceptance:** `run-probe.sh` on the 09/13 probes fails loudly; after the fix batches below, they pass 0/0 full-solution.

---

## 2. Work items

Each item: **one failing regression test → smallest fix → green → re-sweep.** Repro paths
point at the agent-authored probes. 🔴 items block a release; 🟠 are divergence/silent;
🟡 sharp.

### P1 — Runtime & MCP boundary validation (security-relevant)

- [ ] **P1-1 🔴 — invoke args validated against `action.Parameters`.** `DomainEntityInstance.InvokeAction` (lines ~386–404) injects every arg key into `_values` and deletes it after — unknown keys clobber real properties, guard policies are bypassed (invoke with `{"Age":40}` on an `Age>=18` gate with `Age:15` succeeds), and absent params become `Missing.Value`. **Fix:** reject unknown arg keys and require every declared param (fail-closed, DMEFF007-mirror at the runtime boundary). **Test:** `DomainEntityInstanceTests` — unknown-key rejection, guard-bypass regression, missing-param rejection. Repro: `probes/fleet-eval/10-runtime/library.poly`.
- [ ] **P1-2 🔴 — JSON values schema-type-checked at the MCP boundary.** `create_instance({"Qty":"not-a-number"})` succeeds then poisons the instance; `29.99` truncates to `29` (12-F7). **Fix:** coerce-or-reject JSON values against the target property/param CLR type in the tool boundary (mirror the export's typed factories + DTO attributes). **Test:** MCP smoke — wrong-typed create rejected; fractional numeric preserved.
- [ ] **P1-3 🟠 — cross-entity invoke param-ref args fixed in the runtime.** `invoke invoice.Settle(amount: amount)` where `amount` is the caller's action param mangles to the instance handle (11-F1); `EvaluateParameterBindings` compiles with the entity-only provider. **Fix:** thread action params through binding evaluation (root-cause link with P2-1). **Test:** runtime + export agree on param-ref arg values. Repro: `probes/fleet-eval/11-vm/vm-crossinvoke.poly`.

### P2 — Expression type-check escapes + parameter-reference analysis

- [ ] **P2-1 🔴 — parsed-DSL params are `PropertyAccess`, not `ParameterAccess`.** `DslExpressionParser.ParsePrimary` emits `Property(name)` for bare identifiers; `Eval`/`ValidateAssign`/`BuildPostconditionConstraints`/`ConstraintPropagationAnalyzer` consult `paramEnv` only for `ParameterAccess` → the call-chain binding analysis is dead on the product path (05-F1). **Fix:** make the analysis treat a `PropertyAccess` whose name matches an in-scope action parameter as a parameter (or normalize at parse). **Test:** `invoke Add(amount: 50)` on `Total range(0,100)` produces the `DoIt → Add` call-chain warning (05-F1 repro `chainparam.poly`).
- [ ] **P2-2 🔴 — close the `Unknown`-bypass family in `ExpressionTypeAnalyzer`** (03-F1/F4/F6/F9): invoke-arg caller-prop/param args (`InferLiteralAware` without props), if-condition type, binder-root unknown target props, unresolvable assign RHS identifiers. **Fix:** thread props/parameters into invoke-arg inference; type-check if-conditions; report unknown identifiers on assign/arg paths instead of `Unknown`-skip. **Test:** one per form (`expr-f1…f9*.poly`).
- [ ] **P2-3 🔴 — runtime-keyword assign RHS type-checked** (03-F2): `assign Qty to now` passes analysis → CS0029. **Fix:** mirror `CheckDefault`'s keyword rejection on assign RHS. **Test:** `Number`/`Text` targets rejected at analysis.
- [ ] **P2-4 🔴 — date arithmetic on a date PARAMETER** (03-F3): `assign DueDate to d + 30` (d: Date) passes → CS0019. **Fix:** the `AddDays` lowering keys on `PropertyAccess` only; widen to param-typed dates. **Test:** date param arithmetic compiles.
- [ ] **P2-5 🔴 — non-boolean if-conditions rejected** (03-F4): `if (Qty)` passes. **Fix:** type-check the condition. **Test:** numeric/text condition rejected at analysis.
- [ ] **P2-6 🔴 — bare enum member in action-body `if`** (03-F5): `if (Genre is Fiction)` passes in actions but is rejected in policies. **Fix:** consistent enum-member resolution in `if` conditions. **Test:** both contexts agree.
- [ ] **P2-7 🟠 — decimal literals parse as Text** (03-F7, 11-F4, 14-F1): `Total * 0.9` (guide §8) rejected; `default(0.5)` mis-typed. **Fix:** `DslExpressionParser.ParsePrimary` numeric fallback for decimals. **Test:** fractional arithmetic + default. Note: affects the DatePack-adjacent numeric surface — confirm scope with the date-pack deferral.
- [ ] **P2-8 🟠 — `default(<bare non-member>)` on enum property** (03-F8): passes analysis → misleading codegen "not a member of enum". **Fix:** `CheckDefault` membership check when the target IS enum (assign/create-in/comparison siblings already reject). **Test:** `default(Bogus)` on an enum prop rejected at analysis.
- [ ] **P2-9 🟠 — enum-member invoke args** (03-F10): `status: "Active"`/`"Bogus"` pass → CS1503; lowering never qualifies enum literals in invoke args. **Fix:** membership check + literal qualification in the invoke-arg path. **Test:** valid member compiles, non-member rejected.
- [ ] **P2-10 🟠 — `null` not universally compatible** (03-F11): `assign Qty to null` passes → CS0037. **Fix:** `Null` category allowed only for reference/nullable targets. **Test:** null-to-Number rejected at analysis.

### P3 — Export correctness (entities + transport)

- [ ] **P3-1 🔴 — transport child-endpoint `dto` param** (09-F1): child action endpoints with params never declare `dto` (CS0103). **Fix:** parent-ctx branch in `AppendActionEndpointStatements` adds the `{Action}Dto` lambda param. **Test:** full-solution compile of `09-transport/warehouse.poly` (via P0-0b).
- [ ] **P3-2 🔴 — duplicate shadow-key `id` params** (09-F2): parent+child both shadow-keyed → duplicate `id`. **Fix:** disambiguate the child route token/param. **Test:** `09-transport/clinic.poly` full-solution compile.
- [ ] **P3-3 🔴 — to-one nav not emitted as a collection child** (09-F3): `.Collection()` on a reference nav (CS1660/CS0411); Doctor loses root CRUD. **Fix:** branch on `ReferenceNavigations` vs `CollectionNavigations`; skip list endpoints for to-one children. **Test:** `clinic.poly`.
- [ ] **P3-4 🟠 — child detail ignores `{id}`** (09-F4): shadow-keyed child returns `FirstOrDefault()` — any `{id}` returns the first record, silently. **Fix:** filter by child key. **Test:** two children, `{id}` returns the matching one.
- [ ] **P3-5 🟠 — CS8602 on child-detail back-ref** (09-F5): `e.Warehouse.Code` on `Warehouse?`. **Fix:** null-forgive or FK-compare. **Test:** 0-warning full-solution.
- [ ] **P3-6 🟠 — grandchildren orphaned** (09-F6): non-root children get no CRUD and their actions float to root scope without parent checks. **Fix:** route through the aggregate chain or fail loud at analysis. **Test:** `warehouse.poly` Delivery endpoints.
- [ ] **P3-7 🟠 — seed silently inserts nothing** (09-F7): `MakeSampleValue` ignores `pattern` → `if (result.IsSuccess)` silently skips. **Fix:** honor `PatternConstraint` in sample values or surface seed failures. **Test:** pattern-constrained root seeds.
- [ ] **P3-8 🟠 — demo.http bodies fail the emitted DTO validation** (09-F8): sample values violate `[Range]`/`[Required]`/`[RegularExpression]` → every POST 400. **Fix:** sample generation honors the same constraints the DTO emitter applies. **Test:** round-trip demo.http bodies through the DTO attributes.
- [ ] **P3-9 🟡 — arithmetic assign gets no implicit `[Range]`** (09-F9): `assign Capacity to Capacity + delta` → `AdjustCapacityDto.delta` unbounded. **Fix:** derive a conservative bound from the target's verified range or fail loud on unconditional arithmetic param flows.
- [ ] **P3-10 🔴 — reserved names as DSL identifiers** (01-F3, 07-F4/F5/F10): C# keywords, `CurrentStage`, `DomainResult`, `Create` action, `namespace`/`event` entity/props → no analysis rejection, raw emission (identifier injection). **Fix:** reject reserved generated names at analysis (structural), fail-loud. **Test:** one per name class.
- [ ] **P3-11 🔴 — `for`-invoke inside a `-> EntityType` action** (07-F1): fail-fast `return result0;` / `return Failure(...)` from a `DomainResult<Order>` method (CS0029). **Fix:** lower to `DomainResult<T>` or reject at analysis. **Test:** `07-export/export-edges.poly` full-solution compile.
- [ ] **P3-12 🔴 — duplicate create locals** (07-F2): two creates of the same type → `{camel}Result`/`{camel}` twice (CS0128). **Fix:** per-statement sequence in unwrap-local naming. **Test:** `isolated-two-creates.poly`.
- [ ] **P3-13 🔴 — singular-nav subscription registration derefs nullable nav** (07-F3): `this.Node.RegisterDeviceOnlineSubscriber(this)` on `Node?` (CS8602+NRE). **Fix:** guard or link-time registration. **Test:** `isolated-stagescoped.poly` 0/0.
- [ ] **P3-14 🟠 — `not_`-prefixed user policy + `require not_X`** (07-F6): exporter strips the prefix and gates on the WRONG policy (silent divergence, wrong message). **Fix:** distinguish synthetic negation from user names (store the negated flag, not name-mangling). **Test:** export + runtime agree on `not_Paid`.
- [ ] **P3-15 🟠 — `default("A")` string sibling on an enum param** (07-F7): emits a raw string (CS1750). **Fix:** qualify string-literal defaults for enum targets like assign/create-in. **Test:** `isolated-enum-default.poly`.
- [ ] **P3-16 🔴 — create-in into a self-relationship target** (04-F1): `IsBackReference` conflates self-rel with back-ref → CS1503. **Fix:** separate self-rel from back-reference semantics. **Test:** `selfrel-createin.poly` full-solution compile.
- [ ] **P3-17 🔴 — camelCase nav with internal capital** (04-F2): `aRefs` → `_arefs` vs `_aRefs` (CS1061). **Fix:** single source of truth for the backing-field name. **Test:** `edges.poly`.
- [ ] **P3-18 🔴 — `create`/`create in` in entry effects** (10-F2): passes analysis → export CS1061, orphaned child at runtime. **Fix:** uniform rejection at authoring (guide §9: create is action-only). **Test:** entry-effect create rejected; runtime+export consistent.
- [ ] **P3-19 🟠 — `-> T` final-conditional producer → throwing stub** (04-F3): guide ✅ shape emits `throw NotSupportedException`. **Fix:** lower the branch-created return (or reject). **Test:** `cond-return.poly` returns the created entity.
- [ ] **P3-20 🔴 — create/create-in binding a defaulted prop + sibling `default(now/today/guid)`** (12-F1): codegen failure (`AppendDefaultedPropArgs` calls `LowerDefaultConstantNode` unconditionally). **Fix:** guard the runtime-keyword branch. **Test:** `12-mcp/mcp-create-defaults-fail.poly` compiles.

### P4 — Printer round-trip + guide drift

- [ ] **P4-1 🔴 — printer drops parens on `not (…)`** (01-F1): `not (Total > 0)` → `not Total > 0` (unparseable). **Fix:** `ExpressionPrinter.Not` parens. **Test:** round-trip.
- [ ] **P4-2 🔴 — mixed `require` printer drift** (01-F2): `require A` + `require not B` → `require A, not B` (unparseable). **Fix:** printer splits positive/negated gates. **Test:** round-trip.
- [ ] **P4-3 🟠 — create-in printer emits `,` separators** (06-F4): parser rejects. **Fix:** whitespace separators. **Test:** `export_dsl → apply_dsl` on a multi-initializer create.
- [ ] **P4-4 🟡 — `EqualityConstraint` prints `/* equals */` comment** (02-F4): unparseable round-trip. **Fix:** printer emits a re-parseable form or the model drops the constraint. **Test:** round-trip.
- [ ] **P4-5 🟠 — guide sweep** (01-F5/F6, 06-F4, 12-F4/F5, 14-F1…F13): stale sections — §8 invoke `any/all` + decimal example, §11 inline `enum(...)`, §0.4 `;` create-in, §6 dotted binder args, §0.3 DMEFF011 example + to-one claims, §9 `unlink_instances` (shipped), duplicate-annotation "parse error" claim (silently last-wins), agent-vs-product guide divergence (14-F12). **Fix:** update the guide to the shipped surface; add a smoke test per corrected example. **Acceptance:** every documented example compiles via `run-probe.sh`.

### P5 — Invariant & constraint envelope soundness

- [ ] **P5-1 🟠 — null range bounds → 0** (02-F3, 05-F3): `AbstractValue.ToDoubleOrNull` + `EffectAnalyzer.ToDouble` lack the null guard → one-sided envelopes inverted → false errors on open ranges. **Fix:** null-short-circuit both. **Also fix the latent NRE:** `ValidateCallChainPostconditions:1220` `vr.Min!.Value`/`vr.Max!.Value` must be null-safe. **Test:** `range(100, )` and `range(, 50)` assigns warn, not error (05-F3 `openrange.poly`).
- [ ] **P5-2 🟠 — unknown-writer envelopes must NOT mark verified** (05-F2): `ComputeVerifiedRanges` skips null `ValueRange` postconditions and marks the property verified → bogus DB CHECKs. **Fix:** an unknown writer for a property disqualifies verification (or emits no CHECK). **Test:** `param-flow.poly` `--mode db` emits no CHECK for unverified props.
- [ ] **P5-3 🟡 — binder-rooted fan-out args evaluated against the CALLER** (05-F4): `ApplyForEachInvoke` doesn't thread `targetEntity` into binding `Eval`. **Fix:** thread it (root-cause link with P2-1). **Test:** `binder-arg.poly` envelope is non-unknown.

### P6 — Subscriptions

- [ ] **P6-1 🔴 — subscription `create` crashes the notify loop** (06-F1, runtime-verified): `Store.Add(child)` mutates `_instances` mid-enumeration → `Collection was modified`. **Fix:** snapshot the sweep list (`_instances.ToArray()`). **Test:** subscription-create through `create_instance → invoke_action` (guide §0.4 shape).
- [ ] **P6-2 🟠 — multi-stage `all` never fires** (06-F2): `when all orders Ready, Delivered` with the set spread across stages → never fires (runtime + export require the same single stage). **Fix:** evaluate the set predicate against the union of `StageNames` on both paths. **Test:** spread-set `all` fires.
- [ ] **P6-3 🟠 — export fires entity-level before stage-scoped** (06-F3): runtime (and guide §7) do stage-first. **Fix:** export handler order matches the runtime dispatch. **Test:** stage write then entity write leaves the stage value.
- [ ] **P6-4 🟡 — missing subscriber/peer property warning suppressed** (06-F5): `SubscriptionAnalyzer` reports a warning; `DslCompiler` surfaces only errors → late CS1061. **Fix:** escalate to an error at authoring. **Test:** `missing-subscriber-prop.poly` rejected at analysis.

### P7 — Sharp / hardening (small, high-clarity)

- [ ] **P7-1 🟡 — `delete` dead grammar** (01-F9): pattern defined, no handler → internal error leaks. **Fix:** remove the grammar or implement fail-loud authoring.
- [ ] **P7-2 🟡 — unbounded nesting → uncatchable StackOverflow** (01-F7): ~4 KB of nested parens crashes the compiler (would take down the shared MCP server). **Fix:** nesting-depth guard in the tokenizer/parser.
- [ ] **P7-3 🟡 — unterminated string scan** (01-F10): silent EOF, confusing error. **Fix:** `ScanString` fails on missing close quote.
- [ ] **P7-4 🟡 — reserved `any`/`all`/`none`/`count` nav names undocumented** (01-F11): unusable in expression reads. **Fix:** document or reject at analysis.
- [ ] **P7-5 🟡 — enum columns `.HasColumnType("<EnumName>")`** (13-F1): compiles 0/0, dies at `EnsureCreated`. **Fix:** provider-valid store type (INTEGER/int) or drop `HasColumnType` for enums. **Test:** sqlite/sqlserver/generic `EnsureCreated`.
- [ ] **P7-6 🟠 — SqlServer `nvarchar(max)` natural keys** (13-F2): invalid SQL Server key columns; `length` max dropped. **Fix:** key/unique text columns map to a bounded type honoring `length`. **Test:** `booking.poly` sqlserver DbContext.
- [ ] **P7-7 🟠 — column names interpolated raw into CHECK SQL** (13-F3a/b): reserved words + `--` comment injection silently disable the CHECK. **Fix:** provider-quoted, deduped constraint SQL/names; validate column names. **Test:** `column("order")` and a comment-injection name.
- [ ] **P7-8 🟡 — effective-policy composition not deduped** (04-F4, 07-F8): entity policy + same `require` emit the gate twice. **Fix:** dedupe in `CapabilityAnalyzer`. **Test:** single gate per policy.
- [ ] **P7-9 🟡 — tautological test** (15-F2): `EH_Vm_Deprecated_NonCritical` asserts `var v = true;`. **Fix:** delete or make it a real oracle.
- [ ] **P7-10 🟡 — brittle exact-format JSON assertions** (15-F5). **Fix:** structural asserts.
- [ ] **P7-11 🟡 — dead commented-out benchmarks + README drift** (15-F6). **Fix:** delete or uncomment; correct `PolicyEvaluator`/`TestTraceWriter` README entries.

---

## 3. Deferred decisions (not silent fixes)

- **05-F5 — entity-level policies gate every action** (unsatisfiable-predicate pair rejects the whole entity). Faithful-but-surprising modeling trap; needs a product decision (per-action gating semantics / guide wording).
- **07-F9 — action named `Create` overloads the factory** (compiles by overload luck). Needs a reserved-name policy.
- **12-F10 — MCP session ownership** (any agent can act on any session). Needs a coordination/ownership decision.
- **05-F6 — dead-store false error** (`assign Qty to 200; assign Qty to 5` on range(0,100) rejects). Per-effect verification has no liveness; needs a product decision.
- **11-F3 — VM truncates non-long value types** (latent; double/float/decimal unreachable via today's DSL). Fix when the numeric surface widens.
- **P2-7 decimal literals** — confirm scope against the DatePack deferral.

---

## 4. Sequencing

1. **P0-0** (gate) — unblocks every other acceptance test. Land with the 09/13 probes failing loud, then going green.
2. **P1** (runtime/MCP arg validation) — security-relevant, small surface.
3. **P3-10, P3-1…P3-3** (export reserved names + the three transport 🔴s) — the highest-volume compile-fails.
4. **P2** (type-check escapes + param-reference fix) — broad, test-per-form.
5. **P4** (printer + guide) — golden workflow restoration.
6. **P3-4…P3-20** (remaining export) then **P5** (invariant) then **P6** (subscriptions) then **P7** (hardening).

Each batch: failing regression test → smallest fix → green → commit. Full suite + all builds (0 warnings) after every batch; `run-probe.sh` on the 09/13/07/05 probes as the acceptance sweep.

## 5. Acceptance

- Full-solution compile gate (P0-0) is green on every probe set, per pack.
- Every 🔴 finding has a regression test that fails before the fix and passes after.
- Suite green (2063+new), all builds 0 warnings, `run-probe.sh` 0/0 full-solution.
- Guide sweep leaves every documented example compilable.
