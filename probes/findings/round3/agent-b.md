# Round-3 discovery findings — agent-b — TYPE ABUSE + CROSS-TYPE OPERATIONS

Agent: `agent-b`. Protocol: [`docs/agent/poly-discovery-loop.md`](../../docs/agent/poly-discovery-loop.md).
Slice: cross-type assigns, incompatible comparisons (Date<Number, Status==5, Text==Boolean),
string↔number concat, arithmetic on enums, date arithmetic everywhere, `default(now|today|guid)`
on every property type, enum-member defaults on non-enums, non-member enum string values, null
flows, Boolean ops on non-bools, zero/negative literals.

Probes: `probes/agent-b/{type-comparisons,type-assign-arithmetic,defaults-abuse,enum-null-bool-abuse}.poly`
(all analysis-pass → export compile-fail), `cc-string-number-concat.poly`, `cc-null-comparison.poly`
(**compile 0/0** — the divergence prizes), `cc-date-vs-string.poly`.
Runtime evidence: scratch console harness in `/tmp/agentb-runtime` + `/tmp/agentb-null` (same public
tool API the MCP exposes; deleted throwaway TUnit from `Poly.Tests/DiscoveryAgentB2`). MCP untouched.

**Headline:** there is **no type-compatibility checking anywhere in the DSL layer**. Every wrong-typed
comparison, assign, arithmetic, and default passes parse + analysis (evolution succeeds), then fails
at the *C#* layer (compile-fail) while the runtime silently accepts and stores garbage. The runtime
and export agree on **none** of the wrong-typed inputs tested. Two probes compile 0/0 and diverge on
behavior. No category of my slice was entirely clean.

---

## 🔴 Export compile-fail class (analysis accepts, generated C# does not compile)

These are the *three-layer defense* gap: parse accepts, analysis accepts, only the Roslyn layer
complains — so MCP users (`apply_dsl` → runtime tools) see no error at all, while anyone running the
export gets CSxxxx. All pass `run-probe.sh` past analysis and fail compile-check.

## F1 — wrong-typed comparisons in policies pass analysis, then CS0019/CS0023/CS0029
- **Signal:** compile-fail
- **Severity:** 🔴
- **Repro:** `probes/agent-b/type-comparisons.poly` — `Age == "18"`, `Name == true`, `Status == 5`,
  `Name == 5`, `Joined < 5`, `Age == true`, `not Age`, `Count is true`, `Status is false` →
  export reports `errors: 12, warnings: 0` (CS0019/CS0023/CS0029).
- **Expected:** DSL-level type checking should reject incompatible operand types at analysis
  (fail-closed), or at least the export should not emit uncompilable C# for DSL analysis accepted.
- **Actual:** every policy parses + analyzes clean (evolution succeeds; `DslCompiler` proceeds to
  codegen). Only the generated C# fails. Runtime evaluation meanwhile **coerces** instead of failing:
  `Count == "18"` (Count=18) → **false**, `Count is true` (Count=18) → **true**,
  `Name == 5` (Name="x") → false, `not Count` → works — silently divergent answers, no error.
- **Proposed patch (not applied):** operand-type validation in the policy/expression analyzer
  (binary ops + `not` + `is`) — reject mismatches with a domain diagnostic; keep the runtime
  coercion only as a documented last-resort or fail loud too.

## F2 — cross-type assigns / arithmetic pass analysis, then CS0029/CS0019; runtime silently stores garbage
- **Signal:** compile-fail (+ divergence)
- **Severity:** 🔴
- **Repro:** `probes/agent-b/type-assign-arithmetic.poly` — `assign Count to "hello"`,
  `assign Name to 42`, `assign Name to Count > 5`, `assign Count to Color + 1`,
  `assign Count to When + 1`, `assign Flag to 1`, `assign Count to Count + "x"`, entry
  `assign Count to "five"` → `errors: 10` (CS0029/CS0019).
- **Expected:** type-incompatible assign RHS rejected at analysis; runtime must not run a different
  behavior than the export.
- **Actual:** analysis accepts; export compile-fails; runtime **succeeds** and stores garbage:
  `assign Count to Name` (Name="hello") → Count=`2` (see F11); `assign Name to Count` (Count=42) →
  Name=`null`; `assign Color to 7` → Color=`null`; create-initializer path instead stores the *raw*
  string (`create Child { Count: Name }` → child Count="hello", no coercion) — the assign and
  create-initializer paths don't even agree with each other.
- **Proposed patch (not applied):** same analyzer fix as F1 (validate assign target type vs RHS
  expression type), plus make `CreateChildInstance` initializers coerce/lower identically to assign.

## F3 — wrong-typed `default(now|today|guid)` and literal defaults pass analysis, then CS0019/CS1750
- **Signal:** compile-fail (+ divergence)
- **Severity:** 🔴
- **Repro:** `probes/agent-b/defaults-abuse.poly` — `Text default(now)`, `Number default(today)`,
  `Boolean default(guid)`, `Color default(now)`, etc. → `errors: 16` (CS0019 `??` mismatch,
  e.g. `string ?? DateTime`). Literal form (`Number default("x")`, `Text default(5)`,
  `Date default(true)`) → CS1750 default-parameter conversion errors.
- **Expected:** `now`/`today`/`guid` are typed values (DateTime/DateOnly/Guid); a `default()` on a
  property of a different type should be rejected at analysis. `default(today)` on a Date and
  `default(now)` on a DateTime are the only sensible pairs.
- **Actual:** analysis accepts every pair; the exporter emits `prop = arg ?? <typed-default>` with
  mismatched types → CS0019/CS1750. Runtime silently stores the **unconverted value**:
  `TNow: Text default(now)` stores a `DateTime` object in a Text prop, `NToday: Number default(today)`
  stores `DateOnly`, `BNow: Boolean default(now)` stores `DateTime` — wrong-typed values in the bag
  with no error, which later reads (string ops, comparisons) will mangle.
- **Proposed patch (not applied):** validate `default()` value type against the property type in
  analysis (parse `now`→DateTime, `today`→DateOnly, `guid`→Guid); runtime `EvaluateDefaultValue`
  should also check (or the analyzer guarantees it).

## F5 — non-member enum strings (assign / entry / default) accepted by runtime, CS1503/throw in export
- **Signal:** divergence (silent-wrong runtime)
- **Severity:** 🟠
- **Repro:** `probes/agent-b/enum-null-bool-abuse.poly` — `assign Color to "Purple"`,
  `entry { assign Color to "NotAMember" }`, `Color default("hello")` (enum prop), bare
  `Color default(Bogus)`.
- **Expected:** a value that is not a member of the target enum should be rejected at analysis
  (fail-closed), like the literal constraint checks; runtime and export should agree.
- **Actual:** runtime accepts all silently (entry stores "NotAMember", action stores "Purple",
  default("hello") yields "hello"). Export: string-literal forms → CS1503 (string→Color);
  bare non-member default → codegen `NotSupportedException` ("'Bogus' is not a member of an enum").
  So runtime accepts what the export refuses — the two disagree on the same DSL. (Round-2
  F-R2-4 covered the create-initializer form; this extends to assign/entry/default.)
- **Proposed patch (not applied):** validate enum-targeted string/bare values against `MemberNames`
  in analysis; make runtime `assign`/`entry` reject non-members the same way.

---

## 🟠 The prize — probes that compile 0/0 and diverge (silent no-op / wrong answer)

## F6 — `assign Name to Name + 5` (Text + Number) compiles 0/0; export "x5", runtime "x"
- **Signal:** export/runtime divergence (silent no-op)
- **Severity:** 🟠 (top of the prize list)
- **Repro:** `probes/agent-b/cc-string-number-concat.poly` (0/0). Runtime harness:
  `assign Name to Name + 5` on Name="x" → `Name` stays `"x"`; `assign Name to 5 + Name` → `"x"`.
- **Expected:** the guide (§8 arithmetic shipped) implies `Text + Number` is string concat; the
  export agrees: `this.Name = this.Name + 5L;` → `"x5"` (and `this.Name = 5L + this.Name` → `"5x"`).
  Both compile 0/0.
- **Actual:** the runtime silently **drops the numeric operand** — `Name + 5` yields `"x"` (no error,
  invoke reports success). Same DSL, same input, different result on export vs runtime.
- **Proposed patch (not applied):** runtime arithmetic must implement string-concat with numeric
  operands the same way the C# export does (append the operand), or reject mixed string/number
  `+` at analysis so neither path pretends.

## F7 — `Name == null` / `Name is null` compiles 0/0 but runtime returns false when Name IS null
- **Signal:** export/runtime divergence (wrong answer, no failure)
- **Severity:** 🟠
- **Repro:** `probes/agent-b/cc-null-comparison.poly` (0/0). Runtime harness: instance created with
  no props (`Name` is literally `null` in the bag) and with `{"Name": null}` →
  `Name == null` = **false**, `Name is null` = **false**, `Name != null` = **true**. Bound "x" →
  `== null` false (correct).
- **Expected:** export emits `this.Name == null` (correct — true when null). A null-valued prop
  compared to `null` must be true; `!= null` must be false.
- **Actual:** the runtime's null equality is **inverted** for absent/null props — every `X == null`
  guard written in DSL silently never fires. Fail-closed and parity both violated.
- **Proposed patch (not applied):** fix the runtime equality path so a `null` literal LHS/RHS
  compares by reference/value (don't coerce the null literal), and add a regression test for the
  three states (unbound, explicit null, bound).

## F8 — `When == "2024-01-01"` / `When < "2025-01-01"` evaluated correctly by runtime; export CS0019
- **Signal:** divergence (export can't express a runtime-supported comparison)
- **Severity:** 🟠
- **Repro:** `probes/agent-b/cc-date-vs-string.poly` (export fails); runtime harness: Date
  `2024-01-01` → `When == "2024-01-01"` = **true**, `When < "2025-01-01"` = **true**.
- **Expected:** the guide's own examples (§0.4-era probes, and natural authoring) compare dates to
  string literals; the runtime parses them correctly.
- **Actual:** the export emits `DateOnly == string` / `DateOnly < string` → CS0019 (compile-fail).
  The runtime is right and the generated C# cannot express the same DSL — the tool surfaces disagree
  on whether this is valid.
- **Proposed patch (not applied):** exporter should convert the string literal to `DateOnly.Parse`/
  `DateOnly` constant when the other operand is a `Date` type; same for `DateTime`.

## F11 — any string/Date coerced to Number at runtime silently becomes constant `2`
- **Signal:** silent gap (wrong data, no failure)
- **Severity:** 🟠
- **Repro:** runtime harness: `assign Count to Name` for Name ∈ {"hello","Red","7","x",""},
  `assign Count to s` (param "hello"), `assign Count to "7"` literal, `assign Count to When + 1`
  (Date) — **all store `2`**. `Count > "0"` (Count=5) → true because `"0"` coerces to `2`.
- **Expected:** a string/date into a Number assign should be rejected at analysis (F2), and if
  evaluated, "7" must become 7 (or fail loud) — never a constant 2.
- **Actual:** the VM member-passthrough resolves the RHS value to the `_values` bag handle, and the
  number coercion returns that handle (2 in fresh sessions) — silently storing garbage numbers that
  feed comparisons, arithmetic, and quantifiers with plausible-but-wrong results (`any items where
  When > 5` → false, `R23`).
- **Proposed patch (not applied):** see F2; at minimum the numeric conversion path must fail loud
  instead of returning a heap handle.

## F9 — `assign Name to true` / `assign Name to 1` silently stores the whole property bag
- **Signal:** silent gap (data corruption with "success")
- **Severity:** 🟠 (worst single-store case)
- **Repro:** runtime harness: `assign Name to true` → Name = `Dictionary<string,object>` containing
  the instance's own bag (`{Name=…, Flag=True}`); `assign Name to 1` → same; `assign Name to Flag`
  (bool prop) → same. InvokeAction returns **success**.
- **Expected:** a Text prop assigned a bool/number literal should either fail loud or store the
  stringified value — never a reference to the property bag.
- **Actual:** the RHS read of a bool literal / number literal resolves to the args bag itself, and
  the assign stores that bag object in the property. Later reads, exports, and any stringification
  are corrupted. Export for `assign Name to true` → CS0029 (compile-fail) — so export refuses and
  runtime silently corrupts.
- **Proposed patch (not applied):** fix the literal/passthrough member read in the VM
  (`DomainExpressionLoweringPass`/`DirectVmAbiEmitter` member path) so constant bools/numbers don't
  resolve to the bag; add a regression test that `assign Name to true` stores "True" or fails.

## F10 — date arithmetic in assign crashes opaquely while the export computes correctly
- **Signal:** fail-loud-but-sharp (opaque crash) + divergence
- **Severity:** 🟡
- **Repro:** runtime harness: `assign When to When + 1` on Date `2024-01-01` →
  `Action execution failed: Unable to cast object of type 'System.String' to type 'System.DateOnly'.`
  Also `assign Name to n` with a declared `(n: Number)` param and **no** arg →
  `Unable to cast object of type 'System.Int64' to type 'System.Reflection.Missing'.`
- **Expected:** `When + 1` should produce 2024-01-02 (the export lowers `this.When.AddDays(1)` and
  compiles 0/0 — it *works*), or the DSL should reject date arithmetic at analysis. Missing required
  params should fail with a domain-level message, not a reflection cast error.
- **Actual:** runtime throws an unhandled `InvalidCastException` (surfaced as a tool failure message
  with an internal CLR cast detail); the export happily computes. Parity broken, and the runtime
  error is opaque.
- **Proposed patch (not applied):** either analyze date arithmetic as unsupported (F1-family) or
  lower `Date + Number` to `AddDays` in the runtime like the exporter does; validate required action
  params before execution.

---

## 🟡 Fail-loud-but-sharp / minor

## F4 — enum-member `default(Red)` on a non-enum prop throws at codegen, runtime silently stores "Red"
- **Signal:** fail-loud-but-sharp (wrong layer) + divergence
- **Severity:** 🟡
- **Repro:** `probes/agent-b/enum-null-bool-abuse.poly` — `Text default(Red)`, `Number default(Green)`,
  `Boolean default(Blue)` → `Code generation failed: … 'Red' is not a member of an enum that 'A' is
  typed with.` Runtime `create_instance` silently stores the member name string ("Red") in the Text
  prop.
- **Expected:** analysis should reject an enum-member default on a non-enum-typed property with a
  domain diagnostic; runtime must not accept it.
- **Actual:** rejection happens at **codegen** (`LowerDefaultConstantNode` throw), after analysis
  passed; the runtime accepts and stores the member string. Layer wrong, and runtime/export disagree.
- **Proposed patch (not applied):** validate in the constraint analyzer that a `PropertyAccess`
  default is an enum member of the property's own enum type (mirror the codegen rule).

## F12 — negative numeric literals are unparseable in policy expressions
- **Signal:** fail-loud-but-sharp
- **Severity:** 🟡
- **Repro:** `policy { Count > -1 }` → `Parse error: Expected expression, got '-'`. Same for
  `assign X to -3`. Meanwhile `create_instance` accepts negative values and `Count >= 0`,
  `Count == 0` evaluate fine.
- **Expected:** `Number` props hold negatives (runtime stores -5); comparisons like `Count > -1` are
  natural and should parse (the export compares `long` fine).
- **Actual:** `ScanNumber` scans digit runs only; the expression grammar has no unary minus
  (round-2 F-R2-7 found the same in `range` bounds). Any negative-bound comparison is impossible to
  author.
- **Proposed patch (not applied):** add unary minus to the expression grammar (and `ScanNumber`),
  consistent with the runtime's ability to store negative values.

---

## Verified clean (no finding) within slice

- **Correct same-type controls:** `assign Count to Count + 5` → 8; `assign Name to Name + "y"` →
  "xy"; `Count == 18`, `Count > 0`, `Count >= 0`, `Count == 0` (Count=-5) all evaluate sanely;
  action params same-type (`assign Count to n`, n=42) → 42; subscription fan-out with link works;
  `not`/`is true` on a **Boolean** prop coerce correctly; `default(now)` on DateTime and
  `default(today)` on Date are the only well-typed default pairs and compile 0/0.
- **Parse-level rejects (category a):** negative literals (`-1`, `-3`) — but see F12 (sharp);
  `Guid` as a property type — parser doesn't know it (exporter maps it — minor asymmetry, not filed).
- **Fail-loud on equality-with-bool:** `Name == true` / `Name is true` on a non-bool string throw
  `String 'x' was not recognized as a valid Boolean.` — fails loud (F1 covers the analysis gap).

---

## The one-line summary

Type compatibility is **unchecked in parse and analysis**: every wrong-typed comparison/assign/
arithmetic/default passes to either a C# compile-fail (F1–F3, F5) or, on the MCP runtime surface,
a silent garbage store — a constant `2` for any string→number (F11), the whole property bag for
`assign X to true` (F9), inverted `== null` (F7), dropped string-concat operands (F6), and a
date-vs-string comparison the runtime handles but the export cannot compile (F8).
