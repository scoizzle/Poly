# Fleet-eval 2026-08-12 — slice 15: Test suite & tooling quality

Agent: test-suite evaluator. Scope: `Poly.Tests/` (2063 TUnit tests, suite verified
green 2063/2063 in 1.77s), `Poly.Tests/TestHelpers/`, `scripts/` (run-probe.sh,
probe-check/, discovery-round.sh, new-probe.sh, restart-poly-mcp.sh),
`Poly.Benchmarks/`. No tests or scripts were edited; findings only.

Method note: this slice probes the test/tooling layer itself, not DSL export output.
Static review of a representative sample (McpSmokeTests, UnifiedAddTests,
OracleToolTests, SurfaceExtensionDogfoodTests, DomainToCSharpExporterTests,
SqlitePackTests, DslCompilerCompileOracleTests, DomainEntityInstanceTests,
P4SubscriptionQuantifierDslTests, fail-closed analysis tests, TestHelpers,
Benchmarks) + live verification of the full suite and of probe-check's exit-code
contract.

---

## F1 — probe-check ignores warnings: the documented "0 errors / 0 warnings" gate is not enforced
- **Signal:** silent-gap (tooling)
- **Severity:** 🟠
- **Slice:** 15-tests / scripts
- **Repro:** `scripts/probe-check/Program.cs:36` returns `errors == 0 ? 0 : 1`.
  Verified live: a file emitting 1 warning (unused variable) prints
  `errors: 0, warnings: 1` and **exits 0**.
- **Expected:** `run-probe.sh` header ("Exits 0 only when the export compiles with
  0 errors / 0 warnings"), discovery-loop doc step 2 ("0 errors / 0 warnings
  required"), and `discovery-round.sh` baseline header all promise a 0/0 gate.
  A warning-bearing export (CS0618 / CS0168 / CS8602-class) must fail the gate.
- **Actual:** warnings are counted and printed but never affect the exit code;
  `discovery-round.sh:43` marks PASS on `^errors: 0` alone. A compile-warning-laden
  export sails through as PASS.
- **Note:** the in-suite Roslyn oracles (DslCompilerCompileOracleTests,
  DomainToCSharpExporterTests.AssertExportCompiles) DO enforce warnings==0 — so
  the gap is specific to the probe tooling, which is exactly where agents rely on it.

## F2 — tautological placeholder test inflates the suite with zero oracle
- **Signal:** oracle-weakening
- **Severity:** 🟡
- **Slice:** 15-tests / Interpretation
- **Repro:** `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs:9` —
  `EH_Vm_Deprecated_NonCritical` asserts `var v = true; await Assert.That(v).IsTrue();`
  and declares an unused `CancellationToken ct`.
- **Expected:** a test named *ExceptionHandling* would catch an exception-handling
  regression in the VM.
- **Actual:** passes with EH completely broken; it is a count-inflating placeholder
  (the class doc says "Pruned: old VM EH tests…"). Naming also violates the
  `Method_Condition_ExpectedResult` convention.

## F3 — full-solution compile oracle covers exactly one DSL shape
- **Signal:** coverage-gap
- **Severity:** 🟡
- **Slice:** 15-tests / Lowering
- **Repro:** `DslCompilerCompileOracleTests` has ONE `[Test]` / one fixture.
  All other compile gates in-suite (`AssertExportCompiles` in
  DomainToCSharpExporterTests) compile ONLY the entity-type projection against
  TRUSTED_PLATFORM_ASSEMBLIES (no ASP.NET/EF refs); SqlitePackTests assert string
  markers on Program.cs/DbContext but never Roslyn-compile the generated solution.
- **Expected:** the generated solution (entities + DbContext + MinimalApi + DTOs)
  should be compile-gated for the newer surfaces (action-param DTOs, enum-typed
  params, verified-range `[Range]`/`[RegularExpression]` propagation, DateOnly
  arithmetic in endpoints, `default(now)` seeds, for-invoke).
- **Actual:** a CS-class bug in Program.cs/DTO generation for any surface beyond
  the single Library fixture is only caught at consumer time. (The one fixture is
  rich — policies/quantifiers/create-in/entry-exit — but has no action params,
  no pattern/length DTO propagation, no `default(now)`/`default(guid)`, no for-invoke.)

## F4 — bifold "if success then X else tautology" assertions weaken fail-closed tests
- **Signal:** oracle-weakening
- **Severity:** 🟡
- **Slice:** 15-tests / Mcp (SurfaceExtensionDogfoodTests)
- **Repro:** `ApplyDsl_UnboundPeerPathPrefix_FailsAnalysis`
  (else branch: `Assert.That(apply.Message.Length).IsGreaterThan(0)`),
  `ApplyDsl_UnknownWhenStage_FailsAnalysis`,
  `PathPrefix_MultipleLinks_EvaluateFailsClosed_ViaMcp`,
  `ExportDomainToCSharp_WithPeerAnalysisError_FailsClosed`
  (else branches re-assert the branch condition, e.g. `Assert.That(apply.Success).IsFalse()`).
- **Expected:** a fail-closed regression test pins ONE required outcome (reject at
  apply OR report analysis errors), so a regression flips it red.
- **Actual:** the else-branch assertion is vacuous (always true inside the branch).
  A shift in which layer rejects (apply vs analyze) or a truncation of the reject
  path passes these without exercising the intended fail-closed contract.

## F5 — brittle exact-format JSON string assertions pin snapshot serialization ABI
- **Signal:** brittleness
- **Severity:** 🟡
- **Slice:** 15-tests / Mcp
- **Repro:** `McpSmokeTests.CreateInstance_AppliesPropertyDefaults`
  (`get.Contains("\"10\"")`, `Contains("\"On\"")`),
  `InvokeAction_RequireNotPolicy_WhenPolicyFalse_Succeeds`
  (`Contains("\"value\":\"1\"")`) — depend on number-values-as-strings AND the exact
  JSON property casing/quoting of `get_instance` snapshots. Also
  `GetDomainAnalysis_WithHints_SuggestsSuggestions` asserts `Message.Contains("hint")`.
- **Expected:** oracle assertions should survive a serializer format change while
  still failing on a semantic regression.
- **Actual:** a deliberate (correct) serialization change breaks these spuriously;
  conversely they document the current ABI only implicitly. Low-freq but real churn
  risk; a typed snapshot assert would be strictly stronger.

## F6 — dead code + doc drift in the benchmark project and product READMEs
- **Signal:** dead-code / consistency
- **Severity:** 🟡
- **Slice:** 15-tests / Poly.Benchmarks + docs
- **Repro:** `Poly.Benchmarks/Playground.cs` and `FluentApiExample.cs` are 100%
  commented-out (V1-era V2 model, removed APIs). `Poly/DomainModeling/README.md:105`
  lists `PolicyEvaluator` under product `Lowering/` — it exists only in
  `Poly.Tests/TestHelpers/`; `Poly/Interpretation/README.md:210` references the
  test-only `TestTraceWriter` as if product-visible.
- **Expected:** benchmarks dir carries only runnable benchmarks; product READMEs
  describe product components.
- **Actual:** dead files still compile (harmless) and the READMEs misattribute
  test-only helpers as product components — a misleading map for agents.

## F7 — run-probe.sh awk dedup: dead array + silent using-drop edge case
- **Signal:** tooling robustness (minor)
- **Severity:** 🟡
- **Slice:** 15-tests / scripts
- **Repro:** `scripts/run-probe.sh:18` — `BEGIN{d["using System;"]=1;...}` array is
  declared but never used; the dedup `!(seen[$0]++)` drops subsequent identical
  `using` lines. For a combined export where file B's `using System;` is dropped
  and file A's block precedes it, the using is preserved (first occurrence), so the
  current behavior is safe — but the dead `d` array shows the intended
  "always keep core usings" guard is not implemented, and a future reordering of the
  concat could silently drop a needed using.
- **Actual:** harmless today; latent fragility.

## F8 — restart-poly-mcp.sh broad SIGKILL pattern
- **Signal:** reliability (minor)
- **Severity:** 🟡
- **Slice:** 15-tests / scripts
- **Repro:** `pkill -9 -f 'Poly.Mcp/bin'` matches any process whose cmdline contains
  "Poly.Mcp/bin"; `-9` gives no graceful shutdown. Post-check `pgrep` + exit 1 is a
  good fail-closed guard. Coordinator-only by policy; low blast radius but worth a
  narrower match.

---

## Verified non-findings (checked, clean)
- Full suite: 2063/2063 green in 1.77s — fast, no flaky/skipped/ignored tests.
- Test helpers are confined to `Poly.Tests/TestHelpers/` (no product-project
  reference to Poly.Tests anywhere; `DomainTestFactory` deliberately sits in the
  `Poly.DomainModeling` namespace inside the test assembly — documented, not a leak).
- Coverage on the recent surfaces called out in the round brief is present:
  for-invoke (exporter + analysis + MCP runtime goldens), subscriptions/quantifiers
  (SubscriptionAnalysisTests, P4SubscriptionQuantifierDslTests, SurfaceExtensionDogfood),
  verified ranges (OpenRange_VerifiedEnvelope_KeepsBoundOpen, ActionDto_*_Range),
  validation attributes ([Required]/[MinLength]/[MaxLength]/[RegularExpression] DTO
  propagation in SqlitePackTests).
- The in-suite Roslyn oracles enforce warning-free output (unlike probe-check, see F1).
- No security-relevant masking found; the MCP session store is an in-memory static
  (per-session GUID isolation makes the parallel test run safe).
