# Fleet evaluation 2026-08-12 — coordinator synthesis

15 slice agents evaluated the Poly project across five lenses (quality / consistency /
product / security / reliability), read-only. Findings per slice in this directory;
probes in `probes/fleet-eval/<slice>/`. **2063/2063 suite green; repo clean.** The
`08-storage` agent returned empty; the storage/DbContext surface was covered by
`13-packs` (which drives `--mode db`/`all`).

## Cross-cutting root causes (deduped — found by 2+ agents)

| Theme | Agents | Root cause |
|-------|--------|------------|
| **Decimal literals parse as Text** | 03-F7, 11-F4, 14-F1 | `DslExpressionParser.ParsePrimary` falls back to a string literal when `long.TryParse` fails → `Total * 0.9` (guide §8 example) rejected, fractional arithmetic unreachable |
| **Open-range `null→0` corruption** | 02-F3, 05-F3 | `Convert.ToDouble(null)==0` in `AbstractValue`/`EffectAnalyzer` inverts one-sided envelopes → false errors on `range(100, )` / `range(, 50)`; latent NRE at `ValidateCallChainPostconditions:1220` |
| **Invoke args unvalidated at runtime/MCP** | 10-F1/F4, 11-F1/F2, 12-F8 | args injected into `_values` with no `action.Parameters` check → real properties clobbered/deleted, guard policies bypassed, `Missing.Value` for absent params, param-ref args mangled to instance handle |
| **Tooling gate only compiles entities mode** | 09 (gate), 13 (gate), 15-F1/F3 | `run-probe.sh` never compiles `Program.cs`/`DbContext`; `probe-check` ignores warnings; full-solution oracle = one fixture → the whole transport + storage surface is invisible to the gate |
| **Guide drift (≈10 sections)** | 01-F5/F6, 06-F4, 12-F4, 14-F1…F13 | §8 `invoke any/all`, §11 inline `enum(...)`, §0.4 `;` create-in, §6 dotted binder args, §0.3 DMEFF011 example, decimal example, stale to-one claims |
| **Printer round-trip drift** | 01-F1/F2, 02-F4, 06-F4 | `not (…)` parens dropped, mixed `require A, not B` comma-joined, create-in `,` separators, `/* equals */` comment — `export_dsl → apply_dsl` breaks on all of them |
| **C# reserved names as DSL identifiers** | 01-F3, 07-F4/F5/F10 | keywords (`namespace`, `event`, `string`), `CurrentStage`, `DomainResult` as entity/prop names → no analysis rejection, raw emission breaks the export (identifier injection) |
| **Expression type-check Unknown-bypass family** | 03-F1/F4/F6/F9, 05-F1 | invoke args, if-conditions, binder-root unknown props, unknown identifiers all resolve `Unknown` → pass analysis → late CS errors |
| **Verified-envelope unsoundness** | 05-F2 | writers with UNKNOWN envelopes still mark a property "verified" → bogus DB CHECKs emitted where the invariant was never proved |
| **Param-reference analysis dead on parsed DSL** | 05-F1, 11-F1 | parsed bare identifiers are `PropertyAccess`, not `ParameterAccess` → call-chain binding analysis + `EvaluateParameterBindings` never fire for real DSL |

## Ranked findings

### 🔴 compile-fail / divergence (must fix)
- **Transport (09-F1/F2/F3):** child-entity action endpoints never declare the `dto` lambda param (CS0103); parent+child both shadow-keyed → duplicate `id` (CS0100); to-one nav treated as aggregate child → `.Collection()` on a reference (CS1660/CS0411). Every generated Program.cs in the probe set fails to compile.
- **Export (07-F1/F2/F3/F5/F10):** `for`-invoke inside a `-> EntityType` action returns non-generic `DomainResult` (CS0029); two creates of the same type → duplicate locals (CS0128); singular-nav subscription registration derefs a nullable nav (CS8602+NRE); `CurrentStage`/`DomainResult` name collisions (CS0102/CS0101).
- **Parser (01-F1/F2):** printer drops parens on `not (…)` and comma-joins mixed `require` → round-trip re-parse fails.
- **Runtime (10-F1/F2):** invoke args injected unvalidated (property clobbering + guard bypass); `create`/`create in` in entry effects passes analysis → export CS1061 + orphaned child at runtime.
- **Expression type (03-F1…F6):** invoke-arg caller-prop/param args, runtime-keyword assign RHS, date-param arithmetic, non-boolean if-conditions, bare enum member in action-`if`, unknown binder props all pass analysis → late CS.
- **Analysis (04-F1/F2):** create-in into a self-relationship target → CS1503; camelCase nav with internal capital → CS1061.
- **MCP (12-F1):** create/create-in binding a defaulted prop + sibling `default(now/today/guid)` fails codegen.

### 🟠 divergence / consistency / product / security
- **Invariant (05-F1/F2/F3):** open-range false errors; verified-unsound CHECKs; dead param-reference analysis.
- **Subscriptions (06-F2/F3):** multi-stage `all` never fires for a spread linked set; export fires entity-level handlers before stage-scoped (runtime/guide do the reverse).
- **Transport (09-F4…F8):** child detail ignores `{id}` (silent wrong record); CS8602 back-ref; grandchildren orphaned (actions float to root scope); seed silently inserts nothing on constraint-violating samples; demo.http bodies fail the generated DTO validation.
- **Export (07-F6/F7):** `not_`-prefixed user policy + `require not_X` strips the prefix and gates on the WRONG policy (silent divergence); `default("A")` string sibling on enum emits a raw string (CS1750).
- **Packs/storage (13-F1/F2/F3):** enum columns emit `.HasColumnType("<EnumName>")` — compiles 0/0, dies at `EnsureCreated`; SqlServer `nvarchar(max)` on natural keys; column names interpolated raw into CHECK SQL (reserved words + `--` comment injection).
- **MCP (12-F10):** no session ownership enforcement — any agent can act on any session.
- **VM (11-F1):** cross-entity invoke arg referencing the caller's action parameter is mangled to the instance handle (export correct, runtime wrong).

### 🟡 sharp
- Subscription create-during-notify crash (06-F1 — collection modified mid-enumeration; runtime-verified), export/runtime stage-vs-entity handler order, dead-store false error (05-F6), entity-level policy duplication (07-F8, 04-F4), `default(Bogus)` enum codegen (03-F8), null universally compatible (03-F11), `delete` dead grammar (01-F9), unbounded nesting → uncatchable StackOverflow (01-F7), fractional JSON truncation (12-F7), double-registered children (12-F9), reserved `any`/`all` nav names (01-F11), unquoted-string scan (01-F10), missing-property subscription warning suppressed (06-F5), VM double/decimal truncation (11-F3), tautological test (15-F2), brittle JSON assertions (15-F5), dead commented-out benchmarks (15-F6).

## Fix plan (test-first, prioritized)

1. **P0 — tooling gate:** `probe-check` enforces warnings; `run-probe.sh`/`discovery-round.sh` compile the FULL generated solution (entities + Program.cs + DbContext) against ASP.NET+EF refs, per pack. Add the transport/storage probes as the gate fixtures. *(fixes the blind spot that hid 09/13's 🔴s)*
2. **P0 — runtime/MCP arg validation:** `InvokeAction` validates args against `action.Parameters` (reject unknown keys, require all params) before injection; schema-type-check JSON values at the tool boundary. *(10-F1/F3/F4, 11-F2, 12-F8)*
3. **P1 — type-check escapes:** close the `Unknown`-bypass family (03) + fix param-reference analysis (05-F1, 11-F1) so parsed-DSL params are `ParameterAccess`-aware through eval/validation. Regression tests per form.
4. **P1 — export correctness:** transport child-endpoint `dto` param, duplicate-key disambiguation, to-one-not-as-collection, child-detail `{id}` filter; export reserved-name rejection (keywords/`CurrentStage`/`DomainResult`); `for`-in-returning and duplicate-create locals.
5. **P1 — printer round-trip + guide drift:** fix printer (parens/require/create-in separators); sweep the guide's stale sections (invoke any/all, inline enum, semicolons, dotted binder, decimal example).
6. **P2 — invariant soundness:** null-guard `ToDoubleOrNull` (both copies) + null-safe `ValidateCallChainPostconditions`; unknown-writer envelopes must NOT mark verified; open-range regression tests.
7. **P2 — subscriptions:** snapshot the notify sweep (06-F1); multi-stage `all` union semantics (06-F2); runtime stage-then-entity ordering (06-F3).

Each fix: one failing regression test → smallest change → green → re-sweep. Defer the modeling traps (05-F5 entity-level policy gating, 07-F9 Create action overload, 12-F10 session ownership) to a decision, not a silent fix.
